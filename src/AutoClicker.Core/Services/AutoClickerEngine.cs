using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Timing;
using AutoClicker.Core.Validation;

namespace AutoClicker.Core.Services;

/// <summary>Platform-independent, cancellable click scheduler.</summary>
public sealed class AutoClickerEngine : IAutoClickerEngine, IDisposable
{
    private static readonly TimeSpan TelemetryPeriod = TimeSpan.FromMilliseconds(100);
    private readonly object _sync = new();
    private readonly IMouseService _mouse;
    private readonly TimeProvider _timeProvider;
    private readonly IEngineDelay _engineDelay;
    private readonly Random _random;
    private Session? _session;
    private EngineTelemetry _telemetry = new(EngineState.Stopped, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
    private bool _disposeRequested;
    private bool _disposed;

    public AutoClickerEngine(IMouseService mouse, TimeProvider? timeProvider = null, Random? random = null, IEngineDelay? engineDelay = null)
    {
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _random = random ?? Random.Shared;
        _engineDelay = engineDelay ?? new TimeProviderEngineDelay();
    }

    public bool IsRunning { get { lock (_sync) return _telemetry.State != EngineState.Stopped; } }
    public EngineTelemetry Telemetry { get { lock (_sync) return _telemetry; } }
    public event EventHandler<EngineTelemetryEventArgs>? TelemetryChanged;
    public event EventHandler<EngineErrorEventArgs>? Error;

    public Task StartAsync(AutoClickerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ProfileValidator.ThrowIfInvalid(profile);
        cancellationToken.ThrowIfCancellationRequested();

        Session session;
        EngineTelemetry starting;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested || _disposed, this);
            if (_session is not null) return Task.CompletedTask;
            session = new Session(profile with { }, cancellationToken);
            _session = session;
            starting = MakeTelemetry(session, EngineState.Starting, TimeSpan.FromMilliseconds(session.Profile.StartDelayMilliseconds));
            _telemetry = starting;
            session.Worker = Task.Run(async () =>
            {
                await session.Ready.Task.ConfigureAwait(false);
                await RunSessionAsync(session).ConfigureAwait(false);
            });
        }

        Publish(starting);
        session.Ready.TrySetResult(true);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Session? session;
        EngineTelemetry? stopping = null;
        lock (_sync)
        {
            session = _session;
            if (session is null) return;
            if (_telemetry.State is not (EngineState.Stopping or EngineState.Stopped))
            {
                session.SetStopReason(_disposeRequested ? EngineStopReason.Disposed : EngineStopReason.StopRequested);
                stopping = _telemetry = MakeTelemetry(session, EngineState.Stopping, RemainingStartDelay(session));
            }
        }
        if (stopping is not null) Publish(stopping);
        if (Telemetry.State != EngineState.Stopped) session.Cancel();
        await session.Worker.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposeRequested = true;
        }
        await StopAsync().ConfigureAwait(false);
        lock (_sync) _disposed = true;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposeRequested = true;
        }
        StopAsync().GetAwaiter().GetResult();
        lock (_sync) _disposed = true;
    }

    private async Task RunSessionAsync(Session session)
    {
        var reason = EngineStopReason.ExternalCancellation;
        Exception? failure = null;
        using var durationMonitorCancellation = new CancellationTokenSource();
        Task? durationMonitor = null;
        try
        {
            session.StartTimestamp = _timeProvider.GetTimestamp();
            session.StartDelayDeadline = AddTimestamp(session.StartTimestamp, TimeSpan.FromMilliseconds(session.Profile.StartDelayMilliseconds));
            await RunStartDelayAsync(session).ConfigureAwait(false);
            session.Token.ThrowIfCancellationRequested();

            session.RunningTimestamp = _timeProvider.GetTimestamp();
            session.HasStartedRunning = true;
            session.NextTelemetryTimestamp = AddTimestamp(session.RunningTimestamp, TelemetryPeriod);
            if (session.Profile.DurationLimitEnabled)
                durationMonitor = EnforceDurationLimitAsync(session, durationMonitorCancellation.Token);
            Update(session, EngineState.Running, TimeSpan.Zero);
            reason = await RunClickLoopAsync(session).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
            reason = session.StopReason ?? EngineStopReason.ExternalCancellation;
        }
        catch (Exception exception)
        {
            failure = exception;
            reason = EngineStopReason.Faulted;
            session.SetStopReason(reason);
        }
        finally
        {
            durationMonitorCancellation.Cancel();
            if (durationMonitor is not null)
            {
                try { await durationMonitor.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception exception) { failure ??= exception; reason = EngineStopReason.Faulted; }
            }
            session.Cancellation.Dispose();
            EngineTelemetry stopped;
            lock (_sync)
            {
                reason = session.StopReason ?? reason;
                _session = null;
                stopped = _telemetry = MakeTelemetry(session, EngineState.Stopped, TimeSpan.Zero) with
                {
                    StopReason = reason,
                    ErrorMessage = failure?.Message
                };
            }
            Publish(stopped);
            if (failure is not null) PublishError(failure);
        }
    }

    private async Task RunStartDelayAsync(Session session)
    {
        var remaining = TimeSpan.FromMilliseconds(session.Profile.StartDelayMilliseconds);
        if (remaining <= TimeSpan.Zero) return;
        var deadline = session.StartDelayDeadline;
        var nextUpdate = _timeProvider.GetTimestamp();
        while (true)
        {
            session.Token.ThrowIfCancellationRequested();
            var now = _timeProvider.GetTimestamp();
            remaining = Remaining(deadline, now);
            if (remaining <= TimeSpan.Zero) return;
            if (now >= nextUpdate)
            {
                Update(session, EngineState.Starting, remaining);
                nextUpdate = AddTimestamp(now, TimeSpan.FromSeconds(1));
            }
            await DelayUntilAsync(Min(deadline, nextUpdate), session.Token).ConfigureAwait(false);
        }
    }

    private async Task<EngineStopReason> RunClickLoopAsync(Session session)
    {
        var profile = session.Profile;
        var schedule = new AbsoluteClickSchedule(_timeProvider.GetTimestamp());
        while (true)
        {
            session.Token.ThrowIfCancellationRequested();
            var now = _timeProvider.GetTimestamp();
            if (profile.DurationLimitEnabled && _timeProvider.GetElapsedTime(session.RunningTimestamp, now) >= profile.DurationLimit)
                return EngineStopReason.DurationLimitReached;
            if (profile.ClickLimitEnabled && session.PhysicalClicks >= profile.ClickLimit)
                return EngineStopReason.ClickLimitReached;

            if (now < schedule.NextDeadline)
            {
                var wake = Min(schedule.NextDeadline, session.NextTelemetryTimestamp);
                if (profile.DurationLimitEnabled)
                    wake = Min(wake, AddTimestamp(session.RunningTimestamp, profile.DurationLimit));
                await DelayUntilAsync(wake, session.Token).ConfigureAwait(false);
                now = _timeProvider.GetTimestamp();
                if (now >= session.NextTelemetryTimestamp)
                    PublishRunning(session);
                continue;
            }

            session.BurstDelayActive = false;
            var plannedCps = NextCyclesPerSecond(profile);
            session.CurrentScheduledCps = plannedCps;
            var position = profile.PositionMode == ClickPositionMode.Fixed
                ? new ScreenPoint(profile.FixedX!.Value, profile.FixedY!.Value)
                : (ScreenPoint?)null;
            var desiredPhysicalClicks = profile.ClickType == ClickType.Double ? 2 : 1;
            if (profile.ClickLimitEnabled)
                desiredPhysicalClicks = (int)Math.Min(desiredPhysicalClicks, profile.ClickLimit - session.PhysicalClicks);

            for (var index = 0; index < desiredPhysicalClicks; index++)
            {
                session.Token.ThrowIfCancellationRequested();
                if (profile.DurationLimitEnabled && _timeProvider.GetElapsedTime(session.RunningTimestamp) >= profile.DurationLimit)
                    return EngineStopReason.DurationLimitReached;
                await _mouse.ClickAsync(profile.MouseButton, position, session.Token).ConfigureAwait(false);
                session.RecordPhysicalClick(_timeProvider.GetTimestamp());
                PublishRunning(session);
                if (profile.ClickLimitEnabled && session.PhysicalClicks >= profile.ClickLimit)
                    break;
            }

            session.LogicalCycles++;
            session.BurstProgress++;
            if (profile.ClickLimitEnabled && session.PhysicalClicks >= profile.ClickLimit)
                return EngineStopReason.ClickLimitReached;

            var interval = TimeSpan.FromMilliseconds(ClickTiming.CpsToInterval(plannedCps));
            if (profile.BurstEnabled && session.BurstProgress >= profile.ClicksPerBurst)
            {
                session.BurstProgress = 0;
                session.BurstDelayActive = true;
                interval += TimeSpan.FromMilliseconds(profile.BurstDelayMilliseconds);
            }
            var completedAt = _timeProvider.GetTimestamp();
            schedule.Advance(interval, completedAt, _timeProvider.TimestampFrequency);
        }
    }

    private async Task EnforceDurationLimitAsync(Session session, CancellationToken monitorToken)
    {
        try
        {
            await Task.Delay(session.Profile.DurationLimit, _timeProvider, monitorToken).ConfigureAwait(false);
            session.SetStopReason(EngineStopReason.DurationLimitReached);
            session.Cancel();
        }
        catch (OperationCanceledException) when (monitorToken.IsCancellationRequested) { }
    }

    private double NextCyclesPerSecond(AutoClickerProfile profile) => profile.RandomizationEnabled
        ? profile.MinimumCps + _random.NextDouble() * (profile.MaximumCps - profile.MinimumCps)
        : profile.Cps;

    private async Task DelayUntilAsync(long deadline, CancellationToken cancellationToken)
    {
        await _engineDelay.DelayUntilAsync(deadline, _timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private void PublishRunning(Session session)
    {
        EngineTelemetry snapshot;
        lock (_sync)
        {
            if (!ReferenceEquals(_session, session)) return;
            var now = _timeProvider.GetTimestamp();
            if (_telemetry.State == EngineState.Running && now < session.NextTelemetryTimestamp) return;
            session.NextTelemetryTimestamp = AddTimestamp(now, TelemetryPeriod);
            snapshot = _telemetry = MakeTelemetry(session, _telemetry.State, TimeSpan.Zero);
        }
        Publish(snapshot);
    }

    private void Update(Session session, EngineState state, TimeSpan startDelayRemaining)
    {
        EngineTelemetry snapshot;
        lock (_sync)
        {
            if (!ReferenceEquals(_session, session)) return;
            if (_telemetry.State == EngineState.Stopping && state != EngineState.Stopping) return;
            snapshot = _telemetry = MakeTelemetry(session, state, startDelayRemaining);
        }
        Publish(snapshot);
    }

    private EngineTelemetry MakeTelemetry(Session session, EngineState state, TimeSpan startDelayRemaining)
    {
        var now = _timeProvider.GetTimestamp();
        var running = session.HasStartedRunning;
        var elapsed = running ? _timeProvider.GetElapsedTime(session.RunningTimestamp, now) : TimeSpan.Zero;
        var actualCps = session.CalculateActualCps(now, _timeProvider);
        var profile = session.Profile;
        return new EngineTelemetry(state, session.PhysicalClicks, actualCps, elapsed, startDelayRemaining)
        {
            LogicalCycleCount = session.LogicalCycles,
            TargetCyclesPerSecond = profile.RandomizationEnabled ? null : profile.Cps,
            MinimumCyclesPerSecond = profile.RandomizationEnabled ? profile.MinimumCps : profile.Cps,
            MaximumCyclesPerSecond = profile.RandomizationEnabled ? profile.MaximumCps : profile.Cps,
            CurrentScheduledCyclesPerSecond = session.CurrentScheduledCps,
            CurrentBurstProgress = session.BurstProgress,
            BurstSize = profile.BurstEnabled ? profile.ClicksPerBurst : 0,
            IsBurstDelayActive = session.BurstDelayActive,
            StopReason = session.StopReason
        };
    }

    private TimeSpan RemainingStartDelay(Session session) => session.StartDelayDeadline == 0
        ? TimeSpan.FromMilliseconds(session.Profile.StartDelayMilliseconds)
        : Remaining(session.StartDelayDeadline, _timeProvider.GetTimestamp());

    private long AddTimestamp(long timestamp, TimeSpan duration) => timestamp + (long)Math.Ceiling(duration.TotalSeconds * _timeProvider.TimestampFrequency);
    private TimeSpan Remaining(long deadline, long now) => deadline <= now ? TimeSpan.Zero : _timeProvider.GetElapsedTime(now, deadline);
    private static long Min(long left, long right) => Math.Min(left, right);
    private void Publish(EngineTelemetry telemetry) => InvokeSafely(TelemetryChanged, this, new EngineTelemetryEventArgs(telemetry));
    private void PublishError(Exception exception) => InvokeSafely(Error, this, new EngineErrorEventArgs(exception, exception.Message));

    private static void InvokeSafely<T>(EventHandler<T>? handlers, object sender, T args) where T : EventArgs
    {
        if (handlers is null) return;
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
        {
            try { handler(sender, args); }
            catch { /* Subscriber errors must not interrupt engine cleanup. */ }
        }
    }

    private sealed class Session
    {
        private readonly object _sync = new();
        private readonly Queue<long> _clickTimestamps = new();
        private EngineStopReason? _stopReason;

        public Session(AutoClickerProfile profile, CancellationToken externalToken)
        {
            Profile = profile;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            Token = Cancellation.Token;
        }

        public AutoClickerProfile Profile { get; }
        public CancellationTokenSource Cancellation { get; }
        public CancellationToken Token { get; }
        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Worker { get; set; } = Task.CompletedTask;
        public long StartTimestamp { get; set; }
        public long RunningTimestamp { get; set; }
        public bool HasStartedRunning { get; set; }
        public long StartDelayDeadline { get; set; }
        public long NextTelemetryTimestamp { get; set; }
        private long _physicalClicks;
        public long PhysicalClicks { get { lock (_sync) return _physicalClicks; } }
        public long LogicalCycles { get; set; }
        public int BurstProgress { get; set; }
        public bool BurstDelayActive { get; set; }
        public double CurrentScheduledCps { get; set; }
        public EngineStopReason? StopReason { get { lock (_sync) return _stopReason; } }

        public void SetStopReason(EngineStopReason reason) { lock (_sync) _stopReason ??= reason; }
        public void Cancel()
        {
            try { Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { /* Cancellation callbacks must not prevent worker cleanup. */ }
        }

        public void RecordPhysicalClick(long timestamp)
        {
            lock (_sync)
            {
                _physicalClicks++;
                _clickTimestamps.Enqueue(timestamp);
            }
        }

        public double CalculateActualCps(long now, TimeProvider timeProvider)
        {
            lock (_sync)
            {
                while (_clickTimestamps.Count > 0 && timeProvider.GetElapsedTime(_clickTimestamps.Peek(), now) > TimeSpan.FromSeconds(1))
                    _clickTimestamps.Dequeue();
                if (_clickTimestamps.Count == 0) return 0;
                var elapsed = timeProvider.GetElapsedTime(_clickTimestamps.Peek(), now);
                return _clickTimestamps.Count / Math.Max(elapsed.TotalSeconds, 0.001);
            }
        }
    }
}

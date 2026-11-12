using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Services;

public sealed record UsageStatisticsSnapshot(long TotalPhysicalClicks, TimeSpan TotalActiveTime, bool IsEngineStopped, string PersistenceWarning);

/// <summary>Observes engine telemetry and stores local lifetime totals at low frequency and lifecycle boundaries.</summary>
public sealed class UsageStatisticsService : IDisposable
{
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly IAutoClickerEngine _engine;
    private readonly IApplicationStore _store;
    private readonly ILogger<UsageStatisticsService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private LifetimeStatistics _totals = new();
    private long _lastSessionClicks;
    private long? _runningTimestamp;
    private ITimer? _checkpointTimer;
    private string _warning = "";
    private bool _initialized;

    public event EventHandler? Changed;

    public UsageStatisticsService(IAutoClickerEngine engine, IApplicationStore store,
        ILogger<UsageStatisticsService> logger, TimeProvider? timeProvider = null)
    {
        _engine = engine; _store = store; _logger = logger; _timeProvider = timeProvider ?? TimeProvider.System;
        _engine.TelemetryChanged += OnTelemetryChanged;
    }

    public UsageStatisticsSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var ticks = _totals.TotalActiveTicks;
                if (_runningTimestamp is { } started)
                    ticks = SaturatingAdd(ticks, _timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp()).Ticks);
                return new(_totals.TotalPhysicalClicks, TimeSpan.FromTicks(Math.Max(0, ticks)),
                    _engine.Telemetry.State == EngineState.Stopped, _warning);
            }
        }
    }

    public void Initialize(LifetimeStatistics statistics)
    {
        lock (_sync)
        {
            _totals = statistics;
            _lastSessionClicks = 0;
            _runningTimestamp = null;
            _initialized = true;
            _warning = "";
        }
        _logger.LogInformation("Usage statistics loaded.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ResetAsync(CancellationToken cancellationToken = default)
    {
        if (_engine.Telemetry.State != EngineState.Stopped) return false;
        await _persistGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        LifetimeStatistics previous;
        lock (_sync)
        {
            if (!_initialized || _engine.Telemetry.State != EngineState.Stopped) { _persistGate.Release(); return false; }
            previous = _totals;
            _totals = new();
        }
        try
        {
            await _store.SaveLifetimeStatisticsAsync(new(), cancellationToken).ConfigureAwait(false);
            SetWarning("");
            _logger.LogInformation("Usage statistics reset.");
            return true;
        }
        catch (Exception exception)
        {
            lock (_sync) _totals = previous;
            SetWarning("Usage statistics could not be saved. Your previous totals are still shown.");
            _logger.LogError(exception, "Usage statistics persistence failed.");
            return false;
        }
        finally { _persistGate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized) return true;
        await _persistGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var statistics = CaptureStatistics();
            await _store.SaveLifetimeStatisticsAsync(statistics, cancellationToken).ConfigureAwait(false);
            SetWarning("");
            _logger.LogInformation("Usage statistics checkpointed.");
            return true;
        }
        catch (Exception exception)
        {
            SetWarning("Usage statistics could not be saved. They remain in memory and will be retried.");
            _logger.LogError(exception, "Usage statistics persistence failed.");
            return false;
        }
        finally { _persistGate.Release(); }
    }

    private void OnTelemetryChanged(object? sender, EngineTelemetryEventArgs args)
    {
        if (!_initialized) return;
        var telemetry = args.Telemetry;
        var checkpoint = false;
        lock (_sync)
        {
            if (telemetry.State == EngineState.Starting) _lastSessionClicks = 0;
            if (telemetry.PhysicalClickCount > _lastSessionClicks)
            {
                _totals = _totals with { TotalPhysicalClicks = SaturatingAdd(_totals.TotalPhysicalClicks, telemetry.PhysicalClickCount - _lastSessionClicks) };
                _lastSessionClicks = telemetry.PhysicalClickCount;
            }
            else if (telemetry.PhysicalClickCount < _lastSessionClicks) _lastSessionClicks = telemetry.PhysicalClickCount;

            if (telemetry.State == EngineState.Running && _runningTimestamp is null)
            {
                _runningTimestamp = _timeProvider.GetTimestamp();
                if (_checkpointTimer is null)
                    _checkpointTimer = _timeProvider.CreateTimer(_ => _ = FlushAsync(), null, CheckpointInterval, CheckpointInterval);
                else _checkpointTimer.Change(CheckpointInterval, CheckpointInterval);
            }
            else if (telemetry.State != EngineState.Running && _runningTimestamp is { } started)
            {
                var elapsed = Math.Max(0, _timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp()).Ticks);
                _totals = _totals with { TotalActiveTicks = SaturatingAdd(_totals.TotalActiveTicks, elapsed) };
                _runningTimestamp = null;
                checkpoint = telemetry.State == EngineState.Stopped;
            }
            if (telemetry.State == EngineState.Stopped)
            {
                checkpoint = true;
                _checkpointTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (checkpoint) _ = FlushAsync();
    }

    private LifetimeStatistics CaptureStatistics()
    {
        lock (_sync)
        {
            var ticks = _totals.TotalActiveTicks;
            if (_runningTimestamp is { } started)
                ticks = SaturatingAdd(ticks, Math.Max(0, _timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp()).Ticks));
            return _totals with { TotalActiveTicks = ticks };
        }
    }

    private void SetWarning(string warning)
    {
        lock (_sync) _warning = warning;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static long SaturatingAdd(long left, long right) => right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;

    public void Dispose()
    {
        _engine.TelemetryChanged -= OnTelemetryChanged;
        _checkpointTimer?.Dispose();
        _persistGate.Dispose();
    }
}

public static class UsageStatisticsFormatter
{
    public static string FormatClicks(long clicks, System.Globalization.CultureInfo? culture = null) =>
        Math.Max(0, clicks).ToString("N0", culture ?? System.Globalization.CultureInfo.CurrentCulture);

    public static string FormatActiveTime(TimeSpan duration, ApplicationLanguage language = ApplicationLanguage.English, LocalizationService? localization = null)
    {
        string Unit(string key, string english) => localization?.Get(key) ?? english;
        var totalSeconds = Math.Max(0, (long)duration.TotalSeconds);
        var days = totalSeconds / 86400;
        var hours = totalSeconds / 3600 % 24;
        var minutes = totalSeconds / 60 % 60;
        var seconds = totalSeconds % 60;
        if (language == ApplicationLanguage.Turkish)
        {
            if (days > 0) return $"{days}{Unit("Unit_DayShort", " g")} {hours}{Unit("Unit_HourShort", " sa")}";
            if (totalSeconds >= 3600) return $"{totalSeconds / 3600}{Unit("Unit_HourShort", " sa")} {minutes}{Unit("Unit_MinuteShort", " dk")}";
            if (totalSeconds >= 60) return $"{totalSeconds / 60}{Unit("Unit_MinuteShort", " dk")} {seconds}{Unit("Unit_SecondShort", " sn")}";
            return $"{seconds}{Unit("Unit_SecondShort", " sn")}";
        }
        if (days > 0) return $"{days}d {hours}h {minutes}m";
        if (totalSeconds >= 3600) return $"{totalSeconds / 3600}h {minutes}m";
        if (totalSeconds >= 60) return $"{totalSeconds / 60}m {seconds}s";
        return $"{seconds}s";
    }
}

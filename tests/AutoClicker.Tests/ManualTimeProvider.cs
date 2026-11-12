namespace AutoClicker.Tests;

/// <summary>Small deterministic TimeProvider for tests that exercise Task.Delay(TimeProvider).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() { lock (_sync) return _utcNow; }
    public override long GetTimestamp() { lock (_sync) return _timestamp; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_sync)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.ChangeLocked(dueTime, period, _timestamp);
            _timers.Add(timer);
            return timer;
        }
    }

    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (_sync)
        {
            _timestamp += duration.Ticks;
            _utcNow += duration;
            foreach (var timer in _timers)
                if (timer.TryFireLocked(_timestamp, out var callback, out var state)) callbacks.Add((callback, state));
        }
        foreach (var (callback, state) in callbacks) callback(state);
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private long _dueTimestamp = long.MaxValue;
        private long _periodTicks = -1;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                if (_disposed) return false;
                ChangeLocked(dueTime, period, owner._timestamp);
                return true;
            }
        }

        public void ChangeLocked(TimeSpan dueTime, TimeSpan period, long now)
        {
            _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : now + Math.Max(0, dueTime.Ticks);
            _periodTicks = period == Timeout.InfiniteTimeSpan ? -1 : Math.Max(0, period.Ticks);
        }

        public bool TryFireLocked(long now, out TimerCallback dueCallback, out object? dueState)
        {
            dueCallback = callback;
            dueState = state;
            if (_disposed || _dueTimestamp > now) return false;
            _dueTimestamp = _periodTicks > 0 ? _dueTimestamp + _periodTicks : long.MaxValue;
            return true;
        }

        public void Dispose()
        {
            lock (owner._sync) { _disposed = true; _dueTimestamp = long.MaxValue; }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

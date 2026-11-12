namespace AutoClicker.Core.Timing;

/// <summary>Waits until a monotonic TimeProvider timestamp without changing the scheduler's clock.</summary>
public interface IEngineDelay
{
    ValueTask DelayUntilAsync(long deadline, TimeProvider timeProvider, CancellationToken cancellationToken);
}

/// <summary>Portable, virtual-clock-friendly delay used on macOS and in tests.</summary>
public sealed class TimeProviderEngineDelay : IEngineDelay
{
    public ValueTask DelayUntilAsync(long deadline, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetTimestamp();
        if (deadline <= now) return ValueTask.CompletedTask;
        return new ValueTask(Task.Delay(timeProvider.GetElapsedTime(now, deadline), timeProvider, cancellationToken));
    }
}

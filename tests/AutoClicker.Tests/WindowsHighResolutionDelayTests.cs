using AutoClicker.Platform.Windows;
using Xunit;

namespace AutoClicker.Tests;

public sealed class WindowsHighResolutionDelayTests
{
    [Fact]
    public async Task Reuses_one_native_timer_for_repeated_waits_and_disposes_it()
    {
        if (!OperatingSystem.IsWindows()) return;

        var delay = new WindowsHighResolutionDelay();
        var clock = TimeProvider.System;
        for (var index = 0; index < 100; index++)
        {
            var deadline = Add(clock.GetTimestamp(), TimeSpan.FromMilliseconds(2), clock.TimestampFrequency);
            await delay.DelayUntilAsync(deadline, clock, CancellationToken.None);
        }

        Assert.Equal(1, delay.TimerHandleCreationCount);
        Assert.True(delay.HighResolutionWaitCount + delay.StandardResolutionWaitCount >= 100);
        delay.Dispose();
        delay.Dispose();

        var nextDeadline = Add(clock.GetTimestamp(), TimeSpan.FromMilliseconds(1), clock.TimestampFrequency);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await delay.DelayUntilAsync(nextDeadline, clock, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_interrupts_a_native_timer_wait()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var delay = new WindowsHighResolutionDelay();
        using var cancellation = new CancellationTokenSource();
        var clock = TimeProvider.System;
        var deadline = Add(clock.GetTimestamp(), TimeSpan.FromSeconds(10), clock.TimestampFrequency);
        var waiting = Task.Run(async () => await delay.DelayUntilAsync(deadline, clock, cancellation.Token));

        await Task.Delay(25);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, delay.TimerHandleCreationCount);
    }

    private static long Add(long timestamp, TimeSpan duration, long frequency) =>
        timestamp + (long)Math.Ceiling(duration.TotalSeconds * frequency);
}

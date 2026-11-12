using AutoClicker.Core.Timing;
using Xunit;

namespace AutoClicker.Tests;

public sealed class TimingSchedulerTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Absolute_schedule_does_not_accumulate_click_execution_time(double cps)
    {
        const long frequency = TimeSpan.TicksPerSecond;
        var interval = TimeSpan.FromSeconds(1 / cps);
        var operationTime = TimeSpan.FromMilliseconds(1).Ticks;
        var schedule = new AbsoluteClickSchedule(0);
        long now = operationTime;
        for (var click = 1; click <= 500; click++)
        {
            schedule.Advance(interval, now, frequency);
            now = schedule.NextDeadline + operationTime;
        }
        var expectedDeadline = 500 * interval.Ticks;
        Assert.Equal(expectedDeadline, schedule.NextDeadline);
    }

    [Fact]
    public void Late_cycles_skip_expired_slots_instead_of_catching_up()
    {
        var schedule = new AbsoluteClickSchedule(0);
        var skipped = schedule.Advance(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(35).Ticks, TimeSpan.TicksPerSecond);
        Assert.Equal(3, skipped);
        Assert.Equal(TimeSpan.FromMilliseconds(40).Ticks, schedule.NextDeadline);
        Assert.Equal(0, schedule.Advance(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(35).Ticks, TimeSpan.TicksPerSecond));
        Assert.Equal(TimeSpan.FromMilliseconds(50).Ticks, schedule.NextDeadline);
    }

    [Fact]
    public async Task Portable_precise_delay_is_cancellable_with_the_manual_clock()
    {
        var clock = new ManualTimeProvider();
        var delay = new TimeProviderEngineDelay();
        using var cancellation = new CancellationTokenSource();
        var waiting = delay.DelayUntilAsync(TimeSpan.FromSeconds(5).Ticks, clock, cancellation.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}

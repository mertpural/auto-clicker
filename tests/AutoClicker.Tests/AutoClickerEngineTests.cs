using System.Collections.Concurrent;
using System.Diagnostics;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Services;
using AutoClicker.Core.Validation;
using Xunit;

namespace AutoClicker.Tests;

public sealed class AutoClickerEngineTests
{
    [Fact]
    public async Task ClickLimitStopsAtExactPhysicalClickCount()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        await engine.StartAsync(new AutoClickerProfile { ClickLimitEnabled = true, ClickLimit = 3, Cps = 500 });

        await Eventually(() => !engine.IsRunning);

        Assert.Equal(3, mouse.Clicks.Count);
        Assert.Equal(3, engine.Telemetry.PhysicalClickCount);
        Assert.Equal(EngineStopReason.ClickLimitReached, engine.Telemetry.StopReason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task DoubleClickRespectsOddAndEvenPhysicalLimits(long limit)
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        await engine.StartAsync(new AutoClickerProfile
        {
            ClickType = ClickType.Double,
            ClickLimitEnabled = true,
            ClickLimit = limit,
            Cps = 500
        });

        await Eventually(() => !engine.IsRunning);

        Assert.Equal(limit, mouse.Clicks.Count);
        Assert.Equal(limit, engine.Telemetry.PhysicalClickCount);
        Assert.Equal((limit + 1) / 2, engine.Telemetry.LogicalCycleCount);
    }

    [Fact]
    public async Task FixedPositionAndButtonArePassedToMouseService()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        var point = new ScreenPoint(120, 340);
        await engine.StartAsync(new AutoClickerProfile
        {
            MouseButton = MouseButton.Right,
            PositionMode = ClickPositionMode.Fixed,
            FixedX = point.X,
            FixedY = point.Y,
            ClickLimitEnabled = true,
            ClickLimit = 1
        });
        await Eventually(() => !engine.IsRunning);

        Assert.Equal(new MouseClick(MouseButton.Right, point), Assert.Single(mouse.Clicks));
    }

    [Fact]
    public async Task CurrentCursorModePassesNullPosition()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        await engine.StartAsync(new AutoClickerProfile { ClickLimitEnabled = true, ClickLimit = 1 });
        await Eventually(() => !engine.IsRunning);
        Assert.Null(Assert.Single(mouse.Clicks).Position);
    }

    [Fact]
    public async Task StartDelayPrecedesRunningAndDoesNotCountTowardElapsedDuration()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        var states = new ConcurrentQueue<EngineState>();
        engine.TelemetryChanged += (_, e) => states.Enqueue(e.Telemetry.State);
        await engine.StartAsync(new AutoClickerProfile
        {
            StartDelayMilliseconds = 100,
            ClickLimitEnabled = true,
            ClickLimit = 1
        });

        Assert.Equal(EngineState.Starting, engine.Telemetry.State);
        Assert.Empty(mouse.Clicks);
        await Eventually(() => !engine.IsRunning);

        Assert.Contains(EngineState.Running, states);
        Assert.Equal(EngineState.Stopped, states.Last());
        Assert.True(engine.Telemetry.Elapsed < TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task StopDuringStartDelayCancelsWithoutClicking()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        await engine.StartAsync(new AutoClickerProfile { StartDelayMilliseconds = 10_000 });
        await engine.StopAsync();

        Assert.Empty(mouse.Clicks);
        Assert.Equal(EngineState.Stopped, engine.Telemetry.State);
        Assert.Equal(EngineStopReason.StopRequested, engine.Telemetry.StopReason);
    }

    [Fact]
    public async Task ManualClockHoldsStartDelayUntilItsDeadline()
    {
        var clock = new ManualTimeProvider();
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse, clock);
        await engine.StartAsync(new AutoClickerProfile { StartDelayMilliseconds = 2000, ClickLimitEnabled = true, ClickLimit = 1 });
        await Eventually(() => engine.Telemetry.State == EngineState.Starting);
        await Task.Delay(10);

        clock.Advance(TimeSpan.FromMilliseconds(1999));
        await Task.Delay(10);
        Assert.Empty(mouse.Clicks);
        Assert.Equal(EngineState.Starting, engine.Telemetry.State);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually(() => !engine.IsRunning);
        Assert.Single(mouse.Clicks);
        Assert.Equal(EngineStopReason.ClickLimitReached, engine.Telemetry.StopReason);
    }

    [Fact]
    public async Task ManualClockEnforcesFractionalCpsInterval()
    {
        var clock = new ManualTimeProvider();
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse, clock);
        await engine.StartAsync(new AutoClickerProfile { Cps = 10, ClickLimitEnabled = true, ClickLimit = 2 });
        await Eventually(() => mouse.Clicks.Count == 1);
        await Task.Delay(10);

        clock.Advance(TimeSpan.FromMilliseconds(99));
        await Task.Delay(10);
        Assert.Single(mouse.Clicks);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually(() => !engine.IsRunning);
        Assert.Equal(2, mouse.Clicks.Count);
    }

    [Fact]
    public async Task ManualClockDurationBeginsAfterStartDelay()
    {
        var clock = new ManualTimeProvider();
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse, clock);
        await engine.StartAsync(new AutoClickerProfile
        {
            StartDelayMilliseconds = 1000,
            DurationLimitEnabled = true,
            DurationLimit = TimeSpan.FromSeconds(1),
            Cps = 1
        });
        await Task.Delay(10);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => engine.Telemetry.State == EngineState.Running);
        Assert.Equal(TimeSpan.Zero, engine.Telemetry.Elapsed);
        await Task.Delay(10);
        clock.Advance(TimeSpan.FromMilliseconds(999));
        await Task.Delay(10);
        Assert.True(engine.IsRunning);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually(() => !engine.IsRunning);
        Assert.Equal(EngineStopReason.DurationLimitReached, engine.Telemetry.StopReason);
        Assert.InRange(engine.Telemetry.Elapsed.TotalSeconds, 1, 1.01);
    }

    [Fact]
    public async Task ConcurrentStartsCreateOnlyOneSessionAndConcurrentStopsJoinIt()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        var profile = new AutoClickerProfile { StartDelayMilliseconds = 10_000 };
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => engine.StartAsync(profile)));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => engine.StopAsync()));

        Assert.Empty(mouse.Clicks);
        Assert.Equal(EngineState.Stopped, engine.Telemetry.State);
    }

    [Fact]
    public async Task Hundreds_of_start_stop_cycles_release_sessions_without_multiplying_events()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        var states = new ConcurrentQueue<EngineState>();
        engine.TelemetryChanged += (_, args) => states.Enqueue(args.Telemetry.State);
        var profile = new AutoClickerProfile { StartDelayMilliseconds = 10_000 };
        for (var index = 0; index < 200; index++)
        {
            await engine.StartAsync(profile);
            await engine.StopAsync();
            Assert.Equal(EngineState.Stopped, engine.Telemetry.State);
        }
        Assert.Empty(mouse.Clicks);
        Assert.InRange(states.Count(state => state == EngineState.Starting), 200, 400);
        Assert.Equal(200, states.Count(state => state == EngineState.Stopping));
        Assert.Equal(200, states.Count(state => state == EngineState.Stopped));
    }

    [Fact]
    public async Task CancellationTokenStopsSession()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        using var cancellation = new CancellationTokenSource();
        await engine.StartAsync(new AutoClickerProfile { StartDelayMilliseconds = 10_000 }, cancellation.Token);
        cancellation.Cancel();
        await Eventually(() => !engine.IsRunning);

        Assert.Equal(EngineStopReason.ExternalCancellation, engine.Telemetry.StopReason);
    }

    [Fact]
    public async Task PreCanceledTokenDoesNotCreateSession()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StartAsync(new AutoClickerProfile(), cancellation.Token));
        Assert.Equal(EngineState.Stopped, engine.Telemetry.State);
    }

    [Fact]
    public async Task InvalidProfileIsRejectedBeforeStarting()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        await Assert.ThrowsAsync<DataValidationException>(() => engine.StartAsync(new AutoClickerProfile { StartDelayMilliseconds = -1 }));
        Assert.Empty(mouse.Clicks);
        Assert.Equal(EngineState.Stopped, engine.Telemetry.State);
    }

    [Fact]
    public async Task StartUsesAnImmutableProfileSnapshot()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        var profile = new AutoClickerProfile { ClickLimitEnabled = true, ClickLimit = 1, MouseButton = MouseButton.Left };
        await engine.StartAsync(profile);
        profile = profile with { MouseButton = MouseButton.Right };
        await Eventually(() => !engine.IsRunning);
        Assert.Equal(MouseButton.Left, Assert.Single(mouse.Clicks).Button);
    }

    [Fact]
    public async Task FixedCpsWaitsBetweenLogicalCycles()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        var elapsed = Stopwatch.StartNew();
        await engine.StartAsync(new AutoClickerProfile { Cps = 20, ClickLimitEnabled = true, ClickLimit = 2 });
        await Eventually(() => !engine.IsRunning);

        Assert.Equal(2, mouse.Clicks.Count);
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(35), $"Only waited {elapsed.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.Equal(20, engine.Telemetry.TargetCyclesPerSecond);
    }

    [Fact]
    public async Task RandomizedRateReportsConfiguredRangeAndNoFixedTarget()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse, random: new Random(42));
        await engine.StartAsync(new AutoClickerProfile { RandomizationEnabled = true, MinimumCps = 10, MaximumCps = 20 });
        await Eventually(() => engine.Telemetry.CurrentScheduledCyclesPerSecond > 0);

        Assert.Null(engine.Telemetry.TargetCyclesPerSecond);
        Assert.Equal(10, engine.Telemetry.MinimumCyclesPerSecond);
        Assert.Equal(20, engine.Telemetry.MaximumCyclesPerSecond);
        Assert.InRange(engine.Telemetry.CurrentScheduledCyclesPerSecond, 10, 20);
        await engine.StopAsync();
    }

    [Fact]
    public async Task BurstAddsPauseAfterConfiguredNumberOfCycles()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        await engine.StartAsync(new AutoClickerProfile
        {
            Cps = 500,
            BurstEnabled = true,
            ClicksPerBurst = 2,
            BurstDelayMilliseconds = 200,
            ClickLimitEnabled = true,
            ClickLimit = 3
        });
        await Eventually(() => mouse.Clicks.Count >= 2);
        await Task.Delay(60);
        Assert.Equal(2, mouse.Clicks.Count);
        await Eventually(() => !engine.IsRunning, 1000);
        Assert.Equal(3, mouse.Clicks.Count);
    }

    [Fact]
    public async Task DurationLimitStopsSessionAndReportsReason()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        await engine.StartAsync(new AutoClickerProfile
        {
            Cps = 500,
            DurationLimitEnabled = true,
            DurationLimit = TimeSpan.FromMilliseconds(60)
        });
        await Eventually(() => !engine.IsRunning, 1500);

        Assert.True(mouse.Clicks.Count > 0);
        Assert.Equal(EngineStopReason.DurationLimitReached, engine.Telemetry.StopReason);
        Assert.InRange(engine.Telemetry.Elapsed.TotalMilliseconds, 50, 300);
    }

    [Fact]
    public async Task MouseFailurePublishesErrorStopsAndAllowsReuse()
    {
        var mouse = new FakeMouseService { Failure = new InvalidOperationException("input unavailable") };
        await using var engine = new AutoClickerEngine(mouse);
        var errors = new ConcurrentQueue<EngineErrorEventArgs>();
        engine.Error += (_, error) => errors.Enqueue(error);
        await engine.StartAsync(new AutoClickerProfile { ClickLimitEnabled = true, ClickLimit = 1 });
        await Eventually(() => !engine.IsRunning);

        Assert.Equal(EngineStopReason.Faulted, engine.Telemetry.StopReason);
        Assert.Equal(0, engine.Telemetry.PhysicalClickCount); // failed native output is not a successful physical click
        Assert.Contains("input unavailable", Assert.Single(errors).Message);
        mouse.Failure = null;
        await engine.StartAsync(new AutoClickerProfile { ClickLimitEnabled = true, ClickLimit = 1 });
        await Eventually(() => !engine.IsRunning);
        Assert.Single(mouse.Clicks);
    }

    [Fact]
    public async Task TelemetryIsPublishedAtBoundedRateAndTracksPhysicalAndLogicalCounts()
    {
        var mouse = new FakeMouseService();
        await using var engine = new AutoClickerEngine(mouse);
        var updates = new ConcurrentQueue<DateTimeOffset>();
        engine.TelemetryChanged += (_, _) => updates.Enqueue(DateTimeOffset.UtcNow);
        await engine.StartAsync(new AutoClickerProfile { ClickType = ClickType.Double, Cps = 500, ClickLimitEnabled = true, ClickLimit = 100 });
        await Eventually(() => engine.Telemetry.PhysicalClickCount >= 50);
        await Eventually(() => !engine.IsRunning);

        Assert.Equal(100, engine.Telemetry.PhysicalClickCount);
        Assert.Equal(50, engine.Telemetry.LogicalCycleCount);
        Assert.True(updates.Count < 15, $"Published {updates.Count} telemetry updates in a short run.");
    }

    [Fact]
    public async Task StopDuringInFlightClickCancelsMouseOperation()
    {
        var mouse = new FakeMouseService { BlockUntilCancelled = true };
        await using var engine = new AutoClickerEngine(mouse);
        await engine.StartAsync(new AutoClickerProfile());
        await Eventually(() => mouse.CallCount > 0);
        var stopping = engine.StopAsync();
        Assert.Equal(EngineState.Stopping, engine.Telemetry.State);
        await stopping;
        Assert.Equal(EngineState.Stopped, engine.Telemetry.State);
    }

    private static async Task Eventually(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > timeoutMilliseconds) throw new TimeoutException("Condition was not met before timeout.");
            await Task.Delay(5);
        }
    }

    private sealed record MouseClick(MouseButton Button, ScreenPoint? Position);

    private sealed class FakeMouseService : IMouseService
    {
        private readonly ConcurrentQueue<MouseClick> _clicks = new();
        private int _callCount;
        public IReadOnlyCollection<MouseClick> Clicks => _clicks.ToArray();
        public int CallCount => Volatile.Read(ref _callCount);
        public Exception? Failure { get; set; }
        public bool BlockUntilCancelled { get; set; }

        public async Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            if (BlockUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (Failure is not null) throw Failure;
            _clicks.Enqueue(new MouseClick(button, position));
        }

        public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default) =>
            ClickAsync(button, position, cancellationToken);
    }
}

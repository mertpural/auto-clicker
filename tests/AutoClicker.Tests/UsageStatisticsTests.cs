using AutoClicker.App.Services;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoClicker.Tests;

public sealed class UsageStatisticsTests
{
    [Fact]
    public void New_user_totals_default_to_zero()
    {
        var data = ApplicationData.CreateDefault();
        Assert.Equal(0, data.LifetimeStatistics.TotalPhysicalClicks);
        Assert.Equal(0, data.LifetimeStatistics.TotalActiveTicks);
    }

    [Fact]
    public void Cumulative_session_snapshots_are_counted_as_deltas_and_sessions_continue()
    {
        var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, new ManualTimeProvider());
        service.Initialize(new LifetimeStatistics { TotalPhysicalClicks = 100 });
        engine.Emit(EngineState.Starting, 0);
        engine.Emit(EngineState.Running, 0);
        engine.Emit(EngineState.Running, 1); // physical single click
        engine.Emit(EngineState.Running, 3); // double click: two physical clicks
        engine.Emit(EngineState.Running, 3);
        engine.Emit(EngineState.Stopping, 3);
        engine.Emit(EngineState.Stopped, 3);
        engine.Emit(EngineState.Starting, 0);
        engine.Emit(EngineState.Running, 0);
        engine.Emit(EngineState.Running, 2);
        Assert.Equal(105, service.Snapshot.TotalPhysicalClicks);
    }

    [Fact]
    public void Failed_click_not_reported_by_engine_does_not_increment_total()
    {
        var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, new ManualTimeProvider());
        service.Initialize(new());
        engine.Emit(EngineState.Starting, 0); engine.Emit(EngineState.Running, 0);
        engine.Emit(EngineState.Running, 1); // only successful physical clicks enter engine telemetry
        Assert.Equal(1, service.Snapshot.TotalPhysicalClicks);
    }

    [Fact]
    public void Running_time_excludes_start_delay_and_time_after_stopping()
    {
        var clock = new ManualTimeProvider(); var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, clock);
        service.Initialize(new());
        engine.Emit(EngineState.Starting, 0); clock.Advance(TimeSpan.FromSeconds(3));
        engine.Emit(EngineState.Running, 0); clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), service.Snapshot.TotalActiveTime);
        engine.Emit(EngineState.Stopping, 0); clock.Advance(TimeSpan.FromSeconds(8));
        engine.Emit(EngineState.Stopped, 0); clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), service.Snapshot.TotalActiveTime);
    }

    [Theory]
    [InlineData(EngineState.Stopping, EngineState.Stopped)] // manual / emergency / shutdown
    [InlineData(EngineState.Stopped, EngineState.Stopped)] // click or duration limit
    public void Any_running_end_finalizes_once(EngineState firstEnd, EngineState finalEnd)
    {
        var clock = new ManualTimeProvider(); var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, clock); service.Initialize(new());
        engine.Emit(EngineState.Starting, 0); engine.Emit(EngineState.Running, 0);
        clock.Advance(TimeSpan.FromSeconds(17)); engine.Emit(firstEnd, 0);
        if (finalEnd != firstEnd) engine.Emit(finalEnd, 0);
        engine.Emit(EngineState.Stopped, 0);
        Assert.Equal(TimeSpan.FromSeconds(17), service.Snapshot.TotalActiveTime);
    }

    [Fact]
    public async Task Click_updates_do_not_write_until_checkpoint_or_stop()
    {
        var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, new ManualTimeProvider()); service.Initialize(new());
        engine.Emit(EngineState.Starting, 0); engine.Emit(EngineState.Running, 0);
        for (var count = 1; count <= 1000; count++) engine.Emit(EngineState.Running, count);
        Assert.Equal(0, store.StatisticsWrites);
        engine.Emit(EngineState.Stopped, 1000);
        await service.FlushAsync();
        Assert.InRange(store.StatisticsWrites, 1, 2);
        Assert.Equal(1000, store.Data.LifetimeStatistics.TotalPhysicalClicks);
    }

    [Fact]
    public async Task Reset_persists_zero_totals_and_is_rejected_while_running()
    {
        var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, new ManualTimeProvider());
        service.Initialize(new LifetimeStatistics { TotalPhysicalClicks = 20, TotalActiveTicks = TimeSpan.FromMinutes(5).Ticks });
        engine.Emit(EngineState.Starting, 0); engine.Emit(EngineState.Running, 0);
        Assert.False(await service.ResetAsync());
        Assert.Equal(20, service.Snapshot.TotalPhysicalClicks);
        engine.Emit(EngineState.Stopped, 0);
        Assert.True(await service.ResetAsync());
        Assert.Equal(0, store.Data.LifetimeStatistics.TotalPhysicalClicks);
        Assert.Equal(0, store.Data.LifetimeStatistics.TotalActiveTicks);
    }

    [Fact]
    public async Task Settings_reset_command_is_disabled_while_running_and_uses_the_required_confirmation()
    {
        var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, new ManualTimeProvider());
        service.Initialize(new LifetimeStatistics { TotalPhysicalClicks = 9 });
        var dialogs = new CapturingDialogs();
        var settings = new AutoClicker.App.ViewModels.SettingsViewModel(new TestThemeService(), usageStatistics: service, dialogs: dialogs);
        Assert.True(settings.ResetStatisticsCommand.CanExecute(null));
        engine.Emit(EngineState.Starting, 0); Assert.False(settings.ResetStatisticsCommand.CanExecute(null));
        engine.Emit(EngineState.Running, 0); Assert.False(settings.ResetStatisticsCommand.CanExecute(null));
        engine.Emit(EngineState.Stopped, 0); Assert.True(settings.ResetStatisticsCommand.CanExecute(null));
        await settings.ResetStatisticsCommand.ExecuteAsync(null);
        Assert.Equal("Reset usage statistics?", dialogs.Title);
        Assert.Equal("This will permanently reset total clicks and active time to zero.", dialogs.Message);
        Assert.Equal("Reset", dialogs.ConfirmLabel);
        Assert.Equal(0, store.Data.LifetimeStatistics.TotalPhysicalClicks);
    }

    [Fact]
    public async Task Flush_while_running_checkpoints_live_active_time_without_finalizing_twice()
    {
        var clock = new ManualTimeProvider(); var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, clock); service.Initialize(new());
        engine.Emit(EngineState.Starting, 0); engine.Emit(EngineState.Running, 0);
        clock.Advance(TimeSpan.FromSeconds(12)); await service.FlushAsync();
        Assert.Equal(TimeSpan.FromSeconds(12).Ticks, store.Data.LifetimeStatistics.TotalActiveTicks);
        engine.Emit(EngineState.Stopped, 0);
        Assert.Equal(TimeSpan.FromSeconds(12).Ticks, store.Data.LifetimeStatistics.TotalActiveTicks);
    }

    [Fact]
    public void Checkpoint_timer_resumes_on_a_later_running_session()
    {
        var clock = new ManualTimeProvider(); var engine = new TestEngine(); var store = new MemoryStore();
        using var service = Create(engine, store, clock); service.Initialize(new());
        engine.Emit(EngineState.Starting, 0); engine.Emit(EngineState.Running, 0);
        clock.Advance(TimeSpan.FromSeconds(30)); Assert.Equal(1, store.StatisticsWrites);
        engine.Emit(EngineState.Stopped, 0);
        engine.Emit(EngineState.Starting, 0); engine.Emit(EngineState.Running, 0);
        clock.Advance(TimeSpan.FromSeconds(30)); Assert.Equal(3, store.StatisticsWrites); // stop flush + second checkpoint
    }

    [Theory]
    [InlineData(42, "42s")]
    [InlineData(754, "12m 34s")]
    [InlineData(13320, "3h 42m")]
    [InlineData(191880, "2d 5h 18m")]
    public void Active_time_formatter_uses_compact_units(long seconds, string expected) =>
        Assert.Equal(expected, UsageStatisticsFormatter.FormatActiveTime(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Click_formatter_keeps_exact_locale_formatted_value()
    {
        var formatted = UsageStatisticsFormatter.FormatClicks(1_245_932);
        Assert.Contains("1", formatted); Assert.Contains("245", formatted); Assert.Contains("932", formatted);
        Assert.DoesNotContain("K", formatted, StringComparison.OrdinalIgnoreCase);
    }

    private static UsageStatisticsService Create(TestEngine engine, MemoryStore store, TimeProvider clock) =>
        new(engine, store, NullLogger<UsageStatisticsService>.Instance, clock);

    private sealed class TestEngine : IAutoClickerEngine
    {
        public bool IsRunning => Telemetry.State == EngineState.Running;
        public EngineTelemetry Telemetry { get; private set; } = new(EngineState.Stopped, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
        public event EventHandler<EngineTelemetryEventArgs>? TelemetryChanged;
        public event EventHandler<EngineErrorEventArgs>? Error { add { } remove { } }
        public void Emit(EngineState state, long clicks)
        { Telemetry = Telemetry with { State = state, ClickCount = clicks }; TelemetryChanged?.Invoke(this, new(Telemetry)); }
        public Task StartAsync(AutoClickerProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MemoryStore : IApplicationStore
    {
        public string ConfigurationDirectory => Path.GetTempPath();
        public ApplicationData Data { get; private set; } = ApplicationData.CreateDefault();
        public int StatisticsWrites { get; private set; }
        public Task<ApplicationData> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Data);
        public Task SaveAsync(ApplicationData data, CancellationToken cancellationToken = default) { Data = data; return Task.CompletedTask; }
        public Task SaveLifetimeStatisticsAsync(LifetimeStatistics statistics, CancellationToken cancellationToken = default)
        { Data = Data with { LifetimeStatistics = statistics }; StatisticsWrites++; return Task.CompletedTask; }
    }

    private sealed class TestThemeService : AutoClicker.App.Services.IThemeService { public void Apply(Appearance appearance) { } }
    private sealed class CapturingDialogs : AutoClicker.App.Services.IDesktopDialogService
    {
        public string Title { get; private set; } = "";
        public string Message { get; private set; } = "";
        public string ConfirmLabel { get; private set; } = "";
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
        { Title = title; Message = message; ConfirmLabel = confirmLabel; return Task.FromResult(true); }
    }
}

using System.Collections.Concurrent;
using AutoClicker.App.Services;
using AutoClicker.App.ViewModels;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoClicker.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public void ExtremelySmallNumericInputShowsValidationInsteadOfOverflowing()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile());
        const decimal tiny = 0.0000000000000000000000000001m;
        editor.Cps = tiny;
        Assert.False(editor.TryGetSnapshot(out _));
        Assert.NotEmpty(editor.CpsError);
        editor.IntervalMilliseconds = tiny;
        Assert.False(editor.TryGetSnapshot(out _));
        Assert.NotEmpty(editor.IntervalError);
    }

    [Fact]
    public void NumericEditorSynchronizesSpeedAndRejectsEmptyValues()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile());
        editor.Cps = 10;
        Assert.Equal(100m, editor.IntervalMilliseconds);
        editor.IntervalMilliseconds = 50;
        Assert.Equal(20m, editor.Cps);
        Assert.True(editor.TryGetSnapshot(out var snapshot));
        Assert.Equal(20, snapshot.Cps);
        editor.IntervalMilliseconds = null;
        Assert.False(editor.TryGetSnapshot(out _));
        Assert.NotEmpty(editor.IntervalError);
    }

    [Fact]
    public async Task Selected_profile_tab_is_preserved_when_switching_profiles()
    {
        var (workspace, _, autosave) = CreateWorkspace();
        await workspace.InitializeAsync();
        workspace.SelectedProfile!.SelectedTabIndex = 3;
        workspace.NewProfileCommand.Execute(null);
        Assert.Equal(3, workspace.SelectedProfile!.SelectedTabIndex);
        workspace.SelectedProfile = workspace.Items[0];
        Assert.Equal(3, workspace.SelectedProfile!.SelectedTabIndex);
        await autosave.CancelAndWaitAsync();
        workspace.Dispose();
    }

    [Fact]
    public async Task Profile_navigation_returns_from_settings_and_about_and_preserves_editor_state()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0];
        var second = new AutoClickerProfile { Name = "Second" };
        store.Data = store.Data with { Profiles = [first, second] };
        var (workspace, _, autosave) = CreateWorkspace(store: store);
        await workspace.InitializeAsync();
        var main = new MainWindowViewModel(workspace, new TestDialogs());
        workspace.SelectedProfile!.SelectedTabIndex = 2;

        main.ShowSettingsCommand.Execute(null);
        Assert.True(main.SettingsNavigationSelected);
        main.OpenProfile(workspace.SelectedProfile);
        Assert.True(main.ProfilesVisible);
        Assert.True(main.ProfilesNavigationSelected);
        Assert.Equal(2, workspace.SelectedProfile!.SelectedTabIndex);

        main.ShowAboutCommand.Execute(null);
        Assert.True(main.AboutNavigationSelected);
        main.OpenProfile(workspace.Items.Single(item => item.Id == second.Id));
        Assert.True(main.ProfilesNavigationSelected);
        Assert.Equal(second.Id, workspace.SelectedProfile!.Id);

        main.ShowSettingsCommand.Execute(null);
        main.ShowProfilesCommand.Execute(null);
        Assert.True(main.ProfilesVisible);
        main.Dispose(); workspace.Dispose(); await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Running_status_displays_measured_actual_cps()
    {
        var localization = new LocalizationService();
        var engine = new FakeEngine();
        var (workspace, _, autosave) = CreateWorkspace(engine: engine, localization: localization);
        await workspace.InitializeAsync();
        engine.PublishTelemetry(new EngineTelemetry(EngineState.Running, 760, 760, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Assert.Contains("760,0 CPS", workspace.EngineStatusText);
        localization.SetLanguage(ApplicationLanguage.English);
        Assert.Contains("760.0 CPS", workspace.EngineStatusText);
        await autosave.CancelAndWaitAsync(); workspace.Dispose();
    }

    [Fact]
    public void AdvancedOptionsRoundTripAndDurationUnitSwitchKeepsTheSameDuration()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile
        {
            StartDelayMilliseconds = 2500,
            ClickLimitEnabled = true,
            ClickLimit = 123456789,
            DurationLimitEnabled = true,
            DurationLimit = TimeSpan.FromSeconds(30),
            BurstEnabled = true,
            ClicksPerBurst = 7,
            BurstDelayMilliseconds = 1500
        });

        editor.DurationUnit = ProfileEditorViewModel.DurationUnitOption.Minutes;
        Assert.Equal(0.5m, editor.DurationAmount);
        editor.DurationAmount = 2;
        editor.StartDelaySeconds = 3.25m;
        editor.ClickLimit = 999;
        editor.ClicksPerBurst = 9;
        editor.BurstDelayMilliseconds = 750;

        Assert.True(editor.TryGetSnapshot(out var snapshot));
        Assert.Equal(3250, snapshot.StartDelayMilliseconds);
        Assert.Equal(999, snapshot.ClickLimit);
        Assert.Equal(TimeSpan.FromMinutes(2), snapshot.DurationLimit);
        Assert.Equal(9, snapshot.ClicksPerBurst);
        Assert.Equal(750, snapshot.BurstDelayMilliseconds);
    }

    [Fact]
    public void InvalidAdvancedNumbersAreRejected()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile());
        editor.ClickLimitEnabled = true;
        editor.ClickLimit = 0;
        Assert.False(editor.TryGetSnapshot(out _));
        editor.ClickLimit = 10;
        editor.DurationLimitEnabled = true;
        editor.DurationAmount = 0;
        Assert.False(editor.TryGetSnapshot(out _));
        editor.DurationAmount = 5;
        editor.BurstEnabled = true;
        editor.ClicksPerBurst = 0;
        Assert.False(editor.TryGetSnapshot(out _));
    }

    [Fact]
    public async Task ProductionStartRemainsDisabledWithoutNativeMouseInput()
    {
        var (workspace, _, autosave) = CreateWorkspace();
        await workspace.InitializeAsync();
        Assert.False(workspace.CanToggleEngine);
        Assert.False(workspace.ToggleEngineCommand.CanExecute(null));
        Assert.Contains("not used in tests", workspace.EngineStatusText);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task MacStartTracksMousePermissionWithoutDependingOnGlobalShortcutAvailability()
    {
        var permission = new MutablePermission { IsAllowed = false };
        var (workspace, _, autosave) = CreateWorkspace(nativeAvailable: true, permissions: permission);
        await workspace.InitializeAsync();
        Assert.False(workspace.CanToggleEngine);
        Assert.False(workspace.ToggleEngineCommand.CanExecute(null));

        permission.IsAllowed = true;
        await workspace.RefreshInputPermissionAsync();

        Assert.True(workspace.CanToggleEngine);
        Assert.True(workspace.ToggleEngineCommand.CanExecute(null));
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task ShowPermissionWarning_IsFalse_WhenMousePermissionIsGranted_EvenIfShortcutsCannotListen()
    {
        var permission = new MutablePermission { IsAllowed = true, CanListen = false };
        var (workspace, _, autosave) = CreateWorkspace(nativeAvailable: true, permissions: permission);
        await workspace.InitializeAsync();

        Assert.True(workspace.CanToggleEngine);
        Assert.False(workspace.ShowPermissionWarning);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task CanToggleEngine_RemainsTrue_WhileEngineIsBusy_ToAllowStop()
    {
        var permission = new MutablePermission { IsAllowed = true };
        var engine = new FakeEngine();
        var (workspace, _, autosave) = CreateWorkspace(nativeAvailable: true, permissions: permission, engine: engine);
        await workspace.InitializeAsync();
        Assert.True(workspace.CanToggleEngine);
        Assert.True(workspace.CanEdit);

        engine.PublishTelemetry(new EngineTelemetry(EngineState.Running, 10, 10, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Assert.True(workspace.EngineBusy);
        Assert.False(workspace.CanEdit);
        Assert.True(workspace.CanToggleEngine);
        Assert.True(workspace.ToggleEngineCommand.CanExecute(null));

        engine.PublishTelemetry(new EngineTelemetry(EngineState.Stopped, 10, 0, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Assert.False(workspace.EngineBusy);
        Assert.True(workspace.CanEdit);
        Assert.True(workspace.CanToggleEngine);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task MouseHold_starts_once_ignores_own_input_and_rearms_after_release()
    {
        var store = new RecordingStore();
        var sourceProfile = store.Data.Profiles[0] with { ClickMode = ClickMode.MouseHold, HoldMouseButton = MouseButton.Right, MouseButton = MouseButton.Right };
        store.Data = store.Data with { Profiles = [sourceProfile], DefaultProfileId = sourceProfile.Id, SelectedProfileId = sourceProfile.Id };
        var autosave = CreateAutosave(store); var engine = new FakeEngine(); var monitor = new FakeMouseMonitor(); var hotkeys = new FakeHotkeys();
        var workspace = new ProfilesViewModel(store, autosave, new SettingsViewModel(new TestThemeService()),
            new TestDialogs(), new TestFolders(), new TestPlatform(true), NullLogger<ProfilesViewModel>.Instance,
            engine: engine, hotkeys: hotkeys, mouseInputMonitor: monitor);
        await workspace.InitializeAsync();
        await workspace.RegisterHotkeysAsync();
        Assert.Equal(1, monitor.StartCalls);

        monitor.Raise(MouseButton.Right, true, generated: true);
        monitor.Raise(MouseButton.Right, true);
        monitor.Raise(MouseButton.Right, true); // duplicate down
        Assert.Equal(1, engine.StartCalls);
        Assert.True(engine.IsRunning);
        monitor.Raise(MouseButton.Right, false);
        Assert.Equal(1, engine.StopCalls);

        monitor.Raise(MouseButton.Right, true);
        Assert.Equal(2, engine.StartCalls);
        await workspace.StopEngineAsync(); // manual Stop disarms until the physical release
        monitor.Raise(MouseButton.Right, true); // a repeated down while still held cannot rearm
        Assert.Equal(2, engine.StartCalls);
        monitor.Raise(MouseButton.Right, false);
        Assert.Equal(2, engine.StopCalls);
        monitor.Raise(MouseButton.Right, true);
        Assert.Equal(3, engine.StartCalls);

        hotkeys.RaiseEmergencyStop();
        Assert.Equal(3, engine.StopCalls);
        monitor.Raise(MouseButton.Right, true); // still held: emergency stop remains latched
        Assert.Equal(3, engine.StartCalls);
        monitor.Raise(MouseButton.Right, false);
        monitor.Raise(MouseButton.Right, true);
        Assert.Equal(4, engine.StartCalls);

        await workspace.StopEngineAsync(); await workspace.DisposeEngineAsync(); await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Hold_mouse_hotkey_emergency_stop_profile_change_and_shutdown_release_the_button()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0] with { ClickMode = ClickMode.HoldMouseButton, MouseButton = MouseButton.Left };
        var second = new AutoClickerProfile { Name = "Second" };
        store.Data = store.Data with { Profiles = [first, second], DefaultProfileId = first.Id, SelectedProfileId = first.Id };
        var autosave = CreateAutosave(store); var hotkeys = new FakeHotkeys(); var mouse = new HoldMouse(); var engine = new FakeEngine();
        var statistics = new UsageStatisticsService(engine, store, NullLogger<UsageStatisticsService>.Instance);
        var hold = new MouseButtonHoldService(mouse, NullLogger<MouseButtonHoldService>.Instance);
        var workspace = new ProfilesViewModel(store, autosave, new SettingsViewModel(new TestThemeService()),
            new TestDialogs(), new TestFolders(), new TestPlatform(true), NullLogger<ProfilesViewModel>.Instance,
            engine: engine, hotkeys: hotkeys, mouseButtonHold: hold, usageStatistics: statistics);
        await workspace.InitializeAsync();
        await workspace.RegisterHotkeysAsync();

        hotkeys.RaiseHold(new Hotkey("F5"));
        await Eventually(() => hold.IsHeld);
        Assert.Equal(new[] { "down:Left" }, mouse.Events);
        Assert.Equal(0, mouse.ClickCount);
        await Task.Delay(20);
        Assert.Equal(0, statistics.Snapshot.TotalPhysicalClicks);
        Assert.Equal(TimeSpan.Zero, statistics.Snapshot.TotalActiveTime);

        hotkeys.RaiseEmergencyStop();
        await Eventually(() => !hold.IsHeld);
        Assert.Equal(new[] { "down:Left", "up:Left" }, mouse.Events);

        await workspace.ToggleEngineCommand.ExecuteAsync(null);
        Assert.True(hold.IsHeld);
        workspace.SelectedProfile = workspace.Items[1];
        Assert.False(hold.IsHeld);
        Assert.Equal("up:Left", mouse.Events[^1]);

        workspace.SelectedProfile = workspace.Items[0];
        await workspace.ToggleEngineCommand.ExecuteAsync(null);
        workspace.SelectedProfile.ClickMode = ClickMode.Toggle;
        Assert.False(hold.IsHeld);
        workspace.SelectedProfile.ClickMode = ClickMode.HoldMouseButton;
        await workspace.ToggleEngineCommand.ExecuteAsync(null);

        await workspace.DeleteCommand.ExecuteAsync(workspace.SelectedProfile);
        Assert.False(hold.IsHeld);
        Assert.Single(workspace.Items);
        workspace.SelectedProfile!.ClickMode = ClickMode.HoldMouseButton;
        await workspace.ToggleEngineCommand.ExecuteAsync(null);
        var main = new MainWindowViewModel(workspace, new TestDialogs());
        Assert.True(await main.PrepareToCloseAsync());
        Assert.False(hold.IsHeld);
        Assert.Equal(5, mouse.Events.Count(item => item == "up:Left"));
        statistics.Dispose(); await autosave.CancelAndWaitAsync();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Inline_rename_enter_commits_and_updates_workspace_and_tray_label()
    {
        var (workspace, store, autosave) = CreateWorkspace();
        await workspace.InitializeAsync();
        var profile = workspace.SelectedProfile!;
        var tray = new AvaloniaTrayService(new MainWindowViewModel(workspace, new TestDialogs()), () => { }, () => { });
        tray.Update();

        Assert.True(workspace.BeginRename(profile));
        profile.RenameText = "Renamed profile";
        Assert.True(workspace.CommitRename(profile)); // Enter and focus-lost use this same commit path.
        Assert.Equal("Renamed profile", profile.Name);
        Assert.Equal("Renamed profile", workspace.SelectedProfile!.Name);
        Assert.Equal("Profile: Renamed profile", tray.CurrentProfileLabel);
        Assert.True(await workspace.FlushAsync());
        Assert.Equal("Renamed profile", store.Data.Profiles.Single().Name);

        await workspace.InitializeAsync();
        Assert.Equal("Renamed profile", workspace.SelectedProfile!.Name);
        tray.Dispose(); await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Inline_rename_escape_empty_whitespace_and_case_insensitive_duplicate_are_rejected()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0];
        var second = new AutoClickerProfile { Name = "Game" };
        store.Data = store.Data with { Profiles = [first, second] };
        var (workspace, _, autosave) = CreateWorkspace(store: store);
        await workspace.InitializeAsync();
        var profile = workspace.Items[1];

        Assert.True(workspace.BeginRename(profile));
        profile.RenameText = "Unsaved";
        workspace.CancelRename(profile); // Escape restores the original name.
        Assert.Equal("Game", profile.Name);
        Assert.False(profile.IsRenaming);

        Assert.True(workspace.BeginRename(profile));
        profile.RenameText = "   ";
        Assert.False(workspace.CommitRename(profile));
        Assert.Equal("Game", profile.Name);
        Assert.Contains("between 1 and 80", profile.NameError);

        profile.RenameText = "DEFAULT";
        Assert.False(workspace.CommitRename(profile));
        Assert.Equal("Game", profile.Name);
        Assert.Contains("not case-sensitive", profile.NameError);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Reorder_commands_share_order_logic_persist_order_and_keep_identity_and_default()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0];
        var second = new AutoClickerProfile { Name = "Second" };
        var third = new AutoClickerProfile { Name = "Third" };
        store.Data = store.Data with { Profiles = [first, second, third], DefaultProfileId = first.Id, SelectedProfileId = second.Id };
        var (workspace, _, autosave) = CreateWorkspace(store: store);
        await workspace.InitializeAsync();
        var activeId = workspace.SelectedProfile!.Id;

        Assert.False(workspace.MoveUpCommand.CanExecute(workspace.Items[0]));
        Assert.False(workspace.MoveDownCommand.CanExecute(workspace.Items[2]));
        workspace.MoveUpCommand.Execute(workspace.Items[1]);
        Assert.Equal(new[] { second.Id, first.Id, third.Id }, workspace.Items.Select(item => item.Id));
        workspace.MoveDownCommand.Execute(workspace.Items.Single(item => item.Id == second.Id));
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, workspace.Items.Select(item => item.Id));
        workspace.MoveToBottomCommand.Execute(workspace.Items.Single(item => item.Id == first.Id));
        Assert.Equal(new[] { second.Id, third.Id, first.Id }, workspace.Items.Select(item => item.Id));
        workspace.MoveToTopCommand.Execute(workspace.Items.Single(item => item.Id == first.Id));
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, workspace.Items.Select(item => item.Id));
        Assert.Equal(activeId, workspace.SelectedProfile!.Id);
        Assert.True(workspace.Items.Single(item => item.Id == first.Id).IsDefault);

        Assert.True(await workspace.FlushAsync());
        await workspace.InitializeAsync();
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, workspace.Items.Select(item => item.Id));
        Assert.Equal(first.Id, store.Data.DefaultProfileId);
        Assert.Equal(activeId, workspace.SelectedProfile!.Id);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Drag_drop_order_logic_is_identity_based_and_rename_follows_profile_id()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0];
        var second = new AutoClickerProfile { Name = "Second" };
        var third = new AutoClickerProfile { Name = "Third" };
        store.Data = store.Data with { Profiles = [first, second, third] };
        var (workspace, _, autosave) = CreateWorkspace(store: store);
        await workspace.InitializeAsync();
        var profile = workspace.Items[0];
        Assert.True(workspace.BeginRename(profile));
        profile.RenameText = "Moved while editing";

        Assert.True(workspace.MoveProfileBefore(profile.Id, third.Id));
        Assert.Equal(profile.Id, workspace.Items[1].Id);
        Assert.True(profile.IsRenaming);
        Assert.True(workspace.CommitRename(profile));
        Assert.Equal("Moved while editing", workspace.Items[1].Name);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Reorder_is_allowed_while_engine_runs_without_switching_the_active_profile()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0];
        var second = new AutoClickerProfile { Name = "Second" };
        store.Data = store.Data with { Profiles = [first, second], SelectedProfileId = first.Id };
        var engine = new FakeEngine();
        var (workspace, _, autosave) = CreateWorkspace(engine: engine, store: store);
        await workspace.InitializeAsync();
        await engine.StartAsync(first);

        Assert.True(workspace.MoveProfileToIndex(workspace.Items[0], 1));
        Assert.Equal(first.Id, workspace.SelectedProfile!.Id);
        workspace.SelectedProfile = workspace.Items[1];
        Assert.Equal(first.Id, workspace.SelectedProfile!.Id);

        await engine.StopAsync(); await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Profile_color_persists_through_duplicate_rename_and_reorder()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0] with { ProfileColor = ProfileColor.Blue };
        var second = new AutoClickerProfile { Name = "Second", ProfileColor = ProfileColor.Pink };
        store.Data = store.Data with { Profiles = [first, second], SelectedProfileId = first.Id };
        var (workspace, _, autosave) = CreateWorkspace(store: store);
        await workspace.InitializeAsync();
        var selected = workspace.SelectedProfile!;

        workspace.DuplicateCommand.Execute(selected);
        var duplicate = workspace.SelectedProfile!;
        Assert.Equal(ProfileColor.Blue, duplicate.ProfileColor);
        Assert.True(workspace.BeginRename(duplicate));
        duplicate.RenameText = "Blue copy";
        Assert.True(workspace.CommitRename(duplicate));
        Assert.True(workspace.MoveProfileToIndex(duplicate, 0));
        Assert.Equal(ProfileColor.Blue, duplicate.ProfileColor);
        Assert.True(await workspace.FlushAsync());

        await workspace.InitializeAsync();
        Assert.Equal(ProfileColor.Blue, workspace.Items.Single(item => item.Name == "Blue copy").ProfileColor);
        Assert.Equal(ProfileColor.Pink, workspace.Items.Single(item => item.Name == "Second").ProfileColor);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task Repeated_profile_switches_do_not_multiply_engine_telemetry_subscriptions()
    {
        var store = new RecordingStore();
        var first = store.Data.Profiles[0]; var second = new AutoClickerProfile { Name = "Second" };
        store.Data = store.Data with { Profiles = [first, second] };
        var autosave = CreateAutosave(store); var engine = new FakeEngine();
        var workspace = new ProfilesViewModel(store, autosave, new SettingsViewModel(new TestThemeService()),
            new TestDialogs(), new TestFolders(), new TestPlatform(false), NullLogger<ProfilesViewModel>.Instance, engine: engine);
        await workspace.InitializeAsync();
        Assert.Equal(1, engine.TelemetryHandlerCount);
        for (var index = 0; index < 200; index++) workspace.SelectedProfile = workspace.Items[index % 2];
        Assert.Equal(1, engine.TelemetryHandlerCount);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task InvalidEditorChangesCannotBePersisted()
    {
        var (workspace, store, autosave) = CreateWorkspace();
        await workspace.InitializeAsync();
        workspace.SelectedProfile!.Cps = 0;
        Assert.False(await workspace.FlushAsync());
        Assert.Empty(store.Saves);
        workspace.SelectedProfile.Cps = 20;
        Assert.True(await workspace.FlushAsync());
        Assert.Equal(20, store.Saves.Last().Profiles[0].Cps);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task DuplicateProfileNamesBlockSavingUntilCorrected()
    {
        var (workspace, store, autosave) = CreateWorkspace();
        await workspace.InitializeAsync();
        workspace.NewProfileCommand.Execute(null);
        workspace.SelectedProfile!.Name = "default";
        Assert.False(await workspace.FlushAsync());
        Assert.NotEmpty(workspace.SelectedProfile.NameError);
        Assert.Empty(store.Saves);
        workspace.SelectedProfile.Name = "Custom";
        Assert.True(await workspace.FlushAsync());
        Assert.Equal(2, store.Saves.Last().Profiles.Count);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task ProfileCommandsPreserveAtLeastOneProfileAndCopySettings()
    {
        var (workspace, store, autosave) = CreateWorkspace();
        await workspace.InitializeAsync();
        var original = workspace.SelectedProfile!;
        Assert.False(workspace.DeleteCommand.CanExecute(original));
        original.Cps = 22;
        workspace.DuplicateCommand.Execute(original);
        var copy = workspace.SelectedProfile!;
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.Cps, copy.Cps);
        workspace.MakeDefaultCommand.Execute(copy);
        Assert.True(copy.IsDefault);
        Assert.False(original.IsDefault);
        await workspace.DeleteCommand.ExecuteAsync(original);
        Assert.Single(workspace.Items);
        Assert.True(await workspace.FlushAsync());
        Assert.Equal(copy.Id, store.Saves.Last().DefaultProfileId);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task RememberSelectionSettingChoosesDefaultWhenDisabled()
    {
        var (workspace, store, autosave) = CreateWorkspace();
        var original = store.Data.Profiles[0];
        var other = new AutoClickerProfile { Name = "Other" };
        store.Data = store.Data with
        {
            Profiles = [original, other], SelectedProfileId = other.Id,
            Settings = store.Data.Settings with { RememberLastSelectedProfile = false }
        };
        await workspace.InitializeAsync();
        Assert.Equal(original.Id, workspace.SelectedProfile!.Id);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task LoadErrorLeavesEditingDisabledAndDoesNotWriteDefaults()
    {
        var (workspace, store, autosave) = CreateWorkspace();
        store.LoadError = new PersistenceException("The existing file has been preserved.", new IOException("corrupt"));
        await workspace.InitializeAsync();
        Assert.False(workspace.CanEdit);
        Assert.True(workspace.HasError);
        Assert.Empty(store.Saves);
        Assert.True(await workspace.FlushAsync());
        Assert.Empty(store.Saves);
        await autosave.CancelAndWaitAsync();
    }

    [Fact]
    public async Task DebounceCoalescesRapidEditsIntoOneWrite()
    {
        var store = new RecordingStore();
        var autosave = CreateAutosave(store);
        for (var cps = 1; cps <= 20; cps++)
            autosave.Schedule(store.Data with { Profiles = [store.Data.Profiles[0] with { Cps = cps }] });
        await store.NextSave.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await autosave.CancelAndWaitAsync();
        Assert.Single(store.Saves);
        Assert.Equal(20, store.Saves.Single().Profiles[0].Cps);
    }

    [Fact]
    public async Task AutosaveCanBeRescheduledAfterItHasFinished()
    {
        var store = new RecordingStore();
        var autosave = CreateAutosave(store);
        autosave.Schedule(store.Data);
        await store.NextSave.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.NextSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        autosave.Schedule(store.Data with { Profiles = [store.Data.Profiles[0] with { Cps = 33 }] });
        await store.NextSave.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await autosave.CancelAndWaitAsync();
        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(33, store.Saves.Last().Profiles[0].Cps);
    }

    private static DebouncedSaveService CreateAutosave(RecordingStore store) => new(store, new InlineDispatcher(), NullLogger<DebouncedSaveService>.Instance);

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Condition was not met before timeout.");
            await Task.Delay(5);
        }
    }

    private static (ProfilesViewModel Workspace, RecordingStore Store, DebouncedSaveService Autosave) CreateWorkspace(
        bool nativeAvailable = false, IAccessibilityPermissionService? permissions = null, IAutoClickerEngine? engine = null,
        RecordingStore? store = null, LocalizationService? localization = null)
    {
        store ??= new RecordingStore();
        var autosave = CreateAutosave(store);
        var workspace = new ProfilesViewModel(store, autosave, new SettingsViewModel(new TestThemeService(), localization: localization),
            new TestDialogs(), new TestFolders(), new TestPlatform(nativeAvailable), NullLogger<ProfilesViewModel>.Instance,
            engine: engine, permissions: permissions, localization: localization);
        return (workspace, store, autosave);
    }

    private sealed class RecordingStore : IApplicationStore
    {
        public string ConfigurationDirectory => Path.GetTempPath();
        public ApplicationData Data { get; set; } = ApplicationData.CreateDefault();
        public Exception? LoadError { get; set; }
        public ConcurrentQueue<ApplicationData> Saves { get; } = [];
        public TaskCompletionSource<bool> NextSave { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ApplicationData> LoadAsync(CancellationToken cancellationToken = default) => LoadError is null ? Task.FromResult(Data) : Task.FromException<ApplicationData>(LoadError);
        public Task SaveAsync(ApplicationData data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saves.Enqueue(data); Data = data; NextSave.TrySetResult(true);
            return Task.CompletedTask;
        }
        public Task SaveLifetimeStatisticsAsync(LifetimeStatistics statistics, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Data = Data with { LifetimeStatistics = statistics }; return Task.CompletedTask; }
    }

    private sealed class InlineDispatcher : IUiDispatcher { public void Post(Action action) => action(); }
    private sealed class TestThemeService : IThemeService { public void Apply(Appearance appearance) { } }
    private sealed class TestDialogs : IDesktopDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => Task.FromResult(true);
    }
    private sealed class TestFolders : IConfigurationFolderService { public void Open(string directory) { } }
    private sealed class TestPlatform(bool nativeAvailable) : IPlatformInfo
    {
        public string Name => nativeAvailable ? "macOS" : "Test";
        public string ConfigurationDirectory => Path.GetTempPath();
        public bool NativeInputAvailable => nativeAvailable;
        public string NativeInputStatus => "Native input is not used in tests.";
    }
    private sealed class MutablePermission : IAccessibilityPermissionService
    {
        public bool IsAllowed { get; set; }
        public bool CanListen { get; set; } = true;
        public bool IsGranted => IsAllowed;
        public bool CanListenForShortcuts => CanListen;
        public string Status => IsAllowed ? "Granted" : "Accessibility permission is required.";
        public Task<bool> RequestAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsAllowed);
        public void OpenSystemSettings() { }
    }

    private sealed class FakeEngine : IAutoClickerEngine
    {
        public bool IsRunning { get; private set; }
        public EngineTelemetry Telemetry { get; private set; } = new(EngineState.Stopped, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int TelemetryHandlerCount => TelemetryChanged?.GetInvocationList().Length ?? 0;
        public event EventHandler<EngineTelemetryEventArgs>? TelemetryChanged;
        public event EventHandler<EngineErrorEventArgs>? Error;
        public Task StartAsync(AutoClickerProfile profile, CancellationToken cancellationToken = default)
        { StartCalls++; IsRunning = true; Telemetry = Telemetry with { State = EngineState.Running }; TelemetryChanged?.Invoke(this, new(Telemetry)); return Task.CompletedTask; }
        public Task StopAsync()
        { StopCalls++; IsRunning = false; Telemetry = Telemetry with { State = EngineState.Stopped }; TelemetryChanged?.Invoke(this, new(Telemetry)); return Task.CompletedTask; }
        public void PublishTelemetry(EngineTelemetry telemetry)
        { Telemetry = telemetry; IsRunning = telemetry.State != EngineState.Stopped; TelemetryChanged?.Invoke(this, new(telemetry)); }
        public void RaiseError(Exception exception) => Error?.Invoke(this, new(exception, exception.Message));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMouseMonitor : IMouseInputMonitor
    {
        public bool IsAvailable => true; public string Status => "Available"; public int StartCalls { get; private set; }
        public event EventHandler<MouseInputEventArgs>? MouseButtonChanged;
        public event EventHandler? EscapePressed;
        public Task<HotkeyRegistrationResult> StartMonitoringAsync(CancellationToken cancellationToken = default)
        { StartCalls++; return Task.FromResult(new HotkeyRegistrationResult(true)); }
        public void RaiseEscape() => EscapePressed?.Invoke(this, EventArgs.Empty);
        public void Raise(MouseButton button, bool pressed, bool generated = false) =>
            MouseButtonChanged?.Invoke(this, new MouseInputEventArgs(button, pressed, generated, new ScreenPoint(0, 0)));
    }

    private sealed class FakeHotkeys : IGlobalHotkeyService
    {
        public event EventHandler<HotkeyEventArgs>? Pressed;
        public event EventHandler<HotkeyEventArgs>? Released { add { } remove { } }
        public Task<HotkeyRegistrationResult> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(true));
        public Task<HotkeyRegistrationResult> RegisterHoldMouseButtonAsync(Hotkey hotkey, CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(true));
        public Task<HotkeyRegistrationResult> RegisterEmergencyStopAsync(Hotkey hotkey, CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(true));
        public void Unregister(Hotkey hotkey) { }
        public void UnregisterHoldMouseButton() { }
        public void UnregisterEmergencyStop() { }
        public void SetActionsSuspended(bool suspended) { }
        public void RaiseEmergencyStop() => Pressed?.Invoke(this, new HotkeyEventArgs(new Hotkey("F12"), isEmergencyStop: true));
        public void RaiseHold(Hotkey hotkey) => Pressed?.Invoke(this, new HotkeyEventArgs(hotkey, isHoldMouseButton: true));
        public void Dispose() { }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class HoldMouse : IMouseService
    {
        public List<string> Events { get; } = [];
        public int ClickCount { get; private set; }
        public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
        { ClickCount++; return Task.CompletedTask; }
        public Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
        { ClickCount += 2; return Task.CompletedTask; }
        public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default)
        { Events.Add($"down:{button}"); return Task.CompletedTask; }
        public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default)
        { Events.Add($"up:{button}"); return Task.CompletedTask; }
    }
}

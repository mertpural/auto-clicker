using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AutoClicker.App.Services;
using AutoClicker.App.ViewModels;
using AutoClicker.App.Views;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using AutoClicker.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ClickMode = AutoClicker.Core.Models.ClickMode;

[assembly: AvaloniaTestApplication(typeof(AutoClicker.Tests.DesktopTestApplication))]

namespace AutoClicker.Tests;

public static class DesktopTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AutoClicker.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class DesktopUiTests
{
    [AvaloniaFact]
    public async Task Sidebar_clicks_return_from_settings_and_about()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        var profiles = fixture.Main.Profiles;
        profiles.NewProfileCommand.Execute(null);
        fixture.Pump();
        var list = fixture.Window.FindControl<ListBox>("ProfilesList")!;
        foreach (var page in new[] { NavigationPage.Settings, NavigationPage.About })
        {
            foreach (var profile in profiles.Items)
            {
                fixture.Main.Page = page; fixture.Pump();
                var container = (Control)list.ContainerFromItem(profile)!;
                Click(fixture, container.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == profile.Name));
                Assert.Equal(NavigationPage.Profiles, fixture.Main.Page);
                Assert.Same(profile, profiles.SelectedProfile);
                var settingsButton = fixture.Window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                    .Single(button => ReferenceEquals(button.Command, fixture.Main.ShowSettingsCommand));
                Assert.False(settingsButton.IsChecked);
            }
            fixture.Main.Page = page; fixture.Pump();
            var navigation = fixture.Window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .Single(button => ReferenceEquals(button.Command, fixture.Main.ShowProfilesCommand));
            Click(fixture, navigation);
            Assert.Equal(NavigationPage.Profiles, fixture.Main.Page);
        }
    }

    private static void Click(UiWorkspace fixture, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        fixture.Window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        fixture.Pump();
    }

    [AvaloniaFact]
    public async Task Language_selector_round_trips_through_actual_bindings()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        fixture.Main.ShowSettingsCommand.Execute(null);
        fixture.Pump();
        var settings = fixture.Window.GetVisualDescendants().OfType<SettingsView>().Single();
        var selector = settings.GetVisualDescendants().OfType<ComboBox>().First();
        var original = fixture.Main.Profiles.SelectedProfile!;
        var order = fixture.Main.Profiles.Items.Select(item => item.Id).ToArray();
        fixture.Main.Settings.Appearance = Appearance.CrimsonGold;
        original.Cps = 83; original.MouseButton = MouseButton.Right; original.ClickType = ClickType.Double;
        original.ProfileColor = ProfileColor.Teal; original.SelectedTabIndex = 2;
        Assert.True(original.TryGetSnapshot(out var snapshot));
        foreach (var language in new[] { ApplicationLanguage.English, ApplicationLanguage.Turkish,
                     ApplicationLanguage.English, ApplicationLanguage.Turkish })
        {
            selector.SelectedItem = selector.Items.OfType<LanguageOption>().Single(option => option.Value == language);
            fixture.Pump();
            Assert.Equal(language, fixture.Localization.Language);
            Assert.Equal(language, fixture.Main.Settings.Language);
            Assert.Equal(language, Assert.IsType<LanguageOption>(selector.SelectedItem).Value);
            Assert.Equal(language == ApplicationLanguage.Turkish ? "Ayarlar" : "Settings",
                settings.GetVisualDescendants().OfType<TextBlock>().First().Text);
            var sidebar = fixture.Window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, fixture.Main.ShowProfilesCommand));
            Assert.Equal(fixture.Localization.Get("Ui_Profiles"), sidebar.Content);
            Assert.Equal(new[] { "Tab_General", "Tab_Advanced", "Tab_Position", "Tab_Statistics" }.Select(key => fixture.Localization.Get(key)),
                fixture.Window.GetVisualDescendants().OfType<TabItem>().Select(item => item.Header));
            Assert.Contains(selector.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == fixture.Main.Settings.SelectedLanguageOption.Label);
            var primary = fixture.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("primary"));
            Assert.Equal(fixture.Localization.Get("Engine_Start"), primary.Content);
            Assert.Contains(fixture.Window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == fixture.Localization.Get("Status_Ready"));
            Assert.Same(original, fixture.Main.Profiles.SelectedProfile);
            Assert.Equal(order, fixture.Main.Profiles.Items.Select(item => item.Id));
            Assert.True(original.TryGetSnapshot(out var current)); Assert.Equal(snapshot, current);
            Assert.Equal(Appearance.CrimsonGold, fixture.Main.Settings.Appearance);
            Assert.Equal(2, original.SelectedTabIndex);
            Assert.Equal(fixture.Localization.Get("Enum_MouseButton_Right"), original.SelectedMouseButtonOption.Label);
        }
        // The reverse starting language uses the same living view and selection objects.
        foreach (var language in new[] { ApplicationLanguage.English, ApplicationLanguage.Turkish, ApplicationLanguage.English })
        {
            selector.SelectedItem = fixture.Main.Settings.LanguageOptions.Single(option => option.Value == language);
            fixture.Pump(); Assert.Equal(language, fixture.Localization.Language);
        }
    }

    [AvaloniaFact]
    public async Task Numeric_controls_follow_language_without_changing_values()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        var editor = fixture.Window.GetVisualDescendants().OfType<ProfileEditorView>().Single();
        var interval = editor.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Value == 1000m / 12m);
        foreach (var language in new[] { ApplicationLanguage.English, ApplicationLanguage.Turkish, ApplicationLanguage.English })
        {
            fixture.Main.Settings.Language = language; fixture.Pump();
            Assert.Equal(1000m / 12m, interval.Value);
            Assert.Contains(language == ApplicationLanguage.English ? "." : ",", interval.Text);
            Assert.Equal(12m, fixture.Main.Profiles.SelectedProfile!.Cps);
        }
    }

    [AvaloniaFact]
    public async Task Drag_preview_cancel_and_drop_preserve_selection_and_write_once()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        var profiles = fixture.Main.Profiles;
        profiles.NewProfileCommand.Execute(null); profiles.NewProfileCommand.Execute(null); fixture.Pump();
        await fixture.Autosave.CancelAndWaitAsync();
        var selected = profiles.SelectedProfile!;
        var source = profiles.Items[0]; var target = profiles.Items[^1];
        var order = profiles.Items.Select(profile => profile.Id).ToArray();
        var saves = fixture.Store.SaveCount;
        Assert.True(profiles.BeginProfileDrag(source));
        var changes = 0; target.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(target.IsDropTarget)) changes++; };
        for (var i = 0; i < 50; i++) profiles.SetDropTarget(target.Id, true);
        Assert.Equal(1, changes); Assert.Equal(0.76, source.DragOpacity);
        Assert.Equal(order, profiles.Items.Select(profile => profile.Id));
        Assert.Same(selected, profiles.SelectedProfile); Assert.Equal(saves, fixture.Store.SaveCount);
        profiles.CancelProfileDrag();
        Assert.Equal(1, source.DragOpacity); Assert.False(target.IsDropTarget);
        Assert.False(profiles.DropProfile(target.Id, true));
        Assert.Equal(order, profiles.Items.Select(profile => profile.Id));
        profiles.BeginRename(source); Assert.False(profiles.BeginProfileDrag(source)); profiles.CancelRename(source);
        Assert.True(profiles.BeginProfileDrag(source));
        Assert.True(profiles.DropProfile(target.Id, true)); fixture.Pump();
        Assert.Same(source, profiles.Items[^1]); Assert.Same(selected, profiles.SelectedProfile);
        Assert.True(source.IsDefault); Assert.Equal(1, source.DragOpacity);
        await fixture.Store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(saves + 1, fixture.Store.SaveCount);
        Assert.Equal(profiles.Items.Select(profile => profile.Id), fixture.Store.Data.Profiles.Select(profile => profile.Id));
    }

    [AvaloniaFact]
    public async Task Pressing_drag_handle_without_crossing_threshold_never_selects_profile()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        fixture.Main.Profiles.NewProfileCommand.Execute(null); fixture.Pump();
        var selected = fixture.Main.Profiles.SelectedProfile;
        var list = fixture.Window.FindControl<ListBox>("ProfilesList")!;
        var container = (Control)list.ContainerFromItem(fixture.Main.Profiles.Items[0])!;
        var handle = container.GetVisualDescendants().OfType<Control>().Single(text => text.Name == "ProfileDragHandle");
        var point = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        Assert.Same(selected, fixture.Main.Profiles.SelectedProfile);
        fixture.Window.MouseMove(point + new Vector(2, 2));
        Assert.Same(selected, fixture.Main.Profiles.SelectedProfile);
        fixture.Window.MouseUp(point, Avalonia.Input.MouseButton.Left); fixture.Pump();
        Assert.Same(selected, fixture.Main.Profiles.SelectedProfile);
        Assert.False(fixture.Main.Profiles.Items[0].IsDragging);
    }

    [AvaloniaFact]
    public async Task Inline_editor_handles_enter_escape_and_focus_loss()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        var profile = fixture.Main.Profiles.SelectedProfile!;
        var list = fixture.Window.FindControl<ListBox>("ProfilesList")!;
        var container = (Control)list.ContainerFromItem(profile)!;
        var editor = container.GetVisualDescendants().OfType<TextBox>().Single(text => text.Name == "InlineProfileName");
        fixture.Main.Profiles.BeginRename(profile); fixture.Pump(); editor.Focus(); editor.Text = "Enter name";
        fixture.Window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None); fixture.Window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None); fixture.Pump();
        Assert.Equal("Enter name", profile.Name); Assert.False(profile.IsRenaming);
        fixture.Main.Profiles.BeginRename(profile); fixture.Pump(); editor.Focus(); editor.Text = "Discard this";
        fixture.Window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None); fixture.Window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None); fixture.Pump();
        Assert.Equal("Enter name", profile.Name); Assert.False(profile.IsRenaming);
        fixture.Main.Profiles.BeginRename(profile); fixture.Pump(); editor.Focus(); editor.Text = "Focus name";
        var navigation = fixture.Window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .Single(button => ReferenceEquals(button.Command, fixture.Main.ShowSettingsCommand));
        Assert.True(navigation.Focus()); fixture.Pump(); Assert.Equal("Focus name", profile.Name); Assert.False(profile.IsRenaming);
    }

    [AvaloniaFact]
    public async Task Theme_resources_resolve_including_system_without_changing_language()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        var keys = ThemeService.GetPalette(Appearance.Night).Keys.Concat(new[] { "NavigationAccentBrush", "ControlAccentColor" });
        foreach (var appearance in fixture.Main.Settings.Appearances)
        {
            fixture.Main.Settings.Appearance = appearance; fixture.Pump();
            foreach (var key in keys) Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out _), key);
            Assert.Equal(ApplicationLanguage.Turkish, fixture.Localization.Language);
        }
    }

    [AvaloniaFact]
    public async Task Language_binding_preserves_running_engine_and_held_mouse()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        fixture.Main.ShowSettingsCommand.Execute(null); fixture.Pump();
        var selector = fixture.Window.GetVisualDescendants().OfType<SettingsView>().Single()
            .GetVisualDescendants().OfType<ComboBox>().Single(control => control.Items.OfType<LanguageOption>().Any());
        async Task SwitchBothWays()
        {
            foreach (var language in new[] { ApplicationLanguage.English, ApplicationLanguage.Turkish })
            { selector.SelectedItem = fixture.Main.Settings.LanguageOptions.Single(option => option.Value == language); fixture.Pump(); await Task.Delay(10); }
        }
        var profile = fixture.Main.Profiles.SelectedProfile!;
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.TelemetryChanged += (_, args) => { if (args.Telemetry.State == EngineState.Running) running.TrySetResult(); };
        await fixture.Engine.StartAsync(new AutoClickerProfile()); await running.Task.WaitAsync(TimeSpan.FromSeconds(3)); fixture.Pump();
        await SwitchBothWays(); Assert.Equal(EngineState.Running, fixture.Engine.Telemetry.State);
        await fixture.Engine.StopAsync(); fixture.Pump();
        profile.ClickMode = ClickMode.HoldMouseButton;
        await fixture.Main.Profiles.ToggleEngineCommand.ExecuteAsync(null); fixture.Pump();
        Assert.True(fixture.Hold.IsHeld); Assert.Equal(1, fixture.Mouse.DownCount);
        await SwitchBothWays();
        Assert.True(fixture.Hold.IsHeld); Assert.Equal(0, fixture.Mouse.UpCount);
        Assert.Equal(ClickMode.HoldMouseButton, profile.ClickMode);
        await fixture.Main.Profiles.StopEngineAsync(); Assert.False(fixture.Hold.IsHeld); Assert.Equal(1, fixture.Mouse.UpCount);
    }

    [AvaloniaFact]
    public async Task Reinitialization_does_not_multiply_subscriptions_and_disposal_detaches_them()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        static int Count(object owner, string field) => ((Delegate?)owner.GetType()
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner))?.GetInvocationList().Length ?? 0;
        var localizationCount = Count(fixture.Localization, "LanguageChanged");
        var engineCount = Count(fixture.Engine, "TelemetryChanged");
        for (var i = 0; i < 5; i++) await fixture.Main.Profiles.InitializeAsync();
        Assert.Equal(localizationCount, Count(fixture.Localization, "LanguageChanged"));
        Assert.Equal(engineCount, Count(fixture.Engine, "TelemetryChanged"));
        Assert.Equal(1, Count(fixture.Autosave, "Completed"));
        fixture.Main.Dispose(); fixture.Main.Profiles.Dispose(); fixture.Main.Settings.Dispose();
        Assert.Equal(0, Count(fixture.Localization, "LanguageChanged"));
        Assert.Equal(0, Count(fixture.Engine, "TelemetryChanged")); Assert.Equal(0, Count(fixture.Engine, "Error"));
        Assert.Equal(0, Count(fixture.Autosave, "Completed")); Assert.Equal(0, Count(fixture.Hold, "Changed"));
    }

    [AvaloniaFact]
    public async Task Repeated_tray_initialization_does_not_duplicate_language_subscription()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        static int Count(LocalizationService service) => ((Delegate?)typeof(LocalizationService)
            .GetField("LanguageChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(service))?.GetInvocationList().Length ?? 0;
        var before = Count(fixture.Localization);
        using var tray = new AvaloniaTrayService(fixture.Main, () => { }, () => { }, fixture.Localization);
        Assert.True(tray.Initialize(Application.Current!));
        Assert.True(tray.Initialize(Application.Current!));
        Assert.Equal(before + 1, Count(fixture.Localization));
        fixture.Main.Settings.Language = ApplicationLanguage.English;
        Assert.StartsWith("Profile:", tray.CurrentProfileLabel);
        fixture.Main.Settings.Language = ApplicationLanguage.Turkish;
        Assert.StartsWith("Profil:", tray.CurrentProfileLabel);
        tray.Dispose(); Assert.Equal(before, Count(fixture.Localization));
    }

    [AvaloniaFact]
    public async Task Responsive_viewports_keep_action_button_and_tabs_in_bounds()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        foreach (var size in new[] { new Size(980, 760), new Size(1000, 800), new Size(1200, 800), new Size(1920, 1080) })
        {
            fixture.Window.Width = size.Width; fixture.Window.Height = size.Height; fixture.Pump();
            foreach (var index in Enumerable.Range(0, 4))
            {
                fixture.Main.Profiles.SelectedProfile!.SelectedTabIndex = index; fixture.Pump();
                var primary = fixture.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("primary"));
                var location = primary.TranslatePoint(default, fixture.Window)!.Value;
                Assert.InRange(location.X + primary.Bounds.Width, 0, size.Width);
                Assert.InRange(location.Y + primary.Bounds.Height, 0, size.Height);
                foreach (var scroll in fixture.Window.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(control => control.IsEffectivelyVisible && control.GetVisualAncestors().All(parent => parent is not NumericUpDown)))
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"Horizontal overflow at {size}, tab {index}: {scroll.Extent}/{scroll.Viewport}");
            }
        }
    }

    [AvaloniaFact]
    public async Task Menu_bar_popup_shows_profile_list_start_button_and_statistics_and_toggles_closed()
    {
        await using var fixture = await UiWorkspace.CreateAsync();
        using var popup = new MenuBarPopupService(fixture.Main, () => { }, () => { }, null);
        popup.Show(); fixture.Pump();
        Assert.True(popup.IsVisible);
        popup.Toggle(); fixture.Pump();
        Assert.False(popup.IsVisible);
    }

    private sealed class UiWorkspace : IAsyncDisposable
    {
        public MainWindow Window { get; }
        public MainWindowViewModel Main { get; }
        public LocalizationService Localization { get; }
        public readonly DebouncedSaveService Autosave;
        public readonly MemoryStore Store;
        public readonly AutoClickerEngine Engine;
        public readonly MouseButtonHoldService Hold;
        public readonly RecordingMouse Mouse;

        private UiWorkspace(MainWindow window, MainWindowViewModel main, LocalizationService localization, DebouncedSaveService autosave,
            MemoryStore store, AutoClickerEngine engine, MouseButtonHoldService hold, RecordingMouse mouse)
        { Window = window; Main = main; Localization = localization; Autosave = autosave; Store = store; Engine = engine; Hold = hold; Mouse = mouse; }

        public static async Task<UiWorkspace> CreateAsync()
        {
            var store = new MemoryStore();
            var localization = new LocalizationService();
            localization.Initialize(Application.Current!);
            var dispatcher = new UiDispatcher();
            var autosave = new DebouncedSaveService(store, dispatcher, NullLogger<DebouncedSaveService>.Instance);
            var settings = new SettingsViewModel(new ThemeService(), localization: localization);
            var dialogs = new Dialogs();
            var mouse = new RecordingMouse();
            var engine = new AutoClickerEngine(mouse);
            var hold = new MouseButtonHoldService(mouse, NullLogger<MouseButtonHoldService>.Instance, localization);
            var profiles = new ProfilesViewModel(store, autosave, settings, dialogs, new Folders(), new Platform(),
                NullLogger<ProfilesViewModel>.Instance, engine: engine, dispatcher: dispatcher, localization: localization, mouseButtonHold: hold);
            await profiles.InitializeAsync();
            var main = new MainWindowViewModel(profiles, dialogs, localization);
            var window = new MainWindow { DataContext = main };
            window.Show();
            var fixture = new UiWorkspace(window, main, localization, autosave, store, engine, hold, mouse);
            fixture.Pump();
            return fixture;
        }

        public void Pump() { Dispatcher.UIThread.RunJobs(); Window.UpdateLayout(); }
        public async ValueTask DisposeAsync()
        { await Engine.StopAsync(); await Hold.ReleaseIfHeldAsync(); Window.Close(); Main.Dispose(); Main.Profiles.Dispose(); Main.Settings.Dispose(); await Autosave.CancelAndWaitAsync(); await Engine.DisposeAsync(); }
    }

    private sealed class MemoryStore : IApplicationStore
    {
        private ApplicationData _data = ApplicationData.CreateDefault();
        public ApplicationData Data => _data;
        public int SaveCount { get; private set; }
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ConfigurationDirectory => "QA";
        public Task<ApplicationData> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_data);
        public Task SaveAsync(ApplicationData data, CancellationToken cancellationToken = default) { _data = data; SaveCount++; Saved.TrySetResult(); return Task.CompletedTask; }
        public Task SaveLifetimeStatisticsAsync(LifetimeStatistics statistics, CancellationToken cancellationToken = default)
        { _data = _data with { LifetimeStatistics = statistics }; return Task.CompletedTask; }
    }
    private sealed class Dialogs : IDesktopDialogService
    { public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => Task.FromResult(true); }
    private sealed class Folders : IConfigurationFolderService { public void Open(string directory) { } }
    private sealed class Platform : IPlatformInfo
    {
        public string Name => "QA"; public string ConfigurationDirectory => "QA";
        public bool NativeInputAvailable => true; public string NativeInputStatus => "QA";
    }

    private sealed class RecordingMouse : IMouseService
    {
        public int DownCount { get; private set; } public int UpCount { get; private set; }
        public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default) { DownCount++; return Task.CompletedTask; }
        public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default) { UpCount++; return Task.CompletedTask; }
    }
}



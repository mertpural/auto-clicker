using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using AutoClicker.App.ViewModels;
using AutoClicker.App.Views;
using AutoClicker.Core.Models;

namespace AutoClicker.App.Services;

internal static class DesktopControlSmoke
{
    public static async Task RunAsync(MainWindow window, MainWindowViewModel main, LocalizationService localization,
        AvaloniaTrayService? tray, string path)
    {
        var steps = new List<object>();
        Button ButtonFor(System.Windows.Input.ICommand command) => window.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, command));
        async Task Click(System.Windows.Input.ICommand command)
        { new ButtonAutomationPeer(ButtonFor(command)).Invoke(); await Task.Delay(30); window.UpdateLayout(); }
        void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
        void CheckPage(NavigationPage page)
        {
            Require(main.Page == page, "Native navigation page differs from expected page.");
            Require(((ToggleButton)ButtonFor(main.ShowSettingsCommand)).IsChecked == (page == NavigationPage.Settings), "Settings visual selection is stale.");
            Require(((ToggleButton)ButtonFor(main.ShowProfilesCommand)).IsChecked == (page == NavigationPage.Profiles), "Profiles visual selection is stale.");
            Require(window.FindControl<ProfileEditorView>("EditorView")!.IsEffectivelyVisible == (page == NavigationPage.Profiles), "Native workspace visibility is stale.");
        }
        Require(localization.Language == ApplicationLanguage.Turkish, "Fresh native smoke must start Turkish.");
        await Click(main.Profiles.NewProfileCommand);
        var selected = main.Profiles.SelectedProfile!;
        var order = main.Profiles.Items.Select(item => item.Id).ToArray();
        Require(selected.TryGetSnapshot(out var profileSnapshot), "Initial profile invalid.");
        await Click(main.ShowSettingsCommand); CheckPage(NavigationPage.Settings);
        await Click(main.ShowProfilesCommand); CheckPage(NavigationPage.Profiles);
        steps.Add(new { navigation = "Settings -> Profiles", passed = true });
        async Task ClickProfile(ProfileEditorViewModel profile)
        {
            var list = window.FindControl<ListBox>("ProfilesList")!;
            var item = (Control)list.ContainerFromItem(profile)!;
            var text = item.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == profile.Name);
            var point = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), window)!.Value;
            using var pointer = new Pointer(1, PointerType.Mouse, true);
            text.RaiseEvent(new PointerPressedEventArgs(text, pointer, window, point, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            await Task.Delay(30); window.UpdateLayout();
            Require(ReferenceEquals(profile, main.Profiles.SelectedProfile), "Native profile pointer event did not select expected profile.");
            CheckPage(NavigationPage.Profiles);
        }
        foreach (var profile in main.Profiles.Items)
        {
            await Click(main.ShowSettingsCommand); await ClickProfile(profile);
            steps.Add(new { navigation = "Settings -> profile", sameProfile = ReferenceEquals(profile, selected), passed = true });
            await Click(main.ShowAboutCommand); await ClickProfile(profile);
            steps.Add(new { navigation = "About -> profile", passed = true });
        }
        await ClickProfile(selected); await Click(main.ShowSettingsCommand);
        var settings = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var selector = settings.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Items.OfType<LanguageOption>().Any());
        var appearance = main.Settings.Appearance;
        selected.SelectedTabIndex = 2;
        foreach (var language in new[] { ApplicationLanguage.English, ApplicationLanguage.Turkish, ApplicationLanguage.English, ApplicationLanguage.Turkish })
        {
            selector.SelectedItem = main.Settings.LanguageOptions.Single(option => option.Value == language);
            await Task.Delay(30); window.UpdateLayout();
            Require(localization.Language == language && main.Settings.Language == language, "Native language binding did not round-trip.");
            Require(selector.SelectedItem is LanguageOption option && option.Value == language, "Native language selection is stale.");
            Require(selector.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == main.Settings.SelectedLanguageOption.Label), "Native ComboBox display is stale.");
            Require(settings.GetVisualDescendants().OfType<TextBlock>().First().Text == localization.Get("Ui_Settings"), "Native Settings header is stale.");
            Require(Equals(ButtonFor(main.ShowProfilesCommand).Content, localization.Get("Ui_Profiles")), "Native sidebar is stale.");
            var headers = window.GetVisualDescendants().OfType<TabItem>().Select(item => item.Header?.ToString()).ToArray();
            Require(headers.SequenceEqual(new[] { "Tab_General", "Tab_Advanced", "Tab_Position", "Tab_Statistics" }.Select(key => localization.Get(key))), "Native tab labels are stale.");
            Require(Equals(ButtonFor(main.Profiles.ToggleEngineCommand).Content, localization.Get("Engine_Start")), "Native action bar is stale.");
            Require(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == localization.Get("Status_Ready")), "Native profile status is stale.");
            Require(selected.SelectedPositionModeOption.Label == localization.Get("Enum_Position_CurrentCursor"), "Native enum label is stale.");
            Require(selected.TryGetSnapshot(out var current) && current == profileSnapshot, "Native language switch changed profile data.");
            Require(ReferenceEquals(selected, main.Profiles.SelectedProfile) && order.SequenceEqual(main.Profiles.Items.Select(item => item.Id)), "Native language switch changed identity/order.");
            Require(appearance == main.Settings.Appearance, "Native language switch changed theme.");
            Require(selected.SelectedTabIndex == 2, "Native language switch changed selected tab.");
            if (tray is not null)
            {
                Require(tray.MenuLabels[2] == localization.Get("Engine_Start") && tray.MenuLabels[3] == localization.Get("Tray_Open")
                    && tray.MenuLabels[4] == localization.Get("Tray_Quit"), "Native tray labels are stale.");
            }
            steps.Add(new { language = language.ToString(), display = main.Settings.SelectedLanguageOption.Label, passed = true, trayLabels = tray?.MenuLabels });
        }
        await Click(main.ShowProfilesCommand); CheckPage(NavigationPage.Profiles);
        await main.Profiles.FlushAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { mode = "native-control-peers-routed-pointer-fake-mouse", passed = true,
            trayChecked = tray is not null, steps }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

using AutoClicker.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoClicker.App.ViewModels;

public enum NavigationPage { Profiles, Settings, About }

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IDesktopDialogService dialogs;
    private readonly LocalizationService? localization;
    public ProfilesViewModel Profiles { get; }

    public MainWindowViewModel(ProfilesViewModel profiles, IDesktopDialogService dialogs, LocalizationService? localization = null)
    { Profiles = profiles; this.dialogs = dialogs; this.localization = localization; if (localization is not null) localization.LanguageChanged += OnLanguageChanged; }
    public SettingsViewModel Settings => Profiles.Settings;
    [ObservableProperty] private NavigationPage _page = NavigationPage.Profiles;
    public bool ProfilesVisible => Page == NavigationPage.Profiles;
    public bool SettingsVisible => Page == NavigationPage.Settings;
    public bool AboutVisible => Page == NavigationPage.About;
    public bool ProfilesNavigationSelected => ProfilesVisible;
    public bool SettingsNavigationSelected => SettingsVisible;
    public bool AboutNavigationSelected => AboutVisible;
    public string Version => localization?.Get("About_Version", typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3))
        ?? $"Version {typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3)}";

    private void OnLanguageChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(Version));

    partial void OnPageChanged(NavigationPage value)
    {
        if (Profiles.SelectedProfile is { } editor) editor.IsRecording = false;
        OnPropertyChanged(nameof(ProfilesVisible)); OnPropertyChanged(nameof(SettingsVisible)); OnPropertyChanged(nameof(AboutVisible));
        OnPropertyChanged(nameof(ProfilesNavigationSelected)); OnPropertyChanged(nameof(SettingsNavigationSelected));
        OnPropertyChanged(nameof(AboutNavigationSelected));
    }

    [RelayCommand] private void ShowProfiles() => Page = NavigationPage.Profiles;
    [RelayCommand] private void ShowSettings() => Page = NavigationPage.Settings;
    [RelayCommand] private void ShowAbout() => Page = NavigationPage.About;

    public void OpenProfile(ProfileEditorViewModel profile)
    {
        if (!Profiles.Items.Contains(profile)) return;
        Profiles.SelectedProfile = profile;
        Page = NavigationPage.Profiles;
    }

    public async Task<bool> PrepareToCloseAsync()
    {
        Profiles.IsShuttingDown = true;
        await Profiles.StopEngineAsync();
        if (Profiles.IsMouseButtonHeld)
        {
            Profiles.IsShuttingDown = false;
            await Profiles.RegisterHotkeysAsync();
            Profiles.ReportError(new InvalidOperationException("The native mouse button remains held."),
                localization?.Get("Error_UnsavedMouseHeld") ?? "The mouse button could not be released. AutoClicker will stay open so you can retry Emergency Stop.");
            return false;
        }
        await Profiles.FlushStatisticsAsync();
        Profiles.UnregisterHotkeys();
        var saved = await Profiles.FlushAsync();
        var exit = saved || await dialogs.ConfirmAsync(localization?.Get("Dialog_Unsaved_Title") ?? "Unsaved changes",
            localization?.Get("Dialog_Unsaved_Message") ?? "Some edits are invalid or could not be saved. Exit and discard unsaved changes?",
            localization?.Get("Common_Exit") ?? "Exit");
        if (exit)
        {
            await Profiles.DisposeHotkeysAsync();
            await Profiles.DisposeEngineAsync();
        }
        else
        {
            Profiles.IsShuttingDown = false;
            await Profiles.RegisterHotkeysAsync();
        }
        return exit;
    }

    public void Dispose() { if (localization is not null) localization.LanguageChanged -= OnLanguageChanged; }
}

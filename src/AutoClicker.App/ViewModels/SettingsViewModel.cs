using System.ComponentModel;
using AutoClicker.App.Services;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService themeService;
    private readonly IStartupRegistrationService? startupRegistration;
    private readonly ILogger<SettingsViewModel>? logger;
    private readonly UsageStatisticsService? usageStatistics;
    private readonly IDesktopDialogService? dialogs;
    private readonly IUiDispatcher? dispatcher;
    private readonly LocalizationService? localization;
    private ApplicationSettings _original = new();
    private bool _loading;
    public event Action? Changed;
    // Design themes first (default Gece Neon), then the classic colour themes.
    public Appearance[] Appearances { get; } =
    [
        Appearance.GeceNeon, Appearance.SadeBeyaz, Appearance.AuroraCam, Appearance.Terminal, Appearance.OyunHud,
        Appearance.System, Appearance.Night, Appearance.CrimsonGold, Appearance.Graphite, Appearance.Light
    ];
    public LocalizedEnumOption<Appearance>[] AppearanceOptions { get; }
    public LocalizedEnumOption<Appearance> SelectedAppearanceOption
    { get => AppearanceOptions.First(option => option.Value == Appearance); set { if (value is not null) Appearance = value.Value; } }
    public bool IsNightTheme => Appearance == Appearance.Night;
    public bool IsCrimsonGoldTheme => Appearance == Appearance.CrimsonGold;
    public bool IsGraphiteTheme => Appearance == Appearance.Graphite;
    public bool IsLightTheme => Appearance == Appearance.Light;
    public bool IsSystemTheme => Appearance == Appearance.System;
    public bool IsDesignTheme => Appearance is Appearance.GeceNeon or Appearance.SadeBeyaz or Appearance.AuroraCam or Appearance.Terminal or Appearance.OyunHud;
    public CloseBehavior[] CloseBehaviors { get; } = Enum.GetValues<CloseBehavior>();
    public LanguageOption[] LanguageOptions { get; }
    public LanguageOption SelectedLanguageOption
    {
        get => LanguageOptions.First(option => option.Value == Language);
        set { if (value is not null) Language = value.Value; }
    }
    public bool StartupRegistrationAvailable => startupRegistration?.IsAvailable == true;
    public string StartupRegistrationStatus => localization?.TranslateKnownTextOrFallback(startupRegistration?.Status ?? localization.Get("Ui_LaunchUnavailable"), "Ui_LaunchStatusUnavailable")
        ?? startupRegistration?.Status ?? "Launch at startup is unavailable.";
    [ObservableProperty] private Appearance _appearance;
    [ObservableProperty] private ApplicationLanguage _language = ApplicationLanguage.Turkish;
    [ObservableProperty] private bool _launchOnStartup;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private CloseBehavior _closeBehavior = CloseBehavior.ExitApplication;
    [ObservableProperty] private bool _rememberLastSelectedProfile = true;
    [ObservableProperty] private bool _confirmBeforeDeletingProfile = true;
    [ObservableProperty] private string _launchOnStartupError = "";
    public bool MinimizeToTray
    {
        get => CloseBehavior == CloseBehavior.MinimizeToTray;
        set => CloseBehavior = value ? CloseBehavior.MinimizeToTray : CloseBehavior.ExitApplication;
    }
    public string EmergencyStopHotkey => _original.EmergencyStopHotkey.ToString();
    public bool CanResetStatistics => usageStatistics is not null && usageStatistics.Snapshot.IsEngineStopped;
    public string StatisticsPersistenceWarning => localization?.TranslateKnownText(usageStatistics?.Snapshot.PersistenceWarning ?? "") ?? usageStatistics?.Snapshot.PersistenceWarning ?? "";

    public SettingsViewModel(IThemeService themeService, IStartupRegistrationService? startupRegistration = null,
        ILogger<SettingsViewModel>? logger = null, UsageStatisticsService? usageStatistics = null,
        IDesktopDialogService? dialogs = null, IUiDispatcher? dispatcher = null, LocalizationService? localization = null)
    {
        this.themeService = themeService; this.startupRegistration = startupRegistration; this.logger = logger;
        this.usageStatistics = usageStatistics; this.dialogs = dialogs; this.dispatcher = dispatcher; this.localization = localization;
        AppearanceOptions = Appearances.Select(value => new LocalizedEnumOption<Appearance>(value, L($"Enum_Appearance_{value}"))).ToArray();
        LanguageOptions = [new(ApplicationLanguage.Turkish, localization?.Get("Language_Turkish") ?? "Türkçe"), new(ApplicationLanguage.English, localization?.Get("Language_English") ?? "English")];
        if (usageStatistics is not null) usageStatistics.Changed += OnUsageStatisticsChanged;
        if (localization is not null) localization.LanguageChanged += OnLanguageChanged;
    }

    private string L(string key) => localization?.Get(key) ?? key;

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(StartupRegistrationStatus)); OnPropertyChanged(nameof(StatisticsPersistenceWarning));
        foreach (var option in LanguageOptions) option.Label = L($"Language_{option.Value}");
        foreach (var option in AppearanceOptions) option.Label = L($"Enum_Appearance_{option.Value}");
        if (LaunchOnStartupError.Length != 0) LaunchOnStartupError = localization?.TranslateKnownText(LaunchOnStartupError) ?? LaunchOnStartupError;
    }

    private void OnUsageStatisticsChanged(object? sender, EventArgs args)
    {
        void Update() { OnPropertyChanged(nameof(CanResetStatistics)); OnPropertyChanged(nameof(StatisticsPersistenceWarning)); ResetStatisticsCommand.NotifyCanExecuteChanged(); }
        if (dispatcher is null) Update(); else dispatcher.Post(Update);
    }

    [RelayCommand(CanExecute = nameof(CanResetStatistics))]
    private async Task ResetStatisticsAsync()
    {
        if (usageStatistics is null || dialogs is null || !CanResetStatistics) return;
        if (!await dialogs.ConfirmAsync(localization?.Get("Dialog_ResetStatistics_Title") ?? "Reset usage statistics?",
                localization?.Get("Dialog_ResetStatistics_Message") ?? "This will permanently reset total clicks and active time to zero.",
                localization?.Get("Common_Reset") ?? "Reset")) return;
        await usageStatistics.ResetAsync();
        OnPropertyChanged(nameof(CanResetStatistics));
        OnPropertyChanged(nameof(StatisticsPersistenceWarning));
    }

    public void Load(ApplicationSettings settings)
    {
        _loading = true;
        _original = settings;
        Appearance = settings.Appearance == Appearance.Dark ? Appearance.Night : Enum.IsDefined(settings.Appearance) ? settings.Appearance : Appearance.System;
        Language = Enum.IsDefined(settings.Language) ? settings.Language : ApplicationLanguage.Turkish;
        LaunchOnStartupError = "";
        LaunchOnStartup = startupRegistration is { IsAvailable: true } ? startupRegistration.IsEnabled : settings.LaunchOnStartup;
        OnPropertyChanged(nameof(StartupRegistrationStatus));
        StartMinimized = settings.StartMinimized;
        CloseBehavior = settings.CloseBehavior;
        RememberLastSelectedProfile = settings.RememberLastSelectedProfile;
        ConfirmBeforeDeletingProfile = settings.ConfirmBeforeDeletingProfile;
        OnPropertyChanged(nameof(LanguageOptions)); OnPropertyChanged(nameof(SelectedLanguageOption));
        OnPropertyChanged(nameof(AppearanceOptions)); OnPropertyChanged(nameof(SelectedAppearanceOption));
        themeService.Apply(Appearance);
        OnPropertyChanged(nameof(EmergencyStopHotkey));
        _loading = false;
    }

    public ApplicationSettings GetSnapshot() => _original with
    {
        Appearance = Appearance, LaunchOnStartup = LaunchOnStartup, StartMinimized = StartMinimized,
        Language = Language,
        CloseBehavior = CloseBehavior, RememberLastSelectedProfile = RememberLastSelectedProfile,
        ConfirmBeforeDeletingProfile = ConfirmBeforeDeletingProfile
    };

    partial void OnAppearanceChanged(Appearance value)
    {
        themeService.Apply(value);
        OnPropertyChanged(nameof(SelectedAppearanceOption));
        OnPropertyChanged(nameof(IsNightTheme)); OnPropertyChanged(nameof(IsCrimsonGoldTheme));
        OnPropertyChanged(nameof(IsGraphiteTheme)); OnPropertyChanged(nameof(IsLightTheme)); OnPropertyChanged(nameof(IsSystemTheme)); OnPropertyChanged(nameof(IsDesignTheme));
    }
    partial void OnLanguageChanged(ApplicationLanguage value)
    {
        localization?.SetLanguage(value);
        OnPropertyChanged(nameof(SelectedLanguageOption));
    }
    partial void OnCloseBehaviorChanged(CloseBehavior value) => OnPropertyChanged(nameof(MinimizeToTray));
    partial void OnLaunchOnStartupChanged(bool value)
    {
        if (_loading || startupRegistration is null) return;
        if (!startupRegistration.SetEnabled(value, out var error))
        {
            logger?.LogWarning("Launch on startup could not be changed: {Reason}", error);
            _loading = true;
            LaunchOnStartup = !value;
            _loading = false;
            LaunchOnStartupError = localization?.Get("Ui_LaunchChangeError") ?? "Launch on startup could not be changed.";
            OnPropertyChanged(nameof(StartupRegistrationStatus));
            return;
        }
        logger?.LogInformation("Launch on startup {State}", value ? "enabled" : "disabled");
        LaunchOnStartupError = "";
        OnPropertyChanged(nameof(StartupRegistrationStatus));
        Changed?.Invoke();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (!_loading && args.PropertyName is nameof(Appearance) or nameof(StartMinimized) or nameof(CloseBehavior)
            or nameof(RememberLastSelectedProfile) or nameof(ConfirmBeforeDeletingProfile) or nameof(Language)) Changed?.Invoke();
    }

    public void Dispose()
    {
        if (usageStatistics is not null) usageStatistics.Changed -= OnUsageStatisticsChanged;
        if (localization is not null) localization.LanguageChanged -= OnLanguageChanged;
    }
}

public sealed class LanguageOption(ApplicationLanguage value, string label) : LocalizedOption(label)
{ public ApplicationLanguage Value { get; } = value; }

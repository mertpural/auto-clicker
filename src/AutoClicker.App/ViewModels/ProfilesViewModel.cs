using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using AutoClicker.App.Services;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using AutoClicker.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.ViewModels;

public partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationStore _store;
    private readonly DebouncedSaveService _autosave;
    private readonly IDesktopDialogService _dialogs;
    private readonly IConfigurationFolderService _folders;
    private readonly ILogger<ProfilesViewModel> _logger;
    private readonly IAutoClickerEngine? _engine;
    private readonly IUiDispatcher? _dispatcher;
    private readonly IGlobalHotkeyService? _hotkeys;
    private readonly IAccessibilityPermissionService? _permissions;
    private readonly IMouseInputMonitor? _mouseInputMonitor;
    private readonly PositionPickingCoordinator? _positionPicker;
    private readonly UsageStatisticsService? _usageStatistics;
    private readonly MouseButtonHoldService? _mouseButtonHold;
    private readonly LocalizationService? _localization;
    private Hotkey? _registeredProfileHotkey;
    private Hotkey? _registeredHoldMouseButtonHotkey;
    private bool _revertingProfileSelection, _revertingHoldConfiguration;
    private Guid? _renamingProfileId;
    private Guid? _dropTargetProfileId;
    private ProfileEditorViewModel? _dragSource;
    private ClickMode _lastSelectedClickMode;
    private MouseButton _lastSelectedMouseButton;
    private bool _hotkeyHoldStarted;
    private bool _mouseHoldTriggerDown;
    private bool _mouseHoldArmed = true;
    private bool _mouseHoldStarted;
    private string _emergencyStopStatus = "Emergency stop not initialized.";
    private string _profileHotkeyStatus = "Profile shortcut not initialized.";
    private readonly bool _nativeInputAvailable;
    private bool _loading;
    private Guid _defaultProfileId;
    private int _selectedProfileTabIndex;
    public ObservableCollection<ProfileEditorViewModel> Items { get; } = [];
    public SettingsViewModel Settings { get; }
    public string ConfigurationDirectory => _store.ConfigurationDirectory;
    public string ConfigurationDirectoryDisplay
    {
        get
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return homeDirectory.Length != 0 && ConfigurationDirectory.StartsWith(homeDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? "~" + ConfigurationDirectory[homeDirectory.Length..] : ConfigurationDirectory;
        }
    }
    public string PlatformName { get; }
    private readonly string _defaultNativeInputStatus;
    public string NativeInputStatus => _localization?.TranslateKnownText(_permissions?.Status ?? _defaultNativeInputStatus) ?? _permissions?.Status ?? _defaultNativeInputStatus;
    public bool IsMacOS => PlatformName == "macOS";
    public string InputPermissionStatus => _localization?.TranslateKnownText(_permissions?.Status ?? "") ?? _permissions?.Status ?? "";
    public bool CanRequestInputPermission => IsMacOS;

    [ObservableProperty] private ProfileEditorViewModel? _selectedProfile;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isShuttingDown;
    [ObservableProperty] private string _saveStatus = "Loading…";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _hotkeyStatus = "Global shortcut not initialized.";
    public bool CanEdit => IsLoaded && !IsShuttingDown && !EngineBusy;
    public bool HasError => ErrorMessage.Length != 0;
    public bool CanRetryLoad => !IsLoaded;
    public EngineTelemetry EngineTelemetry => _engine?.Telemetry ?? new(EngineState.Stopped, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
    public bool EngineBusy => EngineTelemetry.State != EngineState.Stopped;
    public bool CanToggleEngine => !IsShuttingDown && (EngineBusy || _mouseButtonHold?.IsHeld == true
        || (IsLoaded && NativeInputGranted && SelectedProfile?.IsValid == true));
    private bool NativeInputGranted => _nativeInputAvailable && (_permissions?.IsGranted ?? true);
    public bool IsInputPermissionGranted => NativeInputGranted;
    public bool ShowPermissionWarning => IsMacOS && _permissions is not null && !_permissions.IsGranted;
    public bool IsMouseButtonHeld => _mouseButtonHold?.IsHeld == true;
    public string EngineButtonText => SelectedProfile?.ClickMode == ClickMode.HoldMouseButton
        ? IsMouseButtonHeld ? L("Engine_Release") : L("Engine_Hold") : EngineBusy ? L("Engine_Stop") : L("Engine_Start");
    public string EngineStatusText => IsMouseButtonHeld ? _localization?.Get("Status_HoldingMouse", _localization.Get($"Enum_MouseButton_{_mouseButtonHold!.HeldButton}")) ?? _mouseButtonHold!.Status : EngineTelemetry.State switch
    {
        EngineState.Starting => L("Status_Starting", EngineTelemetry.StartDelayRemaining.TotalSeconds),
        EngineState.Running => L("Status_Running", EngineTelemetry.PhysicalClickCount.ToString(_localization?.Culture ?? System.Globalization.CultureInfo.InvariantCulture), EngineTelemetry.ActualPhysicalClicksPerSecond.ToString("0.0", _localization?.Culture ?? System.Globalization.CultureInfo.InvariantCulture)),
        EngineState.Stopping => L("Status_Stopping"),
        _ => _localization?.TranslateKnownText(NativeInputStatus) ?? NativeInputStatus
    };
    public string ProfileStatusText => EngineTelemetry.State == EngineState.Stopped && !IsMouseButtonHeld ? L("Status_Ready") : EngineStatusText;
    public IBrush EngineStatusBrush
    {
        get
        {
            var key = HasError ? "DangerBrush" : IsMouseButtonHeld ? "AccentBrush" : EngineTelemetry.State switch
            {
                EngineState.Running => "SuccessBrush",
                EngineState.Starting or EngineState.Stopping => "WarningBrush",
                _ => "TextSecondary"
            };
            return Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var resource) && resource is IBrush brush
                ? brush : new SolidColorBrush(Color.Parse("#8A8A8A"));
        }
    }
    public string ShortcutSummary
    {
        get
        {
            if (SelectedProfile is not { } profile) return "";
            var shortcuts = new List<string>();
            if (profile.ClickMode is not (ClickMode.MouseHold or ClickMode.HoldMouseButton))
                shortcuts.Add(L("Shortcut_StartStop", profile.HotkeyText));
            if (profile.ClickMode == ClickMode.HoldMouseButton)
                shortcuts.Add(L("Shortcut_HoldMouse", profile.HoldMouseButtonHotkeyText));
            shortcuts.Add(L("Shortcut_Emergency", Settings.EmergencyStopHotkey));
            return string.Join("   ·   ", shortcuts);
        }
    }
    public bool HasShortcutSummary => ShortcutSummary.Length != 0;
    public string TotalClicksText => UsageStatisticsFormatter.FormatClicks(_usageStatistics?.Snapshot.TotalPhysicalClicks ?? 0, _localization?.Culture);
    public string TotalActiveTimeText => UsageStatisticsFormatter.FormatActiveTime(_usageStatistics?.Snapshot.TotalActiveTime ?? TimeSpan.Zero, _localization?.Language ?? ApplicationLanguage.English, _localization);

    public ProfilesViewModel(IApplicationStore store, DebouncedSaveService autosave,
        SettingsViewModel settings, IDesktopDialogService dialogs, IConfigurationFolderService folders,
        IPlatformInfo platform, ILogger<ProfilesViewModel> logger, IAutoClickerEngine? engine = null, IUiDispatcher? dispatcher = null,
        IGlobalHotkeyService? hotkeys = null, IAccessibilityPermissionService? permissions = null,
        IMouseInputMonitor? mouseInputMonitor = null, PositionPickingCoordinator? positionPicker = null,
        UsageStatisticsService? usageStatistics = null, MouseButtonHoldService? mouseButtonHold = null, LocalizationService? localization = null)
    {
        _store = store; _autosave = autosave; Settings = settings;
        _dialogs = dialogs; _folders = folders; _logger = logger;
        PlatformName = platform.Name; _defaultNativeInputStatus = platform.NativeInputStatus;
        _nativeInputAvailable = platform.NativeInputAvailable; _engine = engine; _dispatcher = dispatcher;
        _hotkeys = hotkeys; _permissions = permissions; _mouseInputMonitor = mouseInputMonitor; _positionPicker = positionPicker;
        _usageStatistics = usageStatistics;
        _mouseButtonHold = mouseButtonHold;
        _localization = localization;
        if (_localization is not null)
        {
            _emergencyStopStatus = L("Hotkey_EmergencyNotInitialized");
            _profileHotkeyStatus = L("Hotkey_ProfileNotInitialized");
            SaveStatus = L("Status_Loading");
            HotkeyStatus = L("Hotkey_NotInitialized");
            _localization.LanguageChanged += OnLanguageChanged;
        }
        if (_usageStatistics is not null) _usageStatistics.Changed += OnUsageStatisticsChanged;
        if (_mouseButtonHold is not null) _mouseButtonHold.Changed += OnMouseButtonHoldChanged;
        if (_hotkeys is not null) { _hotkeys.Pressed += OnHotkeyPressed; _hotkeys.Released += OnHotkeyReleased; }
        if (_mouseInputMonitor is not null) _mouseInputMonitor.MouseButtonChanged += OnMouseButtonChanged;
        if (_engine is not null)
        {
            _engine.TelemetryChanged += OnEngineTelemetryChanged;
            _engine.Error += OnEngineError;
        }
        Settings.Changed += OnSettingsChanged;
        _autosave.Completed += OnSaveCompleted;
    }

    private void OnSaveCompleted(SaveResult result)
    {
        SaveStatus = result.Success ? L("Status_Saved") : L("Status_NotSaved");
        ErrorMessage = _localization?.TranslateKnownText(result.ErrorMessage ?? "") ?? result.ErrorMessage ?? "";
    }

    private string L(string key, params object?[] args) => _localization?.Get(key, args) ?? key;
    public string Localize(string key, params object?[] args) => L(key, args);
    private string LocalizeHotkeyError(string? message, string fallbackKey) => message is null ? L(fallbackKey)
        : _localization?.TranslateKnownTextOrFallback(message, "Hotkey_RegistrationFailed") ?? message;

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        void Refresh()
        {
            _emergencyStopStatus = _localization?.TranslateKnownText(_emergencyStopStatus) ?? _emergencyStopStatus;
            _profileHotkeyStatus = _localization?.TranslateKnownText(_profileHotkeyStatus) ?? _profileHotkeyStatus;
            RefreshHotkeyStatus();
            ErrorMessage = _localization?.TranslateKnownText(ErrorMessage) ?? ErrorMessage;
            SaveStatus = _localization?.TranslateKnownText(SaveStatus) ?? SaveStatus;
            foreach (var profile in Items) profile.RefreshLocalizedText();
            OnPropertyChanged(nameof(NativeInputStatus)); OnPropertyChanged(nameof(InputPermissionStatus)); OnPropertyChanged(nameof(ShowPermissionWarning));
                OnPropertyChanged(nameof(EngineStatusText)); OnPropertyChanged(nameof(EngineButtonText));
            OnPropertyChanged(nameof(ProfileStatusText)); OnPropertyChanged(nameof(EngineStatusBrush));
            OnPropertyChanged(nameof(ShortcutSummary)); OnPropertyChanged(nameof(HasShortcutSummary));
            OnPropertyChanged(nameof(TotalClicksText)); OnPropertyChanged(nameof(TotalActiveTimeText));
            UpdateAllEditorStatistics();
        }
        // Language changes originate in the Settings binding on the UI thread. Refresh synchronously
        // so ComboBox sources and their selected records are replaced as one coherent update.
        Refresh();
    }

    partial void OnIsLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanRetryLoad)); OnPropertyChanged(nameof(CanToggleEngine)); UpdateCommands();
    }
    partial void OnIsShuttingDownChanged(bool value)
    {
        if (value) _positionPicker?.CancelActivePick();
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanToggleEngine)); UpdateCommands();
    }
    partial void OnSelectedProfileChanged(ProfileEditorViewModel? oldValue, ProfileEditorViewModel? newValue)
    {
        if (_revertingProfileSelection) return;
        if (oldValue != newValue && EngineBusy)
        {
            _revertingProfileSelection = true;
            SelectedProfile = oldValue;
            _revertingProfileSelection = false;
            return;
        }
        if (oldValue is not null && oldValue != newValue && _mouseButtonHold?.IsHeld == true)
        {
            try { _mouseButtonHold.ReleaseIfHeldAsync().GetAwaiter().GetResult(); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not release the held mouse button before changing profiles");
                _revertingProfileSelection = true;
                SelectedProfile = oldValue;
                _revertingProfileSelection = false;
                ReportError(exception, L("Error_HeldButtonChange"));
                return;
            }
        }
        if (oldValue?.IsRenaming == true) CancelRename(oldValue);
        if (oldValue?.IsPickingPosition == true) _positionPicker?.CancelActivePick();
        if (oldValue is not null)
        {
            oldValue.IsRecording = false;
            oldValue.HotkeyChanged -= OnProfileHotkeyChanged;
            oldValue.RecordingChanged -= OnRecordingChanged;
            oldValue.MouseHoldConfigurationChanged -= OnMouseHoldConfigurationChanged;
            oldValue.HoldButtonModeConfigurationChanged -= OnHoldButtonModeConfigurationChanged;
            oldValue.HoldMouseButtonHotkeyChanged -= OnHoldMouseButtonHotkeyChanged;
            oldValue.PickPositionRequested -= OnPickPositionRequested;
        }
        if (newValue is not null)
        {
            newValue.SelectedTabIndex = _selectedProfileTabIndex;
            newValue.HotkeyChanged += OnProfileHotkeyChanged;
            newValue.RecordingChanged += OnRecordingChanged;
            newValue.MouseHoldConfigurationChanged += OnMouseHoldConfigurationChanged;
            newValue.HoldButtonModeConfigurationChanged += OnHoldButtonModeConfigurationChanged;
            newValue.HoldMouseButtonHotkeyChanged += OnHoldMouseButtonHotkeyChanged;
            newValue.PickPositionRequested += OnPickPositionRequested;
        }
        _lastSelectedClickMode = newValue?.ClickMode ?? ClickMode.Toggle;
        _lastSelectedMouseButton = newValue?.MouseButton ?? MouseButton.Left;
        OnPropertyChanged(nameof(EngineButtonText)); OnPropertyChanged(nameof(EngineStatusText));
        OnPropertyChanged(nameof(ProfileStatusText)); OnPropertyChanged(nameof(EngineStatusBrush));
        OnPropertyChanged(nameof(ShortcutSummary)); OnPropertyChanged(nameof(HasShortcutSummary));
        OnPropertyChanged(nameof(CanToggleEngine));
        UpdateCommands();
        if (!_loading) ScheduleSave();
        if (!_loading && _hotkeys is not null) _ = RegisterSelectedHotkeyAsync(oldValue, newValue);
        ResetMouseHoldState();
        if (!_loading && newValue?.ClickMode == ClickMode.MouseHold) _ = PrepareMouseHoldAsync(newValue);
    }

    private void OnMouseHoldConfigurationChanged()
    {
        ResetMouseHoldState();
        if (SelectedProfile?.ClickMode == ClickMode.MouseHold) _ = PrepareMouseHoldAsync(SelectedProfile);
    }

    private void OnHoldButtonModeConfigurationChanged()
    {
        if (_revertingHoldConfiguration || SelectedProfile is not { } selected) return;
        if (_mouseButtonHold?.IsHeld == true)
        {
            try { _mouseButtonHold.ReleaseIfHeldAsync().GetAwaiter().GetResult(); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not release the held mouse button before changing its profile configuration");
                _revertingHoldConfiguration = true;
                if (selected.ClickMode != _lastSelectedClickMode) selected.ClickMode = _lastSelectedClickMode;
                if (selected.MouseButton != _lastSelectedMouseButton) selected.MouseButton = _lastSelectedMouseButton;
                _revertingHoldConfiguration = false;
                ReportError(exception, L("Error_HeldConfigChange"));
                return;
            }
        }
        _lastSelectedClickMode = selected.ClickMode;
        _lastSelectedMouseButton = selected.MouseButton;
        if (_hotkeys is not null) _ = RegisterSelectedHotkeyAsync(selected, selected);
    }

    private void OnHoldMouseButtonHotkeyChanged()
    {
        if (_hotkeys is not null && SelectedProfile is { } selected) _ = RebindHoldMouseButtonHotkeyAsync(selected);
    }

    private async Task PrepareMouseHoldAsync(ProfileEditorViewModel editor)
    {
        if (_mouseInputMonitor is null)
        {
            editor.MouseHoldStatus = L("Position_MouseHoldMonitoringUnavailable");
            return;
        }
        try
        {
            var result = await _mouseInputMonitor.StartMonitoringAsync();
            editor.MouseHoldStatus = result.Success
                ? L("Position_MouseHoldReady", L($"Enum_MouseButton_{editor.HoldMouseButton}"))
                : _localization?.TranslateKnownText(result.ErrorMessage ?? _mouseInputMonitor.Status) ?? result.ErrorMessage ?? _mouseInputMonitor.Status;
            if (result.Success) _logger.LogInformation("Mouse Hold enabled for {Button} button", editor.HoldMouseButton);
            else _logger.LogWarning("Mouse Hold observation permission unavailable: {Reason}", editor.MouseHoldStatus);
        }
        catch (Exception exception)
        {
            editor.MouseHoldStatus = L("Position_MouseHoldUnavailable");
            _logger.LogWarning(exception, "Unable to start Mouse Hold input monitoring");
        }
    }

    private void OnPickPositionRequested(ProfileEditorViewModel editor) => _ = PickPositionAsync(editor);
    private async Task PickPositionAsync(ProfileEditorViewModel editor)
    {
        if (_positionPicker is null) { editor.PositionPickerStatus = L("Position_PickUnavailable"); return; }
        try { await _positionPicker.PickAsync(editor); }
        catch (Exception exception) { ReportError(exception, L("Position_PickFailed")); }
    }

    private void OnMouseButtonChanged(object? sender, MouseInputEventArgs args)
    {
        void Apply() => _ = HandleMouseButtonAsync(args);
        if (_dispatcher is null) Apply(); else _dispatcher.Post(Apply);
    }

    private async Task HandleMouseButtonAsync(MouseInputEventArgs args)
    {
        if (IsShuttingDown || args.IsAutoClickerGenerated || SelectedProfile is not { ClickMode: ClickMode.MouseHold } selected || selected.IsPickingPosition
            || args.Button != selected.HoldMouseButton) return;
        if (args.IsPressed)
        {
            if (_mouseHoldTriggerDown) return;
            _mouseHoldTriggerDown = true;
            if (!_mouseHoldArmed || _engine is null || !selected.TryGetSnapshot(out var profile) || _engine.IsRunning) return;
            _mouseHoldArmed = false;
            try { await _engine.StartAsync(profile); _mouseHoldStarted = true; }
            catch (Exception exception) { DisarmMouseHoldUntilRelease(); ReportError(exception, L("Error_StartClick")); }
            return;
        }
        _mouseHoldTriggerDown = false;
        _mouseHoldArmed = true;
        if (!_mouseHoldStarted) return;
        _mouseHoldStarted = false;
        await StopEngineAsync();
    }

    private void ResetMouseHoldState()
    {
        _mouseHoldTriggerDown = false;
        _mouseHoldArmed = true;
        _mouseHoldStarted = false;
    }

    private void DisarmMouseHoldUntilRelease()
    {
        if (_mouseHoldTriggerDown) _mouseHoldArmed = false;
        _mouseHoldStarted = false;
    }

    private void OnRecordingChanged(bool recording) => _hotkeys?.SetActionsSuspended(recording);
    private void OnProfileHotkeyChanged()
    {
        if (_hotkeys is not null && SelectedProfile is { } selected) _ = RebindHotkeyAsync(selected);
    }

    public async Task RegisterHotkeysAsync()
    {
        if (_hotkeys is null || SelectedProfile is null) return;
        _hotkeys.SetActionsSuspended(false);
        var emergency = await _hotkeys.RegisterEmergencyStopAsync(Settings.GetSnapshot().EmergencyStopHotkey);
        _emergencyStopStatus = emergency.Success ? L("Status_EmergencyStop", Settings.EmergencyStopHotkey) : LocalizeHotkeyError(emergency.ErrorMessage, "Hotkey_EmergencyUnavailable");
        RefreshHotkeyStatus();
        if (!emergency.Success) _logger.LogWarning("Emergency stop shortcut registration failed: {Reason}", emergency.ErrorMessage);
        await RegisterSelectedHotkeyAsync(null, SelectedProfile);
        if (SelectedProfile.ClickMode == ClickMode.MouseHold) await PrepareMouseHoldAsync(SelectedProfile);
    }

    public void UnregisterHotkeys()
    {
        if (_hotkeys is null) return;
        UnregisterSelectedHotkey();
        _hotkeys.UnregisterEmergencyStop();
    }

    public async Task DisposeHotkeysAsync()
    {
        if (_hotkeys is not null) await _hotkeys.DisposeAsync();
    }

    private async Task RegisterSelectedHotkeyAsync(ProfileEditorViewModel? oldValue, ProfileEditorViewModel? newValue)
    {
        if (_hotkeys is null) return;
        if (newValue?.ClickMode == ClickMode.MouseHold)
        {
            UnregisterSelectedHotkey();
            _profileHotkeyStatus = L("Hotkey_MouseHoldTrigger");
            RefreshHotkeyStatus();
            return;
        }
        if (newValue is null || !newValue.TryGetSnapshot(out var profile))
        {
            UnregisterSelectedHotkey();
            _profileHotkeyStatus = L("Hotkey_ProfileUnavailable");
            RefreshHotkeyStatus();
            return;
        }
        if (newValue.ClickMode == ClickMode.HoldMouseButton)
        {
            if (_registeredProfileHotkey is { } profileKey) _hotkeys.Unregister(profileKey);
            _registeredProfileHotkey = null;
            if (_registeredHoldMouseButtonHotkey == profile.HoldMouseButtonHotkey)
            {
                _profileHotkeyStatus = L("Status_HoldHotkey", profile.HoldMouseButtonHotkey.ToString());
                RefreshHotkeyStatus();
                return;
            }
            var holdResult = await _hotkeys.RegisterHoldMouseButtonAsync(profile.HoldMouseButtonHotkey);
            if (!holdResult.Success)
            {
                _profileHotkeyStatus = LocalizeHotkeyError(holdResult.ErrorMessage, "Hotkey_HoldUnavailable");
                RefreshHotkeyStatus();
                _logger.LogWarning("Hold/release shortcut registration failed: {Reason}", holdResult.ErrorMessage);
                return;
            }
            _registeredHoldMouseButtonHotkey = profile.HoldMouseButtonHotkey;
            _profileHotkeyStatus = L("Status_HoldHotkey", profile.HoldMouseButtonHotkey.ToString());
            RefreshHotkeyStatus();
            return;
        }
        if (_registeredHoldMouseButtonHotkey is not null)
        {
            _hotkeys.UnregisterHoldMouseButton();
            _registeredHoldMouseButtonHotkey = null;
        }
        var previous = _registeredProfileHotkey;
        var result = await _hotkeys.RegisterAsync(profile.StartStopHotkey);
        if (!result.Success)
        {
            if (previous is { } old) { _hotkeys.Unregister(old); _registeredProfileHotkey = null; }
            _profileHotkeyStatus = LocalizeHotkeyError(result.ErrorMessage, "Hotkey_ProfileUnavailable");
            RefreshHotkeyStatus();
            _logger.LogWarning("Profile shortcut registration failed: {Reason}", result.ErrorMessage);
            return;
        }
        if (previous is { } previousKey && previousKey != profile.StartStopHotkey) _hotkeys.Unregister(previousKey);
        _registeredProfileHotkey = profile.StartStopHotkey;
        _profileHotkeyStatus = L("Status_ProfileHotkey", profile.StartStopHotkey.ToString());
        RefreshHotkeyStatus();
    }

    private async Task RebindHotkeyAsync(ProfileEditorViewModel editor)
    {
        if (!editor.TryGetSnapshot(out var snapshot))
        {
            if (_registeredProfileHotkey is { } oldInvalid) { _hotkeys!.Unregister(oldInvalid); _registeredProfileHotkey = null; }
            _hotkeys!.UnregisterHoldMouseButton(); _registeredHoldMouseButtonHotkey = null;
            _profileHotkeyStatus = L("Hotkey_FixProfile");
            RefreshHotkeyStatus();
            return;
        }
        var previous = _registeredProfileHotkey;
        var result = await _hotkeys!.RegisterAsync(snapshot.StartStopHotkey);
        if (result.Success)
        {
            if (previous is { } previousKey && previousKey != snapshot.StartStopHotkey) _hotkeys.Unregister(previousKey);
            _registeredProfileHotkey = snapshot.StartStopHotkey;
            _profileHotkeyStatus = L("Status_ProfileHotkey", snapshot.StartStopHotkey.ToString());
            RefreshHotkeyStatus();
            return;
        }
        if (previous is { } prior) editor.HotkeyText = prior.ToString();
        _profileHotkeyStatus = LocalizeHotkeyError(result.ErrorMessage, "Hotkey_ProfileUnavailable");
        RefreshHotkeyStatus();
        _logger.LogWarning("Profile shortcut rebind failed: {Reason}", result.ErrorMessage);
    }

    private async Task RebindHoldMouseButtonHotkeyAsync(ProfileEditorViewModel editor)
    {
        if (!editor.TryGetSnapshot(out var snapshot))
        {
            _hotkeys!.UnregisterHoldMouseButton(); _registeredHoldMouseButtonHotkey = null;
            _profileHotkeyStatus = L("Hotkey_FixHold");
            RefreshHotkeyStatus();
            return;
        }
        var result = await _hotkeys!.RegisterHoldMouseButtonAsync(snapshot.HoldMouseButtonHotkey);
        if (!result.Success)
        {
            if (_registeredHoldMouseButtonHotkey is { } previous) editor.HoldMouseButtonHotkeyText = previous.ToString();
            _profileHotkeyStatus = LocalizeHotkeyError(result.ErrorMessage, "Hotkey_HoldUnavailable");
            RefreshHotkeyStatus();
            _logger.LogWarning("Hold/release shortcut rebind failed: {Reason}", result.ErrorMessage);
            return;
        }
        _registeredHoldMouseButtonHotkey = snapshot.HoldMouseButtonHotkey;
        _profileHotkeyStatus = L("Status_HoldHotkey", snapshot.HoldMouseButtonHotkey.ToString());
        RefreshHotkeyStatus();
    }

    private void UnregisterSelectedHotkey()
    {
        if (_registeredProfileHotkey is { } key) _hotkeys?.Unregister(key);
        if (_registeredHoldMouseButtonHotkey is not null) _hotkeys?.UnregisterHoldMouseButton();
        _registeredProfileHotkey = null;
        _registeredHoldMouseButtonHotkey = null;
    }

    private void RefreshHotkeyStatus() => HotkeyStatus = $"{_emergencyStopStatus} · {_profileHotkeyStatus}";

    public Task RefreshInputPermissionAsync()
    {
        OnPropertyChanged(nameof(NativeInputStatus)); OnPropertyChanged(nameof(InputPermissionStatus)); OnPropertyChanged(nameof(ShowPermissionWarning));
        OnPropertyChanged(nameof(CanToggleEngine)); UpdateCommands();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RequestInputPermissionAsync()
    {
        if (_permissions is null) return;
        await _permissions.RequestAsync();
        await RefreshInputPermissionAsync();
        if (_permissions.IsGranted || _permissions.CanListenForShortcuts) await RegisterHotkeysAsync();
    }

    [RelayCommand]
    private void OpenInputPermissionSettings()
    {
        try { _permissions?.OpenSystemSettings(); }
        catch (Exception exception) { ReportError(exception, L("Error_OpenMacSettings")); }
    }

    [RelayCommand]
    private async Task RetryInputPermissionAsync()
    {
        await RefreshInputPermissionAsync();
        if (_permissions is { } permissions && (permissions.IsGranted || permissions.CanListenForShortcuts)) await RegisterHotkeysAsync();
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs args) => DispatchHotkey(args, true);
    private void OnHotkeyReleased(object? sender, HotkeyEventArgs args) => DispatchHotkey(args, false);
    private void DispatchHotkey(HotkeyEventArgs args, bool pressed)
    {
        void Run() => _ = HandleHotkeyAsync(args, pressed);
        if (_dispatcher is null) Run(); else _dispatcher.Post(Run);
    }

    private async Task HandleHotkeyAsync(HotkeyEventArgs args, bool pressed)
    {
        try
        {
            if (args.IsEmergencyStop) { if (pressed) { DisarmMouseHoldUntilRelease(); await StopEngineAsync(); } return; }
            if (args.IsHoldMouseButton)
            {
                if (!pressed || IsShuttingDown || _mouseButtonHold is null || SelectedProfile is not { } holdProfile
                    || holdProfile.ClickMode != ClickMode.HoldMouseButton
                    || !holdProfile.TryGetSnapshot(out var holdSnapshot)
                    || holdSnapshot.HoldMouseButtonHotkey != args.Hotkey) return;
                await _mouseButtonHold.ToggleAsync(holdSnapshot.MouseButton);
                return;
            }
            if (IsShuttingDown) return;
            if (_engine is null || SelectedProfile is null || !SelectedProfile.TryGetSnapshot(out var profile)) return;
            if (profile.ClickMode == ClickMode.MouseHold) return;
            if (profile.ClickMode == ClickMode.Hold)
            {
                if (pressed && !_hotkeyHoldStarted)
                {
                    _hotkeyHoldStarted = true;
                    if (!_engine.IsRunning) await _engine.StartAsync(profile);
                }
                else if (!pressed && _hotkeyHoldStarted) { _hotkeyHoldStarted = false; await StopEngineAsync(); }
                return;
            }
            if (pressed)
            {
                if (_engine?.IsRunning == true) await StopEngineAsync();
                else await ToggleEngineCommand.ExecuteAsync(null);
            }
        }
        catch (Exception exception) { ReportError(exception, L("Error_RunGlobalHotkey")); }
    }
    partial void OnErrorMessageChanged(string value)
    { OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(EngineStatusBrush)); }

    private void OnEngineTelemetryChanged(object? sender, EngineTelemetryEventArgs args) => DispatchEngineUpdate();
    private void OnMouseButtonHoldChanged(object? sender, EventArgs args) => DispatchEngineUpdate();
    private void OnUsageStatisticsChanged(object? sender, EventArgs args)
    {
        void Update() { OnPropertyChanged(nameof(TotalClicksText)); OnPropertyChanged(nameof(TotalActiveTimeText)); UpdateAllEditorStatistics(); }
        if (_dispatcher is null) Update(); else _dispatcher.Post(Update);
    }
    private void OnEngineError(object? sender, EngineErrorEventArgs args)
    {
        if (_dispatcher is null) HandleEngineError(args);
        else _dispatcher.Post(() => HandleEngineError(args));
    }

    private void HandleEngineError(EngineErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Click engine failed: {Message}", args.Message);
        ErrorMessage = L("Error_ClickExecution");
        if (_mouseButtonHold?.IsHeld == true) _ = ReleaseHeldButtonAfterFailureAsync();
        DispatchEngineUpdate();
    }

    private async Task ReleaseHeldButtonAfterFailureAsync()
    {
        try { await _mouseButtonHold!.ReleaseIfHeldAsync(); }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Could not release the held mouse button after an engine failure");
            ReportError(exception, L("Error_ReleaseAfterInputError"));
        }
    }

    private void DispatchEngineUpdate()
    {
        void Update()
        {
            OnPropertyChanged(nameof(EngineTelemetry)); OnPropertyChanged(nameof(EngineBusy));
            OnPropertyChanged(nameof(EngineStatusText)); OnPropertyChanged(nameof(EngineButtonText));
            OnPropertyChanged(nameof(ProfileStatusText)); OnPropertyChanged(nameof(EngineStatusBrush));
            OnPropertyChanged(nameof(IsMouseButtonHeld));
            OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanToggleEngine));
            UpdateCommands();
        }
        if (_dispatcher is null) Update(); else _dispatcher.Post(Update);
    }

    [RelayCommand(CanExecute = nameof(CanToggleEngine))]
    private async Task ToggleEngineAsync()
    {
        if (_mouseButtonHold?.IsHeld == true && SelectedProfile?.ClickMode != ClickMode.HoldMouseButton)
        {
            await StopEngineAsync();
            return;
        }
        if (SelectedProfile is { ClickMode: ClickMode.HoldMouseButton } holdProfile)
        {
            if (!NativeInputGranted || !holdProfile.TryGetSnapshot(out var holdSnapshot)) return;
            try { await _mouseButtonHold!.ToggleAsync(holdSnapshot.MouseButton); }
            catch (Exception exception) { ReportError(exception, L("Error_HoldToggle")); }
            return;
        }
        if (_engine is null) return;
        if (_engine.IsRunning) { await StopEngineAsync(); return; }
        if (!NativeInputGranted || SelectedProfile is null || !SelectedProfile.TryGetSnapshot(out var profile)) return;
        try { await _engine.StartAsync(profile); }
        catch (Exception exception) { ReportError(exception, L("Error_StartClick")); }
    }

    public async Task StopEngineAsync()
    {
        DisarmMouseHoldUntilRelease();
        Exception? releaseFailure = null;
        if (_mouseButtonHold is not null)
        {
            try { await _mouseButtonHold.ReleaseIfHeldAsync(); }
            catch (Exception exception) { releaseFailure = exception; }
        }
        if (_engine is not null) await _engine.StopAsync();
        if (_usageStatistics is not null) await _usageStatistics.FlushAsync();
        if (releaseFailure is not null)
        {
            _logger.LogError(releaseFailure, "Held mouse button could not be released during stop");
            ReportError(releaseFailure, L("Error_HoldRelease"));
        }
    }
    public Task FlushStatisticsAsync() => _usageStatistics?.FlushAsync() ?? Task.CompletedTask;
    public ValueTask DisposeEngineAsync() => _engine?.DisposeAsync() ?? ValueTask.CompletedTask;

    [RelayCommand]
    public async Task InitializeAsync()
    {
        _loading = true;
        IsLoaded = false;
        SaveStatus = L("Status_Loading");
        ErrorMessage = "";
        try
        {
            await _autosave.CancelAndWaitAsync();
            var data = await _store.LoadAsync();
            _usageStatistics?.Initialize(data.LifetimeStatistics);
            foreach (var item in Items) { item.Changed -= ScheduleSave; item.PropertyChanged -= OnProfileEditorPropertyChanged; item.TabChanged -= OnProfileTabChanged; }
            Items.Clear();
            _renamingProfileId = null; _dropTargetProfileId = null;
            _defaultProfileId = data.DefaultProfileId;
            foreach (var profile in data.Profiles) AddEditor(profile);
            Settings.Load(data.Settings);
            var selectedId = data.Settings.RememberLastSelectedProfile ? data.SelectedProfileId : data.DefaultProfileId;
            SelectedProfile = Items.First(item => item.Id == selectedId);
            IsLoaded = true;
            SaveStatus = L("Status_Saved");
            _logger.LogInformation("Loaded {ProfileCount} profiles on {Platform}", Items.Count, PlatformName);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Configuration initialization failed");
            ErrorMessage = exception is PersistenceException ? _localization?.TranslateKnownText(exception.Message) ?? exception.Message : L("Error_LoadProfiles");
            SaveStatus = L("Status_LoadFailed");
        }
        finally
        {
            _loading = false;
            if (_hotkeys is null && SelectedProfile?.ClickMode == ClickMode.MouseHold) _ = PrepareMouseHoldAsync(SelectedProfile);
            UpdateCommands();
        }
    }

    private void AddEditor(AutoClickerProfile profile)
    {
        var editor = new ProfileEditorViewModel(profile, _localization) { IsDefault = profile.Id == _defaultProfileId };
        editor.Changed += ScheduleSave;
        editor.PropertyChanged += OnProfileEditorPropertyChanged;
        editor.TabChanged += OnProfileTabChanged;
        UpdateEditorStatistics(editor);
        Items.Add(editor);
    }

    private void OnProfileEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProfileEditorViewModel.ProfileColor) && sender is ProfileEditorViewModel editor)
            _logger.LogInformation("Profile color changed: {ProfileId} to {ProfileColor}", editor.Id, editor.ProfileColor);
        if (sender == SelectedProfile && args.PropertyName is nameof(ProfileEditorViewModel.HotkeyText) or nameof(ProfileEditorViewModel.HoldMouseButtonHotkeyText) or nameof(ProfileEditorViewModel.ClickMode))
        { OnPropertyChanged(nameof(ShortcutSummary)); OnPropertyChanged(nameof(HasShortcutSummary)); OnPropertyChanged(nameof(EngineButtonText)); }
    }

    private void OnProfileTabChanged(int value)
    { _selectedProfileTabIndex = value; }

    private void UpdateAllEditorStatistics()
    { foreach (var editor in Items) UpdateEditorStatistics(editor); }

    private void UpdateEditorStatistics(ProfileEditorViewModel editor)
    {
        var culture = _localization?.Culture ?? System.Globalization.CultureInfo.InvariantCulture;
        var language = _localization?.Language ?? ApplicationLanguage.English;
        editor.UpdateStatistics(
            UsageStatisticsFormatter.FormatClicks(_usageStatistics?.Snapshot.TotalPhysicalClicks ?? 0, culture),
            UsageStatisticsFormatter.FormatActiveTime(_usageStatistics?.Snapshot.TotalActiveTime ?? TimeSpan.Zero, language, _localization));
    }

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(EngineStatusBrush));
        ScheduleSave();
    }

    private bool CanAddProfile() => CanEdit && Items.Count < 1000;
    [RelayCommand(CanExecute = nameof(CanAddProfile))]
    private void NewProfile()
    {
        var profile = new AutoClickerProfile { Name = UniqueName(L("Profile_NewName")) };
        AddEditor(profile);
        SelectedProfile = Items[^1];
        ScheduleSave(); UpdateCommands();
    }

    public bool BeginRename(ProfileEditorViewModel? profile)
    {
        if (!CanUseProfile(profile) || profile is null) return false;
        if (_renamingProfileId is { } currentId && currentId != profile.Id)
            Items.FirstOrDefault(item => item.Id == currentId)?.CancelRename();
        profile.BeginRename();
        _renamingProfileId = profile.Id;
        return true;
    }

    public bool CommitRename(ProfileEditorViewModel profile)
    {
        if (_renamingProfileId != profile.Id || !profile.IsRenaming || !Items.Contains(profile)) return false;
        var name = (profile.RenameText ?? "").Trim();
        if (name.Length is < 1 or > 80)
        {
            profile.SetRenameValidationMessage("Validation_ProfileNameLength");
            return false;
        }
        if (Items.Any(item => item.Id != profile.Id && item.NormalizedName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            profile.SetRenameValidationMessage("Validation_DuplicateProfileName");
            return false;
        }
        var changed = !profile.Name.Equals(name, StringComparison.Ordinal);
        profile.FinishRename(name);
        _renamingProfileId = null;
        if (changed) _logger.LogInformation("Profile renamed: {ProfileId} to {ProfileName}", profile.Id, name);
        return true;
    }

    public void CancelRename(ProfileEditorViewModel? profile)
    {
        if (profile is null || _renamingProfileId != profile.Id) return;
        profile.CancelRename();
        _renamingProfileId = null;
    }

    public bool MoveProfile(ProfileEditorViewModel profile, int offset)
    {
        var currentIndex = Items.IndexOf(profile);
        return currentIndex >= 0 && MoveProfileToIndex(profile, currentIndex + offset);
    }

    public bool MoveProfileToIndex(ProfileEditorViewModel profile, int targetIndex)
    {
        if (!CanReorder(profile)) return false;
        var currentIndex = Items.IndexOf(profile);
        var clampedIndex = Math.Clamp(targetIndex, 0, Items.Count - 1);
        if (currentIndex == clampedIndex) return false;
        Items.Move(currentIndex, clampedIndex);
        _logger.LogInformation("Profile reordered: {ProfileId} to index {ProfileIndex}", profile.Id, clampedIndex);
        ScheduleSave();
        UpdateCommands();
        return true;
    }

    public bool MoveProfileBefore(Guid sourceProfileId, Guid targetProfileId, bool afterTarget = false)
    {
        var source = Items.FirstOrDefault(profile => profile.Id == sourceProfileId);
        var targetIndex = Items.ToList().FindIndex(profile => profile.Id == targetProfileId);
        if (source is null || targetIndex < 0 || sourceProfileId == targetProfileId) return false;
        var insertionIndex = targetIndex + (afterTarget ? 1 : 0);
        var sourceIndex = Items.IndexOf(source);
        if (sourceIndex < insertionIndex) insertionIndex--;
        return MoveProfileToIndex(source, insertionIndex);
    }

    public void SetDropTarget(Guid? profileId, bool after = false)
    {
        if (_dropTargetProfileId == profileId)
        {
            if (profileId is { } sameId && Items.FirstOrDefault(profile => profile.Id == sameId) is { } same)
                same.DropAfter = after;
            return;
        }
        if (_dropTargetProfileId is { } previousId && Items.FirstOrDefault(profile => profile.Id == previousId) is { } previous)
            previous.IsDropTarget = false;
        _dropTargetProfileId = profileId;
        if (profileId is { } nextId && Items.FirstOrDefault(profile => profile.Id == nextId) is { } next)
        { next.DropAfter = after; next.IsDropTarget = true; }
    }

    public bool BeginProfileDrag(ProfileEditorViewModel profile)
    {
        CancelProfileDrag();
        if (!CanReorder(profile) || Items.Any(item => item.IsRenaming)) return false;
        _dragSource = profile; profile.IsDragging = true;
        return true;
    }

    public bool DropProfile(Guid targetId, bool after)
    {
        var source = _dragSource;
        CancelProfileDrag();
        return source is not null && !Items.Any(item => item.IsRenaming) && MoveProfileBefore(source.Id, targetId, after);
    }

    public void CancelProfileDrag()
    {
        if (_dragSource is not null) _dragSource.IsDragging = false;
        _dragSource = null; SetDropTarget(null);
    }

    private bool CanReorder(ProfileEditorViewModel? profile) => IsLoaded && !IsShuttingDown && profile is not null && Items.Contains(profile);
    private bool CanMoveUp(ProfileEditorViewModel? profile) => CanReorder(profile) && Items.IndexOf(profile!) > 0;
    private bool CanMoveDown(ProfileEditorViewModel? profile) => CanReorder(profile) && Items.IndexOf(profile!) < Items.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp(ProfileEditorViewModel? profile) { if (profile is not null) MoveProfile(profile, -1); }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown(ProfileEditorViewModel? profile) { if (profile is not null) MoveProfile(profile, 1); }

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private void MoveToTop(ProfileEditorViewModel? profile) { if (profile is not null) MoveProfileToIndex(profile, 0); }

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private void MoveToBottom(ProfileEditorViewModel? profile) { if (profile is not null) MoveProfileToIndex(profile, Items.Count - 1); }

    private bool CanUseProfile(ProfileEditorViewModel? profile) => CanEdit && profile is not null && Items.Contains(profile);
    [RelayCommand(CanExecute = nameof(CanUseProfile))]
    private void MakeDefault(ProfileEditorViewModel? profile)
    {
        if (profile is null) return;
        _defaultProfileId = profile.Id;
        foreach (var item in Items) item.IsDefault = item.Id == _defaultProfileId;
        ScheduleSave();
    }

    private bool CanDuplicate(ProfileEditorViewModel? profile) => CanAddProfile() && profile?.IsValid == true && Items.Contains(profile);
    [RelayCommand(CanExecute = nameof(CanDuplicate))]
    private void Duplicate(ProfileEditorViewModel? profile)
    {
        if (profile is null || !profile.TryGetSnapshot(out var source)) return;
        AddEditor(source with { Id = Guid.NewGuid(), Name = UniqueName(L("Profile_CopyName", source.Name)) });
        SelectedProfile = Items[^1];
        ScheduleSave(); UpdateCommands();
    }

    private bool CanDelete(ProfileEditorViewModel? profile) => CanUseProfile(profile) && Items.Count > 1;
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(ProfileEditorViewModel? profile)
    {
        if (profile is null) return;
        try
        {
            if (profile == SelectedProfile && _mouseButtonHold?.IsHeld == true)
                await _mouseButtonHold.ReleaseIfHeldAsync();
            if (Settings.ConfirmBeforeDeletingProfile && !await _dialogs.ConfirmAsync(L("Dialog_DeleteProfile_Title"), L("Dialog_DeleteProfile_Message", profile.Name), L("Common_Delete"))) return;
            if (!CanDelete(profile)) return;
            var wasSelected = SelectedProfile == profile;
            profile.Changed -= ScheduleSave;
            profile.PropertyChanged -= OnProfileEditorPropertyChanged;
            profile.TabChanged -= OnProfileTabChanged;
            if (profile.IsRenaming) CancelRename(profile);
            Items.Remove(profile);
            if (_defaultProfileId == profile.Id) _defaultProfileId = Items[0].Id;
            foreach (var item in Items) item.IsDefault = item.Id == _defaultProfileId;
            if (wasSelected) SelectedProfile = Items.First(item => item.Id == _defaultProfileId);
            ScheduleSave(); UpdateCommands();
        }
        catch (Exception exception) { ReportError(exception, L("Error_DeleteProfile")); }
    }

    [RelayCommand]
    private void OpenConfigurationFolder()
    {
        try { _folders.Open(ConfigurationDirectory); }
        catch (Exception exception) { ReportError(exception, L("Error_OpenFolder")); }
    }

    [RelayCommand]
    private async Task SaveNowAsync() => await FlushAsync();

    private string UniqueName(string seed)
    {
        var name = seed.Length > 70 ? seed[..70] : seed;
        var candidate = name;
        for (var number = 2; Items.Any(item => item.NormalizedName.Equals(candidate, StringComparison.OrdinalIgnoreCase)); number++)
            candidate = $"{name} {number}";
        return candidate;
    }

    private bool TrySnapshot(out ApplicationData snapshot)
    {
        var profiles = new List<AutoClickerProfile>();
        var valid = true;
        var duplicateNames = Items.GroupBy(item => item.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var editor in Items)
        {
            editor.SetDuplicateName(duplicateNames.Contains(editor.NormalizedName));
            if (!editor.TryGetSnapshot(out var profile)) valid = false;
            profiles.Add(profile);
        }
        var settings = Settings.GetSnapshot();
        snapshot = new ApplicationData
        {
            Profiles = profiles, DefaultProfileId = _defaultProfileId,
            SelectedProfileId = settings.RememberLastSelectedProfile ? SelectedProfile?.Id ?? _defaultProfileId : _defaultProfileId,
            Settings = settings
        };
        if (!valid) return false;
        try { ApplicationDataValidator.Validate(snapshot); return true; }
        catch (DataValidationException exception) { ErrorMessage = _localization?.TranslateValidation(exception.Message) ?? exception.Message; return false; }
    }

    private void ScheduleSave()
    {
        if (_loading || !CanEdit) return;
        ErrorMessage = "";
        if (!TrySnapshot(out var snapshot))
        {
            _autosave.CancelPending();
            SaveStatus = L("Status_FixInvalid");
            UpdateCommands();
            return;
        }
        SaveStatus = L("Status_Saving");
        _autosave.Schedule(snapshot);
        UpdateCommands();
    }

    public async Task<bool> FlushAsync()
    {
        if (!IsLoaded) { await _autosave.CancelAndWaitAsync(); return true; }
        if (!TrySnapshot(out var snapshot))
        {
            await _autosave.CancelAndWaitAsync();
            SaveStatus = L("Status_FixInvalid");
            return false;
        }
        var result = await _autosave.FlushAsync(snapshot);
        SaveStatus = result.Success ? L("Status_Saved") : L("Status_NotSaved");
        ErrorMessage = _localization?.TranslateKnownText(result.ErrorMessage ?? "") ?? result.ErrorMessage ?? "";
        return result.Success;
    }

    public void ReportError(Exception exception, string message)
    {
        _logger.LogError(exception, "{UserMessage}", message);
        ErrorMessage = message;
    }

    private void UpdateCommands()
    {
        NewProfileCommand.NotifyCanExecuteChanged(); DuplicateCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged(); MakeDefaultCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged(); MoveDownCommand.NotifyCanExecuteChanged();
        MoveToTopCommand.NotifyCanExecuteChanged(); MoveToBottomCommand.NotifyCanExecuteChanged();
        ToggleEngineCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        CancelProfileDrag();
        if (SelectedProfile is { } selected)
        {
            selected.HotkeyChanged -= OnProfileHotkeyChanged;
            selected.RecordingChanged -= OnRecordingChanged;
            selected.MouseHoldConfigurationChanged -= OnMouseHoldConfigurationChanged;
            selected.HoldButtonModeConfigurationChanged -= OnHoldButtonModeConfigurationChanged;
            selected.HoldMouseButtonHotkeyChanged -= OnHoldMouseButtonHotkeyChanged;
            selected.PickPositionRequested -= OnPickPositionRequested;
        }
        _autosave.Completed -= OnSaveCompleted;
        if (_engine is not null)
        { _engine.TelemetryChanged -= OnEngineTelemetryChanged; _engine.Error -= OnEngineError; }
        Settings.Changed -= OnSettingsChanged;
        if (_localization is not null) _localization.LanguageChanged -= OnLanguageChanged;
        if (_usageStatistics is not null) _usageStatistics.Changed -= OnUsageStatisticsChanged;
        if (_mouseButtonHold is not null) _mouseButtonHold.Changed -= OnMouseButtonHoldChanged;
        if (_hotkeys is not null) { _hotkeys.Pressed -= OnHotkeyPressed; _hotkeys.Released -= OnHotkeyReleased; }
        if (_mouseInputMonitor is not null) _mouseInputMonitor.MouseButtonChanged -= OnMouseButtonChanged;
        foreach (var item in Items)
        { item.Changed -= ScheduleSave; item.PropertyChanged -= OnProfileEditorPropertyChanged; item.TabChanged -= OnProfileTabChanged; }
    }
}

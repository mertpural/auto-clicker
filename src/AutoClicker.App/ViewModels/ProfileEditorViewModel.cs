using System.ComponentModel;
using AutoClicker.App.Services;
using AutoClicker.Core.Models;
using AutoClicker.Core.Timing;
using AutoClicker.Core.Validation;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProfileColorKind = AutoClicker.Core.Models.ProfileColor;

namespace AutoClicker.App.ViewModels;

public abstract class LocalizedOption(string label) : ObservableObject
{
    private string _label = label;
    public string Label { get => _label; internal set => SetProperty(ref _label, value); }
}
public sealed class ClickModeOption(ClickMode value, string label) : LocalizedOption(label)
{ public ClickMode Value { get; } = value; }
public sealed class LocalizedEnumOption<T>(T value, string label) : LocalizedOption(label)
{ public T Value { get; } = value; }

public partial class ProfileEditorViewModel : ObservableObject
{
    private readonly AutoClickerProfile _original;
    private readonly LocalizationService _localization;
    private bool _initializing = true;
    private bool _synchronizingSpeed;
    private bool _duplicateName;
    private string? _renameValidationKey;
    private IReadOnlyList<ValidationIssue> _issues = [];
    public enum DurationUnitOption { Seconds, Minutes, Hours }
    private bool _synchronizingDuration;
    public Guid Id => _original.Id;
    public string NormalizedName => (Name ?? "").Trim();
    public event Action? Changed;
    public event Action? HotkeyChanged;
    public event Action<bool>? RecordingChanged;
    public event Action<ProfileEditorViewModel>? PickPositionRequested;
    public event Action? MouseHoldConfigurationChanged;
    public event Action? HoldButtonModeConfigurationChanged;
    public event Action? HoldMouseButtonHotkeyChanged;
    public event Action<int>? TabChanged;
    public LocalizedEnumOption<MouseButton>[] MouseButtons { get; }
    public LocalizedEnumOption<MouseButton>[] HoldMouseButtons => MouseButtons;
    public LocalizedEnumOption<ClickType>[] ClickTypes { get; }
    public ClickMode[] ClickModes { get; } = Enum.GetValues<ClickMode>();
    public ClickModeOption[] ClickModeOptions { get; }
    public LocalizedEnumOption<ClickPositionMode>[] PositionModes { get; }
    public LocalizedEnumOption<ProfileColorKind>[] ProfileColors { get; }
    public LocalizedEnumOption<DurationUnitOption>[] DurationUnitsOptions { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _renameText = "";
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private bool _dropAfter;
    [ObservableProperty] private ProfileColorKind _profileColor;
    [ObservableProperty] private MouseButton _mouseButton;
    [ObservableProperty] private ClickType _clickType;
    [ObservableProperty] private ClickMode _clickMode;
    [ObservableProperty] private MouseButton _holdMouseButton;
    [ObservableProperty] private ClickPositionMode _positionMode;
    [ObservableProperty] private decimal? _fixedX;
    [ObservableProperty] private decimal? _fixedY;
    [ObservableProperty] private decimal? _cps;
    [ObservableProperty] private decimal? _intervalMilliseconds;
    [ObservableProperty] private bool _randomizationEnabled;
    [ObservableProperty] private decimal? _minimumCps;
    [ObservableProperty] private decimal? _maximumCps;
    [ObservableProperty] private string _hotkeyText = "";
    [ObservableProperty] private string _holdMouseButtonHotkeyText = "";
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _totalClicksText = "0";
    [ObservableProperty] private string _totalActiveTimeText = "0";
    [ObservableProperty] private bool _isPickingPosition;
    [ObservableProperty] private string _positionPickerStatus = "";
    [ObservableProperty] private string _mouseHoldStatus = "";
    [ObservableProperty] private bool _isRecording;
    private bool _recordingHoldMouseButtonHotkey;
    [ObservableProperty] private string _recordingMessage = "";
    [ObservableProperty] private bool _startDelayEnabled;
    [ObservableProperty] private decimal? _startDelaySeconds;
    [ObservableProperty] private bool _clickLimitEnabled;
    [ObservableProperty] private decimal? _clickLimit;
    [ObservableProperty] private bool _durationLimitEnabled;
    [ObservableProperty] private decimal? _durationAmount;
    [ObservableProperty] private DurationUnitOption _durationUnit;
    [ObservableProperty] private bool _burstEnabled;
    [ObservableProperty] private decimal? _clicksPerBurst;
    [ObservableProperty] private decimal? _burstDelayMilliseconds;
    public DurationUnitOption[] DurationUnits { get; } = Enum.GetValues<DurationUnitOption>();

    public string DefaultLabel => IsDefault ? L("Profile_Default") : "";
    public string NameError => _renameValidationKey is not null ? L(_renameValidationKey) : _duplicateName ? L("Validation_DuplicateProfileName") : ErrorFor(nameof(AutoClickerProfile.Name));
    public string ProfileColorHex => ProfileColor switch
    {
        ProfileColorKind.Red => "#C56A70", ProfileColorKind.Orange => "#C98A52", ProfileColorKind.Yellow => "#B9A254",
        ProfileColorKind.Green => "#6C9A76", ProfileColorKind.Teal => "#5A9D9A", ProfileColorKind.Blue => "#6487B5",
        ProfileColorKind.Purple => "#8D78AF", ProfileColorKind.Pink => "#B8799A", _ => "#858B95"
    };
    public IBrush ProfileColorBrush => new SolidColorBrush(Color.Parse(ProfileColorHex));
    public string CpsError => ErrorFor(nameof(AutoClickerProfile.Cps));
    public string IntervalError => IntervalMilliseconds is null or < 2 or > 1000000 ? L("Validation_IntervalRange") : "";
    public string RangeError => string.Join(" ", _issues.Where(issue => issue.Property is nameof(AutoClickerProfile.MinimumCps) or nameof(AutoClickerProfile.MaximumCps)).Select(issue => _localization?.TranslateValidation(issue.Message) ?? issue.Message).Distinct());
    public string HotkeyError => ErrorFor(nameof(AutoClickerProfile.StartStopHotkey));
    public string HoldMouseButtonHotkeyError => ErrorFor(nameof(AutoClickerProfile.HoldMouseButtonHotkey));
    public string ClickModeError => "";
    public bool IsFixedPosition => PositionMode == ClickPositionMode.Fixed;
    public bool IsDropBefore => IsDropTarget && !DropAfter;
    public bool IsDropAfter => IsDropTarget && DropAfter;
    public double DragOpacity => IsDragging ? 0.76 : 1;
    partial void OnIsDraggingChanged(bool value) => OnPropertyChanged(nameof(DragOpacity));
    public bool IsMouseHold => ClickMode == ClickMode.MouseHold;
    public bool IsHoldMouseButtonMode => ClickMode == ClickMode.HoldMouseButton;
    public bool IsCpsModeEnabled => ClickMode != ClickMode.HoldMouseButton;
    public bool IsRandomizationConfigurationEnabled => IsCpsModeEnabled && RandomizationEnabled;
    public bool IsStartStopHotkeyVisible => ClickMode is not (ClickMode.MouseHold or ClickMode.HoldMouseButton);
    public ClickModeOption SelectedClickModeOption
    {
        get => ClickModeOptions.First(option => option.Value == ClickMode);
        set { if (value is not null) ClickMode = value.Value; }
    }
    public LocalizedEnumOption<MouseButton> SelectedMouseButtonOption
    { get => MouseButtons.First(option => option.Value == MouseButton); set { if (value is not null) MouseButton = value.Value; } }
    public LocalizedEnumOption<MouseButton> SelectedHoldMouseButtonOption
    { get => HoldMouseButtons.First(option => option.Value == HoldMouseButton); set { if (value is not null) HoldMouseButton = value.Value; } }
    public LocalizedEnumOption<ClickType> SelectedClickTypeOption
    { get => ClickTypes.First(option => option.Value == ClickType); set { if (value is not null) ClickType = value.Value; } }
    public LocalizedEnumOption<ClickPositionMode> SelectedPositionModeOption
    { get => PositionModes.First(option => option.Value == PositionMode); set { if (value is not null) PositionMode = value.Value; } }
    public LocalizedEnumOption<ProfileColorKind> SelectedProfileColorOption
    { get => ProfileColors.First(option => option.Value == ProfileColor); set { if (value is not null) ProfileColor = value.Value; } }
    public LocalizedEnumOption<DurationUnitOption> SelectedDurationUnitOption
    { get => DurationUnitsOptions.First(option => option.Value == DurationUnit); set { if (value is not null) DurationUnit = value.Value; } }
    public bool HasPositionPickerStatus => !string.IsNullOrWhiteSpace(PositionPickerStatus);
    public bool HasMouseHoldStatus => !string.IsNullOrWhiteSpace(MouseHoldStatus);
    public bool IsValid => _issues.Count == 0 && !_duplicateName && IntervalError.Length == 0 && ClickModeError.Length == 0;
    public string ValidationMessage => string.Join(" ", _issues.Select(issue => _localization.TranslateValidation(issue.Message))
        .Concat(_duplicateName ? [NameError] : Array.Empty<string>())
        .Concat(ClickModeError.Length != 0 ? [ClickModeError] : Array.Empty<string>()).Distinct());
    public string RecordingLabel => IsRecording
        ? RecordingMessage.Length == 0 ? L("Hotkey_PressCombination") : RecordingMessage
        : ClickMode == ClickMode.HoldMouseButton ? HoldMouseButtonHotkeyText : HotkeyText;

    public ProfileEditorViewModel(AutoClickerProfile profile, LocalizationService? localization = null)
    {
        _localization = localization ?? new LocalizationService();
        if (localization is null) _localization.SetLanguage(AutoClicker.Core.Models.ApplicationLanguage.English);
        MouseButtons = CreateOptions<MouseButton>("MouseButton");
        ClickTypes = CreateOptions<ClickType>("ClickType");
        ClickModeOptions = Enum.GetValues<ClickMode>().Select(value => new ClickModeOption(value, L($"Enum_ClickMode_{value}"))).ToArray();
        PositionModes = CreateOptions<ClickPositionMode>("Position");
        ProfileColors = CreateOptions<ProfileColorKind>("ProfileColor");
        DurationUnitsOptions = CreateOptions<DurationUnitOption>("Duration");
        ProfileValidator.ThrowIfInvalid(profile);
        _original = profile;
        Name = profile.Name; RenameText = profile.Name; ProfileColor = profile.ProfileColor; MouseButton = profile.MouseButton; ClickType = profile.ClickType;
        ClickMode = profile.ClickMode; HoldMouseButton = profile.HoldMouseButton; PositionMode = profile.PositionMode;
        FixedX = profile.FixedX; FixedY = profile.FixedY; Cps = (decimal)profile.Cps;
        IntervalMilliseconds = 1000m / (decimal)profile.Cps;
        RandomizationEnabled = profile.RandomizationEnabled;
        MinimumCps = (decimal)profile.MinimumCps; MaximumCps = (decimal)profile.MaximumCps;
        HotkeyText = profile.StartStopHotkey.ToString();
        HoldMouseButtonHotkeyText = profile.HoldMouseButtonHotkey.ToString();
        StartDelayEnabled = profile.StartDelayMilliseconds > 0;
        StartDelaySeconds = profile.StartDelayMilliseconds > 0 ? profile.StartDelayMilliseconds / 1000m : 3;
        ClickLimitEnabled = profile.ClickLimitEnabled; ClickLimit = profile.ClickLimit;
        DurationLimitEnabled = profile.DurationLimitEnabled; DurationUnit = DurationUnitOption.Seconds;
        DurationAmount = Math.Max(0.001m, profile.DurationLimit.Ticks / (decimal)TimeSpan.TicksPerSecond);
        BurstEnabled = profile.BurstEnabled; ClicksPerBurst = profile.ClicksPerBurst;
        BurstDelayMilliseconds = profile.BurstDelayMilliseconds;
        _initializing = false;
        Validate();
    }

    private string L(string key) => _localization.Get(key);
    public System.Globalization.NumberFormatInfo NumberFormat => _localization.Culture.NumberFormat;

    private LocalizedEnumOption<T>[] CreateOptions<T>(string prefix) where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new LocalizedEnumOption<T>(value, L($"Enum_{prefix}_{value}"))).ToArray();
    private void RefreshOptions<T>(IEnumerable<LocalizedEnumOption<T>> options, string prefix)
    { foreach (var option in options) option.Label = L($"Enum_{prefix}_{option.Value}"); }

    public void UpdateStatistics(string totalClicks, string activeTime)
    { TotalClicksText = totalClicks; TotalActiveTimeText = activeTime; }

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(NumberFormat));
        // Keep collection and item identities stable while a two-way selection binding is running.
        RefreshOptions(MouseButtons, "MouseButton"); RefreshOptions(ClickTypes, "ClickType");
        RefreshOptions(PositionModes, "Position"); RefreshOptions(ProfileColors, "ProfileColor");
        RefreshOptions(DurationUnitsOptions, "Duration");
        foreach (var option in ClickModeOptions) option.Label = L($"Enum_ClickMode_{option.Value}");
        if (_localization is not null)
        {
            PositionPickerStatus = _localization.TranslateKnownText(PositionPickerStatus);
            MouseHoldStatus = _localization.TranslateKnownText(MouseHoldStatus);
            RecordingMessage = _localization.TranslateKnownText(RecordingMessage);
        }
        foreach (var property in new[] { nameof(DefaultLabel), nameof(NameError), nameof(CpsError), nameof(IntervalError), nameof(RangeError), nameof(HotkeyError), nameof(HoldMouseButtonHotkeyError), nameof(ValidationMessage), nameof(RecordingLabel) })
            OnPropertyChanged(property);
    }

    partial void OnCpsChanged(decimal? value)
    {
        if (_initializing || _synchronizingSpeed) return;
        _synchronizingSpeed = true;
        IntervalMilliseconds = value >= (decimal)ClickTiming.MinimumCps && value <= (decimal)ClickTiming.MaximumCps
            ? 1000m / value.Value : null;
        _synchronizingSpeed = false;
    }

    partial void OnIntervalMillisecondsChanged(decimal? value)
    {
        if (_initializing || _synchronizingSpeed) return;
        _synchronizingSpeed = true;
        Cps = value is >= 2 and <= 1000000 ? 1000m / value.Value : null;
        _synchronizingSpeed = false;
    }

    partial void OnIsDefaultChanged(bool value) => OnPropertyChanged(nameof(DefaultLabel));
    partial void OnSelectedTabIndexChanged(int value) => TabChanged?.Invoke(value);
    partial void OnPositionModeChanged(ClickPositionMode value) => OnPropertyChanged(nameof(IsFixedPosition));
    partial void OnClickModeChanged(ClickMode value)
    {
        OnPropertyChanged(nameof(IsMouseHold)); OnPropertyChanged(nameof(IsHoldMouseButtonMode));
        OnPropertyChanged(nameof(IsCpsModeEnabled)); OnPropertyChanged(nameof(IsStartStopHotkeyVisible));
        OnPropertyChanged(nameof(IsRandomizationConfigurationEnabled)); OnPropertyChanged(nameof(SelectedClickModeOption));
        OnPropertyChanged(nameof(RecordingLabel));
        MouseHoldConfigurationChanged?.Invoke(); HoldButtonModeConfigurationChanged?.Invoke();
    }
    partial void OnRandomizationEnabledChanged(bool value) => OnPropertyChanged(nameof(IsRandomizationConfigurationEnabled));
    partial void OnHoldMouseButtonChanged(MouseButton value) => MouseHoldConfigurationChanged?.Invoke();
    partial void OnMouseButtonChanged(MouseButton value) => HoldButtonModeConfigurationChanged?.Invoke();
    partial void OnPositionPickerStatusChanged(string value) => OnPropertyChanged(nameof(HasPositionPickerStatus));
    partial void OnMouseHoldStatusChanged(string value) => OnPropertyChanged(nameof(HasMouseHoldStatus));
    partial void OnIsRecordingChanged(bool value)
    {
        RecordingMessage = "";
        OnPropertyChanged(nameof(RecordingLabel));
        RecordingChanged?.Invoke(value);
    }
    partial void OnRecordingMessageChanged(string value) => OnPropertyChanged(nameof(RecordingLabel));
    partial void OnHotkeyTextChanged(string value)
    {
        OnPropertyChanged(nameof(RecordingLabel));
        if (!_initializing) HotkeyChanged?.Invoke();
    }
    partial void OnHoldMouseButtonHotkeyTextChanged(string value)
    {
        OnPropertyChanged(nameof(HoldMouseButtonHotkeyError));
        OnPropertyChanged(nameof(RecordingLabel));
        if (!_initializing) HoldMouseButtonHotkeyChanged?.Invoke();
    }
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(NormalizedName));
    partial void OnProfileColorChanged(ProfileColorKind value)
    { OnPropertyChanged(nameof(ProfileColorHex)); OnPropertyChanged(nameof(ProfileColorBrush)); }
    partial void OnIsDropTargetChanged(bool value)
    { OnPropertyChanged(nameof(IsDropBefore)); OnPropertyChanged(nameof(IsDropAfter)); }
    partial void OnDropAfterChanged(bool value)
    { OnPropertyChanged(nameof(IsDropBefore)); OnPropertyChanged(nameof(IsDropAfter)); }
    partial void OnDurationUnitChanged(DurationUnitOption value)
    {
        if (_initializing || _synchronizingDuration) return;
        _synchronizingDuration = true;
        var seconds = DurationToSeconds(DurationAmount ?? 0, _lastDurationUnit);
        DurationAmount = SecondsToDuration(seconds, value);
        _lastDurationUnit = value;
        _synchronizingDuration = false;
    }

    private DurationUnitOption _lastDurationUnit = DurationUnitOption.Seconds;
    private static decimal DurationToSeconds(decimal amount, DurationUnitOption unit) => unit switch
    {
        DurationUnitOption.Seconds => amount,
        DurationUnitOption.Minutes => amount * 60m,
        DurationUnitOption.Hours => amount * 3600m,
        _ => 0
    };
    private static decimal SecondsToDuration(decimal seconds, DurationUnitOption unit) => unit switch
    {
        DurationUnitOption.Seconds => seconds,
        DurationUnitOption.Minutes => seconds / 60m,
        DurationUnitOption.Hours => seconds / 3600m,
        _ => 0
    };

    private int StartDelayMs => TryMilliseconds(StartDelaySeconds, out var value) ? value : -1;
    private static bool TryMilliseconds(decimal? seconds, out int value)
    {
        var milliseconds = (seconds ?? 0) * 1000m;
        if (milliseconds < 0 || milliseconds > int.MaxValue) { value = -1; return false; }
        value = decimal.ToInt32(decimal.Round(milliseconds, 0, MidpointRounding.AwayFromZero));
        return true;
    }

    private TimeSpan DurationValue
    {
        get
        {
            try
            {
                var ticks = DurationToSeconds(DurationAmount ?? 0, DurationUnit) * TimeSpan.TicksPerSecond;
                return ticks is < 0 or > long.MaxValue ? TimeSpan.Zero : TimeSpan.FromTicks(decimal.ToInt64(decimal.Round(ticks, 0, MidpointRounding.AwayFromZero)));
            }
            catch (OverflowException) { return TimeSpan.Zero; }
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (_initializing || _synchronizingSpeed) return;
        if (args.PropertyName is nameof(Name) or nameof(ProfileColor) or nameof(MouseButton) or nameof(ClickType) or nameof(ClickMode)
            or nameof(HoldMouseButton) or nameof(PositionMode) or nameof(FixedX) or nameof(FixedY)
            or nameof(Cps) or nameof(IntervalMilliseconds) or nameof(RandomizationEnabled)
            or nameof(MinimumCps) or nameof(MaximumCps) or nameof(HotkeyText) or nameof(HoldMouseButtonHotkeyText)
            or nameof(StartDelayEnabled) or nameof(StartDelaySeconds) or nameof(ClickLimitEnabled) or nameof(ClickLimit)
            or nameof(DurationLimitEnabled) or nameof(DurationAmount) or nameof(DurationUnit) or nameof(BurstEnabled)
            or nameof(ClicksPerBurst) or nameof(BurstDelayMilliseconds))
        {
            Validate();
            Changed?.Invoke();
        }
    }

    public void SetDuplicateName(bool value)
    {
        _duplicateName = value;
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    public void BeginRename()
    {
        RenameText = Name;
        SetRenameValidationMessage(null);
        IsRenaming = true;
    }

    public void CancelRename()
    {
        RenameText = Name;
        SetRenameValidationMessage(null);
        IsRenaming = false;
    }

    public void SetRenameValidationMessage(string? resourceKey)
    {
        _renameValidationKey = resourceKey;
        OnPropertyChanged(nameof(NameError));
    }

    public void FinishRename(string committedName)
    {
        Name = committedName;
        RenameText = committedName;
        SetRenameValidationMessage(null);
        IsRenaming = false;
    }

    public bool TryGetSnapshot(out AutoClickerProfile snapshot)
    {
        Validate();
        snapshot = BuildSnapshot();
        return IsValid;
    }

    private AutoClickerProfile BuildSnapshot()
    {
        Hotkey.TryParse(HotkeyText, out var hotkey);
        Hotkey.TryParse(HoldMouseButtonHotkeyText, out var holdMouseButtonHotkey);
        return _original with
        {
            Name = NormalizedName, ProfileColor = ProfileColor, MouseButton = MouseButton, ClickType = ClickType, ClickMode = ClickMode, HoldMouseButton = HoldMouseButton,
            PositionMode = PositionMode, FixedX = ToCoordinate(FixedX), FixedY = ToCoordinate(FixedY),
            Cps = (double)(Cps ?? 0), RandomizationEnabled = RandomizationEnabled,
            MinimumCps = (double)(MinimumCps ?? 0), MaximumCps = (double)(MaximumCps ?? 0),
            StartStopHotkey = hotkey!, HoldMouseButtonHotkey = holdMouseButtonHotkey!,
            StartDelayMilliseconds = StartDelayEnabled ? StartDelayMs : 0,
            ClickLimitEnabled = ClickLimitEnabled,
            ClickLimit = ClickLimit is >= 0 and <= long.MaxValue ? decimal.ToInt64(ClickLimit.Value) : -1,
            DurationLimitEnabled = DurationLimitEnabled,
            DurationLimit = DurationValue,
            BurstEnabled = BurstEnabled,
            ClicksPerBurst = ClicksPerBurst is >= 1 and <= int.MaxValue ? decimal.ToInt32(ClicksPerBurst.Value) : 0,
            BurstDelayMilliseconds = BurstDelayMilliseconds is >= 0 and <= int.MaxValue ? decimal.ToInt32(BurstDelayMilliseconds.Value) : -1
        };
    }

    private static int? ToCoordinate(decimal? value) => value is >= int.MinValue and <= int.MaxValue ? decimal.ToInt32(value.Value) : null;

    private void Validate()
    {
        _issues = ProfileValidator.Validate(BuildSnapshot());
        foreach (var property in new[] { nameof(NameError), nameof(CpsError), nameof(IntervalError), nameof(RangeError), nameof(HotkeyError), nameof(HoldMouseButtonHotkeyError), nameof(ClickModeError), nameof(IsValid), nameof(ValidationMessage) })
            OnPropertyChanged(property);
    }

    private string ErrorFor(string property) => string.Join(" ", _issues.Where(issue => issue.Property == property).Select(issue => _localization?.TranslateValidation(issue.Message) ?? issue.Message));

    [RelayCommand]
    private void RecordHotkey() { _recordingHoldMouseButtonHotkey = false; IsRecording = !IsRecording; }

    [RelayCommand]
    private void RecordHoldMouseButtonHotkey() { _recordingHoldMouseButtonHotkey = true; IsRecording = !IsRecording; }

    [RelayCommand]
    private void PickPosition() => PickPositionRequested?.Invoke(this);

    public void CompleteRecording(Hotkey hotkey)
    {
        if (_recordingHoldMouseButtonHotkey) HoldMouseButtonHotkeyText = hotkey.ToString();
        else HotkeyText = hotkey.ToString();
        _recordingHoldMouseButtonHotkey = false;
        IsRecording = false;
    }

    public void SetPickedPosition(ScreenPoint point)
    {
        FixedX = point.X;
        FixedY = point.Y;
        PositionMode = ClickPositionMode.Fixed;
    }
}

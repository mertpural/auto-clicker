using System.Collections;
using System.Globalization;
using System.Resources;
using Avalonia;
using AutoClicker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Services;

/// <summary>Loads embedded string resources and publishes their current values as Avalonia dynamic resources.</summary>
public sealed class LocalizationService
{
    private readonly ResourceManager _resources = new("AutoClicker.App.src.AutoClicker.App.Localization.Strings", typeof(LocalizationService).Assembly);
    private readonly ILogger<LocalizationService>? _logger;
    private Application? _application;
    private static readonly IReadOnlyDictionary<string, string> ValidationKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Profile ID cannot be empty."] = "Validation_ProfileIdEmpty",
        ["Enter a name between 1 and 80 characters."] = "Validation_ProfileNameLength",
        ["Choose a supported profile color."] = "Validation_ProfileColor",
        ["CPS must be between 0.001 and 500."] = "Validation_CpsRange",
        ["Minimum CPS must be between 0.001 and 500."] = "Validation_MinimumCpsRange",
        ["Maximum CPS must be between 0.001 and 500."] = "Validation_MaximumCpsRange",
        ["Minimum CPS cannot exceed maximum CPS."] = "Validation_CpsOrder",
        ["Choose a supported mouse button."] = "Validation_MouseButton",
        ["Choose a supported click type."] = "Validation_ClickType",
        ["Choose a supported click mode."] = "Validation_ClickMode",
        ["Choose a supported position mode."] = "Validation_PositionMode",
        ["Choose a supported hold button."] = "Validation_HoldMouseButton",
        ["Enabled click limit must be at least 1."] = "Validation_ClickLimit",
        ["Enabled duration limit must be greater than zero."] = "Validation_DurationLimit",
        ["Start delay cannot be negative."] = "Validation_StartDelay",
        ["A burst must contain at least 1 click."] = "Validation_BurstSize",
        ["Burst delay cannot be negative."] = "Validation_BurstDelay",
        ["Choose a start / stop shortcut."] = "Validation_StartStopHotkey",
        ["Choose a hold/release shortcut."] = "Validation_HoldHotkey",
        ["Hold/release and start/stop shortcuts must be different."] = "Validation_HotkeyConflict",
        ["Hold/release shortcut is reserved for Emergency Stop."] = "Validation_HotkeyEmergencyConflict",
        ["Enter a fixed X coordinate."] = "Validation_FixedX",
        ["Enter a fixed Y coordinate."] = "Validation_FixedY",
        ["This configuration was created with an unsupported application version."] = "Validation_UnsupportedVersion",
        ["Configuration must contain between 1 and 1000 profiles."] = "Validation_ProfileCount",
        ["Profile IDs must be unique."] = "Validation_ProfileIdsUnique",
        ["Profile names must be unique."] = "Validation_ProfileNamesUnique",
        ["Default and selected profiles must exist."] = "Validation_ProfileReferences",
        ["Application settings are invalid."] = "Validation_SettingsInvalid",
        ["Lifetime statistics are invalid."] = "Validation_StatisticsInvalid",
        ["Emergency stop must differ from every start / stop shortcut."] = "Validation_EmergencyStartConflict",
        ["Emergency stop must differ from every hold/release shortcut."] = "Validation_EmergencyHoldConflict"
    };

    public ApplicationLanguage Language { get; private set; } = ApplicationLanguage.Turkish;
    public CultureInfo Culture => Language == ApplicationLanguage.English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("tr-TR");
    public event EventHandler? LanguageChanged;

    public LocalizationService(ILogger<LocalizationService>? logger = null) => _logger = logger;

    public IReadOnlyList<string> Keys => GetKeys();

    public bool HasTranslation(string key, ApplicationLanguage language)
    {
        var culture = language == ApplicationLanguage.English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.InvariantCulture;
        return _resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)?.GetString(key, ignoreCase: false) is not null;
    }

    public string Get(string key, params object?[] arguments)
    {
        var value = _resources.GetString(key, Culture);
        if (value is null)
        {
            _logger?.LogWarning("Localization key {LocalizationKey} is missing for {Language}", key, Language);
            value = _resources.GetString(key, CultureInfo.GetCultureInfo("en-US")) ?? key;
        }
        return arguments.Length == 0 ? value : string.Format(Culture, value, arguments);
    }

    public string TranslateValidation(string message)
    {
        if (ValidationKeys.TryGetValue(message, out var key)) return Get(key);
        foreach (var pair in ValidationKeys.OrderByDescending(item => item.Key.Length))
            message = message.Replace(pair.Key, Get(pair.Value), StringComparison.Ordinal);
        return message;
    }

    public string TranslateKnownText(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message;
        foreach (var key in GetKeys())
        {
            var english = _resources.GetString(key, CultureInfo.GetCultureInfo("en-US"));
            var turkish = _resources.GetString(key, CultureInfo.InvariantCulture);
            if (string.Equals(message, english, StringComparison.Ordinal) || string.Equals(message, turkish, StringComparison.Ordinal)) return Get(key);
        }
        foreach (var templateKey in new[] { "Status_HoldingMouse", "Position_MouseHoldReady", "Status_MouseHoldReady" })
        foreach (var button in Enum.GetValues<MouseButton>())
        {
            var englishButton = _resources.GetString($"Enum_MouseButton_{button}", CultureInfo.GetCultureInfo("en-US"));
            var turkishButton = _resources.GetString($"Enum_MouseButton_{button}", CultureInfo.InvariantCulture);
            if (string.Equals(message, string.Format(CultureInfo.GetCultureInfo("en-US"), _resources.GetString(templateKey, CultureInfo.GetCultureInfo("en-US"))!, englishButton), StringComparison.Ordinal)
                || string.Equals(message, string.Format(CultureInfo.InvariantCulture, _resources.GetString(templateKey, CultureInfo.InvariantCulture)!, turkishButton), StringComparison.Ordinal))
                return Get(templateKey, Get($"Enum_MouseButton_{button}"));
        }
        foreach (var (englishPrefix, turkishPrefix, key) in new[] { ("Emergency stop: ", "Acil durdurma: ", "Status_EmergencyStop"), ("Profile: ", "Profil: ", "Status_ProfileHotkey"), ("Hold/release: ", "Basılı tut / bırak: ", "Status_HoldHotkey") })
        {
            var prefix = message.StartsWith(englishPrefix, StringComparison.Ordinal) ? englishPrefix : message.StartsWith(turkishPrefix, StringComparison.Ordinal) ? turkishPrefix : null;
            if (prefix is not null) return Get(key, message[prefix.Length..]);
        }
        foreach (var (englishPrefix, turkishPrefix) in new[] { ("Position selected: ", "Konum seçildi: ") })
        {
            var prefix = message.StartsWith(englishPrefix, StringComparison.Ordinal) ? englishPrefix : message.StartsWith(turkishPrefix, StringComparison.Ordinal) ? turkishPrefix : null;
            if (prefix is not null)
            {
                var coordinates = message[prefix.Length..].TrimEnd('.').Split(',', 2);
                if (coordinates.Length == 2 && int.TryParse(coordinates[0].Trim(), out var x) && int.TryParse(coordinates[1].Trim(), out var y))
                    return Get("Position_Selected", x, y);
            }
        }
        var mousePermission = message.Contains("Accessibility permission is needed to control the mouse.", StringComparison.Ordinal)
            || message.Contains("Mouse’u kontrol etmek için Erişilebilirlik izni gerekiyor.", StringComparison.Ordinal);
        var inputPermission = message.Contains("Input Monitoring is needed for global shortcuts.", StringComparison.Ordinal)
            || message.Contains("Global kısayollar için Giriş İzleme izni gerekiyor.", StringComparison.Ordinal)
            || message.Contains("Input Monitoring, then retry.", StringComparison.Ordinal)
            || message.Contains("Enable AutoClicker under System Settings", StringComparison.Ordinal) && message.Contains("Input Monitoring", StringComparison.Ordinal);
        if (message.Contains("could not create a global keyboard listener", StringComparison.Ordinal))
            return Get("Permission_KeyboardMouseUnavailable");
        if (mousePermission || inputPermission)
        {
            var messages = new List<string>();
            if (mousePermission) messages.Add(Get("Permission_MouseMissing"));
            if (inputPermission) messages.Add(Get("Permission_InputMissing"));
            if (message.Contains("Enable AutoClicker in System Settings", StringComparison.Ordinal)
                || message.Contains("Enable AutoClicker under System Settings", StringComparison.Ordinal)
                || message.Contains("AutoClicker’a Sistem Ayarları", StringComparison.Ordinal)) messages.Add(Get("Permission_SettingsHint"));
            return string.Join(" ", messages);
        }
        return message;
    }

    public string TranslateKnownTextOrFallback(string message, string fallbackKey) =>
        IsKnownText(message) ? TranslateKnownText(message) : Get(fallbackKey);

    private bool IsKnownText(string message)
    {
        foreach (var key in GetKeys())
        {
            if (message == _resources.GetString(key, CultureInfo.GetCultureInfo("en-US"))
                || message == _resources.GetString(key, CultureInfo.InvariantCulture)) return true;
        }
        foreach (var templateKey in new[] { "Status_HoldingMouse", "Position_MouseHoldReady", "Status_MouseHoldReady" })
        foreach (var button in Enum.GetValues<MouseButton>())
        {
            var english = string.Format(CultureInfo.GetCultureInfo("en-US"), _resources.GetString(templateKey, CultureInfo.GetCultureInfo("en-US"))!, _resources.GetString($"Enum_MouseButton_{button}", CultureInfo.GetCultureInfo("en-US")));
            var turkish = string.Format(CultureInfo.InvariantCulture, _resources.GetString(templateKey, CultureInfo.InvariantCulture)!, _resources.GetString($"Enum_MouseButton_{button}", CultureInfo.InvariantCulture));
            if (message == english || message == turkish) return true;
        }
        return message.StartsWith("Emergency stop: ", StringComparison.Ordinal) || message.StartsWith("Acil durdurma: ", StringComparison.Ordinal)
            || message.StartsWith("Profile: ", StringComparison.Ordinal) || message.StartsWith("Profil: ", StringComparison.Ordinal)
            || message.StartsWith("Hold/release: ", StringComparison.Ordinal) || message.StartsWith("Basılı tut / bırak: ", StringComparison.Ordinal)
            || message.StartsWith("Position selected: ", StringComparison.Ordinal) || message.StartsWith("Konum seçildi: ", StringComparison.Ordinal)
            || message.Contains("Accessibility permission is needed to control the mouse.", StringComparison.Ordinal)
            || message.Contains("Input Monitoring is needed for global shortcuts.", StringComparison.Ordinal)
            || message.Contains("Input Monitoring, then retry.", StringComparison.Ordinal)
            || message.Contains("could not create a global keyboard listener", StringComparison.Ordinal)
            || message.Contains("Enable AutoClicker under System Settings", StringComparison.Ordinal)
            || message.Contains("Mouse’u kontrol etmek için Erişilebilirlik izni gerekiyor.", StringComparison.Ordinal)
            || message.Contains("Global kısayollar için Giriş İzleme izni gerekiyor.", StringComparison.Ordinal);
    }

    public void Initialize(Application application, ApplicationLanguage language = ApplicationLanguage.Turkish)
    {
        _application = application;
        Language = Enum.IsDefined(language) ? language : ApplicationLanguage.Turkish;
        ApplyCulture();
        ApplyResources();
    }

    public void SetLanguage(ApplicationLanguage language)
    {
        if (!Enum.IsDefined(language)) language = ApplicationLanguage.Turkish;
        if (Language == language) return;
        Language = language;
        ApplyCulture();
        ApplyResources();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyCulture()
    {
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }

    private string[] GetKeys()
    {
        var set = _resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        return set?.Cast<DictionaryEntry>().Select(item => (string)item.Key).Order(StringComparer.Ordinal).ToArray() ?? [];
    }

    private void ApplyResources()
    {
        if (_application is null) return;
        foreach (var key in GetKeys()) _application.Resources[key] = Get(key);
    }
}

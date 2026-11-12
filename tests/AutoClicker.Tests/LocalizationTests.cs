using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using AutoClicker.App.Services;
using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using AutoClicker.App.ViewModels;
using Xunit;

namespace AutoClicker.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void Every_dynamic_resource_in_views_has_a_localization_or_theme_definition()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AutoClicker.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, "src/AutoClicker.App");
        var localization = new LocalizationService();
        var defined = localization.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(directory, "App.axaml")), "x:Key=\"([^\"]+)\""))
            defined.Add(((System.Text.RegularExpressions.Match)match).Groups[1].Value);
        foreach (var file in Directory.EnumerateFiles(directory, "*.axaml", SearchOption.AllDirectories))
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "DynamicResource\\s+([^}]+)"))
            Assert.True(defined.Contains(match.Groups[1].Value), $"Undefined resource {match.Groups[1].Value} in {Path.GetFileName(file)}");
    }

    [Fact]
    public void Turkish_is_the_default_for_new_and_legacy_settings()
    {
        Assert.Equal(ApplicationLanguage.Turkish, new ApplicationSettings().Language);
        var legacy = JsonSerializer.Deserialize<ApplicationSettings>("{}", ProfileJson.CreateOptions());
        Assert.Equal(ApplicationLanguage.Turkish, legacy!.Language);
    }

    [Fact]
    public async Task Language_and_theme_persist_and_legacy_settings_without_language_load_as_Turkish()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AutoClicker.Tests", Guid.NewGuid().ToString("N"));
        using var store = new JsonApplicationStore(directory);
        var data = await store.LoadAsync();
        await store.SaveAsync(data with { Settings = data.Settings with { Language = ApplicationLanguage.English, Appearance = Appearance.Graphite } });
        using var reopened = new JsonApplicationStore(directory);
        var restoredSettings = (await reopened.LoadAsync()).Settings;
        Assert.Equal(ApplicationLanguage.English, restoredSettings.Language);
        Assert.Equal(Appearance.Graphite, restoredSettings.Appearance);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(reopened.FilePath))!.AsObject();
        json["settings"]!.AsObject().Remove("language");
        await File.WriteAllTextAsync(reopened.FilePath, json.ToJsonString());
        using var legacyStore = new JsonApplicationStore(directory);
        Assert.Equal(ApplicationLanguage.Turkish, (await legacyStore.LoadAsync()).Settings.Language);
    }

    [Fact]
    public void Runtime_switch_updates_settings_and_resource_service_in_both_directions()
    {
        var localization = new LocalizationService();
        var settings = new SettingsViewModel(new NoopThemeService(), localization: localization);
        settings.Load(new ApplicationSettings());
        Assert.Equal(ApplicationLanguage.Turkish, settings.Language);
        Assert.Equal("Profiller", localization.Get("Ui_Profiles"));

        var changes = 0;
        localization.LanguageChanged += (_, _) => changes++;
        settings.SelectedLanguageOption = settings.LanguageOptions.Single(option => option.Value == ApplicationLanguage.English);
        Assert.Equal(ApplicationLanguage.English, settings.GetSnapshot().Language);
        Assert.Equal("Profiles", localization.Get("Ui_Profiles"));
        settings.SelectedLanguageOption = settings.LanguageOptions.Single(option => option.Value == ApplicationLanguage.Turkish);
        Assert.Equal("Profiller", localization.Get("Ui_Profiles"));
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(ApplicationLanguage.Turkish, ApplicationLanguage.English, ApplicationLanguage.Turkish)]
    [InlineData(ApplicationLanguage.English, ApplicationLanguage.Turkish, ApplicationLanguage.English)]
    public void Repeated_runtime_language_switches_refresh_selector_and_profile_enum_labels(
        ApplicationLanguage first, ApplicationLanguage second, ApplicationLanguage third)
    {
        var localization = new LocalizationService();
        var settings = new SettingsViewModel(new NoopThemeService(), localization: localization);
        var initial = new ApplicationSettings { Language = first };
        settings.Load(initial);
        localization.SetLanguage(first);
        var editor = new ProfileEditorViewModel(new AutoClickerProfile(), localization);

        void AssertLanguage(ApplicationLanguage language)
        {
            Assert.Equal(language, settings.SelectedLanguageOption.Value);
            Assert.Equal(language == ApplicationLanguage.Turkish ? "Aç / Kapat" : "Toggle",
                editor.SelectedClickModeOption.Label);
            Assert.Equal(language == ApplicationLanguage.Turkish ? "Sol" : "Left",
                editor.SelectedMouseButtonOption.Label);
            Assert.Equal(language == ApplicationLanguage.Turkish ? "İmlecin Bulunduğu Yer" : "Current Cursor",
                editor.SelectedPositionModeOption.Label);
            Assert.Equal(language == ApplicationLanguage.Turkish ? "tr-TR" : "en-US", CultureInfo.CurrentCulture.Name);
        }

        AssertLanguage(first);
        settings.SelectedLanguageOption = settings.LanguageOptions.Single(option => option.Value == second);
        editor.RefreshLocalizedText();
        AssertLanguage(second);
        settings.SelectedLanguageOption = settings.LanguageOptions.Single(option => option.Value == third);
        editor.RefreshLocalizedText();
        AssertLanguage(third);
        settings.SelectedLanguageOption = settings.LanguageOptions.Single(option => option.Value == second);
        editor.RefreshLocalizedText();
        AssertLanguage(second);
        settings.Dispose();
    }

    [Fact]
    public void Every_embedded_resource_has_a_translation_in_both_languages()
    {
        var localization = new LocalizationService();
        Assert.NotEmpty(localization.Keys);
        foreach (var key in localization.Keys)
        {
            Assert.True(localization.HasTranslation(key, ApplicationLanguage.Turkish), $"Turkish resource is missing: {key}");
            Assert.True(localization.HasTranslation(key, ApplicationLanguage.English), $"English resource is missing: {key}");
        }
        Assert.Equal("missing-key", localization.Get("missing-key"));
    }

    [Fact]
    public void Profile_tabs_and_theme_labels_are_localized_in_both_languages()
    {
        var localization = new LocalizationService();
        Assert.Equal("Temel", localization.Get("Tab_General"));
        Assert.Equal("Gelişmiş", localization.Get("Tab_Advanced"));
        Assert.Equal("Konum", localization.Get("Tab_Position"));
        Assert.Equal("İstatistik", localization.Get("Tab_Statistics"));
        Assert.Equal("Gece", localization.Get("Enum_Appearance_Night"));
        Assert.Equal("Kızıl Altın", localization.Get("Enum_Appearance_CrimsonGold"));
        Assert.Equal("Grafit", localization.Get("Enum_Appearance_Graphite"));
        Assert.StartsWith("Tıklama hızını", localization.Get("Tooltip_Randomization"));
        localization.SetLanguage(ApplicationLanguage.English);
        Assert.Equal("General", localization.Get("Tab_General"));
        Assert.Equal("Advanced", localization.Get("Tab_Advanced"));
        Assert.Equal("Position", localization.Get("Tab_Position"));
        Assert.Equal("Statistics", localization.Get("Tab_Statistics"));
        Assert.Equal("Night", localization.Get("Enum_Appearance_Night"));
        Assert.Equal("Crimson Gold", localization.Get("Enum_Appearance_CrimsonGold"));
        Assert.Equal("Graphite", localization.Get("Enum_Appearance_Graphite"));
        Assert.Contains("Emergency Stop: F12", localization.Get("Shortcut_Emergency", "F12"));
    }

    [Fact]
    public void Legacy_dark_appearance_migrates_to_night_and_all_themes_apply_at_runtime()
    {
        var theme = new TrackingThemeService();
        var settings = new SettingsViewModel(theme);
        settings.Load(new ApplicationSettings { Appearance = Appearance.Dark });
        Assert.Equal(Appearance.Night, settings.Appearance);
        Assert.Equal(Appearance.Night, settings.GetSnapshot().Appearance);
        Assert.Equal(Appearance.Night, theme.LastApplied);
        Assert.Equal(10, settings.AppearanceOptions.Length);
        Assert.DoesNotContain(settings.AppearanceOptions, option => option.Value == Appearance.Dark);

        foreach (var appearance in settings.Appearances)
        {
            settings.Appearance = appearance;
            Assert.Equal(appearance, theme.LastApplied);
        }
    }

    [Theory]
    [InlineData("Enum_MouseButton_Left", "Sol", "Left")]
    [InlineData("Enum_MouseButton_Right", "Sağ", "Right")]
    [InlineData("Enum_MouseButton_Middle", "Orta", "Middle")]
    [InlineData("Enum_ClickType_Single", "Tek Tıklama", "Single")]
    [InlineData("Enum_ClickType_Double", "Çift Tıklama", "Double")]
    [InlineData("Enum_ClickMode_Toggle", "Aç / Kapat", "Toggle")]
    [InlineData("Enum_ClickMode_Hold", "Klavye ile Basılı Tut", "Keyboard Hold")]
    [InlineData("Enum_ClickMode_MouseHold", "Mouse ile Tetikle", "Mouse Hold Trigger")]
    [InlineData("Enum_ClickMode_HoldMouseButton", "Mouse Tuşunu Basılı Tut", "Hold Mouse Button")]
    [InlineData("Enum_Position_CurrentCursor", "İmlecin Bulunduğu Yer", "Current Cursor")]
    [InlineData("Enum_Position_Fixed", "Sabit Konum", "Fixed Position")]
    [InlineData("Enum_ProfileColor_Teal", "Turkuaz", "Teal")]
    [InlineData("Enum_ProfileColor_Pink", "Pembe", "Pink")]
    public void Enum_display_labels_localize_without_changing_enum_values(string key, string turkish, string english)
    {
        var localization = new LocalizationService();
        Assert.Equal(turkish, localization.Get(key));
        localization.SetLanguage(ApplicationLanguage.English);
        Assert.Equal(english, localization.Get(key));
        Assert.Equal("\"HoldMouseButton\"", JsonSerializer.Serialize(ClickMode.HoldMouseButton, ProfileJson.CreateOptions()));
    }

    [Fact]
    public void Validation_and_status_strings_follow_the_selected_language()
    {
        var localization = new LocalizationService();
        Assert.Equal("Profil adı 1 ile 80 karakter arasında olmalı.", localization.TranslateValidation("Enter a name between 1 and 80 characters."));
        Assert.Equal("Sol mouse tuşu basılı tutuluyor", localization.Get("Status_HoldingMouse", localization.Get("Enum_MouseButton_Left")));
        var turkishHoldHelp = localization.Get("Position_MouseHoldReady", "Sol");
        localization.SetLanguage(ApplicationLanguage.English);
        Assert.Equal("Enter a name between 1 and 80 characters.", localization.TranslateValidation("Enter a name between 1 and 80 characters."));
        Assert.Equal("Holding Left Mouse Button", localization.Get("Status_HoldingMouse", localization.Get("Enum_MouseButton_Left")));
        var englishHoldHelp = localization.Get("Position_MouseHoldReady", "Left");
        Assert.Equal(englishHoldHelp, localization.TranslateKnownText(turkishHoldHelp));
        localization.SetLanguage(ApplicationLanguage.Turkish);
        Assert.Equal(turkishHoldHelp, localization.TranslateKnownText(englishHoldHelp));
    }

    [Fact]
    public void MacOS_permission_and_native_status_messages_are_localized()
    {
        var localization = new LocalizationService();
        Assert.Contains("Erişilebilirlik izni gerekiyor", localization.TranslateKnownText("Accessibility permission is needed to control the mouse. Enable AutoClicker in System Settings → Privacy & Security."));
        Assert.Equal(localization.Get("Hotkey_EnableInputMonitoring"), localization.TranslateKnownText("Enable AutoClicker under System Settings → Privacy & Security → Input Monitoring, then retry."));
        Assert.Equal(localization.Get("Hotkey_MacListenerFailed"), localization.TranslateKnownText("macOS could not create a global keyboard listener. Check Input Monitoring permission and retry."));
        Assert.Equal(localization.Get("Permission_MacNative"), localization.TranslateKnownText("macOS uses Quartz mouse events. Enable Accessibility for mouse control and Input Monitoring for global shortcuts."));
    }

    [Fact]
    public void Statistics_use_selected_culture_without_changing_numeric_editing_culture()
    {
        Assert.Equal("1.234.567", UsageStatisticsFormatter.FormatClicks(1_234_567, System.Globalization.CultureInfo.GetCultureInfo("tr-TR")));
        Assert.Equal("1,234,567", UsageStatisticsFormatter.FormatClicks(1_234_567, System.Globalization.CultureInfo.GetCultureInfo("en-US")));
        var localization = new LocalizationService();
        Assert.Equal("2 g 5 sa", UsageStatisticsFormatter.FormatActiveTime(TimeSpan.FromHours(53), ApplicationLanguage.Turkish, localization));
        localization.SetLanguage(ApplicationLanguage.English);
        Assert.Equal("2d 5h 0m", UsageStatisticsFormatter.FormatActiveTime(TimeSpan.FromHours(53), ApplicationLanguage.English, localization));
    }

    [Fact]
    public void Changing_language_does_not_change_profile_settings_or_serialized_enum_values()
    {
        var profile = new AutoClickerProfile
        {
            ClickMode = ClickMode.HoldMouseButton, ProfileColor = ProfileColor.Blue,
            StartStopHotkey = new Hotkey("F4"), HoldMouseButtonHotkey = new Hotkey("F5")
        };
        var before = ProfileJson.Serialize(profile);
        var localization = new LocalizationService();
        localization.SetLanguage(ApplicationLanguage.English);
        localization.SetLanguage(ApplicationLanguage.Turkish);
        Assert.Equal(before, ProfileJson.Serialize(profile));
        Assert.Contains("\"clickMode\": \"HoldMouseButton\"", before);
        Assert.Contains("\"profileColor\": \"Blue\"", before);
    }

    private class NoopThemeService : IThemeService
    {
        public virtual void Apply(Appearance appearance) { }
    }

    private sealed class TrackingThemeService : NoopThemeService
    {
        public Appearance LastApplied { get; private set; }
        public override void Apply(Appearance appearance) => LastApplied = appearance;
    }
}

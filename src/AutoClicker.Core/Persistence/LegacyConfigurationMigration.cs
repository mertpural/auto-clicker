using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoClicker.Core.Models;

namespace AutoClicker.Core.Persistence;

/// <summary>
/// Converts the pre-1.0 layout (settings.json with only LastProfileId plus a separate profiles.json) into
/// current <see cref="ApplicationData"/>. Source files are copied to *.legacy-backup, never deleted.
/// </summary>
internal static class LegacyConfigurationMigration
{
    public static bool TryMigrate(string settingsPath, out ApplicationData? data)
    {
        data = null;
        var directory = Path.GetDirectoryName(settingsPath)!;
        var legacyProfilesPath = Path.Combine(directory, "profiles.json");
        if (!File.Exists(legacyProfilesPath)) return false;
        var usedKeys = new HashSet<Hotkey>();
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (settings.RootElement.ValueKind != JsonValueKind.Object
                || settings.RootElement.EnumerateObject().Any(p => p.Name.Equals("profiles", StringComparison.OrdinalIgnoreCase)
                                                                    || p.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase)))
                return false;
            var lastId = settings.RootElement.TryGetProperty("LastProfileId", out var last) && last.ValueKind == JsonValueKind.String ? last.GetString() : null;

            using var legacy = JsonDocument.Parse(File.ReadAllText(legacyProfilesPath));
            if (legacy.RootElement.ValueKind != JsonValueKind.Array) return false;

            var profiles = new List<AutoClickerProfile>();
            var idMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in legacy.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || profiles.Count >= 1000) continue;
                var legacyId = Text(item, "Id") ?? Guid.NewGuid().ToString();
                var name = (Text(item, "Name") ?? "Profile").Trim();
                if (name.Length == 0) name = "Profile";
                if (name.Length > 80) name = name[..80];
                var unique = name;
                for (var n = 2; !names.Add(unique); n++) unique = $"{name} {n}";

                var toggle = ParseKey(Text(item, "ToggleHotkey")) ?? new Hotkey("F6");
                var hold = ParseKey(Text(item, "HoldHotkey"));
                if (hold is null || hold == toggle) hold = FirstFree("F5", "F4", "F3", "F2", "F1", "F7", "F8");
                // Within one profile the two shortcuts differ; F12 stays reserved for Emergency Stop.
                if (toggle == new Hotkey("F12")) toggle = new Hotkey("F6");
                if (hold == toggle || hold == new Hotkey("F12")) hold = FirstFree("F5", "F4", "F3", "F2", "F1", "F7", "F8");

                var cps = item.TryGetProperty("Cps", out var cpsValue) && cpsValue.TryGetDouble(out var parsed) && double.IsFinite(parsed) && parsed >= 0.001 ? Math.Min(parsed, 500) : 12;
                var profile = new AutoClickerProfile
                {
                    Id = StableGuid(legacyId),
                    Name = unique,
                    Cps = cps,
                    MinimumCps = Math.Min(9, cps),
                    MaximumCps = Math.Max(13, cps),
                    MouseButton = Enum(item, "MouseButton", MouseButton.Left),
                    ClickMode = Enum(item, "Mode", ClickMode.Toggle),
                    StartStopHotkey = toggle,
                    HoldMouseButtonHotkey = hold
                };
                idMap[legacyId] = profile.Id;
                profiles.Add(profile);
            }
            if (profiles.Count == 0) return false;

            var defaultProfile = idMap.TryGetValue("default-profile", out var defaultId) ? defaultId : profiles[0].Id;
            var selected = lastId is not null && idMap.TryGetValue(lastId, out var selectedId) ? selectedId : defaultProfile;
            data = new ApplicationData { Profiles = profiles, DefaultProfileId = defaultProfile, SelectedProfileId = selected };

            File.Copy(settingsPath, settingsPath + ".legacy-backup", overwrite: false);
            File.Copy(legacyProfilesPath, legacyProfilesPath + ".legacy-backup", overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            data = null;
            return false;
        }

        Hotkey FirstFree(params string[] keys)
        {
            foreach (var key in keys) { var candidate = new Hotkey(key); if (usedKeys.Add(candidate)) return candidate; }
            return new Hotkey("F5");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static T Enum<T>(JsonElement element, string name, T fallback) where T : struct, Enum =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && System.Enum.IsDefined(typeof(T), number)
            ? (T)System.Enum.ToObject(typeof(T), number) : fallback;

    // Legacy shortcuts such as "Mouse4" are not keyboard keys and fall back to a default.
    private static Hotkey? ParseKey(string? text) => text is not null && Hotkey.TryParse(text, out var key) ? key : null;

    private static Guid StableGuid(string value) => Guid.TryParse(value, out var guid) ? guid : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}

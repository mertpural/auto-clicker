using System.Text.Json;
using System.Text.Json.Serialization;
using AutoClicker.Core.Models;
using AutoClicker.Core.Validation;

namespace AutoClicker.Core.Persistence;

public static class ProfileJson
{
    // Return separate options so callers cannot mutate the shared serializer configuration.
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new HotkeyJsonConverter() }
    };

    public static string Serialize(AutoClickerProfile profile)
    {
        ProfileValidator.ThrowIfInvalid(profile);
        return JsonSerializer.Serialize(profile, CreateOptions());
    }

    public static AutoClickerProfile Deserialize(string json)
    {
        var profile = JsonSerializer.Deserialize<AutoClickerProfile>(json, CreateOptions())
            ?? throw new DataValidationException("Profile JSON cannot be empty.");
        ProfileValidator.ThrowIfInvalid(profile);
        return profile;
    }

    private sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
    {
        public override Hotkey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Shortcut must be a string, such as Ctrl + F6.");
            try { return Hotkey.Parse(reader.GetString()!); }
            catch (ArgumentException exception) { throw new JsonException("Shortcut is invalid.", exception); }
        }

        public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}

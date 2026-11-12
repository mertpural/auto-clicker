namespace AutoClicker.Core.Models;

[Flags]
public enum HotkeyModifiers { None = 0, Control = 1, Shift = 2, Alt = 4, Meta = 8 }

/// <summary>Platform-neutral key names; adapters translate these into native key codes.</summary>
public sealed record Hotkey
{
    public string Key { get; }
    public HotkeyModifiers Modifiers { get; }

    public Hotkey(string key, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = key.Trim().ToUpperInvariant();
        if (normalized is "CTRL" or "CONTROL" or "SHIFT" or "ALT" or "OPTION" or "CMD" or "COMMAND" or "META" or "WIN")
            throw new ArgumentException("A modifier must be combined with a non-modifier key.", nameof(key));
        var valid = normalized.Length == 1 && char.IsAsciiLetterOrDigit(normalized[0])
            || normalized.StartsWith('F') && int.TryParse(normalized.AsSpan(1), out var number) && number is >= 1 and <= 24
            || normalized is "SPACE" or "ENTER" or "TAB" or "HOME" or "END" or "INSERT" or "DELETE" or "PAGEUP" or "PAGEDOWN";
        if (!valid || (modifiers & ~(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Meta)) != 0)
            throw new ArgumentException("Choose a letter, number, function key, or supported navigation key.", nameof(key));
        if (normalized.StartsWith('F') && int.TryParse(normalized.AsSpan(1), out var functionKey))
            normalized = $"F{functionKey}";
        Key = normalized;
        Modifiers = modifiers;
    }

    public static Hotkey Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        foreach (var token in tokens[..^1])
        {
            var modifier = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "SHIFT" => HotkeyModifiers.Shift,
                "ALT" or "OPTION" => HotkeyModifiers.Alt,
                "CMD" or "COMMAND" or "META" or "WIN" => HotkeyModifiers.Meta,
                _ => throw new ArgumentException($"Unknown modifier: {token}.", nameof(text))
            };
            if (modifiers.HasFlag(modifier)) throw new ArgumentException("A modifier cannot be repeated.", nameof(text));
            modifiers |= modifier;
        }
        return new Hotkey(tokens[^1], modifiers);
    }

    public static bool TryParse(string text, out Hotkey? hotkey)
    {
        try { hotkey = Parse(text); return true; }
        catch (ArgumentException) { hotkey = null; return false; }
    }

    public override string ToString()
    {
        var tokens = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) tokens.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) tokens.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) tokens.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Meta)) tokens.Add("Meta");
        tokens.Add(Key);
        return string.Join(" + ", tokens);
    }
}

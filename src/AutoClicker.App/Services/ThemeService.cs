using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using AutoClicker.Core.Models;

namespace AutoClicker.App.Services;

public interface IThemeService
{
    void Apply(Appearance appearance);
}

public sealed class ThemeService : IThemeService
{
    private static readonly string[] PaletteKeys = ["AppBackground", "SurfaceBackground", "SurfaceElevated", "BorderBrush", "TextPrimary", "TextSecondary", "AccentBrush", "AccentHoverBrush", "NavigationAccentBrush", "SelectionBrush", "DangerBrush", "SuccessBrush", "WarningBrush", "InputBackground", "SidebarBackground", "PrimaryText", "ControlAccentColor"];
    private static readonly IReadOnlyDictionary<Appearance, IReadOnlyDictionary<string, string>> Palettes = new Dictionary<Appearance, IReadOnlyDictionary<string, string>>
    {
        [Appearance.Night] = Palette("#151515", "#1D1D1D", "#242424", "#343434", "#F1F1F1", "#A4A4A4", "#89939B", "#A1ABB3", "#30353A", "#A85852", "#63866E", "#A68A50", "#202020", "#1A1A1A"),
        [Appearance.CrimsonGold] = Palette("#151515", "#1D1D1D", "#242424", "#343434", "#F1F1EF", "#A6A39B", "#B79A4A", "#CCAF5A", "#2A2020", "#A94E49", "#73846D", "#B79A4A", "#202020", "#191919"),
        [Appearance.Graphite] = Palette("#151719", "#1C1F22", "#24282C", "#343A3F", "#EEF0F2", "#A4A9AE", "#71808D", "#8998A5", "#303A43", "#A85852", "#668574", "#A18B58", "#202428", "#191C1F"),
        [Appearance.Light] = Palette("#F3F3F2", "#FFFFFF", "#FAFAF8", "#D7D7D5", "#202020", "#6D6D6D", "#626B73", "#4F5860", "#E5E8EA", "#A64848", "#47785A", "#947C40", "#FFFFFF", "#EBEBE9"),
        // Design themes: each also loads its own style set (Designs/*.axaml) for shapes, tabs and surfaces.
        [Appearance.GeceNeon] = Palette("#0B1020", "#121A2E", "#18223A", "#26324F", "#E8EEFF", "#8C9AC2", "#3DD6F5", "#7BE4FA", "#1B2A4D", "#FF6B81", "#3DDC97", "#FFC857", "#0F1730", "#0E1428", "#06202A"),
        [Appearance.SadeBeyaz] = Palette("#F5F5F7", "#FFFFFF", "#F2F2F4", "#E2E2E6", "#1D1D1F", "#6E6E73", "#0A84FF", "#0070E0", "#E5F0FF", "#E5342B", "#2DA44E", "#D98200", "#FFFFFF", "#EBEBEF", "#FFFFFF"),
        [Appearance.AuroraCam] = Palette("#1B1033", "#2A1F4F", "#33275E", "#4A3C80", "#F5F3FF", "#B7ADDB", "#C084FC", "#D8B4FE", "#4B3A8F", "#FF7A93", "#5EEAD4", "#FDE68A", "#1F1640", "#1A0F33", "#1B1033"),
        [Appearance.Terminal] = Palette("#050805", "#0A100A", "#0F170F", "#1F5A1F", "#7CFF7C", "#3FA83F", "#39FF14", "#7CFF7C", "#0F2A0F", "#FF4D4D", "#39FF14", "#FFD700", "#050805", "#070B07", "#021002"),
        [Appearance.OyunHud] = Palette("#0D0D0F", "#151518", "#1C1C21", "#2C2C33", "#F4F4F5", "#9A9AA3", "#FF8A00", "#FFA033", "#2A1D0C", "#FF4D4D", "#4ADE80", "#FACC15", "#101013", "#0A0A0C", "#0D0D0F")
    };

    private static readonly IReadOnlyDictionary<Appearance, string> DesignStyles = new Dictionary<Appearance, string>
    {
        [Appearance.GeceNeon] = "GeceNeon", [Appearance.SadeBeyaz] = "SadeBeyaz", [Appearance.AuroraCam] = "AuroraCam",
        [Appearance.Terminal] = "Terminal", [Appearance.OyunHud] = "OyunHud"
    };

    private StyleInclude? _designStyles;
    private string? _designName;

    internal static IReadOnlyDictionary<string, string> GetPalette(Appearance appearance) =>
        Palettes.TryGetValue(appearance == Appearance.Dark ? Appearance.Night : appearance, out var palette)
            ? palette : new Dictionary<string, string>();

    public void Apply(Appearance appearance)
    {
        if (Application.Current is not { } app) return;
        if (appearance == Appearance.Dark) appearance = Appearance.Night;
        SetDesignStyles(app, appearance);
        if (appearance == Appearance.System || !Enum.IsDefined(appearance))
        {
            app.RequestedThemeVariant = ThemeVariant.Default;
            foreach (var key in PaletteKeys) app.Resources.Remove(key);
            return;
        }

        app.RequestedThemeVariant = appearance is Appearance.Light or Appearance.SadeBeyaz ? ThemeVariant.Light : ThemeVariant.Dark;
        foreach (var (key, color) in Palettes[appearance])
            app.Resources[key] = new SolidColorBrush(Color.Parse(color));
        app.Resources["NavigationAccentBrush"] = app.Resources[appearance == Appearance.CrimsonGold ? "CrimsonAccentBrush" : "AccentBrush"];
        app.Resources["ControlAccentColor"] = Color.Parse(Palettes[appearance]["AccentBrush"]);
    }

    private void SetDesignStyles(Application app, Appearance appearance)
    {
        var wanted = DesignStyles.TryGetValue(appearance, out var name) ? name : null;
        if (string.Equals(_designName, wanted, StringComparison.Ordinal)) return;
        if (_designStyles is not null) app.Styles.Remove(_designStyles);
        _designStyles = null; _designName = null;
        if (wanted is null) return;
        _designStyles = new StyleInclude(new Uri("avares://AutoClicker/"))
        { Source = new Uri($"avares://AutoClicker/src/AutoClicker.App/Designs/{wanted}.axaml") };
        _designName = wanted;
        app.Styles.Add(_designStyles);
    }

    private static IReadOnlyDictionary<string, string> Palette(string background, string surface, string elevated, string border,
        string text, string secondary, string accent, string accentHover, string selection, string danger, string success,
        string warning, string input, string sidebar, string? primaryText = null) => new Dictionary<string, string>
    {
        ["AppBackground"] = background, ["SurfaceBackground"] = surface, ["SurfaceElevated"] = elevated, ["BorderBrush"] = border,
        ["TextPrimary"] = text, ["TextSecondary"] = secondary, ["AccentBrush"] = accent, ["AccentHoverBrush"] = accentHover,
        ["SelectionBrush"] = selection, ["DangerBrush"] = danger, ["SuccessBrush"] = success, ["WarningBrush"] = warning,
        ["InputBackground"] = input, ["SidebarBackground"] = sidebar, ["PrimaryText"] = primaryText ?? background
    };
}

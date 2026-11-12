using AutoClicker.App.Services;
using AutoClicker.App.ViewModels;
using AutoClicker.Core.Models;
using Xunit;

namespace AutoClicker.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public void Crimson_gold_uses_neutral_graphite_surfaces_and_all_semantic_palette_keys()
    {
        var palette = ThemeService.GetPalette(Appearance.CrimsonGold);
        Assert.Equal("#151515", palette["AppBackground"]);
        Assert.Equal("#1D1D1D", palette["SurfaceBackground"]);
        Assert.Equal("#242424", palette["SurfaceElevated"]);
        Assert.Equal("#202020", palette["InputBackground"]);
        Assert.Equal("#191919", palette["SidebarBackground"]);
        Assert.Equal("#B79A4A", palette["AccentBrush"]);
        Assert.Equal("#CCAF5A", palette["AccentHoverBrush"]);

        foreach (var appearance in new[] { Appearance.Night, Appearance.CrimsonGold, Appearance.Graphite, Appearance.Light })
            Assert.Equal(ThemeService.GetPalette(Appearance.Night).Keys.Order(), ThemeService.GetPalette(appearance).Keys.Order());
    }

    [Fact]
    public void Design_themes_have_the_same_palette_keys_and_black_is_the_default()
    {
        var expected = ThemeService.GetPalette(Appearance.Night).Keys.Order().ToArray();
        foreach (var appearance in new[] { Appearance.GeceNeon, Appearance.SadeBeyaz, Appearance.AuroraCam, Appearance.Terminal, Appearance.OyunHud })
            Assert.Equal(expected, ThemeService.GetPalette(appearance).Keys.Order().ToArray());
        Assert.Equal(Appearance.GeceNeon, new ApplicationSettings().Appearance);
        Assert.Equal("#0B1020", ThemeService.GetPalette(Appearance.GeceNeon)["AppBackground"]);
        Assert.Equal("#F5F5F7", ThemeService.GetPalette(Appearance.SadeBeyaz)["AppBackground"]);
    }

    [Fact]
    public void Changing_theme_does_not_change_profile_data()
    {
        var localization = new LocalizationService();
        var profile = new ProfileEditorViewModel(new AutoClickerProfile { Name = "Profile", Cps = 123 }, localization);
        Assert.True(profile.TryGetSnapshot(out var before));
        var settings = new SettingsViewModel(new NoopThemeService(), localization: localization);
        settings.Load(new ApplicationSettings());
        settings.Appearance = Appearance.CrimsonGold;
        Assert.True(profile.TryGetSnapshot(out var after));
        Assert.Equal(before, after);
        settings.Dispose();
    }

    private sealed class NoopThemeService : IThemeService { public void Apply(Appearance appearance) { } }
}

using System.Text.Json;
using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using AutoClicker.Core.Validation;
using Xunit;

namespace AutoClicker.Tests;

public sealed class ProfileTests
{
    [Fact]
    public void Supported_cps_range_matches_the_product_ceiling()
    {
        var supported = new AutoClickerProfile { Cps = 500, MinimumCps = 0.001, MaximumCps = 500 };
        Assert.Empty(ProfileValidator.Validate(supported));
        var aboveLimit = supported with { Cps = 500.1 };
        Assert.Contains(ProfileValidator.Validate(aboveLimit), issue => issue.Property == nameof(aboveLimit.Cps));
        Assert.Equal(500, AutoClicker.Core.Timing.ClickTiming.MaximumCps);
    }

    [Fact]
    public void ProfileRoundTripPreservesEveryOption()
    {
        var profile = new AutoClickerProfile
        {
            Name = "Minecraft", MouseButton = MouseButton.Middle, HoldMouseButton = MouseButton.Right, ClickType = ClickType.Double,
            ClickMode = ClickMode.Hold, Cps = 12.4, RandomizationEnabled = true,
            MinimumCps = 9.4, MaximumCps = 13.2, ClickLimitEnabled = true, ClickLimit = 12345,
            DurationLimitEnabled = true, DurationLimit = TimeSpan.FromMinutes(5), StartDelayMilliseconds = 3000,
            StartStopHotkey = Hotkey.Parse("Ctrl + Shift + A"), PositionMode = ClickPositionMode.Fixed,
            FixedX = -830, FixedY = 440, BurstEnabled = true, ClicksPerBurst = 7, BurstDelayMilliseconds = 1500
        };
        var json = ProfileJson.Serialize(profile);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Ctrl + Shift + A", document.RootElement.GetProperty("startStopHotkey").GetString());
        Assert.Contains("\"mouseButton\": \"Middle\"", json);
        Assert.Contains("\"holdMouseButton\": \"Right\"", json);
        Assert.Equal(profile, ProfileJson.Deserialize(json));
    }

    [Fact]
    public void ReadsTheRequestedPortableJsonFormat()
    {
        var profile = ProfileJson.Deserialize("""
            {"name":"Minecraft","mouseButton":"Left","clickType":"Single","clickMode":"Toggle",
             "cps":12,"randomizationEnabled":true,"minimumCps":9,"maximumCps":13,"startStopHotkey":"F6"}
            """);
        Assert.Equal("Minecraft", profile.Name);
        Assert.Equal(ProfileColor.Gray, profile.ProfileColor);
        Assert.Equal(12, profile.Cps);
        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal(new Hotkey("F6"), profile.StartStopHotkey);
        Assert.False(profile.ClickLimitEnabled);
        Assert.False(profile.DurationLimitEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), profile.DurationLimit);
        Assert.Equal(0, profile.StartDelayMilliseconds);
        Assert.False(profile.BurstEnabled);
        Assert.Equal(5, profile.ClicksPerBurst);
        Assert.Equal(1000, profile.BurstDelayMilliseconds);
        Assert.Equal(MouseButton.Right, profile.HoldMouseButton);
        Assert.Equal(new Hotkey("F5"), profile.HoldMouseButtonHotkey);
    }

    [Fact]
    public void HoldMouseButtonModeAndHotkeyRoundTripWithoutRemovingExistingSettings()
    {
        var profile = new AutoClickerProfile
        {
            ClickMode = ClickMode.HoldMouseButton,
            MouseButton = MouseButton.Middle,
            HoldMouseButtonHotkey = new Hotkey("F7"),
            Cps = 23,
            BurstEnabled = true
        };

        var json = ProfileJson.Serialize(profile);
        var restored = ProfileJson.Deserialize(json);

        Assert.Equal(ClickMode.HoldMouseButton, restored.ClickMode);
        Assert.Equal(MouseButton.Middle, restored.MouseButton);
        Assert.Equal(new Hotkey("F7"), restored.HoldMouseButtonHotkey);
        Assert.Equal(profile.Cps, restored.Cps);
        Assert.True(restored.BurstEnabled);
    }

    [Fact]
    public void ProfileColor_round_trips_in_the_existing_profile_json_format()
    {
        var profile = new AutoClickerProfile { ProfileColor = ProfileColor.Teal };

        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(profile));

        Assert.Equal(ProfileColor.Teal, restored.ProfileColor);
    }

    [Fact]
    public void HoldMouseButtonHotkeyCannotConflictWithStartStopOrEmergencyStop()
    {
        var profile = new AutoClickerProfile
        {
            ClickMode = ClickMode.HoldMouseButton,
            StartStopHotkey = new Hotkey("F5"),
            HoldMouseButtonHotkey = new Hotkey("F5")
        };
        Assert.Contains(ProfileValidator.Validate(profile), issue => issue.Property == nameof(profile.HoldMouseButtonHotkey));

        profile = profile with { StartStopHotkey = new Hotkey("F6"), HoldMouseButtonHotkey = new Hotkey("F12") };
        Assert.Contains(ProfileValidator.Validate(profile), issue => issue.Property == nameof(profile.HoldMouseButtonHotkey));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"cps\":0}")]
    [InlineData("{\"name\":\" \"}")]
    [InlineData("{\"minimumCps\":14,\"maximumCps\":9}")]
    [InlineData("{\"clickLimitEnabled\":true,\"clickLimit\":0}")]
    [InlineData("{\"positionMode\":\"Fixed\"}")]
    [InlineData("{\"clicksPerBurst\":0}")]
    public void RejectsInvalidProfileValues(string json) => Assert.Throws<DataValidationException>(() => ProfileJson.Deserialize(json));

    [Theory]
    [InlineData("{\"mouseButton\":\"ExtraUnknown\"}")]
    [InlineData("{\"mouseButton\":99}")]
    [InlineData("{\"startStopHotkey\":\"Ctrl + Ctrl + F6\"}")]
    [InlineData("{broken")]
    public void RejectsMalformedProfileJson(string json) => Assert.Throws<JsonException>(() => ProfileJson.Deserialize(json));

    [Fact]
    public void MouseHoldMayUseTheSameButtonForTriggerAndOutput()
    {
        var profile = new AutoClickerProfile { ClickMode = ClickMode.MouseHold, HoldMouseButton = MouseButton.Left };
        Assert.DoesNotContain(ProfileValidator.Validate(profile), issue => issue.Property == nameof(profile.HoldMouseButton));
    }

    [Theory]
    [InlineData("ctrl+shift+a", "Ctrl + Shift + A")]
    [InlineData("Command + F8", "Meta + F8")]
    [InlineData("F12", "F12")]
    public void HotkeysNormalizePortableNames(string input, string expected) => Assert.Equal(expected, Hotkey.Parse(input).ToString());
}

using AutoClicker.App.ViewModels;
using AutoClicker.Core.Models;
using Xunit;

namespace AutoClicker.Tests;

public sealed class ProfileEditorInputTests
{
    [Fact]
    public void Tab_state_defaults_to_general_and_can_be_switched_without_changing_profile_values()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile { ClickMode = ClickMode.HoldMouseButton, Cps = 34, RandomizationEnabled = true });
        Assert.Equal(0, editor.SelectedTabIndex);
        editor.SelectedTabIndex = 1;
        Assert.Equal(1, editor.SelectedTabIndex);
        Assert.False(editor.IsCpsModeEnabled);
        Assert.False(editor.IsRandomizationConfigurationEnabled);
        Assert.True(editor.TryGetSnapshot(out var profile));
        Assert.Equal(34, profile.Cps);
        Assert.True(profile.RandomizationEnabled);
    }

    [Fact]
    public void Hold_mouse_hotkey_recording_shows_the_correct_shortcut_and_prompt()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile
        {
            ClickMode = ClickMode.HoldMouseButton,
            HoldMouseButtonHotkey = new Hotkey("F5")
        });
        Assert.Equal("F5", editor.RecordingLabel);
        editor.RecordHoldMouseButtonHotkeyCommand.Execute(null);
        Assert.Equal("Press a key combination… (Esc to cancel)", editor.RecordingLabel);
        editor.IsRecording = false;
        Assert.Equal("F5", editor.RecordingLabel);
    }

    [Fact]
    public void Fixed_position_coordinates_round_trip_through_editor()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile
        {
            PositionMode = ClickPositionMode.Fixed,
            FixedX = -1440,
            FixedY = 320
        });

        Assert.True(editor.IsFixedPosition);
        Assert.True(editor.TryGetSnapshot(out var profile));
        Assert.Equal(ClickPositionMode.Fixed, profile.PositionMode);
        Assert.Equal(-1440, profile.FixedX);
        Assert.Equal(320, profile.FixedY);
    }

    [Fact]
    public void Mouse_hold_profile_exposes_trigger_and_output_buttons_independently()
    {
        var editor = new ProfileEditorViewModel(new AutoClickerProfile
        {
            ClickMode = ClickMode.MouseHold, HoldMouseButton = MouseButton.Right, MouseButton = MouseButton.Left
        });
        Assert.True(editor.IsValid);
        Assert.Equal(MouseButton.Right, editor.HoldMouseButton);
        Assert.Contains(ClickMode.MouseHold, editor.ClickModes);
        Assert.Equal("Mouse Hold Trigger", editor.SelectedClickModeOption.Label);
        Assert.True(editor.TryGetSnapshot(out var snapshot));
        Assert.Equal(MouseButton.Right, snapshot.HoldMouseButton);
        Assert.Equal(MouseButton.Left, snapshot.MouseButton);
    }
}

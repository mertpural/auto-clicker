using Avalonia;
using AutoClicker.App.ViewModels;
using AutoClicker.App.Views;
using AutoClicker.Core.Models;
using Xunit;

namespace AutoClicker.Tests;

public sealed class ProfileDragGestureTests
{
    [Fact]
    public void Drag_starts_only_after_a_five_pixel_pointer_threshold()
    {
        Assert.False(ProfileDragGesture.HasPassedThreshold(new Point(10, 10), new Point(13, 12)));
        Assert.True(ProfileDragGesture.HasPassedThreshold(new Point(10, 10), new Point(10, 15)));
    }

    [Fact]
    public void Rename_mode_disables_drag_start()
    {
        var profile = new ProfileEditorViewModel(new AutoClickerProfile());
        Assert.True(ProfileDragGesture.CanStart(profile));
        profile.BeginRename();
        Assert.False(ProfileDragGesture.CanStart(profile));
    }
}

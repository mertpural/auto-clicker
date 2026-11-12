using Avalonia;
using AutoClicker.App.ViewModels;

namespace AutoClicker.App.Views;

internal static class ProfileDragGesture
{
    public const double Threshold = 5;

    public static bool CanStart(ProfileEditorViewModel profile) => !profile.IsRenaming;

    public static bool HasPassedThreshold(Point origin, Point current)
    {
        var delta = current - origin;
        return delta.X * delta.X + delta.Y * delta.Y >= Threshold * Threshold;
    }
}

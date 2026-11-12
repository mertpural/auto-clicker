using Avalonia;
using Avalonia.Controls;
using AutoClicker.App.ViewModels;
using AutoClicker.App.Views;
using AutoClicker.Core.Abstractions;

namespace AutoClicker.App.Services;

/// <summary>
/// Shows the menu bar popover under the status item. The popup hides when it loses focus; the tray click
/// that follows a hide-by-deactivation is treated as the toggle that closed it, so the popup does not reopen.
/// </summary>
public sealed class MenuBarPopupService : IDisposable
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(350);
    private readonly MainWindowViewModel _viewModel;
    private readonly Action _open, _quit;
    private readonly ICursorService? _cursor;
    private MenuBarPopup? _popup;
    private DateTimeOffset _hiddenAt = DateTimeOffset.MinValue;

    public MenuBarPopupService(MainWindowViewModel viewModel, Action open, Action quit, ICursorService? cursor)
    { _viewModel = viewModel; _open = open; _quit = quit; _cursor = cursor; }

    public bool IsVisible => _popup is { IsVisible: true };

    public void Toggle()
    {
        if (IsVisible) { _popup!.Hide(); return; }
        if (DateTimeOffset.UtcNow - _hiddenAt < ReopenGuard) return;
        Show();
    }

    public void Show()
    {
        var popup = _popup ??= Create();
        popup.DataContext = _viewModel;
        popup.WindowState = WindowState.Normal;
        popup.Topmost = true;
        Position(popup);
        popup.Show();
        popup.Activate();
    }

    private MenuBarPopup Create()
    {
        var popup = new MenuBarPopup { DataContext = _viewModel };
        popup.Deactivated += (_, _) => Hide();
        popup.Closed += (_, _) => _popup = null;
        popup.OpenRequested += (_, _) => { Hide(); _open(); };
        popup.QuitRequested += (_, _) => { Hide(); _quit(); };
        return popup;
    }

    private void Hide()
    {
        if (_popup is not { IsVisible: true } popup) return;
        _hiddenAt = DateTimeOffset.UtcNow;
        popup.Hide();
    }

    private void Position(Window popup)
    {
        // Place the popover under the pointer (the status item was just clicked), clamped to the working area.
        if (_cursor is null || popup.Screens is not { } screens) return;
        try
        {
            var point = _cursor.GetPosition();
            var screen = screens.All.FirstOrDefault(candidate => candidate.Bounds.Contains(new PixelPoint((int)(point.X * candidate.Scaling), (int)(point.Y * candidate.Scaling))))
                ?? screens.Primary;
            if (screen is null) return;
            var scaling = screen.Scaling;
            var width = (int)Math.Round((popup.Width > 0 ? popup.Width : 360) * scaling);
            var work = screen.WorkingArea;
            var x = (int)Math.Round(point.X * scaling) - width / 2;
            x = Math.Clamp(x, work.X + 8, Math.Max(work.X + 8, work.Right - width - 8));
            popup.Position = new PixelPoint(x, work.Y - (int)Math.Round(16 * scaling));
        }
        catch
        {
            // Positioning is best effort; the popup still opens at the default location.
        }
    }

    public void Dispose()
    {
        _popup?.Close();
        _popup = null;
    }
}

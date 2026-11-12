using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Services;

public interface IWindowPresentationService
{
    void HideMainWindow();
    void ShowAndActivateMainWindow();
}

public sealed class PositionPickingCoordinator
{
    private readonly IMouseInputMonitor inputMonitor;
    private readonly IWindowPresentationService window;
    private readonly ILogger<PositionPickingCoordinator> logger;
    private readonly LocalizationService? localization;
    private readonly Func<CancellationToken, Task> instructionDelay;
    private CancellationTokenSource? _activePickCancellation;

    public PositionPickingCoordinator(IMouseInputMonitor inputMonitor, IWindowPresentationService window,
        ILogger<PositionPickingCoordinator> logger, Func<CancellationToken, Task>? instructionDelay = null, LocalizationService? localization = null)
    {
        this.inputMonitor = inputMonitor; this.window = window; this.logger = logger; this.localization = localization;
        this.instructionDelay = instructionDelay ?? (token => Task.Delay(TimeSpan.FromMilliseconds(650), token));
    }

    public async Task<bool> PickAsync(ProfileEditorViewModel editor, CancellationToken cancellationToken = default)
    {
        var activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Interlocked.CompareExchange(ref _activePickCancellation, activeCancellation, null) is not null)
        { activeCancellation.Dispose(); editor.PositionPickerStatus = localization?.Get("Position_PickAlreadyActive") ?? "Position picking is already active."; return false; }
        cancellationToken = activeCancellation.Token;
        var hidden = false;
        var captureEnabled = 0;
        var selection = new TaskCompletionSource<ScreenPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void MouseChanged(object? sender, MouseInputEventArgs args)
        {
            if (Volatile.Read(ref captureEnabled) != 0 && args.Button == MouseButton.Left && args.IsPressed && !args.IsAutoClickerGenerated)
                selection.TrySetResult(args.Position);
        }
        void EscapePressed(object? sender, EventArgs args) => selection.TrySetResult(null);
        var subscribed = false;
        try
        {
            var result = await inputMonitor.StartMonitoringAsync(cancellationToken).ConfigureAwait(true);
            if (!result.Success)
            {
                editor.PositionPickerStatus = localization?.TranslateKnownText(result.ErrorMessage ?? inputMonitor.Status) ?? result.ErrorMessage ?? inputMonitor.Status;
                return false;
            }
            editor.IsPickingPosition = true;
            editor.PositionPickerStatus = localization?.Get("Position_PickInstruction") ?? "Click anywhere to choose the position. Press Esc to cancel.";
            logger.LogInformation("Pick Position started");
            inputMonitor.MouseButtonChanged += MouseChanged;
            inputMonitor.EscapePressed += EscapePressed;
            subscribed = true;
            // Leave the instruction visible long enough to read before hiding the window.
            await instructionDelay(cancellationToken).ConfigureAwait(true);
            window.HideMainWindow();
            hidden = true;
            Volatile.Write(ref captureEnabled, 1);
            var point = await selection.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            if (point is { } selected)
            {
                editor.SetPickedPosition(selected);
                editor.PositionPickerStatus = localization?.Get("Position_Selected", selected.X, selected.Y) ?? $"Position selected: {selected.X}, {selected.Y}.";
                logger.LogInformation("Fixed position selected: {X},{Y}", selected.X, selected.Y);
                return true;
            }
            editor.PositionPickerStatus = localization?.Get("Position_Cancelled") ?? "Position picking cancelled. Coordinates were not changed.";
            return false;
        }
        catch (OperationCanceledException)
        {
            editor.PositionPickerStatus = localization?.Get("Position_Cancelled") ?? "Position picking cancelled. Coordinates were not changed.";
            return false;
        }
        finally
        {
            if (subscribed)
            {
                inputMonitor.MouseButtonChanged -= MouseChanged;
                inputMonitor.EscapePressed -= EscapePressed;
            }
            editor.IsPickingPosition = false;
            if (hidden) window.ShowAndActivateMainWindow();
            Interlocked.Exchange(ref _activePickCancellation, null)?.Dispose();
        }
    }

    public void CancelActivePick() => Volatile.Read(ref _activePickCancellation)?.Cancel();
}

public sealed class AvaloniaWindowPresentationService : IWindowPresentationService
{
    public Avalonia.Controls.Window? MainWindow { get; set; }
    public void HideMainWindow() => MainWindow?.Hide();
    public void ShowAndActivateMainWindow()
    {
        if (MainWindow is null) return;
        MainWindow.ShowInTaskbar = true;
        MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
        MainWindow.Show();
        MainWindow.Activate();
    }
}

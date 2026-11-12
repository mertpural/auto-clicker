using AutoClicker.App.Services;
using AutoClicker.App.ViewModels;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoClicker.Tests;

public sealed class PositionPickingTests
{
    [Fact]
    public async Task Captures_global_click_coordinates_after_hiding_and_restores_window()
    {
        var monitor = new FakeMonitor(); var window = new FakeWindow();
        var picker = new PositionPickingCoordinator(monitor, window, NullLogger<PositionPickingCoordinator>.Instance, _ => Task.CompletedTask);
        var editor = NewEditor();
        var picking = picker.PickAsync(editor);
        await window.Hidden.Task.WaitAsync(TimeSpan.FromSeconds(2));
        monitor.Raise(new MouseInputEventArgs(MouseButton.Left, true, false, new ScreenPoint(-1920, 415)));

        Assert.True(await picking);
        Assert.Equal(-1920, editor.FixedX); Assert.Equal(415, editor.FixedY);
        Assert.Equal(1, window.HideCount); Assert.Equal(1, window.ShowCount);
        Assert.Equal(0, monitor.SubscriberCount);
    }

    [Fact]
    public async Task Escape_cancels_without_changing_coordinates()
    {
        var monitor = new FakeMonitor(); var window = new FakeWindow();
        var picker = new PositionPickingCoordinator(monitor, window, NullLogger<PositionPickingCoordinator>.Instance, _ => Task.CompletedTask);
        var editor = NewEditor();
        var picking = picker.PickAsync(editor);
        await window.Hidden.Task.WaitAsync(TimeSpan.FromSeconds(2));
        monitor.Cancel();

        Assert.False(await picking);
        Assert.Equal(2, editor.FixedX); Assert.Equal(3, editor.FixedY);
        Assert.Equal(1, window.ShowCount); Assert.Equal(0, monitor.SubscriberCount);
    }

    private static ProfileEditorViewModel NewEditor() => new(new AutoClickerProfile
    { PositionMode = ClickPositionMode.Fixed, FixedX = 2, FixedY = 3 });

    private sealed class FakeWindow : IWindowPresentationService
    {
        public TaskCompletionSource Hidden { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int HideCount { get; private set; }
        public int ShowCount { get; private set; }
        public void HideMainWindow() { HideCount++; Hidden.TrySetResult(); }
        public void ShowAndActivateMainWindow() => ShowCount++;
    }

    private sealed class FakeMonitor : IMouseInputMonitor
    {
        public bool IsAvailable => true; public string Status => "Ready";
        public event EventHandler<MouseInputEventArgs>? MouseButtonChanged;
        public event EventHandler? EscapePressed;
        public int SubscriberCount => (MouseButtonChanged?.GetInvocationList().Length ?? 0) + (EscapePressed?.GetInvocationList().Length ?? 0);
        public Task<HotkeyRegistrationResult> StartMonitoringAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(true));
        public void Raise(MouseInputEventArgs args) => MouseButtonChanged?.Invoke(this, args);
        public void Cancel() => EscapePressed?.Invoke(this, EventArgs.Empty);
    }
}

using AutoClicker.Core.Models;

namespace AutoClicker.Core.Abstractions;

public interface IPlatformInfo
{
    string Name { get; }
    string ConfigurationDirectory { get; }
    bool NativeInputAvailable { get; }
    string NativeInputStatus { get; }
}

public interface IMouseService
{
    Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default);
    Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default);
    Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default);
    Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default);
}

public interface ICursorService
{
    ScreenPoint GetPosition();
    void SetPosition(ScreenPoint position);
    Task<ScreenPoint> PickPositionAsync(CancellationToken cancellationToken = default);
}

public sealed record HotkeyRegistrationResult(bool Success, string? ErrorMessage = null);
public sealed class HotkeyEventArgs(Hotkey hotkey, bool isInjected = false, bool isEmergencyStop = false, bool isReleased = false, bool isHoldMouseButton = false) : EventArgs
{
    public Hotkey Hotkey { get; } = hotkey;
    public bool IsInjected { get; } = isInjected;
    public bool IsEmergencyStop { get; } = isEmergencyStop;
    public bool IsReleased { get; } = isReleased;
    public bool IsHoldMouseButton { get; } = isHoldMouseButton;
}

public interface IGlobalHotkeyService : IDisposable, IAsyncDisposable
{
    Task<HotkeyRegistrationResult> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken = default);
    Task<HotkeyRegistrationResult> RegisterHoldMouseButtonAsync(Hotkey hotkey, CancellationToken cancellationToken = default);
    Task<HotkeyRegistrationResult> RegisterEmergencyStopAsync(Hotkey hotkey, CancellationToken cancellationToken = default);
    void Unregister(Hotkey hotkey);
    void UnregisterHoldMouseButton();
    void UnregisterEmergencyStop();
    void SetActionsSuspended(bool suspended);
    event EventHandler<HotkeyEventArgs>? Pressed;
    event EventHandler<HotkeyEventArgs>? Released;
}

public sealed class MouseInputEventArgs(MouseButton button, bool isPressed, bool isAutoClickerGenerated, ScreenPoint position) : EventArgs
{
    public MouseButton Button { get; } = button;
    public bool IsPressed { get; } = isPressed;
    public bool IsAutoClickerGenerated { get; } = isAutoClickerGenerated;
    public ScreenPoint Position { get; } = position;
}

public interface IMouseInputMonitor
{
    bool IsAvailable { get; }
    string Status { get; }
    Task<HotkeyRegistrationResult> StartMonitoringAsync(CancellationToken cancellationToken = default);
    event EventHandler<MouseInputEventArgs>? MouseButtonChanged;
    event EventHandler? EscapePressed;
}

public interface IStartupRegistrationService
{
    bool IsAvailable { get; }
    bool IsEnabled { get; }
    string Status { get; }
    bool SetEnabled(bool enabled, out string? errorMessage);
}

public interface ISingleInstanceService : IDisposable, IAsyncDisposable
{
    bool TryAcquire();
    Task<bool> RequestActivationAsync(CancellationToken cancellationToken = default);
    Task RunActivationListenerAsync(Func<Task> activate, CancellationToken cancellationToken = default);
}

public interface IAccessibilityPermissionService
{
    bool IsGranted { get; }
    /// <summary>False when global shortcuts (Input Monitoring on macOS) are not permitted.</summary>
    bool CanListenForShortcuts => true;
    string Status { get; }
    Task<bool> RequestAsync(CancellationToken cancellationToken = default);
    void OpenSystemSettings();
}

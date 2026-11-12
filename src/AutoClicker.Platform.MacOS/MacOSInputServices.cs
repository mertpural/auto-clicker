using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;

namespace AutoClicker.Platform.MacOS;

public sealed class MacOSAccessibilityPermissionService : IAccessibilityPermissionService
{
    private readonly IMacOSNativeApi _native;
    public MacOSAccessibilityPermissionService() : this(new MacOSNativeApi()) { }
    internal MacOSAccessibilityPermissionService(IMacOSNativeApi native) => _native = native;
    public bool IsGranted => OperatingSystem.IsMacOS() && (Safe(_native.IsAccessibilityTrusted) || Safe(_native.CanPostEvents));
    public bool CanListenForShortcuts => OperatingSystem.IsMacOS() && (Safe(_native.IsAccessibilityTrusted) || Safe(_native.CanListenForEvents));
    public string Status
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return "macOS native input is available only on macOS.";
            if (IsGranted) return "Mouse control and global shortcuts are permitted.";
            return "Accessibility permission is needed to control the mouse. Enable AutoClicker in System Settings → Privacy & Security → Accessibility.";
        }
    }
    public Task<bool> RequestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return Task.FromResult(false);
        try
        {
            if (!Safe(_native.IsAccessibilityTrusted))
            {
                _native.RequestAccessibilityTrust();
            }
            return Task.FromResult(IsGranted);
        }
        catch { return Task.FromResult(IsGranted); }
    }
    public void OpenSystemSettings()
    {
        if (!OperatingSystem.IsMacOS() || !_native.OpenAccessibilitySettings())
            throw new InvalidOperationException("Could not open macOS Privacy & Security settings. Open System Settings → Privacy & Security manually.");
    }
    private static bool Safe(Func<bool> check) { try { return check(); } catch { return false; } }
}

public sealed class MacOSCursorService : ICursorService
{
    private readonly IMacOSNativeApi _native;
    private readonly IAccessibilityPermissionService _permissions;
    public MacOSCursorService(IAccessibilityPermissionService permissions) : this(permissions, new MacOSNativeApi()) { }
    internal MacOSCursorService(IAccessibilityPermissionService permissions, IMacOSNativeApi native) { _permissions = permissions; _native = native; }
    public ScreenPoint GetPosition()
    {
        var evt = _native.CreateEventAtCurrentCursor();
        if (evt == IntPtr.Zero) throw new InvalidOperationException("macOS could not read the current cursor position.");
        try { var p = _native.GetEventLocation(evt); return new((int)Math.Round(p.X, MidpointRounding.AwayFromZero), (int)Math.Round(p.Y, MidpointRounding.AwayFromZero)); }
        finally { _native.Release(evt); }
    }
    public void SetPosition(ScreenPoint position)
    {
        if (!_permissions.IsGranted) throw new PlatformNotSupportedException("Enable Accessibility permission in System Settings to move the cursor.");
        _native.WarpCursor(position.X, position.Y);
    }
    public Task<ScreenPoint> PickPositionAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(GetPosition()); }
}

public sealed class MacOSMouseService : IMouseService
{
    internal const int AutoClickerUserDataField = 42;
    internal const long AutoClickerInputMarker = 0x4155434C49434B52;
    private readonly IAccessibilityPermissionService _permissions;
    private readonly ICursorService _cursor;
    private readonly IMacOSNativeApi _native;
    public MacOSMouseService(IAccessibilityPermissionService permissions, ICursorService cursor) : this(permissions, cursor, new MacOSNativeApi()) { }
    internal MacOSMouseService(IAccessibilityPermissionService permissions, ICursorService cursor, IMacOSNativeApi native) { _permissions = permissions; _cursor = cursor; _native = native; }
    private const int ClickStateField = 1;
    private static readonly long OwnWindowCacheTicks = System.Diagnostics.Stopwatch.Frequency / 10;
    private readonly object _ownWindowGate = new();
    private ScreenPoint _ownWindowPoint;
    private long _ownWindowCheckedAt;
    private bool _ownWindowChecked, _ownWindowResult;

    /// <summary>Never click AutoClicker's own UI: an automated click on the Stop button would immediately restart clicking.</summary>
    private bool IsOverOwnWindow(ScreenPoint point)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_ownWindowGate)
        {
            if (_ownWindowChecked && point == _ownWindowPoint && now - _ownWindowCheckedAt < OwnWindowCacheTicks) return _ownWindowResult;
            bool result;
            try { result = _native.IsOwnWindowAt(point.X, point.Y); } catch { result = false; }
            _ownWindowPoint = point; _ownWindowCheckedAt = now; _ownWindowResult = result; _ownWindowChecked = true;
            return result;
        }
    }
    public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
        => ClickAsync(button, position, 1, cancellationToken);

    private Task ClickAsync(MouseButton button, ScreenPoint? position, long clickState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_permissions.IsGranted) throw new PlatformNotSupportedException("Enable Accessibility permission in System Settings → Privacy & Security to control the mouse.");
        var events = button switch
        {
            MouseButton.Left => (Down: 1u, Up: 2u, NativeButton: 0u),
            MouseButton.Right => (Down: 3u, Up: 4u, NativeButton: 1u),
            MouseButton.Middle => (Down: 25u, Up: 26u, NativeButton: 2u),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
        };
        var point = position ?? _cursor.GetPosition();
        if (IsOverOwnWindow(point)) return Task.CompletedTask;
        if (position is not null && _cursor.GetPosition() != point) _cursor.SetPosition(point);
        var down = _native.CreateMouseEvent(events.Down, events.NativeButton, point.X, point.Y);
        if (down == IntPtr.Zero) throw new InvalidOperationException("CoreGraphics could not create the mouse-down event.");
        IntPtr up = IntPtr.Zero;
        try
        {
            up = _native.CreateMouseEvent(events.Up, events.NativeButton, point.X, point.Y);
            if (up == IntPtr.Zero) throw new InvalidOperationException("CoreGraphics could not create the mouse-up event.");
            // CGEvent user data is app-specific metadata read by our passive event tap.
            _native.SetEventIntegerValue(down, AutoClickerUserDataField, AutoClickerInputMarker);
            _native.SetEventIntegerValue(up, AutoClickerUserDataField, AutoClickerInputMarker);
            // macOS apps only recognise a double click when the second pair carries click count 2.
            _native.SetEventIntegerValue(down, ClickStateField, clickState);
            _native.SetEventIntegerValue(up, ClickStateField, clickState);
            cancellationToken.ThrowIfCancellationRequested();
            _native.PostEvent(down);
            _native.PostEvent(up);
            return Task.CompletedTask;
        }
        finally { _native.Release(down); _native.Release(up); }
    }

    public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default) => PostButtonEvent(button, pressed: true, cancellationToken);
    public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default) => PostButtonEvent(button, pressed: false, cancellationToken);

    private Task PostButtonEvent(MouseButton button, bool pressed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_permissions.IsGranted) throw new PlatformNotSupportedException("Enable Accessibility permission in System Settings → Privacy & Security to control the mouse.");
        var (down, up, nativeButton) = button switch
        {
            MouseButton.Left => (1u, 2u, 0u),
            MouseButton.Right => (3u, 4u, 1u),
            MouseButton.Middle => (25u, 26u, 2u),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
        };
        var point = _cursor.GetPosition();
        var evt = _native.CreateMouseEvent(pressed ? down : up, nativeButton, point.X, point.Y);
        if (evt == IntPtr.Zero) throw new InvalidOperationException("CoreGraphics could not create the mouse button event.");
        try
        {
            _native.SetEventIntegerValue(evt, AutoClickerUserDataField, AutoClickerInputMarker);
            cancellationToken.ThrowIfCancellationRequested();
            _native.PostEvent(evt);
            return Task.CompletedTask;
        }
        finally { _native.Release(evt); }
    }

    public async Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
    {
        await ClickAsync(button, position, 1, cancellationToken).ConfigureAwait(false);
        await ClickAsync(button, position, 2, cancellationToken).ConfigureAwait(false);
    }
}

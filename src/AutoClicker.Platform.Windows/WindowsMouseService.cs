using System.ComponentModel;
using System.Runtime.InteropServices;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;

namespace AutoClicker.Platform.Windows;

internal interface IWindowsInputApi
{
    uint SendInput(uint count, Input[] inputs, int inputSize);
    int GetLastError();
    bool SetCursorPosition(int x, int y);
    bool GetCursorPosition(out NativePoint point);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint { public int X; public int Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    public uint Type;
    public InputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MouseInput Mouse;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

internal sealed class WindowsInputApi : IWindowsInputApi
{
    public uint SendInput(uint count, Input[] inputs, int inputSize) => NativeMethods.SendInput(count, inputs, inputSize);
    public int GetLastError() => Marshal.GetLastWin32Error();
    public bool SetCursorPosition(int x, int y) => NativeMethods.SetCursorPos(x, y);
    public bool GetCursorPosition(out NativePoint point) => NativeMethods.GetCursorPos(out point);
}

public sealed class WindowsMouseService : IMouseService
{
    internal static readonly nuint AutoClickerInputMarker = IntPtr.Size == 8
        ? unchecked((nuint)0x4155434C49434B52UL) : unchecked((nuint)0x41434C52U);
    private readonly IWindowsInputApi _api;
    private readonly ICursorService _cursor;
    internal WindowsMouseService(IWindowsInputApi api, ICursorService cursor) { _api = api; _cursor = cursor; }
    public WindowsMouseService(ICursorService cursor) : this(new WindowsInputApi(), cursor) { }
    public WindowsMouseService() : this(new WindowsInputApi(), new WindowsCursorService()) { }

    public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (position is { } point) _cursor.SetPosition(point);
        var (down, up) = MouseFlags(button);
        var events = new[] { Event(down), Event(up) };
        var sent = _api.SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
        if (sent == events.Length) return Task.CompletedTask;

        var error = _api.GetLastError();
        if (sent > 0)
        {
            // A partial send may have left the button down; make one best-effort release.
            _api.SendInput(1, [Event(up)], Marshal.SizeOf<Input>());
        }
        throw new Win32Exception(error, $"Windows accepted {sent} of {events.Length} mouse input events.");
    }

    public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default) => SendButtonEvent(button, down: true, cancellationToken);
    public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default) => SendButtonEvent(button, down: false, cancellationToken);

    private Task SendButtonEvent(MouseButton button, bool down, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flags = MouseFlags(button);
        var input = Event(down ? flags.Down : flags.Up);
        var sent = _api.SendInput(1, [input], Marshal.SizeOf<Input>());
        if (sent == 1) return Task.CompletedTask;
        throw new Win32Exception(_api.GetLastError(), $"Windows did not accept the mouse button {(down ? "down" : "up")} event.");
    }

    public async Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
    {
        await ClickAsync(button, position, cancellationToken).ConfigureAwait(false);
        await ClickAsync(button, position, cancellationToken).ConfigureAwait(false);
    }

    internal static (uint Down, uint Up) MouseFlags(MouseButton button) => button switch
    {
        MouseButton.Left => (0x0002, 0x0004),
        MouseButton.Right => (0x0008, 0x0010),
        MouseButton.Middle => (0x0020, 0x0040),
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private static Input Event(uint flags) => new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = flags, ExtraInfo = AutoClickerInputMarker } } };
}

public sealed class WindowsCursorService : ICursorService
{
    private readonly IWindowsInputApi _api;
    internal WindowsCursorService(IWindowsInputApi api) => _api = api;
    public WindowsCursorService() : this(new WindowsInputApi()) { }
    public ScreenPoint GetPosition()
    {
        if (!_api.GetCursorPosition(out var point)) throw new Win32Exception(_api.GetLastError(), "Unable to read the cursor position.");
        return new ScreenPoint(point.X, point.Y);
    }
    public void SetPosition(ScreenPoint position)
    {
        if (!_api.SetCursorPosition(position.X, position.Y)) throw new Win32Exception(_api.GetLastError(), "Unable to set the cursor position.");
    }
    public Task<ScreenPoint> PickPositionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetPosition());
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] internal static extern uint SendInput(uint count, [In] Input[] inputs, int inputSize);
    [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out NativePoint point);
}

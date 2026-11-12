using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;

namespace AutoClicker.Platform.Windows;

/// <summary>WH_KEYBOARD_LL-based global shortcuts. The hook callback only snapshots and queues events.</summary>
public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService, IMouseInputMonitor
{
    private readonly object _gate = new();
    private readonly Dictionary<Hotkey, bool> _active = [];
    private readonly HashSet<uint> _downKeys = [];
    private readonly HashSet<MouseButton> _downMouseButtons = [];
    private readonly Dictionary<uint, (Hotkey Hotkey, bool IsHoldMouseButton)> _pressedHotkeys = [];
    private readonly Channel<HotkeyEventArgs> _events = Channel.CreateUnbounded<HotkeyEventArgs>(new() { SingleReader = true, SingleWriter = false });
    private readonly Channel<MouseInputEventArgs> _mouseEvents = Channel.CreateUnbounded<MouseInputEventArgs>(new() { SingleReader = true, SingleWriter = false });
    private readonly Channel<bool> _escapeEvents = Channel.CreateUnbounded<bool>(new() { SingleReader = true, SingleWriter = false });
    private readonly TaskCompletionSource<HotkeyRegistrationResult> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private readonly Task _dispatchTask, _mouseDispatchTask, _escapeDispatchTask;
    private readonly HookProc _callback;
    private readonly HookProc _mouseCallback;
    private nint _hook, _mouseHook;
    private uint _threadId;
    private Hotkey? _emergency;
    private Hotkey? _holdMouseButton;
    private bool _actionsSuspended;
    private bool _resumeAfterKeysReleased;
    private bool _disposed;

    public event EventHandler<HotkeyEventArgs>? Pressed;
    public event EventHandler<HotkeyEventArgs>? Released;
    public event EventHandler<MouseInputEventArgs>? MouseButtonChanged;
    public event EventHandler? EscapePressed;
    public bool IsAvailable => _ready.Task.IsCompletedSuccessfully && _ready.Task.Result.Success;
    public string Status => IsAvailable ? "Global keyboard and mouse input monitoring is available." : _ready.Task.IsCompletedSuccessfully ? _ready.Task.Result.ErrorMessage ?? "Global input monitoring is unavailable." : "Global input monitoring is starting.";

    public WindowsGlobalHotkeyService()
    {
        _callback = HookCallback; // Keep delegate rooted for the hook lifetime.
        _mouseCallback = MouseHookCallback;
        _dispatchTask = Task.Run(DispatchEventsAsync);
        _mouseDispatchTask = Task.Run(DispatchMouseEventsAsync);
        _escapeDispatchTask = Task.Run(DispatchEscapeEventsAsync);
        _thread = new Thread(HookThread) { IsBackground = true, Name = "AutoClicker keyboard hook" };
        _thread.Start();
    }

    public async Task<HotkeyRegistrationResult> StartMonitoringAsync(CancellationToken cancellationToken = default) =>
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async Task<HotkeyRegistrationResult> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken = default)
    {
        var ready = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!ready.Success) return ready;
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcuts are shutting down.");
            if (_emergency == hotkey) return new(false, "This shortcut is reserved for emergency stop.");
            if (_holdMouseButton == hotkey) return new(false, "The hold/release shortcut conflicts with the selected profile shortcut.");
            _active.TryAdd(hotkey, false);
        }
        return new(true);
    }

    public async Task<HotkeyRegistrationResult> RegisterHoldMouseButtonAsync(Hotkey hotkey, CancellationToken cancellationToken = default)
    {
        var ready = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!ready.Success) return ready;
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcuts are shutting down.");
            if (_emergency == hotkey) return new(false, "The hold/release shortcut is reserved for emergency stop.");
            if (_active.ContainsKey(hotkey)) return new(false, "The hold/release shortcut conflicts with the selected profile shortcut.");
            _holdMouseButton = hotkey;
        }
        return new(true);
    }

    public async Task<HotkeyRegistrationResult> RegisterEmergencyStopAsync(Hotkey hotkey, CancellationToken cancellationToken = default)
    {
        var ready = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!ready.Success) return ready;
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcuts are shutting down.");
            if (_active.ContainsKey(hotkey) || _holdMouseButton == hotkey) return new(false, "Emergency stop conflicts with a profile shortcut.");
            _emergency = hotkey;
        }
        return new(true);
    }

    public void Unregister(Hotkey hotkey) { lock (_gate) { _active.Remove(hotkey); ClearPressed(hotkey); } }
    public void UnregisterHoldMouseButton() { lock (_gate) { if (_holdMouseButton is { } key) ClearPressed(key); _holdMouseButton = null; } }
    public void UnregisterEmergencyStop() { lock (_gate) { if (_emergency is { } key) ClearPressed(key); _emergency = null; } }

    public void SetActionsSuspended(bool suspended)
    {
        lock (_gate)
        {
            if (suspended) { _actionsSuspended = true; _resumeAfterKeysReleased = false; }
            else { _resumeAfterKeysReleased = _downKeys.Count != 0; _actionsSuspended = _resumeAfterKeysReleased; }
        }
    }

    private void HookThread()
    {
        _threadId = Native.GetCurrentThreadId();
        Native.PeekMessage(out _, 0, 0, 0, 0); // Create the thread message queue before Dispose can post WM_QUIT.
        _hook = Native.SetWindowsHookEx(13, _callback, 0, 0);
        if (_hook == 0)
        {
            _ready.TrySetResult(new(false, new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the global keyboard hook.").Message));
            return;
        }
        _mouseHook = Native.SetWindowsHookEx(14, _mouseCallback, 0, 0);
        if (_mouseHook == 0)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the global mouse hook.").Message;
            Native.UnhookWindowsHookEx(_hook); _hook = 0;
            _ready.TrySetResult(new(false, error));
            return;
        }
        SeedModifiers();
        _ready.TrySetResult(new(true));
        while (Native.GetMessage(out var message, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(in message);
            Native.DispatchMessage(in message);
        }
        if (_hook != 0) { Native.UnhookWindowsHookEx(_hook); _hook = 0; }
        if (_mouseHook != 0) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; }
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            var down = wParam == 0x0100 || wParam == 0x0104;
            var up = wParam == 0x0101 || wParam == 0x0105;
            if (down || up) ProcessKey(data.VirtualKey, down);
        }
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && lParam != 0)
        {
            var data = Marshal.PtrToStructure<MouseHookData>(lParam);
            var message = unchecked((uint)wParam.ToInt64());
            var (button, down) = message switch
            {
                0x0201 => (MouseButton.Left, true), 0x0202 => (MouseButton.Left, false),
                0x0204 => (MouseButton.Right, true), 0x0205 => (MouseButton.Right, false),
                0x0207 => (MouseButton.Middle, true), 0x0208 => (MouseButton.Middle, false),
                _ => ((MouseButton)(-1), false)
            };
            if ((int)button >= 0)
            {
                var generated = data.ExtraInfo == WindowsMouseService.AutoClickerInputMarker;
                var emit = false;
                if (generated) emit = true;
                else lock (_gate)
                {
                    var wasDown = _downMouseButtons.Contains(button);
                    if (down && !wasDown) { _downMouseButtons.Add(button); emit = true; }
                    else if (!down && wasDown) { _downMouseButtons.Remove(button); emit = true; }
                }
                if (emit) _mouseEvents.Writer.TryWrite(new MouseInputEventArgs(button, down,
                    generated, new ScreenPoint(data.Point.X, data.Point.Y)));
            }
        }
        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    internal void ProcessKey(uint vk, bool down)
    {
        lock (_gate)
        {
            var wasDown = _downKeys.Contains(vk);
            if (down) _downKeys.Add(vk); else _downKeys.Remove(vk);
            if (down && !wasDown)
            {
                if (vk == 0x1B) _escapeEvents.Writer.TryWrite(true);
                var emergency = _emergency is { } stop && IsMatch(stop, vk);
                if (emergency) _events.Writer.TryWrite(new HotkeyEventArgs(_emergency!, isEmergencyStop: true));
                else if (!_actionsSuspended && !_resumeAfterKeysReleased)
                {
                    if (_holdMouseButton is { } holdHotkey && IsMatch(holdHotkey, vk))
                    {
                        _pressedHotkeys[vk] = (holdHotkey, true);
                        _events.Writer.TryWrite(new HotkeyEventArgs(holdHotkey, isHoldMouseButton: true));
                    }
                    else
                    {
                        foreach (var key in _active.Keys)
                        {
                            if (IsMatch(key, vk))
                            {
                                _pressedHotkeys[vk] = (key, false);
                                _events.Writer.TryWrite(new HotkeyEventArgs(key));
                                break;
                            }
                        }
                    }
                }
            }
            if (!down && _pressedHotkeys.Remove(vk, out var pressed)) _events.Writer.TryWrite(new HotkeyEventArgs(pressed.Hotkey, isReleased: true, isHoldMouseButton: pressed.IsHoldMouseButton));
            if (_resumeAfterKeysReleased && _downKeys.Count == 0) { _resumeAfterKeysReleased = false; _actionsSuspended = false; }
            if (!down && IsModifier(vk))
            {
                foreach (var pair in _pressedHotkeys.Where(pair => !IsMatch(pair.Value.Hotkey, pair.Key)).ToArray())
                {
                    _pressedHotkeys.Remove(pair.Key);
                    _events.Writer.TryWrite(new HotkeyEventArgs(pair.Value.Hotkey, isReleased: true, isHoldMouseButton: pair.Value.IsHoldMouseButton));
                }
            }
        }
    }

    private bool IsMatch(Hotkey hotkey, uint vk)
    {
        var actual = HotkeyModifiers.None;
        if (AnyDown(0x11, 0xA2, 0xA3)) actual |= HotkeyModifiers.Control;
        if (AnyDown(0x10, 0xA0, 0xA1)) actual |= HotkeyModifiers.Shift;
        if (AnyDown(0x12, 0xA4, 0xA5)) actual |= HotkeyModifiers.Alt;
        if (AnyDown(0x5B, 0x5C)) actual |= HotkeyModifiers.Meta;
        return Matches(hotkey, vk, actual);
    }
    private bool AnyDown(uint first, uint second, uint third = 0) =>
        _downKeys.Contains(first) || _downKeys.Contains(second) || third != 0 && _downKeys.Contains(third);
    private void SeedModifiers()
    {
        foreach (var key in new uint[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
            if (IsPressed((int)key)) _downKeys.Add(key);
    }
    internal static bool Matches(Hotkey hotkey, uint vk, HotkeyModifiers actualModifiers) =>
        KeyCode(hotkey.Key) == vk && actualModifiers == hotkey.Modifiers;
    private static bool IsPressed(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
    private static bool IsModifier(uint vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    internal static uint KeyCode(string key) => key switch
    {
        "SPACE" => 0x20, "ENTER" => 0x0D, "TAB" => 0x09, "HOME" => 0x24, "END" => 0x23,
        "INSERT" => 0x2D, "DELETE" => 0x2E, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
        "META" => 0x5B,
        _ when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) => key[0],
        _ when key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var f) => (uint)(0x70 + f - 1),
        _ => throw new ArgumentException($"Unsupported key: {key}", nameof(key))
    };

    private void ClearPressed(Hotkey key)
    {
        foreach (var pair in _pressedHotkeys.Where(pair => pair.Value.Hotkey == key).ToArray()) _pressedHotkeys.Remove(pair.Key);
    }

    private async Task DispatchEventsAsync()
    {
        await foreach (var args in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (args.IsEmergencyStop || !args.IsReleased) Pressed?.Invoke(this, args);
                else Released?.Invoke(this, args);
            }
            catch { /* A subscriber must not terminate the event dispatcher. */ }
        }
    }

    private async Task DispatchMouseEventsAsync()
    {
        await foreach (var args in _mouseEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate) if (_disposed) break;
            try { MouseButtonChanged?.Invoke(this, args); } catch { }
        }
    }
    private async Task DispatchEscapeEventsAsync()
    {
        await foreach (var _ in _escapeEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate) if (_disposed) break;
            try { EscapePressed?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        await _ready.Task.ConfigureAwait(false);
        if ((_hook != 0 || _mouseHook != 0) && _threadId != 0) Native.PostThreadMessage(_threadId, 0x0012, 0, 0);
        if (_thread.IsAlive) await Task.Run(_thread.Join).ConfigureAwait(false);
        _events.Writer.TryComplete();
        _mouseEvents.Writer.TryComplete(); _escapeEvents.Writer.TryComplete();
        await _dispatchTask.ConfigureAwait(false);
        await _mouseDispatchTask.ConfigureAwait(false); await _escapeDispatchTask.ConfigureAwait(false);
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    [StructLayout(LayoutKind.Sequential)] private struct KeyboardHookData { public uint VirtualKey, ScanCode, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookData { public NativePoint Point; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint HWnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public NativePoint Point; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint HookProc(int code, nint wParam, nint lParam);
    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] internal static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);
        [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
        [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
        [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
        [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TranslateMessage(in Message message);
        [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)] internal static extern nint DispatchMessage(in Message message);
        [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi)] internal static extern uint GetCurrentThreadId();
    }
}

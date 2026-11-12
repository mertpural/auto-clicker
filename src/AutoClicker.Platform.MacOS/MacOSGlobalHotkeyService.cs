using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using System.Threading.Channels;

namespace AutoClicker.Platform.MacOS;

public sealed class MacOSGlobalHotkeyService : IGlobalHotkeyService, IMouseInputMonitor
{
    private const uint KeyDownEvent = 10, KeyUpEvent = 11, FlagsChangedEvent = 12, SystemDefinedEvent = 14;
    private const uint LeftDownEvent = 1, LeftUpEvent = 2, RightDownEvent = 3, RightUpEvent = 4, OtherDownEvent = 25, OtherUpEvent = 26;
    private const uint TapDisabledTimeout = 0xFFFFFFFE, TapDisabledByUser = 0xFFFFFFFF;
    private const int KeyCodeField = 9, AutorepeatField = 8;
    private const int MouseButtonField = 3, UserDataField = 42, EscapeKeyCode = 53;
    private const ulong ControlFlag = 1UL << 18, ShiftFlag = 1UL << 17, OptionFlag = 1UL << 19, CommandFlag = 1UL << 20;
    private readonly object _gate = new();
    private readonly IMacOSNativeApi _native;
    private readonly Func<bool> _canListenForShortcuts;
    private readonly Func<bool> _isMacOS;
    private readonly MacEventTapCallback _callback;
    private readonly Channel<HotkeyEventArgs> _eventQueue = Channel.CreateUnbounded<HotkeyEventArgs>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Channel<MouseInputEventArgs> _mouseQueue = Channel.CreateUnbounded<MouseInputEventArgs>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Channel<bool> _escapeQueue = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task _eventDispatcher, _mouseDispatcher, _escapeDispatcher;
    private readonly HashSet<ushort> _downKeys = [];
    private readonly HashSet<MouseButton> _downMouseButtons = [];
    private readonly HashSet<Hotkey> _active = [];
    private Thread? _thread;
    private IntPtr _tap, _source, _runLoop;
    private Hotkey? _profile, _holdMouseButton, _emergency;
    private bool _actionsSuspended, _suppressProfileUntilKeysUp, _disposed;
    private TaskCompletionSource<bool>? _started;
    private Task? _disposeTask;

    public event EventHandler<HotkeyEventArgs>? Pressed;
    public event EventHandler<HotkeyEventArgs>? Released;
    public event EventHandler<MouseInputEventArgs>? MouseButtonChanged;
    public event EventHandler? EscapePressed;
    public bool IsAvailable => _thread is { IsAlive: true } && _tap != IntPtr.Zero;
    public string Status => IsAvailable ? "Global keyboard and mouse input monitoring is available." : "Enable AutoClicker under System Settings → Privacy & Security → Input Monitoring, then retry.";

    public MacOSGlobalHotkeyService(IAccessibilityPermissionService permissions)
        : this(permissions, new MacOSNativeApi(), () => permissions is MacOSAccessibilityPermissionService mac && mac.CanListenForShortcuts, OperatingSystem.IsMacOS) { }
    internal MacOSGlobalHotkeyService(IAccessibilityPermissionService permissions, IMacOSNativeApi native,
        Func<bool> canListenForShortcuts, Func<bool>? isMacOS = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        _native = native;
        _canListenForShortcuts = canListenForShortcuts;
        _isMacOS = isMacOS ?? OperatingSystem.IsMacOS;
        _callback = OnNativeEvent;
        _eventDispatcher = DispatchQueuedEventsAsync();
        _mouseDispatcher = DispatchMouseEventsAsync();
        _escapeDispatcher = DispatchEscapeEventsAsync();
    }

    public Task<HotkeyRegistrationResult> StartMonitoringAsync(CancellationToken cancellationToken = default) => EnsureListenerAsync(cancellationToken);

    public async Task<HotkeyRegistrationResult> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (!MacKeyCodes.TryGet(hotkey.Key, out _)) return new(false, $"{hotkey.Key} is not supported as a macOS global shortcut.");
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcut service has been disposed.");
            if (hotkey == _emergency) return new(false, "The profile shortcut cannot match Emergency Stop.");
            if (hotkey == _holdMouseButton) return new(false, "The profile shortcut conflicts with the hold/release shortcut.");
        }
        var listening = await EnsureListenerAsync(cancellationToken).ConfigureAwait(false);
        if (!listening.Success) return listening;
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcut service has been disposed.");
            _profile = hotkey;
            _active.RemoveWhere(key => key != hotkey && key != _holdMouseButton);
            _downKeys.Clear();
            _suppressProfileUntilKeysUp = false;
        }
        return new(true);
    }

    public async Task<HotkeyRegistrationResult> RegisterHoldMouseButtonAsync(Hotkey hotkey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (!MacKeyCodes.TryGet(hotkey.Key, out _)) return new(false, $"{hotkey.Key} is not supported as a macOS global shortcut.");
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcut service has been disposed.");
            if (hotkey == _emergency) return new(false, "The hold/release shortcut is reserved for Emergency Stop.");
            if (hotkey == _profile) return new(false, "The hold/release shortcut conflicts with the profile shortcut.");
        }
        var listening = await EnsureListenerAsync(cancellationToken).ConfigureAwait(false);
        if (!listening.Success) return listening;
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcut service has been disposed.");
            _holdMouseButton = hotkey;
            _active.RemoveWhere(key => key != _profile && key != hotkey);
        }
        return new(true);
    }

    public async Task<HotkeyRegistrationResult> RegisterEmergencyStopAsync(Hotkey hotkey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (!MacKeyCodes.TryGet(hotkey.Key, out _)) return new(false, $"{hotkey.Key} is not supported as a macOS global shortcut.");
        lock (_gate)
        {
            if (_disposed) return new(false, "Global shortcut service has been disposed.");
            if (hotkey == _profile || hotkey == _holdMouseButton) return new(false, "Emergency Stop must use a different shortcut from profile actions.");
        }
        var listening = await EnsureListenerAsync(cancellationToken).ConfigureAwait(false);
        if (!listening.Success) return listening;
        lock (_gate) { if (_disposed) return new(false, "Global shortcut service has been disposed."); _emergency = hotkey; }
        return new(true);
    }

    private Task<HotkeyRegistrationResult> EnsureListenerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isMacOS()) return Task.FromResult(new HotkeyRegistrationResult(false, "Global shortcuts are available only on macOS."));
        if (!_canListenForShortcuts()) return Task.FromResult(new HotkeyRegistrationResult(false, "Enable AutoClicker under System Settings → Privacy & Security → Accessibility, then retry."));
        Task<bool> started;
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(new HotkeyRegistrationResult(false, "Global shortcut service has been disposed."));
            if (_thread is { IsAlive: true } && _tap != IntPtr.Zero) return Task.FromResult(new HotkeyRegistrationResult(true));
            if (_started is { Task.IsCompleted: true } && !_started.Task.Result)
            {
                _thread = null;
            }
            if (_thread is null || !_thread.IsAlive)
            {
                _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _thread = new Thread(EventLoopMain) { IsBackground = true, Name = "AutoClicker macOS event tap" };
                _thread.Start();
            }
            started = _started!.Task;
        }
        return WaitForStartAsync(started, cancellationToken);
    }

    private static async Task<HotkeyRegistrationResult> WaitForStartAsync(Task<bool> started, CancellationToken cancellationToken)
    {
        try { return await started.WaitAsync(cancellationToken).ConfigureAwait(false) ? new(true) : new(false, "macOS could not create a global keyboard listener. Check Accessibility permission in System Settings and retry."); }
        catch (OperationCanceledException) { throw; }
    }

    private void EventLoopMain()
    {
        IntPtr tap = IntPtr.Zero, source = IntPtr.Zero, runLoop = IntPtr.Zero;
        try
        {
            tap = _native.CreateEventTap(_callback);
            if (tap == IntPtr.Zero) { _started?.TrySetResult(false); return; }
            source = _native.CreateRunLoopSource(tap);
            if (source == IntPtr.Zero) { _started?.TrySetResult(false); return; }
            runLoop = _native.GetCurrentRunLoop();
            if (runLoop == IntPtr.Zero) { _started?.TrySetResult(false); return; }
            _native.AddRunLoopSource(runLoop, source);
            lock (_gate) { if (_disposed) { _started?.TrySetResult(false); return; } _tap = tap; _source = source; _runLoop = runLoop; }
            _native.EnableEventTap(tap, true);
            _started?.TrySetResult(true);
            _native.RunLoopRun();
        }
        catch { _started?.TrySetResult(false); }
        finally
        {
            lock (_gate) { if (_tap == tap) { _tap = IntPtr.Zero; _source = IntPtr.Zero; _runLoop = IntPtr.Zero; } }
            if (runLoop != IntPtr.Zero && source != IntPtr.Zero) _native.RemoveRunLoopSource(runLoop, source);
            if (source != IntPtr.Zero) _native.Release(source);
            if (tap != IntPtr.Zero) _native.Release(tap);
        }
    }

    internal IntPtr OnNativeEvent(IntPtr proxy, uint type, IntPtr evt, IntPtr refcon)
    {
        try
        {
            if (type == TapDisabledTimeout || type == TapDisabledByUser)
            {
                lock (_gate) if (!_disposed && _tap != IntPtr.Zero) _native.EnableEventTap(_tap, true);
                return evt;
            }
            if (evt == IntPtr.Zero) return evt;
            if (type is LeftDownEvent or LeftUpEvent or RightDownEvent or RightUpEvent or OtherDownEvent or OtherUpEvent)
            {
                var buttonNumber = type is LeftDownEvent or LeftUpEvent ? 0 : type is RightDownEvent or RightUpEvent ? 1 : (int)_native.GetEventIntegerValue(evt, MouseButtonField);
                var button = buttonNumber switch { 0 => MouseButton.Left, 1 => MouseButton.Right, 2 => MouseButton.Middle, _ => (MouseButton)(-1) };
                if ((int)button >= 0)
                {
                    var pressed = type is LeftDownEvent or RightDownEvent or OtherDownEvent;
                    var ownEvent = _native.GetEventIntegerValue(evt, UserDataField) == MacOSMouseService.AutoClickerInputMarker;
                    var point = _native.GetEventLocation(evt);
                    var emit = ownEvent;
                    if (!ownEvent) lock (_gate)
                    {
                        if (!_disposed)
                        {
                            var wasDown = _downMouseButtons.Contains(button);
                            if (pressed && !wasDown) { _downMouseButtons.Add(button); emit = true; }
                            else if (!pressed && wasDown) { _downMouseButtons.Remove(button); emit = true; }
                        }
                    }
                    if (emit) _mouseQueue.Writer.TryWrite(new MouseInputEventArgs(button, pressed, ownEvent,
                        new ScreenPoint((int)Math.Round(point.X, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y, MidpointRounding.AwayFromZero))));
                }
                return evt;
            }
            ushort keyCode;
            if (type == SystemDefinedEvent)
            {
                // Without fn, Mac keyboards send F1–F12 as media keys (F12 = volume up, F7 = rewind, ...).
                if (!_native.TryReadSystemDefinedKey(evt, out var mediaKey, out var mediaPressed) || !MacKeyCodes.TryMapMediaKey(mediaKey, out keyCode)) return evt;
                type = mediaPressed ? KeyDownEvent : KeyUpEvent;
            }
            else if (type is KeyDownEvent or KeyUpEvent or FlagsChangedEvent)
                keyCode = MacKeyCodes.Normalize((ushort)_native.GetEventIntegerValue(evt, KeyCodeField));
            else return evt;
            var flags = ToModifiers(_native.GetEventFlags(evt));
            List<HotkeyEventArgs>? events = null;
            lock (_gate)
            {
                if (_disposed) return evt;
                if (type == KeyDownEvent)
                {
                    var wasDown = !_downKeys.Add(keyCode);
                    if (!wasDown && keyCode == EscapeKeyCode) _escapeQueue.Writer.TryWrite(true);
                    if (!wasDown && _emergency is { } emergency && Matches(emergency, keyCode, flags))
                    {
                        events = [new HotkeyEventArgs(emergency, isEmergencyStop: true)];
                    }
                    else if (!wasDown && !_actionsSuspended && !_suppressProfileUntilKeysUp)
                    {
                        if (_profile is { } profile && Matches(profile, keyCode, flags) && _active.Add(profile))
                            events = [new HotkeyEventArgs(profile)];
                        else if (_holdMouseButton is { } holdHotkey && Matches(holdHotkey, keyCode, flags) && _active.Add(holdHotkey))
                            events = [new HotkeyEventArgs(holdHotkey, isHoldMouseButton: true)];
                    }
                }
                else if (type == KeyUpEvent)
                {
                    _downKeys.Remove(keyCode);
                    if (_profile is { } profile && MacKeyCodes.TryGet(profile.Key, out var registered) && registered == keyCode && _active.Remove(profile))
                        events = [new HotkeyEventArgs(profile, isReleased: true)];
                    if (_holdMouseButton is { } holdHotkey && MacKeyCodes.TryGet(holdHotkey.Key, out var holdKey) && holdKey == keyCode && _active.Remove(holdHotkey))
                        events = [new HotkeyEventArgs(holdHotkey, isReleased: true, isHoldMouseButton: true)];
                    if (_downKeys.Count == 0) _suppressProfileUntilKeysUp = false;
                }
                else
                {
                    if (_profile is { } profile && _active.Contains(profile) && ToModifiers(_native.GetEventFlags(evt)) != profile.Modifiers)
                    { _active.Remove(profile); events = [new HotkeyEventArgs(profile, isReleased: true)]; }
                    if (_holdMouseButton is { } holdHotkey && _active.Contains(holdHotkey) && ToModifiers(_native.GetEventFlags(evt)) != holdHotkey.Modifiers)
                    { _active.Remove(holdHotkey); events = [new HotkeyEventArgs(holdHotkey, isReleased: true, isHoldMouseButton: true)]; }
                    if (_downKeys.Count == 0) _suppressProfileUntilKeysUp = false;
                }
            }
            if (events is not null) foreach (var item in events) Queue(item);
        }
        catch { }
        return evt;
    }

    private async Task DispatchQueuedEventsAsync()
    {
        await foreach (var args in _eventQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate) if (_disposed) break;
            try { if (args.IsReleased) Released?.Invoke(this, args); else Pressed?.Invoke(this, args); } catch { }
        }
    }

    private async Task DispatchMouseEventsAsync()
    {
        await foreach (var args in _mouseQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate) if (_disposed) break;
            try { MouseButtonChanged?.Invoke(this, args); } catch { }
        }
    }
    private async Task DispatchEscapeEventsAsync()
    {
        await foreach (var _ in _escapeQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate) if (_disposed) break;
            try { EscapePressed?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }

    private void Queue(HotkeyEventArgs args) => _eventQueue.Writer.TryWrite(args);

    private static bool Matches(Hotkey hotkey, ushort key, HotkeyModifiers modifiers) => MacKeyCodes.TryGet(hotkey.Key, out var wanted) && wanted == key && hotkey.Modifiers == modifiers;
    private static HotkeyModifiers ToModifiers(ulong flags)
    {
        var result = HotkeyModifiers.None;
        if ((flags & ControlFlag) != 0) result |= HotkeyModifiers.Control;
        if ((flags & ShiftFlag) != 0) result |= HotkeyModifiers.Shift;
        if ((flags & OptionFlag) != 0) result |= HotkeyModifiers.Alt;
        if ((flags & CommandFlag) != 0) result |= HotkeyModifiers.Meta;
        return result;
    }

    public void Unregister(Hotkey hotkey)
    {
        lock (_gate) { if (_profile != hotkey) return; _profile = null; _active.Remove(hotkey); _suppressProfileUntilKeysUp = _downKeys.Count != 0; }
    }
    public void UnregisterHoldMouseButton() { lock (_gate) { if (_holdMouseButton is { } key) _active.Remove(key); _holdMouseButton = null; } }
    public void UnregisterEmergencyStop() { lock (_gate) _emergency = null; }
    public void SetActionsSuspended(bool suspended)
    {
        lock (_gate)
        {
            if (_actionsSuspended == suspended) return;
            _actionsSuspended = suspended;
            if (suspended)
            {
                if (_profile is { } profile && _active.Remove(profile)) Queue(new HotkeyEventArgs(profile, isReleased: true));
            }
            else if (_downKeys.Count != 0) _suppressProfileUntilKeysUp = true;
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            _profile = _holdMouseButton = _emergency = null;
            _active.Clear();
            var thread = _thread;
            if (_tap != IntPtr.Zero) _native.EnableEventTap(_tap, false);
            _native.StopRunLoop(_runLoop);
            _native.WakeRunLoop(_runLoop);
            _disposeTask = FinishDisposeAsync(thread);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task FinishDisposeAsync(Thread? thread)
    {
        if (thread is not null && thread != Thread.CurrentThread) await Task.Run(() => thread.Join(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
        _eventQueue.Writer.TryComplete();
        _mouseQueue.Writer.TryComplete(); _escapeQueue.Writer.TryComplete();
        await _eventDispatcher.ConfigureAwait(false);
        await _mouseDispatcher.ConfigureAwait(false); await _escapeDispatcher.ConfigureAwait(false);
        lock (_gate) _thread = null;
    }
}

internal static class MacKeyCodes
{
    private static readonly IReadOnlyDictionary<string, ushort> Codes = Build();
    public static bool TryGet(string key, out ushort code) => Codes.TryGetValue(key, out code);

    // Special-function key codes emitted by F1–F6 on Apple keyboards when fn is not held.
    private static readonly IReadOnlyDictionary<ushort, ushort> Alternates = new Dictionary<ushort, ushort>
    {
        [0x91] = 0x7A, // brightness down -> F1
        [0x90] = 0x78, // brightness up -> F2
        [0xA0] = 0x63, // Mission Control -> F3
        [0x83] = 0x76, [0xB1] = 0x76, // Launchpad / Spotlight -> F4
        [0xB0] = 0x60, // Dictation -> F5
        [0xB2] = 0x61, // Do Not Disturb -> F6
    };
    public static ushort Normalize(ushort code) => Alternates.TryGetValue(code, out var mapped) ? mapped : code;

    // NX_KEYTYPE_* values carried by NX_SYSDEFINED (subtype 8) media key events.
    private static readonly IReadOnlyDictionary<int, ushort> MediaKeys = new Dictionary<int, ushort>
    {
        [3] = 0x7A, [2] = 0x78,            // brightness down / up -> F1 / F2
        [22] = 0x60, [21] = 0x61,          // keyboard illumination down / up -> F5 / F6
        [18] = 0x62, [20] = 0x62,          // previous / rewind -> F7
        [16] = 0x64,                       // play -> F8
        [17] = 0x65, [19] = 0x65,          // next / fast -> F9
        [7] = 0x6D,                        // mute -> F10
        [1] = 0x67,                        // volume down -> F11
        [0] = 0x6F,                        // volume up -> F12
    };
    public static bool TryMapMediaKey(int keyType, out ushort code) => MediaKeys.TryGetValue(keyType, out code);
    private static Dictionary<string, ushort> Build()
    {
        var map = new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["A"] = 0x00, ["S"] = 0x01, ["D"] = 0x02, ["F"] = 0x03, ["H"] = 0x04, ["G"] = 0x05,
            ["Z"] = 0x06, ["X"] = 0x07, ["C"] = 0x08, ["V"] = 0x09, ["B"] = 0x0B, ["Q"] = 0x0C,
            ["W"] = 0x0D, ["E"] = 0x0E, ["R"] = 0x0F, ["Y"] = 0x10, ["T"] = 0x11, ["1"] = 0x12,
            ["2"] = 0x13, ["3"] = 0x14, ["4"] = 0x15, ["6"] = 0x16, ["5"] = 0x17, ["="] = 0x18,
            ["9"] = 0x19, ["7"] = 0x1A, ["-"] = 0x1B, ["8"] = 0x1C, ["0"] = 0x1D, ["]"] = 0x1E,
            ["O"] = 0x1F, ["U"] = 0x20, ["["] = 0x21, ["I"] = 0x22, ["P"] = 0x23, ["ENTER"] = 0x24,
            ["L"] = 0x25, ["J"] = 0x26, ["'"] = 0x27, ["K"] = 0x28, [";"] = 0x29, ["\\"] = 0x2A,
            [","] = 0x2B, ["/"] = 0x2C, ["N"] = 0x2D, ["M"] = 0x2E, ["."] = 0x2F, ["TAB"] = 0x30,
            ["SPACE"] = 0x31, ["`"] = 0x32, ["DELETE"] = 0x33, ["HOME"] = 0x73, ["END"] = 0x77,
            ["INSERT"] = 0x72, ["PAGEUP"] = 0x74, ["PAGEDOWN"] = 0x79,
            ["F1"] = 0x7A, ["F2"] = 0x78, ["F3"] = 0x63, ["F4"] = 0x76, ["F5"] = 0x60,
            ["F6"] = 0x61, ["F7"] = 0x62, ["F8"] = 0x64, ["F9"] = 0x65, ["F10"] = 0x6D,
            ["F11"] = 0x67, ["F12"] = 0x6F, ["F13"] = 0x69, ["F14"] = 0x6B, ["F15"] = 0x71,
            ["F16"] = 0x6A, ["F17"] = 0x40, ["F18"] = 0x4F, ["F19"] = 0x50, ["F20"] = 0x5A
        };
        return map;
    }
}

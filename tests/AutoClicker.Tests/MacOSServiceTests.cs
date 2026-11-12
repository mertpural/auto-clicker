using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Platform.MacOS;
using Xunit;

namespace AutoClicker.Tests;

public sealed class MacOSServiceTests
{
    [Theory]
    [InlineData(MouseButton.Left, 1u, 2u, 0u)]
    [InlineData(MouseButton.Right, 3u, 4u, 1u)]
    [InlineData(MouseButton.Middle, 25u, 26u, 2u)]
    public async Task ClickMapsToOneDownAndUpPair(MouseButton button, uint down, uint up, uint nativeButton)
    {
        var native = new FakeNative();
        var cursor = new FakeCursor { Position = new(42, -8) };
        var service = new MacOSMouseService(new GrantedPermission(), cursor, native);

        await service.ClickAsync(button);

        Assert.Equal(new uint[] { down, up }, native.Posted.Select(e => e.Type));
        Assert.All(native.Posted, e => Assert.Equal(nativeButton, e.Button));
        Assert.All(native.Posted, e => Assert.Equal((42d, -8d), (e.X, e.Y)));
        Assert.Equal(new long[] { MacOSMouseService.AutoClickerInputMarker, MacOSMouseService.AutoClickerInputMarker }, native.TaggedMarkers);
        Assert.Equal(new ScreenPoint(42, -8), cursor.Position);
        Assert.Empty(cursor.SetCalls);
        Assert.Equal(2, native.Released.Count);
    }

    [Fact]
    public async Task DoubleClickSendsClickCountOneThenTwo()
    {
        var native = new FakeNative();
        var service = new MacOSMouseService(new GrantedPermission(), new FakeCursor { Position = new(1, 1) }, native);
        await service.DoubleClickAsync(MouseButton.Left);
        Assert.Equal(new long[] { 1, 1, 2, 2 }, native.ClickStates);
    }

    [Theory]
    [InlineData(MouseButton.Left, 1u, 2u, 0u)]
    [InlineData(MouseButton.Right, 3u, 4u, 1u)]
    [InlineData(MouseButton.Middle, 25u, 26u, 2u)]
    public async Task StatefulButtonEventsPostOneTaggedQuartzEventAndReleaseItsResource(MouseButton button, uint down, uint up, uint nativeButton)
    {
        var native = new FakeNative();
        var service = new MacOSMouseService(new GrantedPermission(), new FakeCursor { Position = new(42, -8) }, native);

        await service.ButtonDownAsync(button);
        Assert.Equal((down, nativeButton), (Assert.Single(native.Posted).Type, native.Posted[0].Button));
        Assert.Equal(MacOSMouseService.AutoClickerInputMarker, Assert.Single(native.TaggedMarkers));
        Assert.Single(native.Released);

        native.Posted.Clear();
        native.TaggedMarkers.Clear();
        await service.ButtonUpAsync(button);
        Assert.Equal((up, nativeButton), (Assert.Single(native.Posted).Type, native.Posted[0].Button));
        Assert.Equal(MacOSMouseService.AutoClickerInputMarker, Assert.Single(native.TaggedMarkers));
        Assert.Equal(2, native.Released.Count);
    }

    [Fact]
    public async Task FixedPositionWarpsOnlyWhenNeededAndPostsAtNativeGlobalCoordinates()
    {
        var native = new FakeNative();
        var cursor = new FakeCursor { Position = new(0, 0) };
        var service = new MacOSMouseService(new GrantedPermission(), cursor, native);

        await service.ClickAsync(MouseButton.Left, new(-1920, 1100));

        Assert.Equal(new ScreenPoint(-1920, 1100), Assert.Single(cursor.SetCalls));
        Assert.All(native.Posted, e => Assert.Equal((-1920d, 1100d), (e.X, e.Y)));
    }

    [Fact]
    public async Task MissingPermissionFailsBeforeCreatingNativeEvents()
    {
        var native = new FakeNative();
        var service = new MacOSMouseService(new DeniedPermission(), new FakeCursor(), native);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.ClickAsync(MouseButton.Left));

        Assert.Empty(native.Posted);
        Assert.Empty(native.Created);
    }

    [Fact]
    public async Task CreationFailureReleasesPreviouslyCreatedNativeEvent()
    {
        var native = new FakeNative { FailOnCreateNumber = 2 };
        var service = new MacOSMouseService(new GrantedPermission(), new FakeCursor(), native);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClickAsync(MouseButton.Left));

        Assert.Single(native.Released);
        Assert.Empty(native.Posted);
    }

    [Fact]
    public void CursorReadUsesNativeQuartzLocationAndReleasesTemporaryEvent()
    {
        var native = new FakeNative { CursorLocation = (-2560.5, -20.5) };
        var service = new MacOSCursorService(new GrantedPermission(), native);

        Assert.Equal(new ScreenPoint(-2561, -21), service.GetPosition());
        Assert.Single(native.Released);
    }

    [Theory]
    [InlineData("A", 0x00)]
    [InlineData("K", 0x28)]
    [InlineData("F6", 0x61)]
    [InlineData("F12", 0x6F)]
    [InlineData("PAGEDOWN", 0x79)]
    public void SupportedHotkeysMapToMacVirtualKeyCodes(string key, int expected)
    {
        Assert.True(MacKeyCodes.TryGet(key, out var actual));
        Assert.Equal((ushort)expected, actual);
    }

    [Fact]
    public void UnsupportedFunctionKeysFailMapping()
    {
        Assert.False(MacKeyCodes.TryGet("F21", out _));
    }

    [Fact]
    public async Task EventTapDeliversHoldReleaseOnKeyOrModifierReleaseAndEmergencyPriority()
    {
        var native = new FakeNative();
        var permission = new GrantedPermission();
        var service = new MacOSGlobalHotkeyService(permission, native, () => true, () => true);
        var pressed = new TaskCompletionSource<HotkeyEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<HotkeyEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pressCount = 0;
        service.Pressed += (_, args) => { Interlocked.Increment(ref pressCount); pressed.TrySetResult(args); };
        service.Released += (_, args) => released.TrySetResult(args);
        try
        {
            Assert.True((await service.RegisterAsync(new Hotkey("F6", HotkeyModifiers.Control))).Success);
            Assert.True((await service.RegisterEmergencyStopAsync(new Hotkey("F12"))).Success);
            native.Emit(10, 0x61, 1UL << 18);
            Assert.False((await pressed.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsEmergencyStop);
            native.Emit(10, 0x61, 1UL << 18); // keyboard autorepeat
            await Task.Delay(30);
            Assert.Equal(1, pressCount);
            native.Emit(12, 0x3B, 0); // release Control while F6 remains down
            Assert.True((await released.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsReleased);

            var emergency = new TaskCompletionSource<HotkeyEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Pressed += (_, args) => { if (args.IsEmergencyStop) emergency.TrySetResult(args); };
            native.Emit(10, 0x6F, 0);
            Assert.True((await emergency.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsEmergencyStop);
        }
        finally { await service.DisposeAsync(); }
        native.Emit(10, 0x6F, 0);
        Assert.Equal(2, pressCount);
    }

    [Fact]
    public async Task HoldMouseGlobalHotkeyIgnoresAutorepeatAndRejectsConflicts()
    {
        var native = new FakeNative();
        var service = new MacOSGlobalHotkeyService(new GrantedPermission(), native, () => true, () => true);
        var presses = new System.Collections.Concurrent.ConcurrentQueue<HotkeyEventArgs>();
        var releases = new System.Collections.Concurrent.ConcurrentQueue<HotkeyEventArgs>();
        using var received = new SemaphoreSlim(0);
        service.Pressed += (_, args) => { presses.Enqueue(args); received.Release(); };
        service.Released += (_, args) => { releases.Enqueue(args); received.Release(); };
        try
        {
            Assert.True((await service.RegisterEmergencyStopAsync(new Hotkey("F12"))).Success);
            Assert.True((await service.RegisterHoldMouseButtonAsync(new Hotkey("F5"))).Success);
            Assert.False((await service.RegisterAsync(new Hotkey("F5"))).Success);
            Assert.False((await service.RegisterHoldMouseButtonAsync(new Hotkey("F12"))).Success);

            native.Emit(10, 0x60, 0); // F5 down
            native.Emit(10, 0x60, 0); // autorepeat
            await received.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(presses.TryDequeue(out var first));
            Assert.True(first!.IsHoldMouseButton);
            Assert.Empty(presses);

            native.Emit(11, 0x60, 0); // release does not toggle
            await received.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(releases.TryDequeue(out var release));
            Assert.True(release!.IsReleased);
            Assert.True(release.IsHoldMouseButton);

            native.Emit(10, 0x60, 0); // next real press toggles again
            await received.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(presses.TryDequeue(out var second));
            Assert.True(second!.IsHoldMouseButton);
        }
        finally { await service.DisposeAsync(); }
    }

    [Fact]
    public async Task DisabledTapIsReenabledAndCallbacksAfterDisposeAreIgnored()
    {
        var native = new FakeNative();
        var service = new MacOSGlobalHotkeyService(new GrantedPermission(), native, () => true, () => true);
        var pressed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Pressed += (_, _) => pressed.TrySetResult(true);
        Assert.True((await service.RegisterEmergencyStopAsync(new Hotkey("F12"))).Success);
        native.Emit(0xFFFFFFFE, 0, 0);
        Assert.Equal(2, native.EnableCalls.Count(value => value));
        await service.DisposeAsync();
        native.Emit(10, 0x6F, 0);
        Assert.False(pressed.Task.IsCompleted);
    }

    [Fact]
    public async Task Passive_mouse_monitor_deduplicates_physical_down_and_marks_own_events()
    {
        var native = new FakeNative();
        var service = new MacOSGlobalHotkeyService(new GrantedPermission(), native, () => true, () => true);
        var events = new System.Collections.Concurrent.ConcurrentQueue<MouseInputEventArgs>();
        using var received = new SemaphoreSlim(0);
        service.MouseButtonChanged += (_, args) => { events.Enqueue(args); received.Release(); };
        try
        {
            Assert.True((await service.StartMonitoringAsync()).Success);
            native.EmitMouse(1, 0); native.EmitMouse(1, 0); // duplicate down is ignored
            native.EmitMouse(1, 0, MacOSMouseService.AutoClickerInputMarker);
            native.EmitMouse(2, 0, MacOSMouseService.AutoClickerInputMarker);
            native.EmitMouse(2, 0);
            for (var i = 0; i < 4; i++) await received.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { await service.DisposeAsync(); }
        var delivered = events.ToArray();
        Assert.Equal(4, delivered.Length);
        Assert.Equal(new[] { false, true, true, false }, delivered.Select(e => e.IsAutoClickerGenerated));
        Assert.Equal(new[] { true, true, false, false }, delivered.Select(e => e.IsPressed));
    }

    [Fact]
    public async Task Mac_media_and_special_function_keys_trigger_F_key_shortcuts_without_fn()
    {
        var native = new FakeNative();
        var service = new MacOSGlobalHotkeyService(new GrantedPermission(), native, () => true, () => true);
        var presses = new System.Collections.Concurrent.ConcurrentQueue<HotkeyEventArgs>();
        using var received = new SemaphoreSlim(0);
        service.Pressed += (_, args) => { presses.Enqueue(args); received.Release(); };
        try
        {
            Assert.True((await service.RegisterEmergencyStopAsync(new Hotkey("F12"))).Success);
            Assert.True((await service.RegisterAsync(new Hotkey("F6"))).Success);
            native.EmitMediaKey(0, pressed: true);   // volume up == F12 without fn
            Assert.True(await received.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(presses.TryDequeue(out var emergency) && emergency!.IsEmergencyStop);
            native.EmitMediaKey(0, pressed: false);
            native.Emit(10, 0xB2, 0);                // Do Not Disturb key code == F6 without fn
            Assert.True(await received.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(presses.TryDequeue(out var profile) && !profile!.IsEmergencyStop && profile.Hotkey == new Hotkey("F6"));
        }
        finally { await service.DisposeAsync(); }
    }

    [Fact]
    public async Task Clicks_over_AutoClicker_own_window_are_skipped()
    {
        var native = new FakeNative { OwnWindow = true };
        var service = new MacOSMouseService(new GrantedPermission(), new FakeCursor { Position = new(5, 5) }, native);
        await service.ClickAsync(MouseButton.Left);
        Assert.Empty(native.Posted);
    }

    [Fact]
    public void AccessibilityTrusted_GrantsPermissions_WithoutRequiringPreflightListenOrPost()
    {
        var native = new FakeNative { AccessibilityTrusted = true, PostEventsAllowed = false, ListenEventsAllowed = false };
        var service = new MacOSAccessibilityPermissionService(native);

        if (OperatingSystem.IsMacOS())
        {
            Assert.True(service.IsGranted);
            Assert.True(service.CanListenForShortcuts);
        }
    }

    [Fact]
    public async Task RequestAsync_DoesNotRePrompt_WhenAccessibilityAlreadyTrusted()
    {
        var native = new FakeNative { AccessibilityTrusted = true, PostEventsAllowed = false, ListenEventsAllowed = false };
        var service = new MacOSAccessibilityPermissionService(native);

        var result = await service.RequestAsync();

        if (OperatingSystem.IsMacOS())
        {
            Assert.True(result);
            Assert.Equal(0, native.RequestTrustCalls);
        }
    }

    [Fact]
    public void DeniedAccessibility_ReportsNotGranted()
    {
        var native = new FakeNative { AccessibilityTrusted = false, PostEventsAllowed = false, ListenEventsAllowed = false };
        var service = new MacOSAccessibilityPermissionService(native);

        Assert.False(service.IsGranted);
        Assert.False(service.CanListenForShortcuts);
    }

    private sealed class GrantedPermission : IAccessibilityPermissionService
    {
        public bool IsGranted => true;
        public string Status => "Granted";
        public Task<bool> RequestAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void OpenSystemSettings() { }
    }
    private sealed class DeniedPermission : IAccessibilityPermissionService
    {
        public bool IsGranted => false;
        public string Status => "Denied";
        public Task<bool> RequestAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public void OpenSystemSettings() { }
    }
    private sealed class FakeCursor : ICursorService
    {
        public ScreenPoint Position { get; set; }
        public List<ScreenPoint> SetCalls { get; } = [];
        public ScreenPoint GetPosition() => Position;
        public void SetPosition(ScreenPoint position) { Position = position; SetCalls.Add(position); }
        public Task<ScreenPoint> PickPositionAsync(CancellationToken cancellationToken = default) => Task.FromResult(Position);
    }
    private sealed class FakeNative : IMacOSNativeApi
    {
        private int _next = 100;
        private int _createCount;
        private readonly Dictionary<IntPtr, (uint Type, uint Button, double X, double Y)> _events = [];
        private readonly Dictionary<IntPtr, (uint Type, uint Button)> _mouseEvents = [];
        private readonly Dictionary<IntPtr, long> _userData = [];
        private readonly Dictionary<IntPtr, (long KeyCode, ulong Flags)> _keyEvents = [];
        private readonly ManualResetEventSlim _stopped = new();
        private int _nextKeyEvent = 1000;
        private MacEventTapCallback? _callback;
        public int FailOnCreateNumber { get; set; }
        public (double X, double Y) CursorLocation { get; set; }
        public List<(uint Type, uint Button, double X, double Y)> Created { get; } = [];
        public List<(uint Type, uint Button, double X, double Y)> Posted { get; } = [];
        public List<IntPtr> Released { get; } = [];
        public List<bool> EnableCalls { get; } = [];
        public List<long> TaggedMarkers { get; } = [];
        public List<long> ClickStates { get; } = [];
        public bool AccessibilityTrusted { get; set; } = true;
        public bool PostEventsAllowed { get; set; } = true;
        public bool ListenEventsAllowed { get; set; } = true;
        public int RequestTrustCalls { get; private set; }
        public bool IsAccessibilityTrusted() => AccessibilityTrusted;
        public bool RequestAccessibilityTrust() { RequestTrustCalls++; return true; }
        public bool CanPostEvents() => PostEventsAllowed;
        public bool RequestPostEventAccess() => true;
        public bool CanListenForEvents() => ListenEventsAllowed;
        public bool RequestListenEventAccess() => true;
        public bool OpenAccessibilitySettings() => true;
        public IntPtr CreateMouseEvent(uint type, uint button, double x, double y)
        {
            _createCount++;
            if (_createCount == FailOnCreateNumber) return IntPtr.Zero;
            var handle = (IntPtr)(++_next);
            var evt = (type, button, x, y);
            Created.Add(evt); _events[handle] = evt; return handle;
        }
        public IntPtr CreateEventAtCurrentCursor() => (IntPtr)1;
        public (double X, double Y) GetEventLocation(IntPtr evt) => CursorLocation;
        public void PostEvent(IntPtr evt) => Posted.Add(_events[evt]);
        public void WarpCursor(double x, double y) => CursorLocation = (x, y);
        public void Release(IntPtr value) { if (value != IntPtr.Zero) Released.Add(value); }
        public IntPtr CreateEventTap(MacEventTapCallback callback) { _callback = callback; return (IntPtr)10; }
        public IntPtr CreateRunLoopSource(IntPtr port) => (IntPtr)11;
        public IntPtr GetCurrentRunLoop() => (IntPtr)12;
        public void AddRunLoopSource(IntPtr runLoop, IntPtr source) { }
        public void RemoveRunLoopSource(IntPtr runLoop, IntPtr source) { }
        public void RunLoopRun() => _stopped.Wait(TimeSpan.FromSeconds(5));
        public void StopRunLoop(IntPtr runLoop) => _stopped.Set();
        public void WakeRunLoop(IntPtr runLoop) { }
        public void EnableEventTap(IntPtr port, bool enabled) => EnableCalls.Add(enabled);
        public long GetEventIntegerValue(IntPtr evt, int field) => _mouseEvents.TryGetValue(evt, out var mouse)
            ? field == 3 ? mouse.Button : _userData.GetValueOrDefault(evt)
            : _keyEvents[evt].KeyCode;
        public void SetEventIntegerValue(IntPtr evt, int field, long value)
        {
            if (field == 1) { ClickStates.Add(value); return; }
            TaggedMarkers.Add(value); _userData[evt] = value;
        }
        public ulong GetEventFlags(IntPtr evt) => _keyEvents[evt].Flags;
        public void Emit(uint type, long keyCode, ulong flags)
        {
            var evt = (IntPtr)Interlocked.Increment(ref _nextKeyEvent);
            _keyEvents[evt] = (keyCode, flags);
            _callback!(IntPtr.Zero, type, evt, IntPtr.Zero);
        }
        public void EmitMouse(uint type, uint button, long userData = 0)
        {
            var evt = (IntPtr)Interlocked.Increment(ref _nextKeyEvent);
            _mouseEvents[evt] = (type, button); _userData[evt] = userData;
            _callback!(IntPtr.Zero, type, evt, IntPtr.Zero);
        }
        private readonly Dictionary<IntPtr, (int KeyType, bool Pressed)> _mediaEvents = [];
        public bool OwnWindow { get; set; }
        public bool IsOwnWindowAt(double x, double y) => OwnWindow;
        public bool TryReadSystemDefinedKey(IntPtr evt, out int keyType, out bool pressed)
        {
            var found = _mediaEvents.TryGetValue(evt, out var media);
            keyType = media.KeyType; pressed = media.Pressed;
            return found;
        }
        public void EmitMediaKey(int keyType, bool pressed)
        {
            var evt = (IntPtr)Interlocked.Increment(ref _nextKeyEvent);
            _mediaEvents[evt] = (keyType, pressed); _keyEvents[evt] = (0, 0);
            _callback!(IntPtr.Zero, 14, evt, IntPtr.Zero);
        }
    }
}

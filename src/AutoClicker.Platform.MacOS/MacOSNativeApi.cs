using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoClicker.Platform.MacOS;

public interface IMacOSNativeApi
{
    bool IsAccessibilityTrusted();
    bool RequestAccessibilityTrust();
    bool CanPostEvents();
    bool RequestPostEventAccess();
    bool CanListenForEvents();
    bool RequestListenEventAccess();
    bool OpenAccessibilitySettings();
    IntPtr CreateMouseEvent(uint type, uint button, double x, double y);
    IntPtr CreateEventAtCurrentCursor();
    (double X, double Y) GetEventLocation(IntPtr evt);
    void PostEvent(IntPtr evt);
    void WarpCursor(double x, double y);
    void Release(IntPtr value);
    IntPtr CreateEventTap(MacEventTapCallback callback);
    IntPtr CreateRunLoopSource(IntPtr port);
    IntPtr GetCurrentRunLoop();
    void AddRunLoopSource(IntPtr runLoop, IntPtr source);
    void RemoveRunLoopSource(IntPtr runLoop, IntPtr source);
    void RunLoopRun();
    void StopRunLoop(IntPtr runLoop);
    void WakeRunLoop(IntPtr runLoop);
    void EnableEventTap(IntPtr port, bool enabled);
    long GetEventIntegerValue(IntPtr evt, int field);
    void SetEventIntegerValue(IntPtr evt, int field, long value) { }
    ulong GetEventFlags(IntPtr evt);
    /// <summary>Reads an NX_SYSDEFINED media/special key (sent by F1–F12 on Mac keyboards when fn is not held).</summary>
    bool TryReadSystemDefinedKey(IntPtr evt, out int keyType, out bool pressed) { keyType = 0; pressed = false; return false; }
    /// <summary>True when the front-most visible window at the Quartz point belongs to this process.</summary>
    bool IsOwnWindowAt(double x, double y) => false;
}

public delegate IntPtr MacEventTapCallback(IntPtr proxy, uint type, IntPtr evt, IntPtr refcon);

public sealed class MacOSNativeApi : IMacOSNativeApi
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint SessionEventTap = 1;
    private const uint HeadInsert = 0;
    private const uint ListenOnly = 1;
    private const uint KeyDown = 10;
    private const uint KeyUp = 11;
    private const uint FlagsChanged = 12;
    private const uint TapDisabledTimeout = 0xFFFFFFFE;
    private const uint TapDisabledByUser = 0xFFFFFFFF;
    private const int Utf8Encoding = 0x08000100;
    private static readonly Lazy<IntPtr> CoreFoundationHandle = new(() => NativeLibrary.TryLoad(CoreFoundation, out var library) ? library : IntPtr.Zero);

    public bool IsAccessibilityTrusted() => AXIsProcessTrusted();
    public bool RequestAccessibilityTrust()
    {
        var key = CFStringCreateWithCString(IntPtr.Zero, "AXTrustedCheckOptionPrompt", Utf8Encoding);
        var promptValue = GetCFBooleanTrue();
        if (key == IntPtr.Zero || promptValue == IntPtr.Zero) return AXIsProcessTrusted();
        try
        {
            var keyCallbacks = GetCFExport("kCFTypeDictionaryKeyCallBacks");
            var valueCallbacks = GetCFExport("kCFTypeDictionaryValueCallBacks");
            var dict = keyCallbacks == IntPtr.Zero || valueCallbacks == IntPtr.Zero ? IntPtr.Zero
                : CFDictionaryCreate(IntPtr.Zero, [key], [promptValue], 1, keyCallbacks, valueCallbacks);
            if (dict == IntPtr.Zero) return AXIsProcessTrusted();
            try { return AXIsProcessTrustedWithOptions(dict); }
            finally { CFRelease(dict); }
        }
        finally { CFRelease(key); }
    }
    public bool CanPostEvents() => CGPreflightPostEventAccess();
    public bool RequestPostEventAccess() => CGRequestPostEventAccess();
    public bool CanListenForEvents() => CGPreflightListenEventAccess();
    public bool RequestListenEventAccess() => CGRequestListenEventAccess();

    public bool OpenAccessibilitySettings()
    {
        foreach (var url in new[]
        {
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Accessibility",
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension"
        })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("open") { UseShellExecute = false, ArgumentList = { url } });
                if (process is not null) { process.WaitForExit(2500); if (process.ExitCode == 0) return true; }
            }
            catch { }
        }
        try
        {
            using var process = Process.Start(new ProcessStartInfo("open") { UseShellExecute = false, ArgumentList = { "-b", "com.apple.systempreferences" } });
            if (process is not null) { process.WaitForExit(2500); if (process.ExitCode == 0) return true; }
        }
        catch { }
        return false;
    }

    public IntPtr CreateMouseEvent(uint type, uint button, double x, double y) => CGEventCreateMouseEvent(IntPtr.Zero, type, new CGPoint(x, y), button);
    public IntPtr CreateEventAtCurrentCursor() => CGEventCreate(IntPtr.Zero);
    public (double X, double Y) GetEventLocation(IntPtr evt) { var p = CGEventGetLocation(evt); return (p.X, p.Y); }
    public void PostEvent(IntPtr evt) => CGEventPost(SessionEventTap, evt);
    public void WarpCursor(double x, double y)
    {
        var result = CGWarpMouseCursorPosition(new CGPoint(x, y));
        if (result != 0) throw new InvalidOperationException($"macOS could not move the cursor (CoreGraphics error {result}).");
    }
    public void Release(IntPtr value) { if (value != IntPtr.Zero) CFRelease(value); }
    public IntPtr CreateEventTap(MacEventTapCallback callback) => CGEventTapCreate(SessionEventTap, HeadInsert, ListenOnly,
        (1UL << (int)KeyDown) | (1UL << (int)KeyUp) | (1UL << (int)FlagsChanged) | (1UL << SystemDefined)
        | (1UL << 1) | (1UL << 2) | (1UL << 3) | (1UL << 4) | (1UL << 25) | (1UL << 26), callback, IntPtr.Zero);
    public IntPtr CreateRunLoopSource(IntPtr port) => CFMachPortCreateRunLoopSource(IntPtr.Zero, port, 0);
    public IntPtr GetCurrentRunLoop() => CFRunLoopGetCurrent();
    public void AddRunLoopSource(IntPtr runLoop, IntPtr source)
    {
        var defaultMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", Utf8Encoding);
        var commonMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopCommonModes", Utf8Encoding);
        if (defaultMode != IntPtr.Zero)
        {
            try { CFRunLoopAddSource(runLoop, source, defaultMode); }
            finally { CFRelease(defaultMode); }
        }
        if (commonMode != IntPtr.Zero)
        {
            try { CFRunLoopAddSource(runLoop, source, commonMode); }
            finally { CFRelease(commonMode); }
        }
    }
    public void RemoveRunLoopSource(IntPtr runLoop, IntPtr source)
    {
        if (runLoop == IntPtr.Zero || source == IntPtr.Zero) return;
        var defaultMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", Utf8Encoding);
        var commonMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopCommonModes", Utf8Encoding);
        if (defaultMode != IntPtr.Zero)
        {
            try { CFRunLoopRemoveSource(runLoop, source, defaultMode); }
            finally { CFRelease(defaultMode); }
        }
        if (commonMode != IntPtr.Zero)
        {
            try { CFRunLoopRemoveSource(runLoop, source, commonMode); }
            finally { CFRelease(commonMode); }
        }
    }
    public void RunLoopRun() => CFRunLoopRun();
    public void StopRunLoop(IntPtr runLoop) { if (runLoop != IntPtr.Zero) CFRunLoopStop(runLoop); }
    public void WakeRunLoop(IntPtr runLoop) { if (runLoop != IntPtr.Zero) CFRunLoopWakeUp(runLoop); }
    public void EnableEventTap(IntPtr port, bool enabled) => CGEventTapEnable(port, enabled);
    public long GetEventIntegerValue(IntPtr evt, int field) => CGEventGetIntegerValueField(evt, field);
    public void SetEventIntegerValue(IntPtr evt, int field, long value) => CGEventSetIntegerValueField(evt, field, value);
    public ulong GetEventFlags(IntPtr evt) => CGEventGetFlags(evt);

    public bool TryReadSystemDefinedKey(IntPtr evt, out int keyType, out bool pressed)
    {
        keyType = 0; pressed = false;
        var nsEvent = NSEventClass.Value;
        if (evt == IntPtr.Zero || nsEvent == IntPtr.Zero) return false;
        var pool = objc_autoreleasePoolPush();
        try
        {
            var ns = MsgSendPtr(nsEvent, SelEventWithCGEvent.Value, evt);
            if (ns == IntPtr.Zero || MsgSendShort(ns, SelSubtype.Value) != AuxControlButtonsSubtype) return false;
            var data1 = (long)MsgSendNInt(ns, SelData1.Value);
            keyType = (int)((data1 & 0xFFFF0000) >> 16);
            var state = (int)((data1 & 0xFF00) >> 8);
            if (state != 0x0A && state != 0x0B) return false;
            pressed = state == 0x0A;
            return true;
        }
        catch { return false; }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public bool IsOwnWindowAt(double x, double y)
    {
        var keys = WindowKeys.Value;
        if (keys.Pid == IntPtr.Zero) return false;
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == IntPtr.Zero) return false;
        try
        {
            var count = CFArrayGetCount(list);
            for (nint i = 0; i < count; i++) // front-to-back order
            {
                var info = CFArrayGetValueAtIndex(list, i);
                if (info == IntPtr.Zero) continue;
                var alphaRef = CFDictionaryGetValue(info, keys.Alpha);
                if (alphaRef != IntPtr.Zero && CFNumberGetValue(alphaRef, Float64Type, out double alpha) && alpha <= 0.01) continue;
                var boundsRef = CFDictionaryGetValue(info, keys.Bounds);
                if (boundsRef == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(boundsRef, out var r)) continue;
                if (x < r.X || y < r.Y || x >= r.X + r.Width || y >= r.Y + r.Height) continue;
                var pidRef = CFDictionaryGetValue(info, keys.Pid);
                return pidRef != IntPtr.Zero && CFNumberGetValue(pidRef, SInt64Type, out long pid) && pid == Environment.ProcessId;
            }
            return false;
        }
        finally { CFRelease(list); }
    }

    private static readonly Lazy<IntPtr> CoreGraphicsHandle = new(() => NativeLibrary.TryLoad(CoreGraphics, out var library) ? library : IntPtr.Zero);
    private static readonly Lazy<(IntPtr Pid, IntPtr Bounds, IntPtr Alpha)> WindowKeys = new(() =>
        (ReadCGString("kCGWindowOwnerPID"), ReadCGString("kCGWindowBounds"), ReadCGString("kCGWindowAlpha")));
    private static IntPtr ReadCGString(string name) => CoreGraphicsHandle.Value != IntPtr.Zero
        && NativeLibrary.TryGetExport(CoreGraphicsHandle.Value, name, out var address) ? Marshal.ReadIntPtr(address) : IntPtr.Zero;
    private static readonly Lazy<IntPtr> NSEventClass = new(() => { try { return objc_getClass("NSEvent"); } catch { return IntPtr.Zero; } });
    private static readonly Lazy<IntPtr> SelEventWithCGEvent = new(() => sel_registerName("eventWithCGEvent:"));
    private static readonly Lazy<IntPtr> SelSubtype = new(() => sel_registerName("subtype"));
    private static readonly Lazy<IntPtr> SelData1 = new(() => sel_registerName("data1"));

    private static IntPtr GetCFBooleanTrue()
    {
        var value = GetCFExport("kCFBooleanTrue");
        return value == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(value);
    }
    private static IntPtr GetCFExport(string name) => CoreFoundationHandle.Value != IntPtr.Zero
        && NativeLibrary.TryGetExport(CoreFoundationHandle.Value, name, out var value) ? value : IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)] private readonly struct CGPoint(double x, double y) { public readonly double X = x; public readonly double Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, Width, Height; }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const int SystemDefined = 14;
    private const short AuxControlButtonsSubtype = 8;
    private const uint OnScreenOnly = 1, ExcludeDesktopElements = 16;
    private const nint SInt64Type = 4, Float64Type = 6;
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgSendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint MsgSendNInt(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern short MsgSendShort(IntPtr receiver, IntPtr selector);
    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, nint type, out long value);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, nint type, out double value);

    [DllImport(ApplicationServices)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
    [DllImport(ApplicationServices)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightPostEventAccess();
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestPostEventAccess();
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightListenEventAccess();
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestListenEventAccess();
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, int encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreate(IntPtr allocator, [In] IntPtr[] keys, [In] IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint type, CGPoint point, uint button);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(uint tap, IntPtr evt);
    [DllImport(CoreGraphics)] private static extern int CGWarpMouseCursorPosition(CGPoint point);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong mask, MacEventTapCallback callback, IntPtr userInfo);
    [DllImport(CoreGraphics)] private static extern void CGEventTapEnable(IntPtr port, [MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport(CoreGraphics)] private static extern long CGEventGetIntegerValueField(IntPtr evt, int field);
    [DllImport(CoreGraphics)] private static extern void CGEventSetIntegerValueField(IntPtr evt, int field, long value);
    [DllImport(CoreGraphics)] private static extern ulong CGEventGetFlags(IntPtr evt);
    [DllImport(CoreFoundation)] private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);
    [DllImport(CoreFoundation)] private static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)] private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] private static extern void CFRunLoopRemoveSource(IntPtr runLoop, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] private static extern void CFRunLoopRun();
    [DllImport(CoreFoundation)] private static extern void CFRunLoopStop(IntPtr runLoop);
    [DllImport(CoreFoundation)] private static extern void CFRunLoopWakeUp(IntPtr runLoop);
}

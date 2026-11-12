using System.Runtime.InteropServices;

namespace AutoClicker.Platform.MacOS;

/// <summary>
/// A real NSStatusItem with a click action. Avalonia's macOS tray exposes no click event (only a menu), so the
/// menu bar popover needs its own status item. Everything goes through the Objective-C runtime and fails closed:
/// <see cref="TryCreate"/> returns null instead of throwing so callers can fall back to the native menu.
/// </summary>
public sealed class MacOSStatusItem : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string TargetClassName = "AutoClickerStatusTarget";
    private delegate void ClickCallback(IntPtr self, IntPtr selector, IntPtr sender);

    // Kept alive for the lifetime of the process: the Objective-C class holds a raw pointer to it.
    private static ClickCallback? s_callback;
    private static Action? s_onClick;

    private IntPtr _statusBar, _item;

    private MacOSStatusItem() { }

    public static MacOSStatusItem? TryCreate(byte[] pngBytes, Action onClick, double iconSize = 18)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            // Loading AppKit makes sure NSStatusBar and NSImage are registered before objc_getClass.
            NativeLibrary.TryLoad(AppKit, out _);
            var statusBarClass = objc_getClass("NSStatusBar");
            if (statusBarClass == IntPtr.Zero) return null;
            var statusBar = Send(statusBarClass, Sel("systemStatusBar"));
            if (statusBar == IntPtr.Zero) return null;
            var item = Send(statusBar, Sel("statusItemWithLength:"), -1.0); // NSVariableStatusItemLength
            if (item == IntPtr.Zero) return null;
            Send(item, Sel("retain"));
            var button = Send(item, Sel("button"));
            if (button == IntPtr.Zero) { Remove(statusBar, item); return null; }

            var image = CreateImage(pngBytes, iconSize);
            if (image != IntPtr.Zero) Send(button, Sel("setImage:"), image);
            else Send(button, Sel("setTitle:"), CreateString("AC"));

            s_onClick = onClick;
            var target = CreateTarget();
            if (target == IntPtr.Zero) { Remove(statusBar, item); return null; }
            Send(button, Sel("setTarget:"), target);
            Send(button, Sel("setAction:"), Sel("onClick:"));
            return new MacOSStatusItem { _statusBar = statusBar, _item = item };
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    private static IntPtr CreateImage(byte[] png, double size)
    {
        if (png.Length == 0) return IntPtr.Zero;
        var buffer = Marshal.AllocHGlobal(png.Length);
        try
        {
            Marshal.Copy(png, 0, buffer, png.Length);
            var data = Send(objc_getClass("NSData"), Sel("dataWithBytes:length:"), buffer, (nint)png.Length);
            if (data == IntPtr.Zero) return IntPtr.Zero;
            var image = Send(Send(objc_getClass("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
            if (image == IntPtr.Zero) return IntPtr.Zero;
            Send(image, Sel("setSize:"), size, size);
            return image;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IntPtr CreateString(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IntPtr CreateTarget()
    {
        var cls = objc_getClass(TargetClassName);
        if (cls == IntPtr.Zero)
        {
            cls = objc_allocateClassPair(objc_getClass("NSObject"), TargetClassName, 0);
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            s_callback = (_, _, _) => s_onClick?.Invoke();
            var imp = Marshal.GetFunctionPointerForDelegate(s_callback);
            if (!class_addMethod(cls, Sel("onClick:"), imp, "v@:@")) return IntPtr.Zero;
            objc_registerClassPair(cls);
        }
        var instance = Send(Send(cls, Sel("alloc")), Sel("init"));
        if (instance != IntPtr.Zero) Send(instance, Sel("retain"));
        return instance;
    }

    private static void Remove(IntPtr statusBar, IntPtr item)
    {
        if (statusBar != IntPtr.Zero && item != IntPtr.Zero) Send(statusBar, Sel("removeStatusItem:"), item);
    }

    public void Dispose()
    {
        if (_item == IntPtr.Zero) return;
        Remove(_statusBar, _item);
        _item = IntPtr.Zero;
    }

    private static IntPtr Sel(string name) => sel_registerName(name);

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);
    [DllImport(ObjC)] private static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, double argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, double first, double second);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument, nint length);
}

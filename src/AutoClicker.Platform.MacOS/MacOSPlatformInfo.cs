using AutoClicker.Core.Abstractions;

namespace AutoClicker.Platform.MacOS;

public sealed class MacOSPlatformInfo : IPlatformInfo
{
    public string Name => "macOS";
    public string ConfigurationDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AutoClicker");
    public bool NativeInputAvailable => true;
    public string NativeInputStatus => "macOS uses Quartz mouse events. Enable Accessibility for mouse control and Input Monitoring for global shortcuts.";
}

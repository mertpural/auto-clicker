using AutoClicker.Core.Abstractions;

namespace AutoClicker.Platform.Windows;

public sealed class WindowsPlatformInfo : IPlatformInfo
{
    public string Name => "Windows";
    public string ConfigurationDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClicker");
    public bool NativeInputAvailable => true;
    public string NativeInputStatus => "Windows native mouse input and global shortcuts are available. Elevated target applications may reject input.";
}

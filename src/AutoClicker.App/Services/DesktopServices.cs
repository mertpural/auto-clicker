using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AutoClicker.App.Services;

public interface IDesktopDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);
}

public sealed class DesktopDialogService : IDesktopDialogService
{
    private readonly LocalizationService? _localization;
    public Window? Owner { get; set; }

    public DesktopDialogService(LocalizationService? localization = null) => _localization = localization;

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        if (Owner is null) throw new InvalidOperationException("The main window is not available.");
        var dialog = new Window
        {
            Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        var cancel = new Button { Content = _localization?.Get("Common_Cancel") ?? "Cancel", MinWidth = 88 };
        var confirm = new Button { Content = confirmLabel, MinWidth = 88 };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { dialog.Close(false); args.Handled = true; }
        };
        dialog.Content = new StackPanel
        {
            Margin = new(24), Spacing = 20,
            Children =
            {
                new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 392 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, confirm }
                }
            }
        };
        dialog.Opened += (_, _) => cancel.Focus();
        return dialog.ShowDialog<bool>(Owner);
    }
}

public interface IConfigurationFolderService
{
    void Open(string directory);
}

public sealed class ConfigurationFolderService : IConfigurationFolderService
{
    public void Open(string directory)
    {
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo("open") { UseShellExecute = false };
            info.ArgumentList.Add(directory);
            Process.Start(info);
        }
        else Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AutoClicker.App.Views;

/// <summary>Small popover shown from the macOS menu bar icon. All logic stays in the shared view models.</summary>
public partial class MenuBarPopup : Window
{
    public event EventHandler? OpenRequested;
    public event EventHandler? QuitRequested;

    public MenuBarPopup()
    {
        InitializeComponent();
        OpenButton.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        QuitButton.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true;
        Hide();
    }
}

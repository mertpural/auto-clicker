using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using AutoClicker.App.ViewModels;
using AutoClicker.Core.Models;
using AutoClicker.Platform.MacOS;
using Avalonia.Threading;

namespace AutoClicker.App.Services;

/// <summary>Shared Avalonia TrayIcon and NativeMenu implementation for Windows and macOS.</summary>
public sealed class AvaloniaTrayService : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Action _open, _quit;
    private readonly Action? _togglePopup;
    private readonly LocalizationService? _localization;
    private readonly NativeMenuItem _stateItem = new() { Header = "Ready", IsEnabled = false };
    private readonly NativeMenuItem _profileItem = new() { Header = "Profile: —", IsEnabled = false };
    private readonly NativeMenuItem _toggleItem = new() { Header = "Start" };
    private readonly NativeMenuItem _openItem = new();
    private readonly NativeMenuItem _quitItem = new();
    private TrayIcon? _icon;
    private MacOSStatusItem? _statusItem;
    private TrayIcons? _icons;
    private WindowIcon? _windowIcon;
    private Stream? _iconStream;
    private ProfileEditorViewModel? _observedProfile;

    /// <param name="togglePopup">When set (macOS menu bar), clicking the icon shows this popup instead of the native menu.</param>
    public AvaloniaTrayService(MainWindowViewModel viewModel, Action open, Action quit, LocalizationService? localization = null, Action? togglePopup = null)
    { _viewModel = viewModel; _open = open; _quit = quit; _localization = localization; _togglePopup = togglePopup; }

    public string CurrentProfileLabel => _localization?.Get("Tray_Profile", _viewModel.Profiles.SelectedProfile?.Name ?? "—")
        ?? "Profile: " + (_viewModel.Profiles.SelectedProfile?.Name ?? "—");
    internal IReadOnlyList<string> MenuLabels => new[] { _stateItem, _profileItem, _toggleItem, _openItem, _quitItem }
        .Select(item => item.Header?.ToString() ?? "").ToArray();

    public bool Initialize(Application app)
    {
        if (_icon is not null || _statusItem is not null) return true;
        try
        {
            if (_togglePopup is not null && TryCreateStatusItem())
            {
                _viewModel.Profiles.PropertyChanged += OnProfilesChanged;
                if (_localization is not null) _localization.LanguageChanged += OnLanguageChanged;
                Update();
                return true;
            }
            _iconStream = CreateIconStream();
            _windowIcon = new WindowIcon(_iconStream);
            var menu = new NativeMenu();
            menu.Items.Add(_stateItem);
            menu.Items.Add(_profileItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            _toggleItem.Click += (_, _) => _ = _viewModel.Profiles.ToggleEngineCommand.ExecuteAsync(null);
            menu.Items.Add(_toggleItem);
            _openItem.Click += (_, _) => _open();
            menu.Items.Add(_openItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            _quitItem.Click += (_, _) => _quit();
            menu.Items.Add(_quitItem);
            _icon = new TrayIcon { Icon = _windowIcon, Menu = menu, ToolTipText = "AutoClicker", IsVisible = true };
            _icon.Clicked += (_, _) => _open();
            _icons = new TrayIcons { _icon };
            app.SetValue(TrayIcon.IconsProperty, _icons);
            _viewModel.Profiles.PropertyChanged += OnProfilesChanged;
            if (_localization is not null) _localization.LanguageChanged += OnLanguageChanged;
            Update();
            return true;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    public void Update()
    {
        var profiles = _viewModel.Profiles;
        if (!ReferenceEquals(_observedProfile, profiles.SelectedProfile))
        {
            if (_observedProfile is not null) _observedProfile.PropertyChanged -= OnProfileChanged;
            _observedProfile = profiles.SelectedProfile;
            if (_observedProfile is not null) _observedProfile.PropertyChanged += OnProfileChanged;
        }
        _stateItem.Header = _localization?.Get("Tray_Status", profiles.EngineStatusText) ?? profiles.EngineStatusText;
        _profileItem.Header = CurrentProfileLabel;
        _toggleItem.Header = profiles.EngineButtonText;
        _toggleItem.IsEnabled = profiles.CanToggleEngine;
        _openItem.Header = _localization?.Get("Tray_Open") ?? "Open AutoClicker";
        _quitItem.Header = _localization?.Get("Tray_Quit") ?? "Quit";
    }

    private void OnProfileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(ProfileEditorViewModel.Name)) _profileItem.Header = CurrentProfileLabel; }

    private void OnProfilesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProfilesViewModel.EngineTelemetry) or nameof(ProfilesViewModel.EngineBusy)
            or nameof(ProfilesViewModel.EngineButtonText) or nameof(ProfilesViewModel.IsMouseButtonHeld)
            or nameof(ProfilesViewModel.EngineStatusText) or nameof(ProfilesViewModel.SelectedProfile) or nameof(ProfilesViewModel.CanToggleEngine)) Update();
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => Update();

    /// <summary>macOS: a status item whose click opens the popup. Returns false so the caller falls back to the native menu.</summary>
    private bool TryCreateStatusItem()
    {
        using var source = AssetLoader.Open(new Uri("avares://AutoClicker/assets/branding/autoclicker-icon-64.png"));
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        _statusItem = MacOSStatusItem.TryCreate(buffer.ToArray(), () => Dispatcher.UIThread.Post(() => _togglePopup?.Invoke()));
        return _statusItem is not null;
    }

    private static MemoryStream CreateIconStream()
    {
        using var source = AssetLoader.Open(new Uri("avares://AutoClicker/assets/branding/autoclicker.ico"));
        var output = new MemoryStream();
        source.CopyTo(output);
        output.Position = 0;
        return output;
    }

    public void Dispose()
    {
        _viewModel.Profiles.PropertyChanged -= OnProfilesChanged;
        if (_localization is not null) _localization.LanguageChanged -= OnLanguageChanged;
        if (_observedProfile is not null) _observedProfile.PropertyChanged -= OnProfileChanged;
        _observedProfile = null;
        _statusItem?.Dispose(); _statusItem = null;
        if (_icon is not null) _icon.IsVisible = false;
        if (_icons is not null && Application.Current is { } app) app.SetValue(TrayIcon.IconsProperty, null);
        _icon?.Dispose(); _icon = null; _icons = null;
        _windowIcon = null;
        _iconStream?.Dispose(); _iconStream = null;
    }
}

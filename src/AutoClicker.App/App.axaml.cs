using Avalonia;
using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AutoClicker.App.Composition;
using AutoClicker.App.Services;
using AutoClicker.App.ViewModels;
using AutoClicker.App.Views;
using AutoClicker.Core.Models;
using AutoClicker.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private MainWindowViewModel? _viewModel;
    private MainWindow? _window;
    private ISingleInstanceService? _singleInstance;
    private AvaloniaTrayService? _tray;
    private MenuBarPopupService? _popup;
    private Task _startupTask = Task.CompletedTask;
    private Task? _shutdownTask;
    private bool _allowClose;
    private bool _forceExit;
    private bool _smokeTest;
    private bool _diagnosticsRequested;
    private CancellationTokenSource? _diagnosticsCancellation;
    private Task? _diagnosticsTask;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var arguments = Environment.GetCommandLineArgs();
            var configuration = arguments.FirstOrDefault(argument => argument.StartsWith("--data-dir=", StringComparison.Ordinal))?[11..];
            _smokeTest = arguments.Contains("--smoke-test", StringComparer.Ordinal);
            _services = ServiceRegistration.Create(configuration, _smokeTest, _smokeTest);
            var appLocalization = _services.GetRequiredService<LocalizationService>();
            appLocalization.Initialize(this);
            void UpdateAboutMenu()
            {
                if (NativeMenu.GetMenu(this)?.Items.FirstOrDefault() is NativeMenuItem about) about.Header = appLocalization.Get("Menu_AboutApp");
            }
            UpdateAboutMenu();
            appLocalization.LanguageChanged += (_, _) => UpdateAboutMenu();
            _logger = _services.GetRequiredService<ILogger<App>>();
            _singleInstance = _services.GetRequiredService<ISingleInstanceService>();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!_singleInstance.TryAcquire())
            {
                _logger.LogInformation("Second instance requested activation");
                _ = ForwardActivationAndExitAsync(desktop);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            if (arguments.Contains("--diagnostics", StringComparer.Ordinal))
            {
                _diagnosticsRequested = true;
                _logger.LogInformation("Performance diagnostics enabled; sampling every 5 seconds.");
            }
            _viewModel = _services.GetRequiredService<MainWindowViewModel>();
            var window = _window = new MainWindow { DataContext = _viewModel };
            if (_smokeTest && arguments.FirstOrDefault(argument => argument.StartsWith("--preview-size=", StringComparison.Ordinal)) is { } sizeArgument)
            {
                var dimensions = sizeArgument[15..].Split('x');
                if (dimensions.Length == 2 && int.TryParse(dimensions[0], out var width) && int.TryParse(dimensions[1], out var height)
                    && width >= 820 && height >= 620) { window.Width = width; window.Height = height; }
            }
            if (!_smokeTest) { window.ShowInTaskbar = false; window.WindowState = WindowState.Minimized; }
            _services.GetRequiredService<AvaloniaWindowPresentationService>().MainWindow = window;
            _services.GetRequiredService<DesktopDialogService>().Owner = window;
            desktop.MainWindow = window;
            if (!_smokeTest || arguments.Contains("--smoke-tray", StringComparer.Ordinal))
            {
                // macOS menu bar: clicking the icon opens a popover with profile, start/stop and usage statistics.
                if (OperatingSystem.IsMacOS() && !_smokeTest)
                    _popup = new MenuBarPopupService(_viewModel, ShowMainWindow, QuitFromTray, _services.GetService<ICursorService>());
                _tray = new AvaloniaTrayService(_viewModel, ShowMainWindow, QuitFromTray, _services.GetRequiredService<LocalizationService>(), _popup is null ? null : _popup.Toggle);
                if (!_tray.Initialize(this)) { _logger.LogWarning("Tray initialization unavailable"); _tray = null; }
            }
            _ = _singleInstance.RunActivationListenerAsync(ActivateOnUiThreadAsync);
            window.Opened += (_, _) => _startupTask = StartAsync(desktop, _smokeTest, arguments);
            window.Closing += (_, args) =>
            {
                if (_allowClose) return;
                // macOS menu bar app: the red button hides the window and the menu bar item keeps running (quit with Cmd+Q or the popup).
                var keepInMenuBar = OperatingSystem.IsMacOS() && _popup is not null;
                if (!_forceExit && (keepInMenuBar || _viewModel?.Settings.CloseBehavior == CloseBehavior.MinimizeToTray) && _tray is not null)
                {
                    args.Cancel = true; window.Hide(); window.ShowInTaskbar = false;
                    _logger?.LogInformation("Application minimized to tray");
                    return;
                }
                args.Cancel = true;
                _shutdownTask ??= CloseAsync(desktop);
            };
            desktop.ShutdownRequested += (_, args) =>
            {
                if (_allowClose) return;
                args.Cancel = true;
                _shutdownTask ??= CloseAsync(desktop);
            };
            desktop.Exit += (_, _) => DisposeServices();
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _logger.LogInformation("AutoClicker started");
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, bool smokeTest, string[] args)
    {
        try
        {
            await _viewModel!.Profiles.InitializeAsync();
            await _viewModel.Profiles.RefreshInputPermissionAsync();
            if (!smokeTest && _viewModel.Profiles.IsMacOS && !_viewModel.Profiles.IsInputPermissionGranted)
            {
                await _viewModel.Profiles.RequestInputPermissionCommand.ExecuteAsync(null);
            }
            if (_viewModel.Profiles.IsLoaded) await _viewModel.Profiles.RegisterHotkeysAsync();
            _tray?.Update();
            if (_diagnosticsRequested)
            {
                _diagnosticsCancellation = new CancellationTokenSource();
                _diagnosticsTask = MonitorProcessAsync(_diagnosticsCancellation.Token);
            }
            if (!smokeTest)
            {
                if (_tray is not null && _viewModel.Settings.StartMinimized
                    && (!_viewModel.Profiles.IsMacOS || _viewModel.Profiles.IsInputPermissionGranted))
                { _window!.ShowInTaskbar = false; _window.Hide(); }
                else ShowMainWindow();
            }
            if (!smokeTest) return;
            var arguments = args;
            if (arguments.FirstOrDefault(argument => argument.StartsWith("--control-smoke-report=", StringComparison.Ordinal)) is { } controlSmoke)
            {
                await DesktopControlSmoke.RunAsync(_window!, _viewModel, _services!.GetRequiredService<LocalizationService>(), _tray, controlSmoke[23..]);
                _allowClose = true; desktop.Shutdown(0); return;
            }
            if (arguments.FirstOrDefault(argument => argument.StartsWith("--retention-report=", StringComparison.Ordinal)) is { } retention)
            {
                await DesktopRetentionAudit.RunAsync(_window!, _viewModel, _services!.GetRequiredService<LocalizationService>(), retention[19..]);
                _allowClose = true; desktop.Shutdown(0); return;
            }
            var theme = arguments.FirstOrDefault(argument => argument.StartsWith("--preview-theme=", StringComparison.Ordinal))?[16..];
            if (Enum.TryParse<Appearance>(theme, out var appearance)) _viewModel.Settings.Appearance = appearance;
            var language = arguments.FirstOrDefault(argument => argument.StartsWith("--preview-language=", StringComparison.Ordinal))?[19..];
            if (Enum.TryParse<ApplicationLanguage>(language, ignoreCase: true, out var applicationLanguage)) _viewModel.Settings.Language = applicationLanguage;
            var previewTab = arguments.FirstOrDefault(argument => argument.StartsWith("--preview-tab=", StringComparison.Ordinal))?[14..];
            if (int.TryParse(previewTab, out var tabIndex)) _viewModel.Profiles.SelectedProfile!.SelectedTabIndex = tabIndex;
            else if (previewTab is not null)
            {
                _viewModel.Profiles.SelectedProfile!.SelectedTabIndex = previewTab.ToUpperInvariant() switch
                {
                    "ADVANCED" => 1,
                    "POSITION" => 2,
                    "STATISTICS" => 3,
                    _ => 0
                };
            }
            if (arguments.FirstOrDefault(argument => argument.StartsWith("--preview-page=", StringComparison.OrdinalIgnoreCase)) is { } previewPage)
            {
                switch (previewPage[15..].ToUpperInvariant())
                {
                    case "ABOUT": _viewModel.ShowAboutCommand.Execute(null); break;
                    case "SETTINGS": _viewModel.ShowSettingsCommand.Execute(null); break;
                    default: _viewModel.ShowProfilesCommand.Execute(null); break;
                }
            }
            // Exercise the real native Avalonia window, configuration load, layout, and shutdown without emitting input.
            var success = _viewModel.Profiles.IsLoaded && await _viewModel.Profiles.FlushAsync();
            if (args.Contains("--smoke-tray", StringComparer.Ordinal)) success &= _tray is not null;
            var diagnosticSmokeSeconds = args.FirstOrDefault(argument => argument.StartsWith("--diagnostics-seconds=", StringComparison.Ordinal))?[22..];
            var diagnosticDuration = int.TryParse(diagnosticSmokeSeconds, out var seconds) && seconds is >= 5 and <= 120
                ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(6);
            await Task.Delay(_diagnosticsRequested ? diagnosticDuration : TimeSpan.FromMilliseconds(300));
            var screenshot = arguments.FirstOrDefault(argument => argument.StartsWith("--screenshot=", StringComparison.Ordinal))?[13..];
            if (screenshot is not null && arguments.Contains("--preview-popup", StringComparer.Ordinal))
            {
                // Render the macOS menu bar popover on its own (no tray, no input).
                var popup = new MenuBarPopup { DataContext = _viewModel };
                popup.Show();
                await Task.Delay(400);
                if (popup.Content is Visual popupVisual) PreviewRenderer.Save(popupVisual, screenshot);
                popup.Close();
            }
            // Render the whole window so the theme background fills the gaps around the cards.
            else if (screenshot is not null && desktop.MainWindow is { } previewWindow) PreviewRenderer.Save(previewWindow, screenshot);
            _logger!.LogInformation("Desktop smoke test completed: {Success}", success);
            _allowClose = true;
            desktop.Shutdown(success ? 0 : 1);
        }
        catch (Exception exception)
        {
            _logger!.LogError(exception, "Startup failed");
            _viewModel!.Profiles.ReportError(exception, _services!.GetRequiredService<LocalizationService>().Get("Error_Startup"));
            if (smokeTest) { _allowClose = true; desktop.Shutdown(1); }
        }
    }

    private async Task CloseAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await _startupTask;
            if (!await _viewModel!.PrepareToCloseAsync()) return;
            _logger!.LogInformation("Configuration flushed; AutoClicker exiting");
            _tray?.Dispose(); _tray = null; _popup?.Dispose(); _popup = null;
            if (_singleInstance is not null) { await _singleInstance.DisposeAsync(); _singleInstance = null; }
            _allowClose = true;
            desktop.Shutdown();
        }
        catch (Exception exception)
        {
            _viewModel!.Profiles.IsShuttingDown = false;
            try { await _viewModel.Profiles.RegisterHotkeysAsync(); }
            catch (Exception restoreException) { _logger?.LogError(restoreException, "Unable to restore global shortcuts after shutdown was cancelled"); }
            _viewModel.Profiles.ReportError(exception, _services!.GetRequiredService<LocalizationService>().Get("Error_Shutdown"));
        }
        finally { _shutdownTask = null; }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        _logger?.LogCritical(args.ExceptionObject as Exception, "Unhandled process exception; terminating: {Terminating}", args.IsTerminating);
        TryReleaseHeldMouseButton("unhandled process exception");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        TryReleaseHeldMouseButton("unobserved background task exception");
        _services?.GetRequiredService<IUiDispatcher>().Post(() =>
            _viewModel?.Profiles.ReportError(args.Exception, _services!.GetRequiredService<LocalizationService>().Get("Error_Background")));
        args.SetObserved();
    }

    private void DisposeServices()
    {
        TryReleaseHeldMouseButton("application exit");
        _diagnosticsCancellation?.Cancel();
        try { _diagnosticsTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _logger?.LogWarning(exception, "Process diagnostics stopped unexpectedly"); }
        _diagnosticsCancellation?.Dispose(); _diagnosticsCancellation = null;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _tray?.Dispose(); _tray = null; _popup?.Dispose(); _popup = null;
        _singleInstance?.Dispose(); _singleInstance = null;
        _services?.Dispose();
    }

    private void TryReleaseHeldMouseButton(string reason)
    {
        try
        {
            var holdService = _services?.GetService<MouseButtonHoldService>();
            if (holdService?.IsHeld == true) holdService.ReleaseIfHeldAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger?.LogCritical(exception, "Could not release held mouse button during {Reason}", reason);
        }
    }

    private async Task MonitorProcessAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        using var process = Process.GetCurrentProcess();
        var lastTimestamp = Stopwatch.GetTimestamp();
        var lastCpuTime = process.TotalProcessorTime;
        while (true)
        {
            process.Refresh();
            var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
            var now = Stopwatch.GetTimestamp(); var cpuTime = process.TotalProcessorTime;
            var sampleSeconds = Stopwatch.GetElapsedTime(lastTimestamp, now).TotalSeconds;
            var cpuPercent = sampleSeconds > 0 ? (cpuTime - lastCpuTime).TotalSeconds / sampleSeconds * 100d : 0;
            lastTimestamp = now; lastCpuTime = cpuTime;
            _logger?.LogInformation("Process diagnostics: state={EngineState}, cpuOneCore={CpuPercent:F1}%, managedHeap={ManagedHeapMb:F1}MiB, workingSet={WorkingSetMb:F1}MiB, privateBytes={PrivateMb:F1}MiB, threads={Threads}, GC={Gc0}/{Gc1}/{Gc2}",
                _viewModel?.Profiles.EngineTelemetry.State.ToString() ?? "Starting", cpuPercent, GC.GetTotalMemory(false) / 1048576d,
                process.WorkingSet64 / 1048576d, process.PrivateMemorySize64 / 1048576d, process.Threads.Count, gc0, gc1, gc2);
            await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ForwardActivationAndExitAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try { await _singleInstance!.RequestActivationAsync(); }
        catch (Exception exception) { _logger?.LogWarning(exception, "Could not activate the running AutoClicker instance"); }
        _singleInstance?.Dispose(); _singleInstance = null;
        _services?.Dispose(); _services = null;
        _allowClose = true; desktop.Shutdown(0);
    }

    private async Task ActivateOnUiThreadAsync()
    {
        if (_allowClose || _shutdownTask is not null) return;
        _services?.GetRequiredService<IUiDispatcher>().Post(() =>
        {
            if (!_allowClose && _shutdownTask is null) ShowMainWindow();
        });
        await Task.CompletedTask;
    }

    private void OnAboutMenuClick(object? sender, EventArgs args)
    {
        ShowMainWindow();
        _viewModel?.ShowAboutCommand.Execute(null);
    }

    private void ShowMainWindow()
    {
        if (_window is null || _allowClose || _shutdownTask is not null) return;
        _window.ShowInTaskbar = true; _window.WindowState = WindowState.Normal; _window.Show(); _window.Activate();
    }

    private void QuitFromTray()
    {
        if (_window is null || _allowClose) return;
        _forceExit = true; _window.Close();
    }
}

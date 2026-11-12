using AutoClicker.App.Services;
using AutoClicker.App.ViewModels;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Persistence;
using AutoClicker.Core.Services;
using AutoClicker.Core.Timing;
using AutoClicker.Platform.MacOS;
using AutoClicker.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Composition;

public static class ServiceRegistration
{
    public static ServiceProvider Create(string? configurationOverride = null, bool isolateSingleInstance = false, bool safeInput = false)
    {
        IPlatformInfo platform = OperatingSystem.IsWindows() ? new WindowsPlatformInfo()
            : OperatingSystem.IsMacOS() ? new MacOSPlatformInfo()
            : throw new PlatformNotSupportedException("AutoClicker currently supports Windows and macOS.");
        var directory = configurationOverride ?? platform.ConfigurationDirectory;
        var services = new ServiceCollection();
        services.AddSingleton(platform);
        services.AddSingleton<IApplicationStore>(_ => new JsonApplicationStore(directory));
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IEngineDelay, WindowsHighResolutionDelay>();
            services.AddSingleton<IMouseService, WindowsMouseService>();
            services.AddSingleton<ICursorService, WindowsCursorService>();
            services.AddSingleton<WindowsGlobalHotkeyService>();
            services.AddSingleton<IGlobalHotkeyService>(provider => provider.GetRequiredService<WindowsGlobalHotkeyService>());
            services.AddSingleton<IMouseInputMonitor>(provider => provider.GetRequiredService<WindowsGlobalHotkeyService>());
            services.AddSingleton<IStartupRegistrationService, WindowsStartupRegistrationService>();
        }
        else
        {
            services.AddSingleton<IEngineDelay, TimeProviderEngineDelay>();
            services.AddSingleton<MacOSAccessibilityPermissionService>();
            services.AddSingleton<IAccessibilityPermissionService>(provider => provider.GetRequiredService<MacOSAccessibilityPermissionService>());
            services.AddSingleton<ICursorService, MacOSCursorService>();
            services.AddSingleton<IMouseService, MacOSMouseService>();
            services.AddSingleton<MacOSGlobalHotkeyService>();
            services.AddSingleton<IGlobalHotkeyService>(provider => provider.GetRequiredService<MacOSGlobalHotkeyService>());
            services.AddSingleton<IMouseInputMonitor>(provider => provider.GetRequiredService<MacOSGlobalHotkeyService>());
            services.AddSingleton<IStartupRegistrationService, MacOSStartupRegistrationService>();
        }
        if (safeInput) services.AddSingleton<IMouseService, SafeSmokeMouseService>();
        services.AddSingleton<IAutoClickerEngine, AutoClickerEngine>();
        services.AddSingleton<MouseButtonHoldService>();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information).AddProvider(new JsonFileLoggerProvider(directory)));
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<UsageStatisticsService>();
        var instanceDirectory = isolateSingleInstance ? directory : platform.ConfigurationDirectory;
        services.AddSingleton<ISingleInstanceService>(provider => new SingleInstanceService(instanceDirectory, provider.GetRequiredService<ILogger<SingleInstanceService>>(), isolateSingleInstance));
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IConfigurationFolderService, ConfigurationFolderService>();
        services.AddSingleton<DesktopDialogService>();
        services.AddSingleton<IDesktopDialogService>(provider => provider.GetRequiredService<DesktopDialogService>());
        services.AddSingleton<DebouncedSaveService>();
        services.AddSingleton<AvaloniaWindowPresentationService>();
        services.AddSingleton<IWindowPresentationService>(provider => provider.GetRequiredService<AvaloniaWindowPresentationService>());
        services.AddSingleton<PositionPickingCoordinator>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}

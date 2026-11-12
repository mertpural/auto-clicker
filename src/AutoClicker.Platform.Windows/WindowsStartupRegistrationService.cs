using Microsoft.Win32;
using System.Runtime.Versioning;

namespace AutoClicker.Platform.Windows;

internal interface IWindowsStartupApi
{
    string? Read(string valueName);
    void Write(string valueName, string? value);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsStartupApi : IWindowsStartupApi
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string? Read(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(valueName) as string;
    }
    public void Write(string valueName, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new IOException("Could not open the current user's startup registry key.");
        if (value is null) key.DeleteValue(valueName, throwOnMissingValue: false);
        else key.SetValue(valueName, value, RegistryValueKind.String);
    }
}

public sealed class WindowsStartupRegistrationService : AutoClicker.Core.Abstractions.IStartupRegistrationService
{
    private const string ValueName = "AutoClicker";
    private readonly IWindowsStartupApi _api;
    private readonly string _command;
    private string _status = "Launch on startup is disabled.";
    public WindowsStartupRegistrationService() : this(CreateApi(), Environment.ProcessPath ?? "AutoClicker.exe",
        System.Reflection.Assembly.GetEntryAssembly()?.Location ?? Path.Combine(AppContext.BaseDirectory, "AutoClicker.dll")) { }
    internal WindowsStartupRegistrationService(IWindowsStartupApi api, string executablePath, string assemblyPath)
    { _api = api; _command = BuildCommand(executablePath, assemblyPath); }
    public bool IsAvailable => OperatingSystem.IsWindows();
    public string Status => _status;
    public bool IsEnabled
    {
        get
        {
            try { var enabled = IsAvailable && string.Equals(_api.Read(ValueName), _command, StringComparison.OrdinalIgnoreCase); _status = enabled ? "Launch on startup is enabled." : "Launch on startup is disabled."; return enabled; }
            catch (Exception ex) { _status = "Could not read launch on startup settings: " + ex.Message; return false; }
        }
    }
    public bool SetEnabled(bool enabled, out string? errorMessage)
    {
        try
        {
            if (!IsAvailable) throw new PlatformNotSupportedException("Windows startup registration is available only on Windows.");
            _api.Write(ValueName, enabled ? _command : null);
            _status = enabled ? "Launch on startup is enabled." : "Launch on startup is disabled.";
            errorMessage = null;
            return true;
        }
        catch (Exception ex) { errorMessage = ex.Message; _status = "Launch on startup could not be changed."; return false; }
    }
    internal static string BuildCommand(string executablePath, string assemblyPath)
    {
        static string Quote(string path) => "\"" + path.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        // Split on both separators so Windows paths are parsed correctly on any host OS.
        var fileName = executablePath[(executablePath.LastIndexOfAny(['\\', '/']) + 1)..];
        var isDotnet = Path.GetFileNameWithoutExtension(fileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        return isDotnet ? $"{Quote(executablePath)} {Quote(assemblyPath)} --startup" : $"{Quote(executablePath)} --startup";
    }

    private static IWindowsStartupApi CreateApi() => OperatingSystem.IsWindows()
        ? new WindowsStartupApi() : throw new PlatformNotSupportedException("Windows startup registration is available only on Windows.");
}

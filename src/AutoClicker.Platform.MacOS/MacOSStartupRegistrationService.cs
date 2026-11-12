using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using AutoClicker.Core.Abstractions;

namespace AutoClicker.Platform.MacOS;

internal interface IMacOSStartupApi
{
    string AgentPath { get; }
    bool Exists(string path);
    string? Read(string path);
    void Write(string path, string contents);
    void Delete(string path);
    bool Load(string path, string label);
    bool Unload(string path, string label);
    bool IsLoaded(string label);
}

internal sealed class MacOSStartupApi : IMacOSStartupApi
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
    public string AgentPath => Path.Combine(_directory, "com.autoclicker.app.plist");
    public bool Exists(string path) => File.Exists(path);
    public string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
    public void Write(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }
    public void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
    public bool Load(string path, string label) => Run("bootstrap", "gui/" + GetUserId(), path);
    public bool Unload(string path, string label) => Run("bootout", "gui/" + GetUserId(), path);
    public bool IsLoaded(string label) => Run("print", "gui/" + GetUserId() + "/" + label);
    private static string GetUserId()
    {
        using var process = Process.Start(new ProcessStartInfo("id") { UseShellExecute = false, RedirectStandardOutput = true, ArgumentList = { "-u" } })
            ?? throw new InvalidOperationException("Could not determine the current user's macOS ID.");
        var userId = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || userId.Length == 0) throw new InvalidOperationException("Could not determine the current user's macOS ID.");
        return userId;
    }
    private static bool Run(string verb, params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("launchctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add(verb);
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); return false; }
        return process.ExitCode == 0;
    }
}

public sealed class MacOSStartupRegistrationService : IStartupRegistrationService
{
    private const string Label = "com.autoclicker.app";
    private readonly IMacOSStartupApi _api;
    private readonly string[] _arguments;
    private string _status = "Launch on startup is disabled.";
    public MacOSStartupRegistrationService() : this(new MacOSStartupApi(), Environment.ProcessPath ?? "AutoClicker", Assembly.GetEntryAssembly()?.Location ?? "") { }
    internal MacOSStartupRegistrationService(IMacOSStartupApi api, string executablePath, string assemblyPath)
    { _api = api; _arguments = BuildArguments(executablePath, assemblyPath); }
    public bool IsAvailable => OperatingSystem.IsMacOS();
    public string Status => _status;
    public bool IsEnabled
    {
        get
        {
            try
            {
                var text = IsAvailable ? _api.Read(_api.AgentPath) : null;
                var enabled = text is not null && IsExpectedAgent(text) && _api.IsLoaded(Label);
                _status = enabled ? "Launch on startup is enabled." : "Launch on startup is disabled.";
                return enabled;
            }
            catch (Exception ex) { _status = "Could not read launch on startup settings: " + ex.Message; return false; }
        }
    }
    public bool SetEnabled(bool enabled, out string? errorMessage)
    {
        if (!IsAvailable) { errorMessage = "macOS startup registration is available only on macOS."; _status = "Launch on startup is unavailable."; return false; }
        var path = _api.AgentPath;
        var previous = _api.Read(path);
        var wasLoaded = false;
        try
        {
            if (enabled)
            {
                wasLoaded = _api.IsLoaded(Label);
                if (wasLoaded && !_api.Unload(path, Label)) throw new InvalidOperationException("launchd could not replace the existing AutoClicker startup item.");
                _api.Write(path, BuildPlist());
                if (!_api.Load(path, Label)) throw new InvalidOperationException("launchd did not accept the AutoClicker startup item.");
            }
            else
            {
                wasLoaded = _api.IsLoaded(Label);
                if (wasLoaded && !_api.Unload(path, Label)) throw new InvalidOperationException("launchd could not remove the AutoClicker startup item.");
                _api.Delete(path);
            }
            _status = enabled ? "Launch on startup is enabled." : "Launch on startup is disabled.";
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                if (previous is null) _api.Delete(path);
                else { _api.Write(path, previous); if (wasLoaded) _api.Load(path, Label); }
            }
            catch { }
            errorMessage = ex.Message; _status = "Launch on startup could not be changed."; return false;
        }
    }
    internal string BuildPlist()
    {
        var document = new XDocument(new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
            new XElement("key", "Label"), new XElement("string", Label),
            new XElement("key", "ProgramArguments"), new XElement("array", _arguments.Select(arg => new XElement("string", arg))),
            new XElement("key", "RunAtLoad"), new XElement("true"),
            new XElement("key", "KeepAlive"), new XElement("false"))));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document;
    }
    private bool IsExpectedAgent(string contents)
    {
        try
        {
            var doc = XDocument.Parse(contents);
            var values = doc.Descendants("array").FirstOrDefault()?.Elements("string").Select(e => e.Value).ToArray() ?? [];
            return values.SequenceEqual(_arguments, StringComparer.Ordinal);
        }
        catch { return false; }
    }
    internal static string[] BuildArguments(string executablePath, string assemblyPath)
    {
        var normalized = executablePath.Replace('\\', '/');
        var marker = ".app/Contents/MacOS/";
        var bundleMarker = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (bundleMarker >= 0)
        {
            var bundlePath = normalized[..(bundleMarker + 4)];
            return ["/usr/bin/open", "-a", bundlePath, "--args", "--startup"];
        }
        return Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [executablePath, assemblyPath, "--startup"] : [executablePath, "--startup"];
    }
}

using AutoClicker.Platform.MacOS;
using AutoClicker.Platform.Windows;
using AutoClicker.App.Services;
using System.Xml.Linq;
using Xunit;

namespace AutoClicker.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void Windows_startup_command_quotes_paths_and_handles_dotnet_host()
    {
        Assert.Equal("\"C:\\Program Files\\AutoClicker\\AutoClicker.exe\" --startup",
            WindowsStartupRegistrationService.BuildCommand("C:\\Program Files\\AutoClicker\\AutoClicker.exe", "unused.dll"));
        Assert.Equal("\"C:\\Program Files\\dotnet.exe\" \"C:\\Program Files\\AutoClicker\\AutoClicker.dll\" --startup",
            WindowsStartupRegistrationService.BuildCommand("C:\\Program Files\\dotnet.exe", "C:\\Program Files\\AutoClicker\\AutoClicker.dll"));
    }

    [Fact]
    public void Mac_startup_agent_contains_login_arguments_without_keepalive()
    {
        var service = new MacOSStartupRegistrationService(new FakeMacStartupApi(), "/Applications/AutoClicker.app/Contents/MacOS/AutoClicker", "unused.dll");
        var plist = service.BuildPlist();
        Assert.Contains("com.autoclicker.app", plist);
        Assert.Contains("<string>--startup</string>", plist);
        var elements = XDocument.Parse(plist).Descendants("dict").Single().Elements().ToArray();
        Assert.Equal("true", elements[Array.FindIndex(elements, item => item.Value == "RunAtLoad") + 1].Name.LocalName);
        Assert.Equal("false", elements[Array.FindIndex(elements, item => item.Value == "KeepAlive") + 1].Name.LocalName);
    }

    [Fact]
    public void Mac_startup_uses_open_on_the_enclosing_app_bundle()
    {
        Assert.Equal(new[] { "/usr/bin/open", "-a", "/Applications/AutoClicker.app", "--args", "--startup" },
            MacOSStartupRegistrationService.BuildArguments("/Applications/AutoClicker.app/Contents/MacOS/AutoClicker", "unused.dll"));
    }

    [Fact]
    public void Product_identity_and_release_version_are_stable()
    {
        Assert.Equal("AutoClicker", ProductIdentity.Name);
        Assert.Equal("com.mertpural.autoclicker", ProductIdentity.BundleIdentifier);
        Assert.Equal(new Version(1, 0, 0, 0), typeof(ProductIdentity).Assembly.GetName().Version);
    }

    private sealed class FakeMacStartupApi : IMacOSStartupApi
    {
        public string AgentPath => "unused";
        public bool Exists(string path) => false;
        public string? Read(string path) => null;
        public void Write(string path, string contents) { }
        public void Delete(string path) { }
        public bool Load(string path, string label) => true;
        public bool Unload(string path, string label) => true;
        public bool IsLoaded(string label) => false;
    }
}

using System.Buffers.Binary;
using Xunit;

namespace AutoClicker.Tests;

public sealed class BrandingTests
{
    private const string IconResource = "assets/branding/autoclicker.ico";
    private const string SmallPngResource = "assets/branding/autoclicker-icon-64.png";

    [Fact]
    public void Canonical_source_master_and_windows_ico_are_present_and_valid()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllBytes(Path.Combine(root, "assets", "branding", "autoclicker-icon-source.png"));
        var master = File.ReadAllBytes(Path.Combine(root, "assets", "branding", "autoclicker-icon-1024.png"));
        AssertPng(source, 1254, 1254, expectedColorType: 6);
        AssertPng(master, 1024, 1024, expectedColorType: 6);

        using var stream = File.OpenRead(Path.Combine(root, IconResource));
        using var reader = new BinaryReader(stream);
        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        var sizes = new List<int>();
        var payloads = new List<(uint Offset, uint Length)>();
        for (var index = 0; index < count; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            reader.ReadBytes(2);
            reader.ReadUInt16();
            Assert.Equal((ushort)32, reader.ReadUInt16());
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(sizes[^1], height == 0 ? 256 : height);
            payloads.Add((offset, length));
        }

        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, sizes);
        foreach (var (offset, length) in payloads)
        {
            stream.Position = offset;
            var signature = reader.ReadBytes(8);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature);
            Assert.True(length > 8);
        }
    }

    [Fact]
    public void Avalonia_windows_installer_and_macos_packaging_use_the_canonical_icon_paths()
    {
        var root = FindRepositoryRoot();
        var project = Read(root, "AutoClicker.csproj");
        var build = Read(root, "Directory.Build.props");
        var window = Read(root, "src/AutoClicker.App/Views/MainWindow.axaml");
        var tray = Read(root, "src/AutoClicker.App/Services/AvaloniaTrayService.cs");
        var installer = Read(root, "packaging/windows/AutoClicker.iss");
        var plist = Read(root, "packaging/macos/Info.plist.in");
        var macScript = Read(root, "scripts/publish-macos.sh");
        var generator = Read(root, "scripts/generate-macos-icons.sh");

        Assert.Contains(IconResource, project);
        Assert.Contains(SmallPngResource, project);
        Assert.Contains("assets/branding/autoclicker.ico", build);
        Assert.Contains("Icon=\"/assets/branding/autoclicker.ico\"", window);
        Assert.Contains("avares://AutoClicker/assets/branding/autoclicker-icon-64.png", window);
        Assert.Contains("avares://AutoClicker/assets/branding/autoclicker.ico", tray);
        Assert.Contains(@"assets\branding\autoclicker.ico", installer);
        Assert.Contains("CFBundleIconFile</key><string>autoclicker.icns</string>", plist);
        Assert.Contains("assets/branding/autoclicker.icns", macScript);
        Assert.Contains("iconutil --convert icns", generator);
    }

    [Fact]
    public void Sidebar_and_about_share_a_theme_independent_branding_asset()
    {
        var window = Read(FindRepositoryRoot(), "src/AutoClicker.App/Views/MainWindow.axaml");
        const string source = "avares://AutoClicker/assets/branding/autoclicker-icon-64.png";
        Assert.Equal(2, CountOccurrences(window, source));
        Assert.DoesNotContain("autoclicker-icon-64.png", Read(FindRepositoryRoot(), "src/AutoClicker.App/Services/ThemeService.cs"));
    }

    private static void AssertPng(byte[] bytes, uint width, uint height, byte expectedColorType)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(width, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(height, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(expectedColorType, bytes[25]);
    }

    private static string Read(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    private static int CountOccurrences(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) / token.Length;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AutoClicker.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate AutoClicker.sln from the test output directory.");
    }
}

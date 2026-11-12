using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using AutoClicker.Core.Validation;
using System.Text.Json.Nodes;
using System.Text.Json;
using Xunit;

namespace AutoClicker.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AutoClicker.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FirstLaunchCreatesDefaultAndSecondLaunchPreservesIt()
    {
        using var store = new JsonApplicationStore(_directory);
        var first = await store.LoadAsync();
        var second = await store.LoadAsync();
        Assert.Single(first.Profiles);
        Assert.Equal("Default", first.Profiles[0].Name);
        Assert.Equal(first.DefaultProfileId, first.SelectedProfileId);
        Assert.Equal(first.Profiles[0], second.Profiles[0]);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public async Task LegacyProfilesFileIsMigratedAndBackedUp()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ \"LastProfileId\": \"roblox-profile\" }");
        File.WriteAllText(Path.Combine(_directory, "profiles.json"), """
            [ { "Id": "default-profile", "Name": "Default", "Mode": 0, "MouseButton": 0, "Cps": 10, "ToggleHotkey": "F6", "HoldHotkey": "Mouse4" },
              { "Id": "roblox-profile", "Name": "Roblox", "Mode": 0, "MouseButton": 0, "Cps": 10, "ToggleHotkey": "F9", "HoldHotkey": "F10" } ]
            """);
        using var store = new JsonApplicationStore(_directory);
        var data = await store.LoadAsync();
        Assert.Equal(["Default", "Roblox"], data.Profiles.Select(p => p.Name));
        Assert.Equal("Roblox", data.Profiles.Single(p => p.Id == data.SelectedProfileId).Name);
        Assert.Equal("Default", data.Profiles.Single(p => p.Id == data.DefaultProfileId).Name);
        Assert.Equal(new Hotkey("F9"), data.Profiles[1].StartStopHotkey);
        Assert.NotEqual(data.Profiles[0].StartStopHotkey, data.Profiles[0].HoldMouseButtonHotkey);
        Assert.True(File.Exists(Path.Combine(_directory, "profiles.json.legacy-backup")));
        Assert.True(File.Exists(Path.Combine(_directory, "profiles.json")));
        var again = await new JsonApplicationStore(_directory).LoadAsync();
        Assert.Equal(data.Profiles.Select(p => p.Id), again.Profiles.Select(p => p.Id));
    }

    [Fact]
    public async Task PersistsProfilesSelectionsAndSettingsAcrossStoreInstances()
    {
        using var store = new JsonApplicationStore(_directory);
        var data = await store.LoadAsync();
        var other = new AutoClickerProfile { Name = "Custom", Cps = 18.3 };
        data = data with
        {
            Profiles = [data.Profiles[0], other], DefaultProfileId = other.Id, SelectedProfileId = other.Id,
            Settings = data.Settings with { Appearance = Appearance.Dark, RememberLastSelectedProfile = false }
        };
        await store.SaveAsync(data);
        using var reopened = new JsonApplicationStore(_directory);
        var actual = await reopened.LoadAsync();
        Assert.Equal(data.Profiles, actual.Profiles);
        Assert.Equal(data.Settings, actual.Settings);
        Assert.Equal(other.Id, actual.DefaultProfileId);
        Assert.Equal(other.Id, actual.SelectedProfileId);
    }

    [Fact]
    public async Task Load_migrates_legacy_rates_above_the_supported_ceiling_and_persists_them()
    {
        using var store = new JsonApplicationStore(_directory);
        var data = ApplicationData.CreateDefault();
        var legacy = data.Profiles[0] with { Cps = 1000, RandomizationEnabled = true, MinimumCps = 700, MaximumCps = 1000 };
        data = data with { Profiles = [legacy] };
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(store.FilePath, JsonSerializer.Serialize(data, ProfileJson.CreateOptions()));

        var loaded = await store.LoadAsync();
        Assert.Equal(500, loaded.Profiles[0].Cps);
        Assert.Equal(500, loaded.Profiles[0].MinimumCps);
        Assert.Equal(500, loaded.Profiles[0].MaximumCps);
        Assert.True(loaded.Profiles[0].RandomizationEnabled);
        var persisted = JsonSerializer.Deserialize<ApplicationData>(await File.ReadAllTextAsync(store.FilePath), ProfileJson.CreateOptions());
        Assert.Equal(500, persisted!.Profiles[0].Cps);
    }

    [Fact]
    public async Task Missing_lifetime_statistics_field_loads_as_zero_and_profile_saves_preserve_totals()
    {
        using var store = new JsonApplicationStore(_directory);
        var data = await store.LoadAsync();
        var legacyJson = JsonNode.Parse(await File.ReadAllTextAsync(store.FilePath))!.AsObject();
        legacyJson.Remove("lifetimeStatistics");
        await File.WriteAllTextAsync(store.FilePath, legacyJson.ToJsonString());
        var oldData = await store.LoadAsync();
        Assert.Equal(0, oldData.LifetimeStatistics.TotalPhysicalClicks);
        Assert.Equal(0, oldData.LifetimeStatistics.TotalActiveTicks);

        await store.SaveLifetimeStatisticsAsync(new LifetimeStatistics { TotalPhysicalClicks = 42, TotalActiveTicks = TimeSpan.FromMinutes(3).Ticks });
        await store.SaveAsync(oldData with { Profiles = [oldData.Profiles[0] with { Cps = 25 }] });
        var reopened = await store.LoadAsync();
        Assert.Equal(42, reopened.LifetimeStatistics.TotalPhysicalClicks);
        Assert.Equal(TimeSpan.FromMinutes(3).Ticks, reopened.LifetimeStatistics.TotalActiveTicks);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":99,\"profiles\":[]}")]
    public async Task CorruptConfigurationIsReportedAndNeverOverwritten(string content)
    {
        using var store = new JsonApplicationStore(_directory);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(store.FilePath, content);
        var error = await Assert.ThrowsAsync<PersistenceException>(() => store.LoadAsync());
        Assert.Contains("preserved", error.Message);
        Assert.Equal(content, await File.ReadAllTextAsync(store.FilePath));
    }

    [Fact]
    public async Task InvalidSavePreservesThePreviousConfiguration()
    {
        using var store = new JsonApplicationStore(_directory);
        var data = await store.LoadAsync();
        var before = await File.ReadAllTextAsync(store.FilePath);
        await Assert.ThrowsAsync<DataValidationException>(() => store.SaveAsync(data with
        {
            Profiles = [data.Profiles[0] with { Cps = -1 }]
        }));
        Assert.Equal(before, await File.ReadAllTextAsync(store.FilePath));
    }

    [Fact]
    public async Task CancelledSavePreservesThePreviousConfiguration()
    {
        using var store = new JsonApplicationStore(_directory);
        var data = await store.LoadAsync();
        var before = await File.ReadAllTextAsync(store.FilePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(data, cancellation.Token));
        Assert.Equal(before, await File.ReadAllTextAsync(store.FilePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentWritesLeaveACompleteDocument()
    {
        using var store = new JsonApplicationStore(_directory);
        var data = await store.LoadAsync();
        await Task.WhenAll(Enumerable.Range(1, 20).Select(index => store.SaveAsync(data with
        {
            Profiles = [data.Profiles[0] with { Cps = index }]
        })));
        var loaded = await store.LoadAsync();
        Assert.InRange(loaded.Profiles[0].Cps, 1, 20);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void DuplicateNamesAndMissingSelectionsAreRejected()
    {
        var data = ApplicationData.CreateDefault();
        Assert.Throws<DataValidationException>(() => ApplicationDataValidator.Validate(data with
        {
            Profiles = [data.Profiles[0], new AutoClickerProfile { Name = "default" }]
        }));
        Assert.Throws<DataValidationException>(() => ApplicationDataValidator.Validate(data with { SelectedProfileId = Guid.NewGuid() }));
    }

    [Fact]
    public void EmergencyStopCannotConflictWithStartShortcut()
    {
        var data = ApplicationData.CreateDefault();
        Assert.Throws<DataValidationException>(() => ApplicationDataValidator.Validate(data with
        {
            Settings = data.Settings with { EmergencyStopHotkey = new Hotkey("F6") }
        }));
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AutoClicker.Tests")) + Path.DirectorySeparatorChar;
        if (!_directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

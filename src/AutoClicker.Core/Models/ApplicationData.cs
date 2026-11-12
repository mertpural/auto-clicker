namespace AutoClicker.Core.Models;

public sealed record ApplicationSettings
{
    public bool LaunchOnStartup { get; init; }
    public bool StartMinimized { get; init; }
    public CloseBehavior CloseBehavior { get; init; } = CloseBehavior.ExitApplication;
    public bool RememberLastSelectedProfile { get; init; } = true;
    public bool ConfirmBeforeDeletingProfile { get; init; } = true;
    public Appearance Appearance { get; init; } = Appearance.GeceNeon;
    public ApplicationLanguage Language { get; init; } = ApplicationLanguage.Turkish;
    public Hotkey EmergencyStopHotkey { get; init; } = new("F12");
}

public sealed record ApplicationData
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public IReadOnlyList<AutoClickerProfile> Profiles { get; init; } = [];
    public Guid DefaultProfileId { get; init; }
    public Guid SelectedProfileId { get; init; }
    public ApplicationSettings Settings { get; init; } = new();
    public LifetimeStatistics LifetimeStatistics { get; init; } = new();

    public static ApplicationData CreateDefault()
    {
        var profile = new AutoClickerProfile();
        return new ApplicationData { Profiles = [profile], DefaultProfileId = profile.Id, SelectedProfileId = profile.Id };
    }
}

/// <summary>Global, local-only usage totals accumulated across application launches.</summary>
public sealed record LifetimeStatistics
{
    public long TotalPhysicalClicks { get; init; }
    public long TotalActiveTicks { get; init; }
}

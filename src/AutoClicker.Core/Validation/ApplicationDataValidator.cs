using AutoClicker.Core.Models;

namespace AutoClicker.Core.Validation;

public static class ApplicationDataValidator
{
    public static void Validate(ApplicationData data)
    {
        if (data.SchemaVersion != ApplicationData.CurrentSchemaVersion)
            throw new DataValidationException("This configuration was created with an unsupported application version.");
        if (data.Profiles is null || data.Profiles.Count is < 1 or > 1000 || data.Profiles.Any(profile => profile is null))
            throw new DataValidationException("Configuration must contain between 1 and 1000 profiles.");
        foreach (var profile in data.Profiles) ProfileValidator.ThrowIfInvalid(profile);
        if (data.Profiles.Select(profile => profile.Id).Distinct().Count() != data.Profiles.Count)
            throw new DataValidationException("Profile IDs must be unique.");
        if (data.Profiles.Select(profile => profile.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Profiles.Count)
            throw new DataValidationException("Profile names must be unique.");
        if (!data.Profiles.Any(profile => profile.Id == data.DefaultProfileId) || !data.Profiles.Any(profile => profile.Id == data.SelectedProfileId))
            throw new DataValidationException("Default and selected profiles must exist.");
        if (data.Settings is null || !Enum.IsDefined(data.Settings.Appearance) || !Enum.IsDefined(data.Settings.Language) || !Enum.IsDefined(data.Settings.CloseBehavior)
            || data.Settings.EmergencyStopHotkey is null)
            throw new DataValidationException("Application settings are invalid.");
        if (data.LifetimeStatistics is null || data.LifetimeStatistics.TotalPhysicalClicks < 0 || data.LifetimeStatistics.TotalActiveTicks < 0)
            throw new DataValidationException("Lifetime statistics are invalid.");
        if (data.Profiles.Any(profile => profile.StartStopHotkey == data.Settings.EmergencyStopHotkey))
            throw new DataValidationException("Emergency stop must differ from every start / stop shortcut.");
        if (data.Profiles.Any(profile => profile.HoldMouseButtonHotkey == data.Settings.EmergencyStopHotkey))
            throw new DataValidationException("Emergency stop must differ from every hold/release shortcut.");
    }
}

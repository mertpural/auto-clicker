using AutoClicker.Core.Models;
using AutoClicker.Core.Timing;

namespace AutoClicker.Core.Validation;

public sealed record ValidationIssue(string Property, string Message);

public static class ProfileValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(AutoClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<ValidationIssue>();
        void Check(bool valid, string property, string message)
        {
            if (!valid) errors.Add(new(property, message));
        }
        static bool ValidCps(double value) => double.IsFinite(value) && value >= ClickTiming.MinimumCps && value <= ClickTiming.MaximumCps;

        Check(profile.Id != Guid.Empty, nameof(profile.Id), "Profile ID cannot be empty.");
        Check(!string.IsNullOrWhiteSpace(profile.Name) && profile.Name.Length <= 80, nameof(profile.Name), "Enter a name between 1 and 80 characters.");
        Check(Enum.IsDefined(profile.ProfileColor), nameof(profile.ProfileColor), "Choose a supported profile color.");
        Check(ValidCps(profile.Cps), nameof(profile.Cps), $"CPS must be between 0.001 and {ClickTiming.MaximumCps:0}.");
        Check(ValidCps(profile.MinimumCps), nameof(profile.MinimumCps), $"Minimum CPS must be between 0.001 and {ClickTiming.MaximumCps:0}.");
        Check(ValidCps(profile.MaximumCps), nameof(profile.MaximumCps), $"Maximum CPS must be between 0.001 and {ClickTiming.MaximumCps:0}.");
        Check(profile.MinimumCps <= profile.MaximumCps, nameof(profile.MinimumCps), "Minimum CPS cannot exceed maximum CPS.");
        Check(Enum.IsDefined(profile.MouseButton), nameof(profile.MouseButton), "Choose a supported mouse button.");
        Check(Enum.IsDefined(profile.ClickType), nameof(profile.ClickType), "Choose a supported click type.");
        Check(Enum.IsDefined(profile.ClickMode), nameof(profile.ClickMode), "Choose a supported click mode.");
        Check(Enum.IsDefined(profile.PositionMode), nameof(profile.PositionMode), "Choose a supported position mode.");
        Check(Enum.IsDefined(profile.HoldMouseButton), nameof(profile.HoldMouseButton), "Choose a supported hold button.");
        Check(profile.ClickLimit >= 0 && (!profile.ClickLimitEnabled || profile.ClickLimit > 0), nameof(profile.ClickLimit), "Enabled click limit must be at least 1.");
        Check(profile.DurationLimit >= TimeSpan.Zero && (!profile.DurationLimitEnabled || profile.DurationLimit > TimeSpan.Zero), nameof(profile.DurationLimit), "Enabled duration limit must be greater than zero.");
        Check(profile.StartDelayMilliseconds >= 0, nameof(profile.StartDelayMilliseconds), "Start delay cannot be negative.");
        Check(profile.ClicksPerBurst >= 1, nameof(profile.ClicksPerBurst), "A burst must contain at least 1 click.");
        Check(profile.BurstDelayMilliseconds >= 0, nameof(profile.BurstDelayMilliseconds), "Burst delay cannot be negative.");
        Check(profile.StartStopHotkey is not null, nameof(profile.StartStopHotkey), "Choose a start / stop shortcut.");
        Check(profile.HoldMouseButtonHotkey is not null, nameof(profile.HoldMouseButtonHotkey), "Choose a hold/release shortcut.");
        if (profile.StartStopHotkey is not null && profile.HoldMouseButtonHotkey is not null)
            Check(profile.StartStopHotkey != profile.HoldMouseButtonHotkey, nameof(profile.HoldMouseButtonHotkey), "Hold/release and start/stop shortcuts must be different.");
        if (profile.HoldMouseButtonHotkey is not null)
            Check(profile.HoldMouseButtonHotkey != new Hotkey("F12"), nameof(profile.HoldMouseButtonHotkey), "Hold/release shortcut is reserved for Emergency Stop.");
        if (profile.PositionMode == ClickPositionMode.Fixed)
        {
            Check(profile.FixedX is not null, nameof(profile.FixedX), "Enter a fixed X coordinate.");
            Check(profile.FixedY is not null, nameof(profile.FixedY), "Enter a fixed Y coordinate.");
        }
        return errors;
    }

    public static void ThrowIfInvalid(AutoClickerProfile profile)
    {
        var errors = Validate(profile);
        if (errors.Count != 0) throw new DataValidationException(string.Join(" ", errors.Select(error => error.Message)));
    }
}

public sealed class DataValidationException(string message) : Exception(message);

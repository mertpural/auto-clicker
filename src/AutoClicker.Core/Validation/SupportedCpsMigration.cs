using AutoClicker.Core.Models;
using AutoClicker.Core.Timing;

namespace AutoClicker.Core.Validation;

/// <summary>One-time compatibility normalization for profiles written before the supported CPS ceiling changed.</summary>
public static class SupportedCpsMigration
{
    public static ApplicationData Apply(ApplicationData data, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(data);
        changed = false;
        if (data.Profiles is null || data.Profiles.Any(profile => profile is null)) return data;
        var profiles = new AutoClickerProfile[data.Profiles.Count];
        for (var index = 0; index < data.Profiles.Count; index++)
        {
            var profile = data.Profiles[index];
            var cps = Math.Min(profile.Cps, ClickTiming.MaximumCps);
            var minimum = Math.Min(profile.MinimumCps, ClickTiming.MaximumCps);
            var maximum = Math.Min(profile.MaximumCps, ClickTiming.MaximumCps);
            if (cps != profile.Cps || minimum != profile.MinimumCps || maximum != profile.MaximumCps)
            {
                profiles[index] = profile with { Cps = cps, MinimumCps = minimum, MaximumCps = maximum };
                changed = true;
            }
            else profiles[index] = profile;
        }
        return changed ? data with { Profiles = profiles } : data;
    }
}

namespace AutoClicker.Core.Timing;

public static class ClickTiming
{
    // Real Windows SendInput runs measured reliably through 500 CPS; higher configured rates
    // materially overstate real output across the tested host, so the supported product ceiling is 500.
    public const double MaximumCps = 500;
    public const double MinimumCps = 0.001;

    public static double CpsToInterval(double cps)
    {
        RequirePositiveFinite(cps, nameof(cps));
        return 1000d / cps;
    }

    public static double IntervalToCps(double milliseconds)
    {
        RequirePositiveFinite(milliseconds, nameof(milliseconds));
        return 1000d / milliseconds;
    }

    public static double RandomInterval(double minimumCps, double maximumCps, Random? random = null)
    {
        RequirePositiveFinite(minimumCps, nameof(minimumCps));
        RequirePositiveFinite(maximumCps, nameof(maximumCps));
        if (minimumCps > maximumCps) throw new ArgumentException("Minimum CPS cannot exceed maximum CPS.");
        var cps = minimumCps + (random ?? Random.Shared).NextDouble() * (maximumCps - minimumCps);
        return CpsToInterval(cps);
    }

    private static void RequirePositiveFinite(double value, string parameter)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(parameter, "Value must be finite and greater than zero.");
    }
}

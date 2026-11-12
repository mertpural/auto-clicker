namespace AutoClicker.Core.Timing;

/// <summary>Tracks absolute cycle deadlines and skips missed slots instead of emitting catch-up clicks.</summary>
public sealed class AbsoluteClickSchedule(long firstDeadline)
{
    public long NextDeadline { get; private set; } = firstDeadline;

    /// <returns>How many expired deadlines were skipped after the next scheduled slot.</returns>
    public long Advance(TimeSpan interval, long now, long timestampFrequency)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (timestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        var period = Math.Max(1, (long)Math.Ceiling(interval.TotalSeconds * timestampFrequency));
        NextDeadline = SaturatingAdd(NextDeadline, period);
        if (NextDeadline > now) return 0;

        var skipped = (now - NextDeadline) / period + 1;
        var advance = skipped > long.MaxValue / period ? long.MaxValue : skipped * period;
        NextDeadline = SaturatingAdd(NextDeadline, advance);
        return skipped;
    }

    private static long SaturatingAdd(long left, long right) => right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;
}

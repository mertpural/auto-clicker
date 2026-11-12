using AutoClicker.Core.Timing;
using Xunit;

namespace AutoClicker.Tests;

public sealed class ClickTimingTests
{
    [Theory]
    [InlineData(10, 100)]
    [InlineData(20, 50)]
    [InlineData(5, 200)]
    [InlineData(12.5, 80)]
    public void ConvertsInBothDirections(double cps, double milliseconds)
    {
        Assert.Equal(milliseconds, ClickTiming.CpsToInterval(cps), 8);
        Assert.Equal(cps, ClickTiming.IntervalToCps(milliseconds), 8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidNumbers(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClickTiming.CpsToInterval(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClickTiming.IntervalToCps(value));
    }

    [Fact]
    public void RandomIntervalsHaveFractionalPrecisionAndStayWithinBounds()
    {
        var random = new Random(42);
        var samples = Enumerable.Range(0, 1000).Select(_ => ClickTiming.RandomInterval(9.4, 13.1, random)).ToArray();
        Assert.All(samples, interval => Assert.InRange(interval, 1000 / 13.1, 1000 / 9.4));
        Assert.Contains(samples, interval => Math.Abs(ClickTiming.IntervalToCps(interval) % 1) > 0.01);
        Assert.True(samples.Distinct().Count() > 900);
    }

    [Fact]
    public void EqualRandomBoundsProduceConstantInterval() => Assert.Equal(100, ClickTiming.RandomInterval(10, 10));

    [Fact]
    public void ReversedRandomBoundsAreRejected() => Assert.Throws<ArgumentException>(() => ClickTiming.RandomInterval(13, 9));
}

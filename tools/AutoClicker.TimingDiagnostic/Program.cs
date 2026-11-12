using System.Diagnostics;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Core.Services;
using AutoClicker.Core.Timing;
using AutoClicker.Platform.Windows;

var seconds = ParseSeconds(args);
var targets = ParseTargets(args);
Console.WriteLine("AutoClicker timing diagnostic: fake mouse output only; no native input is called.");
Console.WriteLine($"OS: {Environment.OSVersion}; duration per target: {seconds}s; targets: {string.Join(", ", targets)} CPS");
using var process = Process.GetCurrentProcess();
var benchmarkCpuStart = process.TotalProcessorTime;
var benchmarkElapsed = Stopwatch.StartNew();
foreach (var cps in targets)
{
    var mouse = new CountingMouseService();
    using var windowsDelay = OperatingSystem.IsWindows() ? new WindowsHighResolutionDelay() : null;
    IEngineDelay delay = (IEngineDelay?)windowsDelay ?? new TimeProviderEngineDelay();
    await using var engine = new AutoClickerEngine(mouse, engineDelay: delay);
    var stopped = new TaskCompletionSource<EngineTelemetry>(TaskCreationOptions.RunContinuationsAsynchronously);
    engine.TelemetryChanged += (_, eventArgs) =>
    {
        if (eventArgs.Telemetry.State == EngineState.Stopped) stopped.TrySetResult(eventArgs.Telemetry);
    };
    await engine.StartAsync(new AutoClickerProfile
    {
        Cps = cps,
        DurationLimitEnabled = true,
        DurationLimit = TimeSpan.FromSeconds(seconds)
    });
    var result = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(seconds + 15));
    var observed = result.Elapsed > TimeSpan.Zero ? result.PhysicalClickCount / result.Elapsed.TotalSeconds : 0;
    var timerDescription = delay is WindowsHighResolutionDelay measuredDelay
        ? $" | High-res waits: {measuredDelay.HighResolutionWaitCount}, standard waits: {measuredDelay.StandardResolutionWaitCount}, timer handles created: {measuredDelay.TimerHandleCreationCount}"
        : " | Timer: TimeProvider";
    Console.WriteLine($"Target: {cps,4:0} CPS | Observed fake-output scheduler: {observed,7:0.0} CPS | Clicks: {mouse.ClickCount,5} | Duration: {result.Elapsed.TotalSeconds:0.000}s{timerDescription}");
}
benchmarkElapsed.Stop();
process.Refresh();
var benchmarkCpu = process.TotalProcessorTime - benchmarkCpuStart;
var cpuPercentOfOneCore = benchmarkElapsed.Elapsed.TotalSeconds > 0
    ? benchmarkCpu.TotalSeconds / benchmarkElapsed.Elapsed.TotalSeconds * 100
    : 0;
Console.WriteLine($"Process CPU across benchmark: {benchmarkCpu.TotalMilliseconds:0.0}ms over {benchmarkElapsed.Elapsed.TotalSeconds:0.0}s ({cpuPercentOfOneCore:0.00}% of one core; short-run values are limited by OS CPU-time resolution).");

static int ParseSeconds(string[] commandLine)
{
    var value = commandLine.FirstOrDefault(item => item.StartsWith("--seconds=", StringComparison.Ordinal))?[10..];
    if (value is null) return 2;
    if (int.TryParse(value, out var seconds) && seconds is >= 1 and <= 30) return seconds;
    throw new ArgumentException("Use --seconds with a value from 1 to 30.");
}

static double[] ParseTargets(string[] commandLine)
{
    var value = commandLine.FirstOrDefault(item => item.StartsWith("--targets=", StringComparison.Ordinal))?[10..];
    if (value is null) return [10, 25, 50, 100, 200, 500];
    var parsed = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(item => double.TryParse(item, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var target)
            ? target : throw new ArgumentException($"Invalid CPS target: {item}"))
        .ToArray();
    if (parsed.Length == 0 || parsed.Any(target => !double.IsFinite(target) || target < 0.001 || target > 500))
        throw new ArgumentException("Use --targets with comma-separated CPS values from 0.001 to 500.");
    return parsed;
}

sealed class CountingMouseService : IMouseService
{
    private long _clickCount;
    public long ClickCount => Interlocked.Read(ref _clickCount);
    public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _clickCount);
        return Task.CompletedTask;
    }
    public Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default) =>
        ClickAsync(button, position, cancellationToken);
    public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

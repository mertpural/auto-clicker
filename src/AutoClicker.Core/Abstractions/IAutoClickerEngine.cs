using AutoClicker.Core.Models;

namespace AutoClicker.Core.Abstractions;

public enum EngineState { Stopped, Starting, Running, Stopping }
public enum EngineStopReason { StopRequested, ExternalCancellation, ClickLimitReached, DurationLimitReached, Faulted, Disposed }

/// <summary>Immutable point-in-time engine status. ClickCount and ActualCps are physical-click aliases.</summary>
public sealed record EngineTelemetry(EngineState State, long ClickCount, double ActualCps, TimeSpan Elapsed, TimeSpan StartDelayRemaining)
{
    public long PhysicalClickCount => ClickCount;
    public long LogicalCycleCount { get; init; }
    public double ActualPhysicalClicksPerSecond => ActualCps;
    public double? TargetCyclesPerSecond { get; init; }
    public double MinimumCyclesPerSecond { get; init; }
    public double MaximumCyclesPerSecond { get; init; }
    public double CurrentScheduledCyclesPerSecond { get; init; }
    public int CurrentBurstProgress { get; init; }
    public int BurstSize { get; init; }
    public bool IsBurstDelayActive { get; init; }
    public EngineStopReason? StopReason { get; init; }
    public string? ErrorMessage { get; init; }
}
public sealed class EngineTelemetryEventArgs(EngineTelemetry telemetry) : EventArgs
{
    public EngineTelemetry Telemetry { get; } = telemetry;
}

public sealed class EngineErrorEventArgs(Exception exception, string message) : EventArgs
{
    public Exception Exception { get; } = exception;
    public string Message { get; } = message;
}

public interface IAutoClickerEngine : IAsyncDisposable
{
    bool IsRunning { get; }
    EngineTelemetry Telemetry { get; }
    event EventHandler<EngineTelemetryEventArgs>? TelemetryChanged;
    event EventHandler<EngineErrorEventArgs>? Error;
    Task StartAsync(AutoClickerProfile profile, CancellationToken cancellationToken = default);
    Task StopAsync();
}

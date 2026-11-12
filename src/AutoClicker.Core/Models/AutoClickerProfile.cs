namespace AutoClicker.Core.Models;

/// <summary>Immutable snapshot for persistence and the future engine.</summary>
public sealed record AutoClickerProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Default";
    public ProfileColor ProfileColor { get; init; } = ProfileColor.Gray;
    public MouseButton MouseButton { get; init; } = MouseButton.Left;
    public ClickType ClickType { get; init; } = ClickType.Single;
    public ClickMode ClickMode { get; init; } = ClickMode.Toggle;
    public double Cps { get; init; } = 12;
    public bool RandomizationEnabled { get; init; }
    public double MinimumCps { get; init; } = 9;
    public double MaximumCps { get; init; } = 13;
    public bool ClickLimitEnabled { get; init; }
    public long ClickLimit { get; init; } = 500;
    public bool DurationLimitEnabled { get; init; }
    public TimeSpan DurationLimit { get; init; } = TimeSpan.FromSeconds(30);
    public int StartDelayMilliseconds { get; init; }
    public Hotkey StartStopHotkey { get; init; } = new("F6");
    public Hotkey HoldMouseButtonHotkey { get; init; } = new("F5");
    public MouseButton HoldMouseButton { get; init; } = MouseButton.Right;
    public ClickPositionMode PositionMode { get; init; } = ClickPositionMode.CurrentCursor;
    public int? FixedX { get; init; }
    public int? FixedY { get; init; }
    public bool BurstEnabled { get; init; }
    public int ClicksPerBurst { get; init; } = 5;
    public int BurstDelayMilliseconds { get; init; } = 1000;
}

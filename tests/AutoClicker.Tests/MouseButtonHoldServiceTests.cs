using AutoClicker.App.Services;
using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoClicker.Tests;

public sealed class MouseButtonHoldServiceTests
{
    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public async Task Toggle_sends_one_down_then_one_up_without_clicks(MouseButton button)
    {
        var mouse = new FakeMouse();
        var service = new MouseButtonHoldService(mouse, NullLogger<MouseButtonHoldService>.Instance);

        await service.ToggleAsync(button);
        Assert.True(service.IsHeld);
        Assert.Equal(button, service.HeldButton);
        Assert.Equal(new[] { $"down:{button}" }, mouse.Events);
        Assert.Empty(mouse.Clicks);

        await service.ToggleAsync(button);
        Assert.False(service.IsHeld);
        Assert.Null(service.HeldButton);
        Assert.Equal(new[] { $"down:{button}", $"up:{button}" }, mouse.Events);
        Assert.Empty(mouse.Clicks);
    }

    [Fact]
    public async Task Repeated_release_is_safe_and_button_change_releases_before_new_down()
    {
        var mouse = new FakeMouse();
        var service = new MouseButtonHoldService(mouse, NullLogger<MouseButtonHoldService>.Instance);

        await service.ToggleAsync(MouseButton.Left);
        await service.ToggleAsync(MouseButton.Right);
        await service.ReleaseIfHeldAsync();
        await service.ReleaseIfHeldAsync();

        Assert.Equal(new[] { "down:Left", "up:Left", "down:Right", "up:Right" }, mouse.Events);
        Assert.False(service.IsHeld);
        Assert.Empty(mouse.Clicks);
    }

    [Fact]
    public async Task Failed_down_leaves_state_clear_and_attempts_rollback_up()
    {
        var mouse = new FakeMouse { DownFailure = new InvalidOperationException("down failed") };
        var service = new MouseButtonHoldService(mouse, NullLogger<MouseButtonHoldService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ToggleAsync(MouseButton.Left));

        Assert.False(service.IsHeld);
        Assert.Null(service.HeldButton);
        Assert.Equal(new[] { "down:Left", "up:Left" }, mouse.Events);
    }

    [Fact]
    public async Task Failed_up_retains_state_and_retry_can_release()
    {
        var mouse = new FakeMouse();
        var service = new MouseButtonHoldService(mouse, NullLogger<MouseButtonHoldService>.Instance);
        await service.ToggleAsync(MouseButton.Middle);
        mouse.FailUpCount = 2;

        await Assert.ThrowsAsync<AggregateException>(() => service.ReleaseIfHeldAsync());
        Assert.True(service.IsHeld);
        Assert.Equal(MouseButton.Middle, service.HeldButton);

        await service.ReleaseIfHeldAsync();
        Assert.False(service.IsHeld);
        Assert.Equal(3, mouse.Events.Count(e => e == "up:Middle"));
    }

    private sealed class FakeMouse : IMouseService
    {
        public List<string> Events { get; } = [];
        public List<string> Clicks { get; } = [];
        public Exception? DownFailure { get; init; }
        public int FailUpCount { get; set; }
        public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
        { Clicks.Add($"click:{button}"); return Task.CompletedTask; }
        public Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
        { Clicks.Add($"double:{button}"); return Task.CompletedTask; }
        public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default)
        { Events.Add($"down:{button}"); if (DownFailure is not null) throw DownFailure; return Task.CompletedTask; }
        public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default)
        { Events.Add($"up:{button}"); if (FailUpCount > 0) { FailUpCount--; throw new InvalidOperationException("up failed"); } return Task.CompletedTask; }
    }
}

using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using AutoClicker.Platform.Windows;
using Xunit;

namespace AutoClicker.Tests;

public sealed class WindowsNativeMappingTests
{
    [Theory]
    [InlineData(MouseButton.Left, 0x0002u, 0x0004u)]
    [InlineData(MouseButton.Right, 0x0008u, 0x0010u)]
    [InlineData(MouseButton.Middle, 0x0020u, 0x0040u)]
    public async Task Click_maps_to_one_down_and_one_up(MouseButton button, uint down, uint up)
    {
        var api = new FakeInputApi();
        var service = new WindowsMouseService(api, new FakeCursor(api));
        await service.ClickAsync(button);

        Assert.Equal(new uint[] { down, up }, api.Submitted.Select(item => item.Data.Mouse.Flags));
        Assert.All(api.Submitted, item => Assert.Equal(WindowsMouseService.AutoClickerInputMarker, item.Data.Mouse.ExtraInfo));
        Assert.Single(api.Submissions);
        Assert.All(api.Submitted, item => Assert.Equal(0u, item.Type));
        Assert.Equal(MarshalSize, api.InputSize);
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, MarshalSize);
    }

    [Theory]
    [InlineData(MouseButton.Left, 0x0002u, 0x0004u)]
    [InlineData(MouseButton.Right, 0x0008u, 0x0010u)]
    [InlineData(MouseButton.Middle, 0x0020u, 0x0040u)]
    public async Task Stateful_button_events_send_one_native_event_with_existing_marker(MouseButton button, uint down, uint up)
    {
        var api = new FakeInputApi();
        var service = new WindowsMouseService(api, new FakeCursor(api));

        await service.ButtonDownAsync(button);
        Assert.Equal(new uint[] { down }, api.Submitted.Select(item => item.Data.Mouse.Flags));
        Assert.All(api.Submitted, item => Assert.Equal(WindowsMouseService.AutoClickerInputMarker, item.Data.Mouse.ExtraInfo));

        api.Submitted.Clear();
        api.Submissions.Clear();
        await service.ButtonUpAsync(button);
        Assert.Equal(new uint[] { up }, api.Submitted.Select(item => item.Data.Mouse.Flags));
        Assert.All(api.Submitted, item => Assert.Equal(WindowsMouseService.AutoClickerInputMarker, item.Data.Mouse.ExtraInfo));
    }

    [Fact]
    public async Task Fixed_position_moves_cursor_before_mouse_events()
    {
        var api = new FakeInputApi();
        var service = new WindowsMouseService(api, new FakeCursor(api));
        await service.ClickAsync(MouseButton.Left, new ScreenPoint(-1920, 300));
        Assert.Equal(new ScreenPoint(-1920, 300), api.Position);
        Assert.Equal(new[] { "position", "input" }, api.Operations);
        Assert.Equal(new uint[] { 0x0002, 0x0004 }, api.Submitted.Select(item => item.Data.Mouse.Flags));
    }

    [Fact]
    public async Task Partial_input_attempts_single_release_and_throws()
    {
        var api = new FakeInputApi { SendCountOverride = 1, Error = 5 };
        var service = new WindowsMouseService(api, new FakeCursor(api));
        var error = await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => service.ClickAsync(MouseButton.Right));
        Assert.Equal(5, error.NativeErrorCode);
        Assert.Equal(2, api.Submissions.Count);
        Assert.Equal(new uint[] { 0x0010 }, api.Submissions[1].Select(item => item.Data.Mouse.Flags));
    }

    [Theory]
    [InlineData("A", 0x41u)]
    [InlineData("F12", 0x7Bu)]
    [InlineData("SPACE", 0x20u)]
    [InlineData("PAGEDOWN", 0x22u)]
    public void Hotkey_name_maps_to_virtual_key(string name, uint code) => Assert.Equal(code, WindowsGlobalHotkeyService.KeyCode(name));

    [Fact]
    public void Hotkey_matching_requires_the_exact_modifier_set()
    {
        var hotkey = new Hotkey("F6", HotkeyModifiers.Control | HotkeyModifiers.Shift);
        Assert.True(WindowsGlobalHotkeyService.Matches(hotkey, 0x75, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.False(WindowsGlobalHotkeyService.Matches(hotkey, 0x75, HotkeyModifiers.Control));
        Assert.False(WindowsGlobalHotkeyService.Matches(hotkey, 0x75, HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt));
    }

    [Fact]
    public async Task Hold_mouse_hotkey_ignores_autorepeat_and_emits_release_only_for_key_state()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var service = new WindowsGlobalHotkeyService();
        Assert.True((await service.StartMonitoringAsync().WaitAsync(TimeSpan.FromSeconds(3))).Success);
        Assert.True((await service.RegisterEmergencyStopAsync(new Hotkey("F12"))).Success);
        Assert.True((await service.RegisterHoldMouseButtonAsync(new Hotkey("F5"))).Success);
        Assert.False((await service.RegisterAsync(new Hotkey("F5"))).Success);
        Assert.False((await service.RegisterHoldMouseButtonAsync(new Hotkey("F12"))).Success);

        var presses = new System.Collections.Concurrent.ConcurrentQueue<HotkeyEventArgs>();
        var releases = new System.Collections.Concurrent.ConcurrentQueue<HotkeyEventArgs>();
        using var received = new SemaphoreSlim(0);
        service.Pressed += (_, args) => { presses.Enqueue(args); received.Release(); };
        service.Released += (_, args) => { releases.Enqueue(args); received.Release(); };

        service.ProcessKey(0x74, true);
        service.ProcessKey(0x74, true); // autorepeat
        await received.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(presses.TryDequeue(out var first));
        Assert.True(first!.IsHoldMouseButton);
        Assert.Empty(presses);

        service.ProcessKey(0x74, false);
        await received.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(releases.TryDequeue(out var released));
        Assert.True(released!.IsReleased);
        Assert.True(released.IsHoldMouseButton);

        service.ProcessKey(0x74, true);
        await received.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(presses.TryDequeue(out var second));
        Assert.True(second!.IsHoldMouseButton);
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Shift")]
    [InlineData("Ctrl + Shift")]
    public void Modifier_cannot_be_used_as_hotkey_base_key(string text) => Assert.False(Hotkey.TryParse(text, out _));

    private static readonly int MarshalSize = System.Runtime.InteropServices.Marshal.SizeOf<Input>();

    private sealed class FakeCursor(FakeInputApi api) : ICursorService
    {
        public ScreenPoint GetPosition() => api.Position;
        public void SetPosition(ScreenPoint position) { api.Position = position; api.Operations.Add("position"); }
        public Task<ScreenPoint> PickPositionAsync(CancellationToken cancellationToken = default) => Task.FromResult(GetPosition());
    }

    private sealed class FakeInputApi : IWindowsInputApi
    {
        public List<Input> Submitted { get; } = [];
        public List<Input[]> Submissions { get; } = [];
        public List<string> Operations { get; } = [];
        public ScreenPoint Position { get; set; }
        public uint? SendCountOverride { get; set; }
        public int Error { get; set; }
        public int InputSize { get; private set; }
        public uint SendInput(uint count, Input[] inputs, int inputSize)
        {
            InputSize = inputSize;
            Submissions.Add(inputs);
            Submitted.AddRange(inputs);
            Operations.Add("input");
            return SendCountOverride ?? count;
        }
        public int GetLastError() => Error;
        public bool SetCursorPosition(int x, int y) { Position = new(x, y); return true; }
        public bool GetCursorPosition(out NativePoint point) { point = new() { X = Position.X, Y = Position.Y }; return true; }
    }
}

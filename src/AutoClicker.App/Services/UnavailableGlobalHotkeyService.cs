using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;

namespace AutoClicker.App.Services;

public sealed class UnavailableGlobalHotkeyService : IGlobalHotkeyService
{
    public event EventHandler<HotkeyEventArgs>? Pressed { add { } remove { } }
    public event EventHandler<HotkeyEventArgs>? Released { add { } remove { } }
    public Task<HotkeyRegistrationResult> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(false, "Global shortcuts are unavailable on this platform."));
    public Task<HotkeyRegistrationResult> RegisterHoldMouseButtonAsync(Hotkey hotkey, CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(false, "Global shortcuts are unavailable on this platform."));
    public Task<HotkeyRegistrationResult> RegisterEmergencyStopAsync(Hotkey hotkey, CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(false, "Global shortcuts are unavailable on this platform."));
    public void Unregister(Hotkey hotkey) { }
    public void UnregisterHoldMouseButton() { }
    public void UnregisterEmergencyStop() { }
    public void SetActionsSuspended(bool suspended) { }
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

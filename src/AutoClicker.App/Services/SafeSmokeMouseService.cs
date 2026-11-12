using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;

namespace AutoClicker.App.Services;

/// <summary>Explicit desktop QA runs render native UI but never emit mouse input.</summary>
internal sealed class SafeSmokeMouseService : IMouseService
{
    public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

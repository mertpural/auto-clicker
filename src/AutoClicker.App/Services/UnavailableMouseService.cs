using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;

namespace AutoClicker.App.Services;

/// <summary>Fail-closed placeholder used until a platform provides native input.</summary>
public sealed class UnavailableMouseService(IPlatformInfo platform) : IMouseService
{
    private PlatformNotSupportedException CreateError() =>
        new($"Native mouse input is unavailable on {platform.Name} in this build.");

    public Task ClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(CreateError());
    }

    public Task DoubleClickAsync(MouseButton button, ScreenPoint? position = null, CancellationToken cancellationToken = default) =>
        ClickAsync(button, position, cancellationToken);
    public Task ButtonDownAsync(MouseButton button, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromException(CreateError()); }
    public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromException(CreateError()); }
}

using AutoClicker.Core.Abstractions;
using AutoClicker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Services;

/// <summary>Owns the application's single synthetic mouse-button hold and its recovery path.</summary>
public sealed class MouseButtonHoldService
{
    private readonly IMouseService _mouse;
    private readonly ILogger<MouseButtonHoldService> _logger;
    private readonly LocalizationService? _localization;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private bool _isHeld;
    private MouseButton? _heldButton;

    public event EventHandler? Changed;

    public MouseButtonHoldService(IMouseService mouse, ILogger<MouseButtonHoldService> logger, LocalizationService? localization = null)
    { _mouse = mouse; _logger = logger; _localization = localization; }

    public bool IsHeld { get { lock (_stateGate) return _isHeld; } }
    public MouseButton? HeldButton { get { lock (_stateGate) return _heldButton; } }
    public string Status => IsHeld && HeldButton is { } button
        ? _localization?.Get("Status_HoldingMouse", _localization.Get($"Enum_MouseButton_{button}")) ?? $"Holding {button} Mouse Button"
        : _localization?.Get("Status_Ready") ?? "Ready";

    public async Task ToggleAsync(MouseButton button, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsHeld && HeldButton == button)
            {
                await ReleaseCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (IsHeld) await ReleaseCoreAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _mouse.ButtonDownAsync(button, cancellationToken).ConfigureAwait(false);
                lock (_stateGate) { _isHeld = true; _heldButton = button; }
                PublishChanged();
            }
            catch
            {
                // A failed native down may have partially reached the OS; send one best-effort up.
                try { await _mouse.ButtonUpAsync(button, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception releaseError) { _logger.LogError(releaseError, "Mouse button rollback failed for {Button}", button); }
                lock (_stateGate) { _isHeld = false; _heldButton = null; }
                PublishChanged();
                throw;
            }
        }
        finally { _operationGate.Release(); }
    }

    public async Task ReleaseIfHeldAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ReleaseCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _operationGate.Release(); }
    }

    private async Task ReleaseCoreAsync(CancellationToken cancellationToken)
    {
        MouseButton button;
        lock (_stateGate)
        {
            if (!_isHeld || _heldButton is not { } heldButton) return;
            button = heldButton;
        }

        Exception? firstFailure = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await _mouse.ButtonUpAsync(button, cancellationToken).ConfigureAwait(false);
                lock (_stateGate) { _isHeld = false; _heldButton = null; }
                PublishChanged();
                return;
            }
            catch (Exception exception) when (attempt == 1)
            {
                firstFailure = exception;
                _logger.LogWarning(exception, "Mouse button release failed for {Button}; retrying once", button);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Mouse button release failed for {Button}; held state remains active", button);
                throw new AggregateException("The held mouse button could not be released after one retry.", firstFailure!, exception);
            }
        }
    }

    private void PublishChanged()
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception) { _logger.LogWarning(exception, "Mouse hold state subscriber failed"); }
        }
    }
}

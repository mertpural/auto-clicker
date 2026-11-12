using AutoClicker.Core.Models;
using AutoClicker.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Services;

public sealed record SaveResult(bool Success, string? ErrorMessage = null);

/// <summary>Snapshots are captured on the UI thread; disk I/O never enumerates live UI collections.</summary>
public sealed class DebouncedSaveService(IApplicationStore store, IUiDispatcher dispatcher, ILogger<DebouncedSaveService> logger)
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);
    private CancellationTokenSource? _pendingCancellation;
    private Task _pendingTask = Task.CompletedTask;
    private long _revision;
    public event Action<SaveResult>? Completed;

    public void Schedule(ApplicationData snapshot)
    {
        CancelPending();
        var cancellation = new CancellationTokenSource();
        _pendingCancellation = cancellation;
        _pendingTask = SaveLaterAsync(snapshot, cancellation, ++_revision);
    }

    public void CancelPending()
    {
        ++_revision;
        var previous = _pendingCancellation;
        _pendingCancellation = null;
        if (previous is null) return;
        previous.Cancel();
        _ = _pendingTask.ContinueWith(_ => previous.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task SaveLaterAsync(ApplicationData snapshot, CancellationTokenSource cancellation, long revision)
    {
        try
        {
            await Task.Delay(Debounce, cancellation.Token).ConfigureAwait(false);
            await store.SaveAsync(snapshot, cancellation.Token).ConfigureAwait(false);
            Notify(new(true), revision);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Superseded snapshots are expected cancellations, not errors.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to persist profile changes");
            Notify(new(false, exception is PersistenceException ? exception.Message : "Unable to save changes. See the application log for details."), revision);
        }
    }

    private void Notify(SaveResult result, long revision) => dispatcher.Post(() =>
    {
        if (revision == _revision) Completed?.Invoke(result);
    });

    public async Task<SaveResult> FlushAsync(ApplicationData snapshot)
    {
        CancelPending();
        await _pendingTask;
        try
        {
            await store.SaveAsync(snapshot);
            return new(true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to flush configuration");
            return new(false, exception is PersistenceException ? exception.Message : "Unable to save changes. See the application log for details.");
        }
    }

    public async Task CancelAndWaitAsync()
    {
        CancelPending();
        await _pendingTask;
    }
}

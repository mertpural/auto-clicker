using System.Text;
using System.Security.Cryptography;
using System.Threading.Channels;
using AutoClicker.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Services;

/// <summary>Uses a stable per-user Windows mutex (file lock on macOS) and watched files for activation.</summary>
public sealed class SingleInstanceService : ISingleInstanceService
{
    private readonly string _lockPath;
    private readonly string _activationDirectory;
    private readonly ILogger<SingleInstanceService> _logger;
    private FileStream? _lock;
    private Mutex? _mutex;
    private Thread? _mutexOwnerThread;
    private readonly ManualResetEventSlim _mutexAcquired = new(false);
    private readonly ManualResetEventSlim _releaseMutex = new(false);
    private bool _ownsMutex;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _listenerGate = new();
    private Task? _listenerTask;
    private int _disposed;
    private readonly bool _isolateIdentity;

    public SingleInstanceService(string configurationDirectory, ILogger<SingleInstanceService> logger, bool isolateIdentity = false)
    {
        _isolateIdentity = isolateIdentity;
        Directory.CreateDirectory(configurationDirectory);
        _lockPath = Path.Combine(configurationDirectory, "instance.lock");
        _activationDirectory = Path.Combine(configurationDirectory, "activation");
        Directory.CreateDirectory(_activationDirectory);
        _logger = logger;
    }

    public bool TryAcquire()
    {
        if (_ownsMutex || _lock is not null) return true;
        if (OperatingSystem.IsWindows()) return TryAcquireWindowsMutex();
        try { _lock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }

    private bool TryAcquireWindowsMutex()
    {
        var identity = ProductIdentity.BundleIdentifier + "|" + Environment.UserName;
        if (_isolateIdentity) identity += "|QA|" + Path.GetFullPath(_activationDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        _mutex = new Mutex(false, "Local\\AutoClicker-" + hash);
        _mutexOwnerThread = new Thread(() =>
        {
            try
            {
                try { _ownsMutex = _mutex.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsMutex = true; }
                _mutexAcquired.Set();
                if (_ownsMutex) { _releaseMutex.Wait(); _mutex.ReleaseMutex(); }
            }
            catch { _mutexAcquired.Set(); }
        }) { IsBackground = true, Name = "AutoClicker instance mutex" };
        _mutexOwnerThread.Start();
        _mutexAcquired.Wait();
        if (!_ownsMutex) { _mutexOwnerThread.Join(); _mutex.Dispose(); _mutex = null; _mutexOwnerThread = null; }
        return _ownsMutex;
    }

    public async Task<bool> RequestActivationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stop.Token.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var pending = Path.Combine(_activationDirectory, requestId + ".tmp");
        var request = Path.Combine(_activationDirectory, requestId + ".activate");
        await File.WriteAllTextAsync(pending, "Activate", Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(pending, request);
        return true;
    }

    public Task RunActivationListenerAsync(Func<Task> activate, CancellationToken cancellationToken = default)
    {
        lock (_listenerGate) return _listenerTask ??= ListenForActivationAsync(activate, cancellationToken);
    }

    private async Task ListenForActivationAsync(Func<Task> activate, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        using var watcher = new FileSystemWatcher(_activationDirectory, "*.activate") { IncludeSubdirectories = false };
        FileSystemEventHandler enqueue = (_, args) => queue.Writer.TryWrite(args.FullPath);
        RenamedEventHandler renamed = (_, args) => queue.Writer.TryWrite(args.FullPath);
        ErrorEventHandler recover = (_, _) => EnqueueExisting();
        watcher.Created += enqueue; watcher.Renamed += renamed; watcher.Error += recover;
        void EnqueueExisting()
        {
            try { foreach (var path in Directory.EnumerateFiles(_activationDirectory, "*.activate")) queue.Writer.TryWrite(path); }
            catch (Exception exception) { _logger.LogWarning(exception, "Could not scan for pending activation requests"); }
        }
        watcher.EnableRaisingEvents = true;
        EnqueueExisting();
        try
        {
            await foreach (var path in queue.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
            {
                try
                {
                    if (!string.Equals(await File.ReadAllTextAsync(path, linked.Token).ConfigureAwait(false), "Activate", StringComparison.Ordinal)) continue;
                    File.Delete(path);
                    if (!linked.IsCancellationRequested) await activate().ConfigureAwait(false);
                    EnqueueExisting();
                }
                catch (FileNotFoundException) { }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
                catch (Exception exception) { _logger.LogWarning(exception, "Single-instance activation IPC failed"); }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { watcher.EnableRaisingEvents = false; queue.Writer.TryComplete(); }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        Task? listener; lock (_listenerGate) listener = _listenerTask;
        if (listener is not null)
        {
            try { await listener.ConfigureAwait(false); }
            catch (Exception exception) { _logger.LogWarning(exception, "Activation listener stopped with an error"); }
        }
        _releaseMutex.Set();
        if (_mutexOwnerThread is { IsAlive: true }) _mutexOwnerThread.Join(TimeSpan.FromSeconds(2));
        _mutex?.Dispose(); _mutex = null;
        _lock?.Dispose(); _lock = null;
        _stop.Dispose();
        _mutexAcquired.Dispose(); _releaseMutex.Dispose();
    }
}

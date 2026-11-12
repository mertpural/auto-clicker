using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using AutoClicker.Core.Timing;

namespace AutoClicker.Platform.Windows;

/// <summary>Waits on a per-call high-resolution kernel timer and the cancellation handle.</summary>
public sealed class WindowsHighResolutionDelay : IEngineDelay, IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private const uint WaitObject0 = 0;
    private const uint WaitFailed = 0xFFFFFFFF;
    private readonly object _sync = new();
    private readonly IntPtr[] _waitHandles = new IntPtr[2];
    private SafeWaitHandle? _timer;
    private bool _timerIsHighResolution;
    private bool _disposed;
    private long _highResolutionWaitCount;
    private long _standardResolutionWaitCount;
    private long _timerHandleCreationCount;

    public long HighResolutionWaitCount => Interlocked.Read(ref _highResolutionWaitCount);
    public long StandardResolutionWaitCount => Interlocked.Read(ref _standardResolutionWaitCount);
    public long TimerHandleCreationCount => Interlocked.Read(ref _timerHandleCreationCount);

    public ValueTask DelayUntilAsync(long deadline, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetTimestamp();
        if (deadline <= now) return ValueTask.CompletedTask;
        WaitUntil(deadline, timeProvider, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    private void WaitUntil(long deadline, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var remaining = timeProvider.GetElapsedTime(timeProvider.GetTimestamp(), deadline);
            if (remaining <= TimeSpan.Zero) return;
            var timer = GetOrCreateTimer();
            if (_timerIsHighResolution) Interlocked.Increment(ref _highResolutionWaitCount);
            else Interlocked.Increment(ref _standardResolutionWaitCount);
            var dueTime = -(long)Math.Ceiling(remaining.TotalSeconds * TimeSpan.TicksPerSecond);
            if (!SetWaitableTimer(timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to set the high-resolution click scheduler timer.");

            var cancelSafeHandle = cancellationToken.WaitHandle.SafeWaitHandle;
            var addedRef = false;
            try
            {
                cancelSafeHandle.DangerousAddRef(ref addedRef);
                _waitHandles[0] = timer.DangerousGetHandle();
                _waitHandles[1] = cancelSafeHandle.DangerousGetHandle();
                var result = WaitForMultipleObjects(2, _waitHandles, false, uint.MaxValue);
                if (result == WaitObject0 + 1) cancellationToken.ThrowIfCancellationRequested();
                if (result == WaitFailed) throw new Win32Exception(Marshal.GetLastWin32Error(), "The click scheduler timer wait failed.");
                if (result != WaitObject0) throw new InvalidOperationException($"Unexpected waitable timer result: {result}.");
            }
            finally
            {
                if (addedRef) cancelSafeHandle.DangerousRelease();
            }
        }
    }

    private SafeWaitHandle GetOrCreateTimer()
    {
        if (_timer is not null) return _timer;
        var timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        if (!timer.IsInvalid) _timerIsHighResolution = true;
        else
        {
            timer.Dispose();

            // Windows before version 1803 does not support the high-resolution flag.
            timer = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TimerAllAccess);
            if (timer.IsInvalid)
            {
                timer.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create a waitable click scheduler timer.");
            }
            _timerIsHighResolution = false;
        }
        _timer = timer;
        Interlocked.Increment(ref _timerHandleCreationCount);
        return timer;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr timerAttributes, string? timerName, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completionRoutine, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint count, [In] IntPtr[] handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);
}

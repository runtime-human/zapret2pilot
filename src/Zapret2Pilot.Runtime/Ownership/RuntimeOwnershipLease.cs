using System;
using System.Threading;

namespace Zapret2Pilot.Runtime.Ownership;

/// <summary>
/// Owns a runtime ownership mutex lease.
/// Dispose this object on the same managed thread that acquired the mutex.
/// </summary>
public sealed class RuntimeOwnershipLease : IDisposable
{
    private readonly Mutex mutex;
    private readonly int ownerManagedThreadId;
    private bool disposed;

    internal RuntimeOwnershipLease(
        string mutexName,
        Mutex mutex,
        bool wasAbandoned)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentNullException.ThrowIfNull(mutex);

        MutexName = mutexName;
        this.mutex = mutex;
        WasAbandoned = wasAbandoned;
        ownerManagedThreadId = Environment.CurrentManagedThreadId;
    }

    public string MutexName { get; }

    public bool WasAbandoned { get; }

    internal void EnsureActiveOwnershipOnCurrentThread()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (Environment.CurrentManagedThreadId != ownerManagedThreadId)
        {
            throw new InvalidOperationException(
                "Runtime ownership lease must be used on the same managed thread that acquired it.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        EnsureActiveOwnershipOnCurrentThread();

        disposed = true;

        try
        {
            mutex.ReleaseMutex();
        }
        finally
        {
            mutex.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

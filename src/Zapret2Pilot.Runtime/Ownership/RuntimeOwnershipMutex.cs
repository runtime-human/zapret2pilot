using System;
using System.Threading;

namespace Zapret2Pilot.Runtime.Ownership;

public sealed class RuntimeOwnershipMutex
{
    private readonly string mutexName;

    public RuntimeOwnershipMutex(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        this.mutexName = mutexName;
    }

    public RuntimeOwnershipAcquireResult TryAcquire(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Mutex mutex = new(initiallyOwned: false, name: mutexName);

        try
        {
            if (!mutex.WaitOne(timeout))
            {
                mutex.Dispose();

                return RuntimeOwnershipAcquireResult.NotAcquired();
            }

            RuntimeOwnershipLease lease = new(
                mutexName,
                mutex,
                wasAbandoned: false);

            return RuntimeOwnershipAcquireResult.CreateAcquired(
                lease,
                wasAbandoned: false);
        }
        catch (AbandonedMutexException)
        {
            RuntimeOwnershipLease lease = new(
                mutexName,
                mutex,
                wasAbandoned: true);

            return RuntimeOwnershipAcquireResult.CreateAcquired(
                lease,
                wasAbandoned: true);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }
}

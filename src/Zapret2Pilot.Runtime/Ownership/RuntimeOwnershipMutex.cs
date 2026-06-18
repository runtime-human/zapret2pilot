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
        bool handleTransferred = false;

        try
        {
            try
            {
                if (!mutex.WaitOne(timeout))
                {
                    return RuntimeOwnershipAcquireResult.NotAcquired();
                }
            }
            catch (AbandonedMutexException)
            {
                RuntimeOwnershipLease abandonedLease = new(
                    mutexName,
                    mutex,
                    wasAbandoned: true);

                handleTransferred = true;

                return RuntimeOwnershipAcquireResult.CreateAcquired(
                    abandonedLease,
                    wasAbandoned: true);
            }

            RuntimeOwnershipLease lease = new(
                mutexName,
                mutex,
                wasAbandoned: false);

            handleTransferred = true;

            return RuntimeOwnershipAcquireResult.CreateAcquired(
                lease,
                wasAbandoned: false);
        }
        finally
        {
            if (!handleTransferred)
            {
                mutex.Dispose();
            }
        }
    }
}

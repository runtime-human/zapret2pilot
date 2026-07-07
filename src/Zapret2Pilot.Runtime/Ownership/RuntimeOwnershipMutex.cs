using System;
using System.Threading;

namespace Zapret2Pilot.Runtime.Ownership;

public sealed class RuntimeOwnershipMutex
{
    private static readonly NamedWaitHandleOptions OwnershipOptions = new()
    {
        CurrentUserOnly = true,
        CurrentSessionOnly = false,
    };

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

        Mutex? mutex = null;
        bool handleTransferred = false;

        try
        {
            try
            {
                mutex = new Mutex(
                    initiallyOwned: false,
                    name: mutexName,
                    options: OwnershipOptions,
                    createdNew: out _);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return RuntimeOwnershipAcquireResult.NotAcquired(
                    RuntimeOwnershipAcquireFailureReason.ExistingObjectRejected);
            }
            catch (UnauthorizedAccessException)
            {
                return RuntimeOwnershipAcquireResult.NotAcquired(
                    RuntimeOwnershipAcquireFailureReason.ExistingObjectAccessDenied);
            }

            try
            {
                if (!mutex.WaitOne(timeout))
                {
                    return RuntimeOwnershipAcquireResult.NotAcquired(
                        RuntimeOwnershipAcquireFailureReason.TimedOut);
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
                mutex?.Dispose();
            }
        }
    }
}

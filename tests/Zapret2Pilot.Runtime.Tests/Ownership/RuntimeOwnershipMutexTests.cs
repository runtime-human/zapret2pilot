using System;
using System.Threading;
using Xunit;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Tests.Ownership;

public sealed class RuntimeOwnershipMutexTests
{
    [Fact]
    public static void TryAcquireReturnsLeaseWhenMutexIsAvailable()
    {
        string mutexName = CreateUniqueMutexName();
        RuntimeOwnershipMutex ownershipMutex = new(mutexName);

        RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);

        using RuntimeOwnershipLease lease = Assert.NotNull(result.Lease);

        Assert.True(result.Acquired);
        Assert.False(result.WasAbandoned);
        Assert.Equal(mutexName, lease.MutexName);
        Assert.False(lease.WasAbandoned);
    }

    [Fact]
    public static void TryAcquireReturnsNotAcquiredWhenMutexIsOwnedByAnotherThread()
    {
        string mutexName = CreateUniqueMutexName();

        using ManualResetEventSlim ownerAcquired = new(initialState: false);
        using ManualResetEventSlim releaseOwner = new(initialState: false);

        Exception? ownerThreadException = null;

        Thread ownerThread = new(() =>
        {
            try
            {
                RuntimeOwnershipMutex ownershipMutex = new(mutexName);
                RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.FromSeconds(5));

                if (!result.Acquired || result.Lease is null)
                {
                    throw new InvalidOperationException("Owner thread failed to acquire runtime ownership mutex.");
                }

                using RuntimeOwnershipLease lease = result.Lease;

                ownerAcquired.Set();
                releaseOwner.Wait();
            }
            catch (Exception exception)
            {
                ownerThreadException = exception;
                ownerAcquired.Set();
            }
        });

        ownerThread.Start();

        Assert.True(ownerAcquired.Wait(TimeSpan.FromSeconds(5)));

        if (ownerThreadException is not null)
        {
            throw new InvalidOperationException("Owner thread failed.", ownerThreadException);
        }

        try
        {
            RuntimeOwnershipMutex contenderMutex = new(mutexName);
            RuntimeOwnershipAcquireResult contenderResult = contenderMutex.TryAcquire(TimeSpan.Zero);

            Assert.False(contenderResult.Acquired);
            Assert.False(contenderResult.WasAbandoned);
            Assert.Null(contenderResult.Lease);
        }
        finally
        {
            releaseOwner.Set();
        }

        Assert.True(ownerThread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public static void DisposeReleasesOwnedMutex()
    {
        string mutexName = CreateUniqueMutexName();

        RuntimeOwnershipMutex firstMutex = new(mutexName);
        RuntimeOwnershipAcquireResult firstResult = firstMutex.TryAcquire(TimeSpan.Zero);

        RuntimeOwnershipLease firstLease = Assert.NotNull(firstResult.Lease);
        firstLease.Dispose();

        RuntimeOwnershipMutex secondMutex = new(mutexName);
        RuntimeOwnershipAcquireResult secondResult = secondMutex.TryAcquire(TimeSpan.Zero);

        using RuntimeOwnershipLease secondLease = Assert.NotNull(secondResult.Lease);

        Assert.True(secondResult.Acquired);
        Assert.Equal(mutexName, secondLease.MutexName);
    }

    [Fact]
    public static void DisposeIsIdempotent()
    {
        string mutexName = CreateUniqueMutexName();
        RuntimeOwnershipMutex ownershipMutex = new(mutexName);

        RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);

        RuntimeOwnershipLease lease = Assert.NotNull(result.Lease);

        lease.Dispose();
        lease.Dispose();

        Assert.True(result.Acquired);
    }

    [Fact]
    public static void LeaseDisposeRejectsDifferentThread()
    {
        string mutexName = CreateUniqueMutexName();
        RuntimeOwnershipMutex ownershipMutex = new(mutexName);

        RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);
        RuntimeOwnershipLease lease = Assert.NotNull(result.Lease);

        Exception? disposeException = null;

        Thread disposeThread = new(() =>
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception exception)
            {
                disposeException = exception;
            }
        });

        disposeThread.Start();

        Assert.True(disposeThread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(disposeException);

        lease.Dispose();
    }

    private static string CreateUniqueMutexName()
    {
        return OperatingSystem.IsWindows()
            ? $@"Local\Z2P_TEST_{Guid.NewGuid():N}"
            : $"Z2P_TEST_{Guid.NewGuid():N}";
    }
}

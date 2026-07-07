using System;
using System.Threading;
using Xunit;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Tests.Ownership;

public sealed class RuntimeOwnershipMutexTests
{
    private static string CreateCompatibleMutexName() => $"Z2P_TEST_{Guid.NewGuid():N}";

    private static bool LocalGlobalNamespaceSeparationIsEnforced()
    {
        string name = $"Z2P_PROBE_{Guid.NewGuid():N}";
        using Mutex local = new(initiallyOwned: false, name: $"Local\\{name}");
        try
        {
            using Mutex global = new(
                initiallyOwned: false,
                name: name,
                options: new NamedWaitHandleOptions { CurrentSessionOnly = false },
                createdNew: out _);
            return false;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return true;
        }
    }

    [Fact]
    public static void TryAcquireReturnsLeaseWhenMutexIsAvailable()
    {
        string mutexName = CreateCompatibleMutexName();
        RuntimeOwnershipMutex ownershipMutex = new(mutexName);

        RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);

        using RuntimeOwnershipLease lease = RuntimeTestData.RequireLease(result);

        Assert.False(result.WasAbandoned);
        Assert.Equal(mutexName, lease.MutexName);
        Assert.False(lease.WasAbandoned);
    }

    [Fact]
    public static void TryAcquireReturnsNotAcquiredWhenMutexIsOwnedByAnotherThread()
    {
        string mutexName = CreateCompatibleMutexName();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using ManualResetEventSlim ownerAcquired = new(initialState: false);
        using ManualResetEventSlim releaseOwner = new(initialState: false);

        Exception? ownerThreadException = null;

        Thread ownerThread = new(() =>
        {
            try
            {
                RuntimeOwnershipMutex ownershipMutex = new(mutexName);
                RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.FromSeconds(5));

                using RuntimeOwnershipLease lease = RuntimeTestData.RequireLease(result);

                ownerAcquired.Set();
                releaseOwner.Wait(cancellationToken);
            }
            catch (Exception exception)
            {
                ownerThreadException = exception;
                ownerAcquired.Set();
            }
        });

        ownerThread.Start();

        Assert.True(ownerAcquired.Wait(TimeSpan.FromSeconds(5), cancellationToken));

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
            Assert.Equal(
                RuntimeOwnershipAcquireFailureReason.TimedOut,
                contenderResult.FailureReason);
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
        string mutexName = CreateCompatibleMutexName();
        RuntimeOwnershipMutex firstMutex = new(mutexName);
        RuntimeOwnershipAcquireResult firstResult = firstMutex.TryAcquire(TimeSpan.Zero);

        RuntimeOwnershipLease firstLease = RuntimeTestData.RequireLease(firstResult);
        firstLease.Dispose();

        RuntimeOwnershipMutex secondMutex = new(mutexName);
        RuntimeOwnershipAcquireResult secondResult = secondMutex.TryAcquire(TimeSpan.Zero);

        using RuntimeOwnershipLease secondLease = RuntimeTestData.RequireLease(secondResult);

        Assert.Equal(mutexName, secondLease.MutexName);
    }

    [Fact]
    public static void DisposeIsIdempotent()
    {
        string mutexName = CreateCompatibleMutexName();
        RuntimeOwnershipMutex ownershipMutex = new(mutexName);
        RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);
        RuntimeOwnershipLease lease = RuntimeTestData.RequireLease(result);

        lease.Dispose();
        lease.Dispose();
    }

    [Fact]
#pragma warning disable CA1707 // Identifiers should not contain underscores (behavior-descriptive test name)
    public static void TryAcquire_WhenExistingMutexHasDifferentScope_ReturnsExistingObjectRejected()
#pragma warning restore CA1707
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!LocalGlobalNamespaceSeparationIsEnforced())
        {
            return;
        }

        string mutexName = CreateCompatibleMutexName();

        // Pre-create a baseline mutex explicitly in the Local\ namespace.
        // RuntimeOwnershipMutex (CurrentSessionOnly = false) opens the object
        // in the Global\ namespace, so this deterministic scope mismatch must
        // surface as WaitHandleCannotBeOpenedException -> ExistingObjectRejected.
        using (Mutex baseline = new(initiallyOwned: false, name: $"Local\\{mutexName}"))
        {
            RuntimeOwnershipMutex ownershipMutex = new(mutexName);
            RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);

            Assert.False(result.Acquired);
            Assert.False(result.WasAbandoned);
            Assert.Null(result.Lease);
            Assert.Equal(
                RuntimeOwnershipAcquireFailureReason.ExistingObjectRejected,
                result.FailureReason);
        }
    }

    [Fact]
#pragma warning disable CA1707 // Identifiers should not contain underscores (behavior-descriptive test name)
    public static void TryAcquire_WhenExistingMutexDeniesAccess_ReturnsExistingObjectAccessDenied()
#pragma warning restore CA1707
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!LocalGlobalNamespaceSeparationIsEnforced())
        {
            return;
        }

        string mutexName = CreateCompatibleMutexName();

        // Pre-create a baseline mutex explicitly in the Local\ namespace.
        // RuntimeOwnershipMutex (CurrentSessionOnly = false) opens the object
        // in the Global\ namespace, so this deterministic scope mismatch must
        // surface as UnauthorizedAccessException -> ExistingObjectAccessDenied.
        using (Mutex baseline = new(initiallyOwned: false, name: $"Local\\{mutexName}"))
        {
            RuntimeOwnershipMutex ownershipMutex = new(mutexName);
            RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);

            Assert.False(result.Acquired);
            Assert.False(result.WasAbandoned);
            Assert.Null(result.Lease);
            Assert.Equal(
                RuntimeOwnershipAcquireFailureReason.ExistingObjectAccessDenied,
                result.FailureReason);
        }
    }
}

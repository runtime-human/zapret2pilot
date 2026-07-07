using System;
using System.Threading;
using Xunit;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Tests.Ownership;

public sealed class RuntimeOwnershipMutexTests
{
    private static string CreateCompatibleMutexName() => RuntimeTestData.CreateUniqueMutexName();

    private static bool LocalGlobalNamespaceSeparationIsEnforced()
    {
        string name = $"Z2P_PROBE_{Guid.NewGuid():N}";
        using Mutex local = new(initiallyOwned: false, name: $"Local\\{name}");
        try
        {
            using Mutex global = new(
                initiallyOwned: false,
                name: $"Global\\{name}",
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

        // CreateUniqueMutexName returns a Global\ prefixed name (matching the
        // production usage of RuntimeOwnershipNames.GlobalMutexName). Place a
        // baseline mutex explicitly in the Local\ namespace so the production
        // RuntimeOwnershipMutex (which opens the Global\ namespace) sees a
        // different object: WaitHandleCannotBeOpenedException ->
        // ExistingObjectRejected.
        string mutexName = RuntimeTestData.CreateUniqueMutexName();
        string localBaselineName = mutexName.StartsWith(@"Global\", StringComparison.Ordinal)
            ? $@"Local\{mutexName.Substring(@"Global\".Length)}"
            : mutexName;

        using (Mutex baseline = new(initiallyOwned: false, name: localBaselineName))
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
    public static void TryAcquire_WhenExistingObjectIsNotAMutex_ReturnsExistingObjectRejected()
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

        // Pre-create a non-mutex named object (EventWaitHandle) at the same
        // Global\ path that RuntimeOwnershipMutex will try to open. The Mutex
        // constructor throws WaitHandleCannotBeOpenedException because the
        // existing object is not a mutex, and the production code maps that
        // to ExistingObjectRejected.
        string name = RuntimeTestData.CreateUniqueMutexName();

        using EventWaitHandle baseline = new(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: name);

        RuntimeOwnershipMutex ownershipMutex = new(name);
        RuntimeOwnershipAcquireResult result = ownershipMutex.TryAcquire(TimeSpan.Zero);

        Assert.False(result.Acquired);
        Assert.False(result.WasAbandoned);
        Assert.Null(result.Lease);
        Assert.Equal(
            RuntimeOwnershipAcquireFailureReason.ExistingObjectRejected,
            result.FailureReason);
    }
}

using System;
using System.Threading;
using Xunit;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Tests.Ownership;

public sealed class RuntimeOwnershipMutexTests
{
    private static string CreateCompatibleMutexName() => $"Z2P_TEST_{Guid.NewGuid():N}";

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

        string mutexName = CreateCompatibleMutexName();

        // Pre-create a mutex using the legacy 2-arg constructor (no
        // NamedWaitHandleOptions). The default scope for that constructor
        // does not match CurrentUserOnly = true, so the subsequent
        // OwnershipOptions-based attempt must surface a scope mismatch as
        // WaitHandleCannotBeOpenedException -> ExistingObjectRejected.
        using (Mutex baseline = new(initiallyOwned: false, name: mutexName))
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

        string mutexName = CreateCompatibleMutexName();

        // Pre-create a mutex with CurrentSessionOnly = true so that the
        // subsequent OwnershipOptions-based attempt (CurrentSessionOnly = false)
        // hits a scope/security mismatch. On Windows, opening a named mutex
        // with options that do not match the existing handle's security
        // descriptor surfaces as UnauthorizedAccessException, which the
        // OwnershipOptions path translates to ExistingObjectAccessDenied.
        NamedWaitHandleOptions differentOptions = new()
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = true,
        };

        using (Mutex baseline = new(
            initiallyOwned: false,
            name: mutexName,
            options: differentOptions,
            createdNew: out bool baselineCreated))
        {
            Assert.True(baselineCreated, "Baseline mutex should have been created.");

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

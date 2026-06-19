using System;
using System.IO;
using System.Threading;
using Xunit;
using Zapret2Pilot.Runtime.Locking;
using Zapret2Pilot.Runtime.Ownership;
using Zapret2Pilot.Runtime.Recovery;

namespace Zapret2Pilot.Runtime.Tests.Recovery;

public sealed class RuntimeStaleLockRecoveryTests
{
    [Fact]
    public static void RecoverAfterOwnershipAcquiredReturnsNoLockFileWhenMissing()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeStaleLockRecovery recovery = CreateRecovery(temporaryDirectory, out _);

        using RuntimeOwnershipLease ownershipLease = RuntimeTestData.AcquireOwnershipLease();

        RuntimeStaleLockRecoveryResult result = recovery.RecoverAfterOwnershipAcquired(ownershipLease);

        Assert.Equal(RuntimeStaleLockRecoveryStatus.NoLockFile, result.Status);
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRemovesInvalidLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeStaleLockRecovery recovery = CreateRecovery(temporaryDirectory, out RuntimeLockFileStore store);
        using RuntimeOwnershipLease ownershipLease = RuntimeTestData.AcquireOwnershipLease();

        File.WriteAllText(store.LockFilePath, "{ invalid json");

        RuntimeStaleLockRecoveryResult result = recovery.RecoverAfterOwnershipAcquired(ownershipLease);

        Assert.Equal(RuntimeStaleLockRecoveryStatus.InvalidLockFileRemoved, result.Status);
        Assert.False(File.Exists(store.LockFilePath));
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRemovesStaleLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeStaleLockRecovery recovery = CreateRecovery(temporaryDirectory, out RuntimeLockFileStore store);
        using RuntimeOwnershipLease ownershipLease = RuntimeTestData.AcquireOwnershipLease();

        store.Write(RuntimeTestData.CreateLockMetadata());

        RuntimeStaleLockRecoveryResult result = recovery.RecoverAfterOwnershipAcquired(ownershipLease);

        Assert.Equal(RuntimeStaleLockRecoveryStatus.StaleLockFileRemoved, result.Status);
        Assert.False(File.Exists(store.LockFilePath));
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRejectsDisposedLease()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeStaleLockRecovery recovery = CreateRecovery(temporaryDirectory, out _);
        RuntimeOwnershipLease ownershipLease = RuntimeTestData.AcquireOwnershipLease();

        ownershipLease.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            recovery.RecoverAfterOwnershipAcquired(ownershipLease));
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRejectsLeaseFromDifferentThread()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeStaleLockRecovery recovery = CreateRecovery(temporaryDirectory, out _);
        RuntimeOwnershipLease ownershipLease = RuntimeTestData.AcquireOwnershipLease();
        Exception? recoveryException = null;

        Thread recoveryThread = new(() =>
        {
            try
            {
                recovery.RecoverAfterOwnershipAcquired(ownershipLease);
            }
            catch (Exception exception)
            {
                recoveryException = exception;
            }
        });

        try
        {
            recoveryThread.Start();

            if (!recoveryThread.Join(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Recovery thread did not finish.");
            }

            Assert.IsType<RuntimeOwnershipThreadAffinityException>(recoveryException);
        }
        finally
        {
            ownershipLease.Dispose();
        }
    }

    private static RuntimeStaleLockRecovery CreateRecovery(
        TemporaryDirectory temporaryDirectory,
        out RuntimeLockFileStore store)
    {
        store = new RuntimeLockFileStore(temporaryDirectory.DirectoryPath);

        return new RuntimeStaleLockRecovery(store);
    }
}

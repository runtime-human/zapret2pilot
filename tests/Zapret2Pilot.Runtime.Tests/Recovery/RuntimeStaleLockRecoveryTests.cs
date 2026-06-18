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
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);
        RuntimeStaleLockRecovery recovery = new(store);
        RuntimeOwnershipMutex ownershipMutex = new(CreateUniqueMutexName());
        RuntimeOwnershipAcquireResult ownershipResult = ownershipMutex.TryAcquire(TimeSpan.Zero);

        using RuntimeOwnershipLease ownershipLease = Assert.NotNull(ownershipResult.Lease);

        RuntimeStaleLockRecoveryResult result = recovery.RecoverAfterOwnershipAcquired(ownershipLease);

        Assert.Equal(RuntimeStaleLockRecoveryStatus.NoLockFile, result.Status);
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRemovesInvalidLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);
        RuntimeStaleLockRecovery recovery = new(store);
        RuntimeOwnershipMutex ownershipMutex = new(CreateUniqueMutexName());
        RuntimeOwnershipAcquireResult ownershipResult = ownershipMutex.TryAcquire(TimeSpan.Zero);

        using RuntimeOwnershipLease ownershipLease = Assert.NotNull(ownershipResult.Lease);

        File.WriteAllText(store.LockFilePath, "{ invalid json");

        RuntimeStaleLockRecoveryResult result = recovery.RecoverAfterOwnershipAcquired(ownershipLease);

        Assert.Equal(RuntimeStaleLockRecoveryStatus.InvalidLockFileRemoved, result.Status);
        Assert.False(File.Exists(store.LockFilePath));
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRemovesStaleLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);
        RuntimeStaleLockRecovery recovery = new(store);
        RuntimeOwnershipMutex ownershipMutex = new(CreateUniqueMutexName());
        RuntimeOwnershipAcquireResult ownershipResult = ownershipMutex.TryAcquire(TimeSpan.Zero);

        using RuntimeOwnershipLease ownershipLease = Assert.NotNull(ownershipResult.Lease);

        store.Write(CreateMetadata());

        RuntimeStaleLockRecoveryResult result = recovery.RecoverAfterOwnershipAcquired(ownershipLease);

        Assert.Equal(RuntimeStaleLockRecoveryStatus.StaleLockFileRemoved, result.Status);
        Assert.False(File.Exists(store.LockFilePath));
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRejectsDisposedLease()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);
        RuntimeStaleLockRecovery recovery = new(store);
        RuntimeOwnershipMutex ownershipMutex = new(CreateUniqueMutexName());
        RuntimeOwnershipAcquireResult ownershipResult = ownershipMutex.TryAcquire(TimeSpan.Zero);

        RuntimeOwnershipLease ownershipLease = Assert.NotNull(ownershipResult.Lease);
        ownershipLease.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            recovery.RecoverAfterOwnershipAcquired(ownershipLease));
    }

    [Fact]
    public static void RecoverAfterOwnershipAcquiredRejectsLeaseFromDifferentThread()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);
        RuntimeStaleLockRecovery recovery = new(store);
        RuntimeOwnershipMutex ownershipMutex = new(CreateUniqueMutexName());
        RuntimeOwnershipAcquireResult ownershipResult = ownershipMutex.TryAcquire(TimeSpan.Zero);

        RuntimeOwnershipLease ownershipLease = Assert.NotNull(ownershipResult.Lease);
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
                throw new InvalidOperationException("Recovery thread did not finish.");
            }

            Assert.IsType<InvalidOperationException>(recoveryException);
        }
        finally
        {
            ownershipLease.Dispose();
        }
    }

    private static RuntimeLockMetadata CreateMetadata()
    {
        return new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: "test-owner",
            process: new RuntimeLockProcessMetadata(
                processId: 1234,
                processName: "runtime-engine",
                executablePath: "runtime-engine.exe",
                commandLineHash: "command-line-hash",
                planHash: "plan-hash",
                processStartedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5)),
            acquiredAtUtc: DateTimeOffset.UtcNow);
    }

    private static string CreateUniqueMutexName()
    {
        return OperatingSystem.IsWindows()
            ? $@"Local\Z2P_TEST_{Guid.NewGuid():N}"
            : $"Z2P_TEST_{Guid.NewGuid():N}";
    }
}

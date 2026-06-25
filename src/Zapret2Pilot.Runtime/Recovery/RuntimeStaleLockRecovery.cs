using System;
using Zapret2Pilot.Runtime.Locking;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Recovery;

public sealed class RuntimeStaleLockRecovery
{
    private readonly IRuntimeLockFileStore lockFileStore;

    public RuntimeStaleLockRecovery(IRuntimeLockFileStore lockFileStore)
    {
        ArgumentNullException.ThrowIfNull(lockFileStore);

        this.lockFileStore = lockFileStore;
    }

    public RuntimeStaleLockRecoveryResult RecoverAfterOwnershipAcquired(
        RuntimeOwnershipLease ownershipLease)
    {
        ArgumentNullException.ThrowIfNull(ownershipLease);

        ownershipLease.EnsureActiveOwnershipOnCurrentThread();

        RuntimeLockFileReadResult readResult = lockFileStore.Read();

        if (readResult.Status == RuntimeLockFileReadStatus.Missing)
        {
            return new RuntimeStaleLockRecoveryResult(
                RuntimeStaleLockRecoveryStatus.NoLockFile);
        }

        if (readResult.Status == RuntimeLockFileReadStatus.Invalid)
        {
            lockFileStore.Delete();

            return new RuntimeStaleLockRecoveryResult(
                RuntimeStaleLockRecoveryStatus.InvalidLockFileRemoved);
        }

        lockFileStore.Delete();

        return new RuntimeStaleLockRecoveryResult(
            RuntimeStaleLockRecoveryStatus.StaleLockFileRemoved);
    }
}

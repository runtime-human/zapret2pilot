namespace Zapret2Pilot.Runtime.Recovery;

public enum RuntimeStaleLockRecoveryStatus
{
    NoLockFile = 0,
    InvalidLockFileRemoved = 1,
    StaleLockFileRemoved = 2
}

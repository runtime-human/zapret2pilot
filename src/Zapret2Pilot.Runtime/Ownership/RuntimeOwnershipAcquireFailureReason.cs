namespace Zapret2Pilot.Runtime.Ownership;

public enum RuntimeOwnershipAcquireFailureReason
{
    None = 0,
    TimedOut = 1,
    ExistingObjectRejected = 2,
    ExistingObjectAccessDenied = 3,
}

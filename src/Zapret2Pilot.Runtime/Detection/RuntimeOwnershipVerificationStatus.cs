namespace Zapret2Pilot.Runtime.Detection;

public enum RuntimeOwnershipVerificationStatus
{
    OwnedByExpectedRuntime = 0,
    NoProcess = 1,
    ProcessNameMismatch = 2,
    ExecutablePathMismatch = 3,
    CommandLineHashMismatch = 4,
    PlanHashMismatch = 5,
    ProcessStartTimeMismatch = 6,
    Unknown = 7
}

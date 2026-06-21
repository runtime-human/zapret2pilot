namespace Zapret2Pilot.Runtime.Windows;

public enum RuntimeJobObjectAssignmentStatus
{
    Assigned = 0,
    UnsupportedPlatform = 1,
    InvalidHandle = 2,
    AccessDenied = 3,
    AlreadyAssigned = 4,
    Failed = 5
}

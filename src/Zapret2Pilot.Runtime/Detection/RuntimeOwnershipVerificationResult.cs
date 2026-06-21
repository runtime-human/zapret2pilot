namespace Zapret2Pilot.Runtime.Detection;

public sealed record class RuntimeOwnershipVerificationResult(
    RuntimeOwnershipVerificationStatus Status)
{
    public static RuntimeOwnershipVerificationResult OwnedByExpectedRuntime()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.OwnedByExpectedRuntime);
    }

    public static RuntimeOwnershipVerificationResult NoProcess()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.NoProcess);
    }

    public static RuntimeOwnershipVerificationResult ProcessNameMismatch()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.ProcessNameMismatch);
    }

    public static RuntimeOwnershipVerificationResult ExecutablePathMismatch()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.ExecutablePathMismatch);
    }

    public static RuntimeOwnershipVerificationResult CommandLineHashMismatch()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.CommandLineHashMismatch);
    }

    public static RuntimeOwnershipVerificationResult CommandLineUnverifiable()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.CommandLineUnverifiable);
    }

    public static RuntimeOwnershipVerificationResult PlanHashMismatch()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.PlanHashMismatch);
    }

    public static RuntimeOwnershipVerificationResult ProcessStartTimeMismatch()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.ProcessStartTimeMismatch);
    }

    public static RuntimeOwnershipVerificationResult Unknown()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.Unknown);
    }
}

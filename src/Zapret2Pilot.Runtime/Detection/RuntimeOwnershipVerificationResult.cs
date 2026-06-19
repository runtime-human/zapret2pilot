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

    public static RuntimeOwnershipVerificationResult Unknown()
    {
        return new RuntimeOwnershipVerificationResult(
            RuntimeOwnershipVerificationStatus.Unknown);
    }
}

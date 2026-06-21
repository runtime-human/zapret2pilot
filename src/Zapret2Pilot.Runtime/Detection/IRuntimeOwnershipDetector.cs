using Zapret2Pilot.Runtime.Locking;

namespace Zapret2Pilot.Runtime.Detection;

public interface IRuntimeOwnershipDetector
{
    RuntimeOwnershipVerificationResult Verify(
        RuntimeLockMetadata metadata,
        string expectedPlanHash);
}

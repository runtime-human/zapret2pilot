using System.Runtime.InteropServices;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Protocol;

[StructLayout(LayoutKind.Auto)]
public readonly record struct BrokerProtocolVersion(ushort Major, ushort Minor)
{
    public static BrokerProtocolVersion V1 => new(1, 0);
}

[StructLayout(LayoutKind.Auto)]
public readonly record struct BrokerProtocolRange(
    BrokerProtocolVersion Minimum,
    BrokerProtocolVersion Maximum)
{
    public static BrokerProtocolRange Current => new(BrokerProtocolVersion.V1, BrokerProtocolVersion.V1);
}

public enum BrokerProtocolRejectionReason
{
    None,
    InvalidRange,
    UnsupportedMajorVersion,
    NoCommonVersion,
}

public sealed record BrokerProtocolNegotiationResult(
    bool Accepted,
    BrokerProtocolVersion? SelectedVersion,
    BrokerProtocolRejectionReason RejectionReason);

public static class BrokerProtocolNegotiator
{
    public static BrokerProtocolNegotiationResult Negotiate(
        BrokerProtocolRange client,
        BrokerProtocolRange server)
    {
        if (Compare(client.Minimum, client.Maximum) > 0 || Compare(server.Minimum, server.Maximum) > 0)
        {
            return new(false, null, BrokerProtocolRejectionReason.InvalidRange);
        }

        ushort lowestMajor = Math.Max(client.Minimum.Major, server.Minimum.Major);
        ushort highestMajor = Math.Min(client.Maximum.Major, server.Maximum.Major);
        if (lowestMajor > highestMajor)
        {
            return new(false, null, BrokerProtocolRejectionReason.UnsupportedMajorVersion);
        }

        for (int major = highestMajor; major >= lowestMajor; major--)
        {
            ushort candidateMajor = checked((ushort)major);
            ushort clientMinimumMinor = candidateMajor == client.Minimum.Major ? client.Minimum.Minor : (ushort)0;
            ushort serverMinimumMinor = candidateMajor == server.Minimum.Major ? server.Minimum.Minor : (ushort)0;
            ushort clientMaximumMinor = candidateMajor == client.Maximum.Major ? client.Maximum.Minor : ushort.MaxValue;
            ushort serverMaximumMinor = candidateMajor == server.Maximum.Major ? server.Maximum.Minor : ushort.MaxValue;

            ushort minimumMinor = Math.Max(clientMinimumMinor, serverMinimumMinor);
            ushort maximumMinor = Math.Min(clientMaximumMinor, serverMaximumMinor);
            if (minimumMinor <= maximumMinor)
            {
                return new(true, new(candidateMajor, maximumMinor), BrokerProtocolRejectionReason.None);
            }
        }

        return new(false, null, BrokerProtocolRejectionReason.NoCommonVersion);
    }

    private static int Compare(BrokerProtocolVersion left, BrokerProtocolVersion right)
    {
        int major = left.Major.CompareTo(right.Major);
        return major != 0 ? major : left.Minor.CompareTo(right.Minor);
    }
}

public enum BrokerMessageKind
{
    Hello,
    GetCapabilities,
    GetRuntimeSnapshot,
    PrepareBundle,
    PreparePlan,
    StartPreparedPlan,
    StopGeneration,
    ShutdownBroker,
}

public interface IBrokerRequest
{
}

public interface IBrokerMutationRequest : IBrokerRequest
{
}

public sealed record BrokerHelloRequest(
    BrokerProtocolRange SupportedProtocols,
    AppSessionId AppSessionId,
    ReadOnlyMemory<byte> ClientNonce,
    ReadOnlyMemory<byte> Proof) : IBrokerRequest;

public sealed record GetCapabilitiesRequest : IBrokerRequest
{
}

public sealed record GetRuntimeSnapshotRequest : IBrokerRequest
{
}

public sealed record PrepareBundleRequest(RuntimeBundleId BundleId) : IBrokerMutationRequest;

public sealed record PreparePlanRequest(
    PreparedBundleId PreparedBundleId,
    Sha256Digest PlanHash) : IBrokerMutationRequest;

public sealed record StartPreparedPlanRequest(
    PreparedPlanId PreparedPlanId,
    RuntimeGeneration ExpectedGeneration) : IBrokerMutationRequest;

public enum BrokerStopReason
{
    UserRequested,
    Superseded,
    Recovery,
    ApplicationShutdown,
}

public sealed record StopGenerationRequest(
    RuntimeGeneration Generation,
    BrokerStopReason Reason) : IBrokerMutationRequest;

public sealed record ShutdownBrokerRequest : IBrokerMutationRequest
{
}

public sealed record BrokerRequestEnvelope(
    BrokerProtocolVersion Protocol,
    AppSessionId AppSessionId,
    BrokerSessionId BrokerSessionId,
    BrokerOperationId OperationId,
    RequestSequence Sequence,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset DeadlineUtc,
    IBrokerRequest Request);

public enum BrokerResponseStatus
{
    Ok,
    Accepted,
    DuplicateInFlight,
    DuplicateCompleted,
    Stale,
    Busy,
    Rejected,
    ProtocolError,
    Unauthorized,
}

public interface IBrokerResponse
{
}

public sealed record BrokerCapabilitiesResponse(BrokerCapabilities Capabilities) : IBrokerResponse;

public sealed record BrokerRuntimeSnapshotResponse(BrokerRuntimeSnapshot Snapshot) : IBrokerResponse;

public sealed record BrokerPreparedBundleResponse(PreparedBundleId PreparedBundleId) : IBrokerResponse;

public sealed record BrokerPreparedPlanResponse(PreparedPlanId PreparedPlanId, Sha256Digest PlanHash) : IBrokerResponse;

public sealed record BrokerMutationAcceptedResponse(
    BrokerOperationId OperationId,
    RuntimeGeneration ObservedGeneration) : IBrokerResponse;

public sealed record BrokerShutdownAcceptedResponse : IBrokerResponse
{
}

public sealed record BrokerErrorResponse(string Code, string Message) : IBrokerResponse;

public sealed record BrokerResponseEnvelope(
    BrokerProtocolVersion Protocol,
    AppSessionId AppSessionId,
    BrokerSessionId BrokerSessionId,
    BrokerOperationId OperationId,
    BrokerResponseStatus Status,
    IBrokerResponse Response);

public sealed record BrokerCapabilities(
    BrokerProtocolVersion Protocol,
    IReadOnlyList<BrokerMessageKind> SupportedMessages,
    BrokerLimitsSnapshot Limits);

public sealed record BrokerLimitsSnapshot(
    int MaxFrameBytes,
    int MaxInFlightQueries,
    int MaxConcurrentMutations,
    int IngressQueueCapacity,
    int ResponseQueueCapacity);

public enum BrokerRuntimeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Degraded,
    Faulted,
}

public sealed record BrokerRuntimeSnapshot(
    RuntimeGeneration Generation,
    BrokerRuntimeState State,
    PreparedPlanId? ActivePlanId,
    BrokerOperationId? ActiveOperationId);

public enum BrokerGenerationRejectionReason
{
    None,
    StaleGeneration,
    FutureGeneration,
}

public sealed record BrokerGenerationDecision(
    bool Accepted,
    BrokerGenerationRejectionReason RejectionReason);

public static class BrokerGenerationGuard
{
    public static BrokerGenerationDecision Validate(RuntimeGeneration expected, RuntimeGeneration current)
    {
        if (expected.Value < current.Value)
        {
            return new(false, BrokerGenerationRejectionReason.StaleGeneration);
        }

        if (expected.Value > current.Value)
        {
            return new(false, BrokerGenerationRejectionReason.FutureGeneration);
        }

        return new(true, BrokerGenerationRejectionReason.None);
    }
}

public enum BrokerRequestFreshnessRejectionReason
{
    None,
    InvalidWindow,
    Expired,
    IssuedInFuture,
}

public sealed record BrokerRequestFreshnessDecision(
    bool Accepted,
    BrokerRequestFreshnessRejectionReason RejectionReason);

public static class BrokerRequestFreshnessGuard
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(1);

    public static BrokerRequestFreshnessDecision Validate(
        BrokerRequestEnvelope request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DeadlineUtc <= request.IssuedAtUtc
            || request.DeadlineUtc - request.IssuedAtUtc > BrokerProtocolLimits.MaxRequestLifetime)
        {
            return new(false, BrokerRequestFreshnessRejectionReason.InvalidWindow);
        }

        if (request.IssuedAtUtc > now + AllowedClockSkew)
        {
            return new(false, BrokerRequestFreshnessRejectionReason.IssuedInFuture);
        }

        if (request.DeadlineUtc < now)
        {
            return new(false, BrokerRequestFreshnessRejectionReason.Expired);
        }

        return new(true, BrokerRequestFreshnessRejectionReason.None);
    }
}

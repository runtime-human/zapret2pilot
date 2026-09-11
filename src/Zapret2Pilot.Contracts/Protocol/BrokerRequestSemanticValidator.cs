using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Protocol;

public enum BrokerRequestSemanticRejectionReason
{
    None,
    InvalidRequestWindow,
    InvalidProtocolRange,
    ProtocolNotAdvertised,
    AppSessionMismatch,
    InvalidClientNonce,
    InvalidProof,
    InvalidRuntimeBundleId,
    InvalidPreparedBundleId,
    InvalidPlanHash,
    InvalidPreparedPlanId,
    InvalidRuntimeGeneration,
    InvalidStopReason,
    UnknownRequestType,
}

public sealed record BrokerRequestSemanticValidationResult(
    bool Accepted,
    BrokerRequestSemanticRejectionReason RejectionReason);

public static class BrokerRequestSemanticValidator
{
    public static BrokerRequestSemanticValidationResult Validate(BrokerRequestEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.DeadlineUtc <= envelope.IssuedAtUtc
            || envelope.DeadlineUtc - envelope.IssuedAtUtc > BrokerProtocolLimits.MaxRequestLifetime)
        {
            return Reject(BrokerRequestSemanticRejectionReason.InvalidRequestWindow);
        }

        return envelope.Request switch
        {
            BrokerHelloRequest hello => ValidateHello(envelope, hello),
            GetCapabilitiesRequest => Accept(),
            GetRuntimeSnapshotRequest => Accept(),
            PrepareBundleRequest prepareBundle => prepareBundle.BundleId.Digest.IsCanonical
                ? Accept()
                : Reject(BrokerRequestSemanticRejectionReason.InvalidRuntimeBundleId),
            PreparePlanRequest preparePlan => ValidatePreparePlan(preparePlan),
            StartPreparedPlanRequest start => ValidateStart(start),
            StopGenerationRequest stop => ValidateStop(stop),
            ShutdownBrokerRequest => Accept(),
            _ => Reject(BrokerRequestSemanticRejectionReason.UnknownRequestType),
        };
    }

    private static BrokerRequestSemanticValidationResult ValidateHello(
        BrokerRequestEnvelope envelope,
        BrokerHelloRequest hello)
    {
        if (hello.AppSessionId.Value == Guid.Empty || hello.AppSessionId != envelope.AppSessionId)
        {
            return Reject(BrokerRequestSemanticRejectionReason.AppSessionMismatch);
        }

        BrokerProtocolNegotiationResult negotiation = BrokerProtocolNegotiator.Negotiate(
            hello.SupportedProtocols,
            new BrokerProtocolRange(envelope.Protocol, envelope.Protocol));
        if (negotiation.RejectionReason == BrokerProtocolRejectionReason.InvalidRange)
        {
            return Reject(BrokerRequestSemanticRejectionReason.InvalidProtocolRange);
        }

        if (!negotiation.Accepted || negotiation.SelectedVersion != envelope.Protocol)
        {
            return Reject(BrokerRequestSemanticRejectionReason.ProtocolNotAdvertised);
        }

        if (hello.ClientNonce.Length != BrokerAuthenticator.NonceSizeBytes)
        {
            return Reject(BrokerRequestSemanticRejectionReason.InvalidClientNonce);
        }

        return hello.Proof.Length == BrokerAuthenticator.ProofSizeBytes
            ? Accept()
            : Reject(BrokerRequestSemanticRejectionReason.InvalidProof);
    }

    private static BrokerRequestSemanticValidationResult ValidatePreparePlan(PreparePlanRequest request)
    {
        if (request.PreparedBundleId.Value == Guid.Empty)
        {
            return Reject(BrokerRequestSemanticRejectionReason.InvalidPreparedBundleId);
        }

        return request.PlanHash.IsCanonical
            ? Accept()
            : Reject(BrokerRequestSemanticRejectionReason.InvalidPlanHash);
    }

    private static BrokerRequestSemanticValidationResult ValidateStart(StartPreparedPlanRequest request)
    {
        if (request.PreparedPlanId.Value == Guid.Empty)
        {
            return Reject(BrokerRequestSemanticRejectionReason.InvalidPreparedPlanId);
        }

        return request.ExpectedGeneration.Value > 0
            ? Accept()
            : Reject(BrokerRequestSemanticRejectionReason.InvalidRuntimeGeneration);
    }

    private static BrokerRequestSemanticValidationResult ValidateStop(StopGenerationRequest request)
    {
        if (request.Generation.Value <= 0)
        {
            return Reject(BrokerRequestSemanticRejectionReason.InvalidRuntimeGeneration);
        }

        return Enum.IsDefined(request.Reason)
            ? Accept()
            : Reject(BrokerRequestSemanticRejectionReason.InvalidStopReason);
    }

    private static BrokerRequestSemanticValidationResult Accept()
        => new(true, BrokerRequestSemanticRejectionReason.None);

    private static BrokerRequestSemanticValidationResult Reject(BrokerRequestSemanticRejectionReason reason)
        => new(false, reason);
}

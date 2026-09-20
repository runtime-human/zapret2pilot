using System.Text.Json;
using System.Text.Json.Serialization;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.Contracts.Transport;

public sealed record BrokerResponseDecodeResult(
    BrokerProtocolDecodeStatus Status,
    BrokerResponseEnvelope? Envelope);

/// <summary>
/// Strict, bounded response codec shared by Broker and Control Plane.
/// Response framing stays identical to request framing; only the typed
/// envelope/body mapping differs.
/// </summary>
public static class BrokerResponseProtocolCodec
{
    private const int MaxErrorCodeLength = 128;
    private const int MaxErrorMessageLength = 2_048;

    private static readonly JsonSerializerOptions StrictJson = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    public static byte[] EncodeResponse(BrokerResponseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelope(envelope);

        (string kind, JsonElement body) = SerializeResponse(envelope.Response);
        BrokerResponseWireEnvelope wire = new(
            envelope.Protocol,
            envelope.AppSessionId.Value,
            envelope.BrokerSessionId.Value,
            envelope.OperationId.Value,
            envelope.Status,
            kind,
            body);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(wire, StrictJson);
        if (payload.Length > BrokerProtocolLimits.MaxFrameBytes)
        {
            throw new InvalidOperationException(
                "Encoded broker response exceeds the protocol frame limit.");
        }

        return payload;
    }

    public static BrokerResponseDecodeResult DecodeResponse(
        ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }

        if (payload.Length > BrokerProtocolLimits.MaxFrameBytes)
        {
            return new(BrokerProtocolDecodeStatus.Oversized, null);
        }

        try
        {
            BrokerResponseWireEnvelope? wire =
                JsonSerializer.Deserialize<BrokerResponseWireEnvelope>(
                    payload,
                    StrictJson);
            if (wire is null
                || wire.AppSessionId == Guid.Empty
                || wire.BrokerSessionId == Guid.Empty
                || wire.OperationId == Guid.Empty
                || string.IsNullOrWhiteSpace(wire.Kind)
                || !Enum.IsDefined(wire.Status))
            {
                return new(BrokerProtocolDecodeStatus.Malformed, null);
            }

            if (wire.Protocol != BrokerProtocolVersion.V1)
            {
                return new(BrokerProtocolDecodeStatus.UnsupportedProtocol, null);
            }

            if (!TryDeserializeResponse(
                    wire.Kind,
                    wire.Body,
                    out IBrokerResponse? response))
            {
                return new(BrokerProtocolDecodeStatus.UnknownMessage, null);
            }

            BrokerResponseEnvelope envelope = new(
                wire.Protocol,
                new AppSessionId(wire.AppSessionId),
                new BrokerSessionId(wire.BrokerSessionId),
                new BrokerOperationId(wire.OperationId),
                wire.Status,
                response!);

            try
            {
                ValidateEnvelope(envelope);
            }
            catch (ArgumentException)
            {
                return new(BrokerProtocolDecodeStatus.InvalidSemantics, null);
            }

            return new(BrokerProtocolDecodeStatus.Success, envelope);
        }
        catch (JsonException)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }
        catch (NotSupportedException)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }
    }

    private static (string Kind, JsonElement Body) SerializeResponse(
        IBrokerResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response switch
        {
            BrokerCapabilitiesResponse value =>
                ("capabilities", JsonSerializer.SerializeToElement(value, StrictJson)),
            BrokerRuntimeSnapshotResponse value =>
                ("runtimeSnapshot", JsonSerializer.SerializeToElement(value, StrictJson)),
            BrokerPreparedBundleResponse value =>
                ("preparedBundle", JsonSerializer.SerializeToElement(value, StrictJson)),
            BrokerPreparedPlanResponse value =>
                ("preparedPlan", JsonSerializer.SerializeToElement(value, StrictJson)),
            BrokerMutationAcceptedResponse value =>
                ("mutationAccepted", JsonSerializer.SerializeToElement(value, StrictJson)),
            BrokerShutdownAcceptedResponse value =>
                ("shutdownAccepted", JsonSerializer.SerializeToElement(value, StrictJson)),
            BrokerErrorResponse value =>
                ("error", JsonSerializer.SerializeToElement(value, StrictJson)),
            _ => throw new NotSupportedException(
                $"Unsupported broker response type: {response.GetType().FullName}"),
        };
    }

    private static bool TryDeserializeResponse(
        string kind,
        JsonElement body,
        out IBrokerResponse? response)
    {
        response = kind switch
        {
            "capabilities" =>
                body.Deserialize<BrokerCapabilitiesResponse>(StrictJson),
            "runtimeSnapshot" =>
                body.Deserialize<BrokerRuntimeSnapshotResponse>(StrictJson),
            "preparedBundle" =>
                body.Deserialize<BrokerPreparedBundleResponse>(StrictJson),
            "preparedPlan" =>
                body.Deserialize<BrokerPreparedPlanResponse>(StrictJson),
            "mutationAccepted" =>
                body.Deserialize<BrokerMutationAcceptedResponse>(StrictJson),
            "shutdownAccepted" =>
                body.Deserialize<BrokerShutdownAcceptedResponse>(StrictJson),
            "error" =>
                body.Deserialize<BrokerErrorResponse>(StrictJson),
            _ => null,
        };

        return response is not null;
    }

    private static void ValidateEnvelope(BrokerResponseEnvelope envelope)
    {
        if (envelope.Protocol != BrokerProtocolVersion.V1)
        {
            throw new ArgumentException(
                "Broker response protocol must be v1.",
                nameof(envelope));
        }

        if (envelope.AppSessionId.Value == Guid.Empty
            || envelope.BrokerSessionId.Value == Guid.Empty
            || envelope.OperationId.Value == Guid.Empty
            || !Enum.IsDefined(envelope.Status))
        {
            throw new ArgumentException(
                "Broker response envelope identity/status is invalid.",
                nameof(envelope));
        }

        switch (envelope.Response)
        {
            case BrokerCapabilitiesResponse capabilities:
                if (capabilities.Capabilities.Protocol != BrokerProtocolVersion.V1
                    || capabilities.Capabilities.SupportedMessages is null
                    || capabilities.Capabilities.SupportedMessages.Count == 0
                    || capabilities.Capabilities.SupportedMessages.Any(
                        static kind => !Enum.IsDefined(kind))
                    || capabilities.Capabilities.Limits.MaxFrameBytes <= 0
                    || capabilities.Capabilities.Limits.MaxInFlightQueries <= 0
                    || capabilities.Capabilities.Limits.MaxConcurrentMutations <= 0
                    || capabilities.Capabilities.Limits.IngressQueueCapacity <= 0
                    || capabilities.Capabilities.Limits.ResponseQueueCapacity <= 0)
                {
                    throw new ArgumentException(
                        "Broker capabilities response is invalid.",
                        nameof(envelope));
                }

                break;

            case BrokerRuntimeSnapshotResponse snapshot:
                if (snapshot.Snapshot.Generation.Value < 0
                    || !Enum.IsDefined(snapshot.Snapshot.State))
                {
                    throw new ArgumentException(
                        "Broker runtime snapshot is invalid.",
                        nameof(envelope));
                }

                break;

            case BrokerPreparedBundleResponse preparedBundle:
                if (preparedBundle.PreparedBundleId.Value == Guid.Empty)
                {
                    throw new ArgumentException(
                        "Prepared bundle id must not be empty.",
                        nameof(envelope));
                }

                break;

            case BrokerPreparedPlanResponse preparedPlan:
                if (preparedPlan.PreparedPlanId.Value == Guid.Empty
                    || !preparedPlan.PlanHash.IsCanonical)
                {
                    throw new ArgumentException(
                        "Prepared plan response is invalid.",
                        nameof(envelope));
                }

                break;

            case BrokerMutationAcceptedResponse mutation:
                if (mutation.OperationId.Value == Guid.Empty
                    || mutation.OperationId != envelope.OperationId
                    || mutation.ObservedGeneration.Value < 0)
                {
                    throw new ArgumentException(
                        "Mutation accepted response is invalid.",
                        nameof(envelope));
                }

                break;

            case BrokerShutdownAcceptedResponse:
                break;

            case BrokerErrorResponse error:
                if (string.IsNullOrWhiteSpace(error.Code)
                    || error.Code.Length > MaxErrorCodeLength
                    || string.IsNullOrWhiteSpace(error.Message)
                    || error.Message.Length > MaxErrorMessageLength)
                {
                    throw new ArgumentException(
                        "Broker error response is invalid.",
                        nameof(envelope));
                }

                break;

            default:
                throw new ArgumentException(
                    "Unknown broker response type.",
                    nameof(envelope));
        }
    }

    private sealed record BrokerResponseWireEnvelope(
        BrokerProtocolVersion Protocol,
        Guid AppSessionId,
        Guid BrokerSessionId,
        Guid OperationId,
        BrokerResponseStatus Status,
        string Kind,
        JsonElement Body);
}

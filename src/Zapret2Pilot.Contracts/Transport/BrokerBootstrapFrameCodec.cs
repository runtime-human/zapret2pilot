using System.Text.Json;
using System.Text.Json.Serialization;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;

namespace Zapret2Pilot.Contracts.Transport;

public sealed record BrokerBootstrapMessage(
    BrokerProtocolVersion Protocol,
    AppSessionId AppSessionId,
    string RuntimePipeName,
    BrokerClientBinding ClientBinding,
    byte[] BootstrapSecret);

public enum BrokerBootstrapFrameDecodeStatus
{
    Success,
    NeedMoreData,
    InvalidLength,
    Oversized,
    Malformed,
    UnsupportedProtocol,
    InvalidSemantics,
}

public sealed record BrokerBootstrapFrameDecodeResult(
    BrokerBootstrapFrameDecodeStatus Status,
    BrokerBootstrapMessage? Bootstrap,
    int ConsumedBytes);

/// <summary>
/// Strict one-shot bootstrap frame used only to transfer AppSession material
/// after both endpoints have verified the actual peer process through the
/// bootstrap pipe. The pipe name/PID may be carried on the elevation command
/// line; the 256-bit secret never is.
/// </summary>
public static class BrokerBootstrapFrameCodec
{
    private const int MaximumPipeNameLength = 128;
    private const int MaximumSidLength = 256;
    private static readonly JsonSerializerOptions StrictJson = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    public static byte[] Encode(BrokerBootstrapMessage bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        BrokerBootstrapFrameDecodeStatus validation = Validate(bootstrap);
        if (validation != BrokerBootstrapFrameDecodeStatus.Success)
        {
            throw new ArgumentException(
                $"Broker bootstrap violates wire contract: {validation}.",
                nameof(bootstrap));
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            bootstrap,
            StrictJson);
        try
        {
            return BrokerFrameCodec.Encode(
                payload,
                BrokerProtocolLimits.MaxBootstrapFrameBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static BrokerBootstrapFrameDecodeResult Decode(
        ReadOnlySpan<byte> input)
    {
        BrokerFrameDecodeResult frame = BrokerFrameCodec.Decode(
            input,
            BrokerProtocolLimits.MaxBootstrapFrameBytes);

        if (frame.Status != BrokerFrameDecodeStatus.Success)
        {
            return new(
                MapFrameStatus(frame.Status),
                null,
                frame.ConsumedBytes);
        }

        try
        {
            BrokerBootstrapMessage? bootstrap =
                JsonSerializer.Deserialize<BrokerBootstrapMessage>(
                    frame.Payload,
                    StrictJson);

            if (bootstrap is null)
            {
                return new(
                    BrokerBootstrapFrameDecodeStatus.Malformed,
                    null,
                    frame.ConsumedBytes);
            }

            BrokerBootstrapFrameDecodeStatus validation =
                Validate(bootstrap);

            return validation == BrokerBootstrapFrameDecodeStatus.Success
                ? new(validation, bootstrap, frame.ConsumedBytes)
                : new(validation, null, frame.ConsumedBytes);
        }
        catch (JsonException)
        {
            return new(
                BrokerBootstrapFrameDecodeStatus.Malformed,
                null,
                frame.ConsumedBytes);
        }
        catch (NotSupportedException)
        {
            return new(
                BrokerBootstrapFrameDecodeStatus.Malformed,
                null,
                frame.ConsumedBytes);
        }
        finally
        {
            if (frame.Payload is not null)
            {
                CryptographicOperations.ZeroMemory(frame.Payload);
            }
        }
    }

    private static BrokerBootstrapFrameDecodeStatus Validate(
        BrokerBootstrapMessage bootstrap)
    {
        if (bootstrap.Protocol != BrokerProtocolVersion.V1)
        {
            return BrokerBootstrapFrameDecodeStatus.UnsupportedProtocol;
        }

        if (bootstrap.AppSessionId.Value == Guid.Empty
            || bootstrap.ClientBinding is null
            || bootstrap.ClientBinding.AppSessionId != bootstrap.AppSessionId
            || bootstrap.ClientBinding.ProcessId <= 0
            || bootstrap.ClientBinding.ProcessCreationTimeFileTime <= 0
            || bootstrap.ClientBinding.IntegrityLevelRid <= 0
            || string.IsNullOrWhiteSpace(bootstrap.ClientBinding.UserSid)
            || bootstrap.ClientBinding.UserSid.Length > MaximumSidLength
            || !bootstrap.ClientBinding.UserSid.StartsWith(
                "S-",
                StringComparison.Ordinal)
            || bootstrap.BootstrapSecret is null
            || bootstrap.BootstrapSecret.Length != BrokerAuthenticator.SecretSizeBytes
            || !IsValidPipeName(bootstrap.RuntimePipeName))
        {
            return BrokerBootstrapFrameDecodeStatus.InvalidSemantics;
        }

        return BrokerBootstrapFrameDecodeStatus.Success;
    }

    private static bool IsValidPipeName(string? pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName)
            || pipeName.Length > MaximumPipeNameLength)
        {
            return false;
        }

        foreach (char character in pipeName)
        {
            if (!(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static BrokerBootstrapFrameDecodeStatus MapFrameStatus(
        BrokerFrameDecodeStatus status)
        => status switch
        {
            BrokerFrameDecodeStatus.NeedMoreData =>
                BrokerBootstrapFrameDecodeStatus.NeedMoreData,
            BrokerFrameDecodeStatus.InvalidLength =>
                BrokerBootstrapFrameDecodeStatus.InvalidLength,
            BrokerFrameDecodeStatus.Oversized =>
                BrokerBootstrapFrameDecodeStatus.Oversized,
            _ => BrokerBootstrapFrameDecodeStatus.Malformed,
        };
}

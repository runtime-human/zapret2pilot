using System.Security.Cryptography;
using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerBootstrapFrameCodecTests
{
    [Fact]
    public static void ValidBootstrapRoundTripsThroughStrictBoundedCodec()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(
            BrokerAuthenticator.SecretSizeBytes);
        BrokerBootstrapMessage bootstrap = CreateBootstrap(secret);

        byte[] frame = BrokerBootstrapFrameCodec.Encode(bootstrap);
        BrokerBootstrapFrameDecodeResult decoded =
            BrokerBootstrapFrameCodec.Decode(frame);

        Assert.Equal(
            BrokerBootstrapFrameDecodeStatus.Success,
            decoded.Status);
        Assert.NotNull(decoded.Bootstrap);
        Assert.Equal(
            bootstrap.AppSessionId,
            decoded.Bootstrap!.AppSessionId);
        Assert.Equal(
            bootstrap.RuntimePipeName,
            decoded.Bootstrap.RuntimePipeName);
        Assert.Equal(
            bootstrap.ClientBinding,
            decoded.Bootstrap.ClientBinding);
        Assert.Equal(
            secret,
            decoded.Bootstrap.BootstrapSecret);
    }

    [Fact]
    public static void UnknownJsonMemberFailsClosed()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(
            BrokerAuthenticator.SecretSizeBytes);
        byte[] frame = BrokerBootstrapFrameCodec.Encode(
            CreateBootstrap(secret));

        BrokerFrameDecodeResult decodedFrame = BrokerFrameCodec.Decode(
            frame,
            BrokerProtocolLimits.MaxBootstrapFrameBytes);
        Assert.Equal(BrokerFrameDecodeStatus.Success, decodedFrame.Status);
        Assert.NotNull(decodedFrame.Payload);

        string json = System.Text.Encoding.UTF8.GetString(
            decodedFrame.Payload!);
        string hostile = json.Replace(
            "\"protocol\"",
            "\"unexpected\":true,\"protocol\"",
            StringComparison.Ordinal);

        byte[] hostileFrame = BrokerFrameCodec.Encode(
            System.Text.Encoding.UTF8.GetBytes(hostile),
            BrokerProtocolLimits.MaxBootstrapFrameBytes);

        BrokerBootstrapFrameDecodeResult result =
            BrokerBootstrapFrameCodec.Decode(hostileFrame);

        Assert.Equal(
            BrokerBootstrapFrameDecodeStatus.Malformed,
            result.Status);
        Assert.Null(result.Bootstrap);
    }

    [Fact]
    public static void BootstrapSecretMustBeExactly256Bits()
    {
        BrokerBootstrapMessage invalid = CreateBootstrap(
            new byte[BrokerAuthenticator.SecretSizeBytes - 1]);

        Assert.Throws<ArgumentException>(
            () => BrokerBootstrapFrameCodec.Encode(invalid));
    }

    [Theory]
    [InlineData("bad\\pipe")]
    [InlineData("bad/pipe")]
    [InlineData("bad:pipe")]
    [InlineData("bad pipe")]
    public static void RuntimePipeNameIsRestrictedToLocalOpaqueName(
        string pipeName)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(
            BrokerAuthenticator.SecretSizeBytes);
        BrokerBootstrapMessage invalid =
            CreateBootstrap(secret) with
            {
                RuntimePipeName = pipeName,
            };

        Assert.Throws<ArgumentException>(
            () => BrokerBootstrapFrameCodec.Encode(invalid));
    }

    [Fact]
    public static void AppSessionMustMatchBinding()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(
            BrokerAuthenticator.SecretSizeBytes);
        BrokerBootstrapMessage invalid =
            CreateBootstrap(secret) with
            {
                AppSessionId = AppSessionId.New(),
            };

        Assert.Throws<ArgumentException>(
            () => BrokerBootstrapFrameCodec.Encode(invalid));
    }

    private static BrokerBootstrapMessage CreateBootstrap(byte[] secret)
    {
        AppSessionId appSessionId = AppSessionId.New();
        BrokerClientBinding binding = new(
            appSessionId,
            ProcessId: 1234,
            ProcessCreationTimeFileTime: 123456789,
            WindowsSessionId: 2,
            UserSid: "S-1-5-21-1000",
            LogonSessionId: new LogonSessionId(7, 8),
            IntegrityLevelRid: 0x2000);

        return new(
            BrokerProtocolVersion.V1,
            appSessionId,
            $"z2p-runtime-{Guid.NewGuid():N}",
            binding,
            secret);
    }
}

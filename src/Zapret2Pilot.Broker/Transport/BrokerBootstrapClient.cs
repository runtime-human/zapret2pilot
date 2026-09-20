using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Broker.Transport;

/// <summary>
/// Elevated Broker side of the one-shot bootstrap channel.
///
/// The only command-line authority is the opaque bootstrap pipe name plus the
/// expected unelevated App PID. The actual pipe server PID/token identity is
/// obtained from Windows and must match the transferred ClientBinding before
/// the HMAC secret becomes Broker session authority.
/// </summary>
public sealed class BrokerBootstrapClient
{
    private readonly IBrokerPeerIdentityResolver peerIdentityResolver;

    public BrokerBootstrapClient(
        IBrokerPeerIdentityResolver peerIdentityResolver)
    {
        ArgumentNullException.ThrowIfNull(peerIdentityResolver);
        this.peerIdentityResolver = peerIdentityResolver;
    }

    public async Task<BrokerStartupContext> ReceiveAsync(
        BrokerBootstrapLaunchOptions launchOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);

        await using NamedPipeClientStream pipe = new(
            ".",
            launchOptions.BootstrapPipeName,
            PipeDirection.In,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(
            checked((int)BrokerProtocolLimits.HandshakeTimeout.TotalMilliseconds),
            cancellationToken).ConfigureAwait(false);

        BrokerResolvedPeer appPeer =
            peerIdentityResolver.ResolveServer(pipe.SafePipeHandle);
        bool leaseTransferred = false;

        try
        {
            if (appPeer.Identity.ProcessId
                != launchOptions.AppProcessId)
            {
                throw new UnauthorizedAccessException(
                    "Bootstrap pipe server PID does not match the launched App PID.");
            }

            byte[]? payload = await BrokerPipeFrameIO.ReadPayloadAsync(
                pipe,
                BrokerProtocolLimits.MaxBootstrapFrameBytes,
                cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                throw new EndOfStreamException(
                    "Control Plane disconnected before sending bootstrap material.");
            }

            byte[] frame = BrokerFrameCodec.Encode(
                payload,
                BrokerProtocolLimits.MaxBootstrapFrameBytes);

            BrokerBootstrapFrameDecodeResult decoded;
            try
            {
                decoded = BrokerBootstrapFrameCodec.Decode(frame);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
                CryptographicOperations.ZeroMemory(frame);
            }

            if (decoded.Status
                    != BrokerBootstrapFrameDecodeStatus.Success
                || decoded.Bootstrap is null)
            {
                throw new InvalidDataException(
                    $"Broker bootstrap frame was rejected: {decoded.Status}.");
            }

            BrokerBootstrapMessage bootstrap = decoded.Bootstrap;
            if (!Matches(
                    bootstrap.ClientBinding,
                    appPeer.Identity)
                || bootstrap.ClientBinding.ProcessId
                    != launchOptions.AppProcessId)
            {
                CryptographicOperations.ZeroMemory(
                    bootstrap.BootstrapSecret);
                throw new UnauthorizedAccessException(
                    "Transferred App identity does not match the actual bootstrap pipe server process.");
            }

            BrokerSessionBootstrap sessionBootstrap = new(
                bootstrap.ClientBinding,
                bootstrap.RuntimePipeName,
                bootstrap.BootstrapSecret);

            BrokerStartupContext context = new(
                sessionBootstrap,
                appPeer.ProcessLease);
            leaseTransferred = true;
            return context;
        }
        finally
        {
            if (!leaseTransferred)
            {
                appPeer.ProcessLease.Dispose();
            }
        }
    }

    private static bool Matches(
        BrokerClientBinding expected,
        BrokerPeerIdentity actual)
        => expected.ProcessId == actual.ProcessId
            && expected.ProcessCreationTimeFileTime
                == actual.ProcessCreationTimeFileTime
            && expected.WindowsSessionId
                == actual.WindowsSessionId
            && string.Equals(
                expected.UserSid,
                actual.UserSid,
                StringComparison.Ordinal)
            && expected.LogonSessionId
                == actual.LogonSessionId
            && expected.IntegrityLevelRid
                == actual.IntegrityLevelRid;
}

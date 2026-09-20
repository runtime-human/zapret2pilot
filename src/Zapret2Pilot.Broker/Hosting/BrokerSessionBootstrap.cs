using System;
using System.Security.Cryptography;
using System.Threading;
using Zapret2Pilot.Contracts.Security;

namespace Zapret2Pilot.Broker.Hosting;

/// <summary>
/// In-memory bootstrap material for one session-scoped Runtime Broker.
///
/// The bootstrap secret is transferred by ownership exactly once to
/// <see cref="Transport.BrokerAuthenticatedSession"/>. It is never read from
/// ordinary CLI arguments, environment variables, or appsettings.
/// </summary>
public sealed class BrokerSessionBootstrap : IDisposable
{
    private byte[]? bootstrapSecret;
    private int disposed;

    public BrokerSessionBootstrap(
        BrokerClientBinding clientBinding,
        string pipeName,
        byte[] bootstrapSecret)
    {
        ArgumentNullException.ThrowIfNull(clientBinding);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(bootstrapSecret);

        if (bootstrapSecret.Length != BrokerAuthenticator.SecretSizeBytes)
        {
            throw new ArgumentException(
                "Bootstrap secret must contain exactly 256 bits.",
                nameof(bootstrapSecret));
        }

        ClientBinding = clientBinding;
        PipeName = pipeName;
        this.bootstrapSecret = bootstrapSecret;
    }

    public BrokerClientBinding ClientBinding { get; }

    public string PipeName { get; }

    internal byte[] TakeBootstrapSecret()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        return Interlocked.Exchange(ref bootstrapSecret, null)
            ?? throw new InvalidOperationException(
                "The Broker bootstrap secret has already been transferred.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        byte[]? secret = Interlocked.Exchange(ref bootstrapSecret, null);
        if (secret is not null)
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}

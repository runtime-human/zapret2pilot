using System;
using System.Threading;
using Zapret2Pilot.Broker.Runtime;

namespace Zapret2Pilot.Broker.Hosting;

/// <summary>
/// Owns the verified one-shot bootstrap result until host composition transfers
/// the App process lease and HMAC secret into their long-lived owners.
/// </summary>
public sealed class BrokerStartupContext : IDisposable
{
    private IBrokerAppSessionLease? appProcessLease;
    private int disposed;

    public BrokerStartupContext(
        BrokerSessionBootstrap sessionBootstrap,
        IBrokerAppSessionLease appProcessLease)
    {
        ArgumentNullException.ThrowIfNull(sessionBootstrap);
        ArgumentNullException.ThrowIfNull(appProcessLease);

        SessionBootstrap = sessionBootstrap;
        this.appProcessLease = appProcessLease;
    }

    public BrokerSessionBootstrap SessionBootstrap { get; }

    internal IBrokerAppSessionLease TakeAppProcessLease()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        return Interlocked.Exchange(ref appProcessLease, null)
            ?? throw new InvalidOperationException(
                "The verified App process lease has already been transferred.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        appProcessLease?.Dispose();
        appProcessLease = null;
        SessionBootstrap.Dispose();
    }
}

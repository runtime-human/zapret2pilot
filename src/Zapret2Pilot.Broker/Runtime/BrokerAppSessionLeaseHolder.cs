using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zapret2Pilot.Broker.Runtime;

public interface IBrokerAppSessionLeaseBinder
{
    bool TryBind(IBrokerAppSessionLease lease);
}

/// <summary>
/// Binds exactly one verified Control Plane process lease to the Broker
/// AppSession. Reconnects may authenticate against the same process identity,
/// but they cannot replace the lifetime owner with another process.
/// </summary>
public sealed class BrokerAppSessionLeaseHolder :
    IBrokerAppSessionLease,
    IBrokerAppSessionLeaseBinder
{
    private readonly TaskCompletionSource<IBrokerAppSessionLease> boundLease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposed;

    public bool TryBind(IBrokerAppSessionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        if (boundLease.TrySetResult(lease))
        {
            return true;
        }

        lease.Dispose();
        return false;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        IBrokerAppSessionLease lease = await boundLease.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        await lease.WaitForExitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (boundLease.Task.IsCompletedSuccessfully)
        {
            boundLease.Task.Result.Dispose();
        }
        else
        {
            boundLease.TrySetCanceled();
        }
    }
}

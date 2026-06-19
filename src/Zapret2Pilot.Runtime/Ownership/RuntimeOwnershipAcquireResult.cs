using System;

namespace Zapret2Pilot.Runtime.Ownership;

public sealed record class RuntimeOwnershipAcquireResult
{
    private RuntimeOwnershipAcquireResult(
        bool acquired,
        bool wasAbandoned,
        RuntimeOwnershipLease? lease)
    {
        if (acquired && lease is null)
        {
            throw new ArgumentException("Acquired ownership result must contain a lease.", nameof(lease));
        }

        if (!acquired && lease is not null)
        {
            throw new ArgumentException("Not-acquired ownership result must not contain a lease.", nameof(lease));
        }

        if (!acquired && wasAbandoned)
        {
            throw new ArgumentException("Not-acquired ownership result cannot be abandoned.", nameof(wasAbandoned));
        }

        Acquired = acquired;
        WasAbandoned = wasAbandoned;
        Lease = lease;
    }

    public bool Acquired { get; }

    public bool WasAbandoned { get; }

    public RuntimeOwnershipLease? Lease { get; }

    public static RuntimeOwnershipAcquireResult NotAcquired()
    {
        return new RuntimeOwnershipAcquireResult(
            acquired: false,
            wasAbandoned: false,
            lease: null);
    }

    public static RuntimeOwnershipAcquireResult CreateAcquired(
        RuntimeOwnershipLease lease,
        bool wasAbandoned)
    {
        ArgumentNullException.ThrowIfNull(lease);

        return new RuntimeOwnershipAcquireResult(
            acquired: true,
            wasAbandoned: wasAbandoned,
            lease: lease);
    }
}

using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerMonotonicTimingTests
{
    [Fact]
    public void RequestRateGateRefillsFromMonotonicElapsedTimeNotUtcJumps()
    {
        ManualTimeProvider timeProvider = new();
        BrokerRequestRateGate gate = new(
            BrokerProtocolLimits.MaxRequestsPerSecond,
            BrokerProtocolLimits.RequestBurstCapacity,
            timeProvider);

        for (int i = 0; i < BrokerProtocolLimits.RequestBurstCapacity; i++)
        {
            Assert.True(gate.TryAcquire());
        }

        Assert.False(gate.TryAcquire());
        timeProvider.JumpUtc(TimeSpan.FromDays(30));
        Assert.False(gate.TryAcquire());
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        for (int i = 0; i < BrokerProtocolLimits.MaxRequestsPerSecond; i++)
        {
            Assert.True(gate.TryAcquire());
        }

        Assert.False(gate.TryAcquire());
    }

    [Fact]
    public void OperationLedgerExpiryUsesMonotonicElapsedTimeNotUtcJumps()
    {
        ManualTimeProvider timeProvider = new();
        BrokerOperationLedger ledger = new(
            capacity: 1,
            ttl: BrokerProtocolLimits.OperationLedgerTtl,
            timeProvider);
        BrokerOperationId firstId = BrokerOperationId.New();
        Sha256Digest firstFingerprint = Sha256Digest.Compute("first"u8);

        Assert.Equal(
            BrokerOperationRegistration.New,
            ledger.Register(firstId, new RequestSequence(1), firstFingerprint));
        timeProvider.JumpUtc(TimeSpan.FromDays(30));
        Assert.Equal(
            BrokerOperationRegistration.CapacityExceeded,
            ledger.Register(BrokerOperationId.New(), new RequestSequence(2), Sha256Digest.Compute("second"u8)));

        timeProvider.Advance(BrokerProtocolLimits.OperationLedgerTtl + TimeSpan.FromTicks(1));
        Assert.Equal(
            BrokerOperationRegistration.New,
            ledger.Register(BrokerOperationId.New(), new RequestSequence(2), Sha256Digest.Compute("second"u8)));
    }
}

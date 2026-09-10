using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerRateAndFakeHarnessTests
{
    [Fact]
    public void RequestRateGateBoundsBurstAndSustainedRefill()
    {
        BrokerRequestRateGate gate = new(
            BrokerProtocolLimits.MaxRequestsPerSecond,
            BrokerProtocolLimits.RequestBurstCapacity);
        DateTimeOffset now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < BrokerProtocolLimits.RequestBurstCapacity; i++)
        {
            Assert.True(gate.TryAcquire(now));
        }

        Assert.False(gate.TryAcquire(now));

        DateTimeOffset oneSecondLater = now + TimeSpan.FromSeconds(1);
        for (int i = 0; i < BrokerProtocolLimits.MaxRequestsPerSecond; i++)
        {
            Assert.True(gate.TryAcquire(oneSecondLater));
        }

        Assert.False(gate.TryAcquire(oneSecondLater));
        Assert.False(gate.TryAcquire(now));
    }

    [Fact]
    public void DisconnectAndReconnectDoNotCreateSecondMutationAuthority()
    {
        FakeBrokerHarness broker = new(new RuntimeGeneration(7));
        BrokerOperationId operationId = BrokerOperationId.New();
        PreparedPlanId planId = PreparedPlanId.New();
        Sha256Digest fingerprint = Sha256Digest.Compute("start-prepared-plan"u8);
        DateTimeOffset now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

        BrokerOperationRegistration first = broker.BeginStart(
            operationId,
            new RequestSequence(1),
            fingerprint,
            planId,
            new RuntimeGeneration(7),
            now);

        broker.Disconnect();
        broker.Reconnect();

        BrokerOperationRegistration replayWhileInFlight = broker.BeginStart(
            operationId,
            new RequestSequence(1),
            fingerprint,
            planId,
            new RuntimeGeneration(7),
            now);

        Assert.Equal(BrokerOperationRegistration.New, first);
        Assert.Equal(BrokerOperationRegistration.DuplicateInFlight, replayWhileInFlight);
        Assert.Equal(1, broker.RuntimeDispatchCount);

        broker.Complete(operationId);

        BrokerOperationRegistration replayAfterCompletion = broker.BeginStart(
            operationId,
            new RequestSequence(1),
            fingerprint,
            planId,
            new RuntimeGeneration(7),
            now);

        Assert.Equal(BrokerOperationRegistration.DuplicateCompleted, replayAfterCompletion);
        Assert.Equal(1, broker.RuntimeDispatchCount);
        Assert.Equal(planId, broker.LastDispatchedPlanId);
    }

    private sealed class FakeBrokerHarness
    {
        private readonly BrokerOperationLedger ledger = new(
            BrokerProtocolLimits.OperationLedgerCapacity,
            BrokerProtocolLimits.OperationLedgerTtl);
        private readonly BrokerConcurrencyGate concurrency = new(
            BrokerProtocolLimits.MaxInFlightQueries,
            BrokerProtocolLimits.MaxConcurrentMutations);
        private readonly RuntimeGeneration generation;
        private BrokerConcurrencyLease? mutationLease;

        public FakeBrokerHarness(RuntimeGeneration generation)
        {
            this.generation = generation;
        }

        public int RuntimeDispatchCount { get; private set; }
        public PreparedPlanId? LastDispatchedPlanId { get; private set; }
        public bool Connected { get; private set; } = true;

        public BrokerOperationRegistration BeginStart(
            BrokerOperationId operationId,
            RequestSequence sequence,
            Sha256Digest fingerprint,
            PreparedPlanId planId,
            RuntimeGeneration expectedGeneration,
            DateTimeOffset now)
        {
            BrokerOperationRegistration registration = ledger.Register(
                operationId,
                sequence,
                fingerprint,
                now);
            if (registration != BrokerOperationRegistration.New)
            {
                return registration;
            }

            BrokerGenerationDecision generationDecision = BrokerGenerationGuard.Validate(
                expectedGeneration,
                generation);
            Assert.True(generationDecision.Accepted);

            mutationLease = Assert.IsType<BrokerConcurrencyLease>(concurrency.TryAcquireMutation());
            RuntimeDispatchCount++;
            LastDispatchedPlanId = planId;
            return registration;
        }

        public void Complete(BrokerOperationId operationId)
        {
            Assert.True(ledger.Complete(operationId));
            mutationLease?.Dispose();
            mutationLease = null;
        }

        public void Disconnect()
        {
            Connected = false;
        }

        public void Reconnect()
        {
            Connected = true;
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Supervisor;
using ContractGeneration = Zapret2Pilot.Contracts.Identity.RuntimeGeneration;
using KernelGeneration = Zapret2Pilot.Runtime.Kernel.RuntimeGeneration;

namespace Zapret2Pilot.Broker.Runtime;

/// <summary>
/// Read-only projection of the existing Runtime Kernel authority.
/// Implementations must not cache or independently mutate lifecycle state.
/// </summary>
public interface IBrokerRuntimeStateProjection
{
    RuntimeKernelState CurrentState { get; }
}

/// <summary>
/// Production projection over the single broker-side RuntimeKernelLoop.
/// </summary>
public sealed class RuntimeKernelStateProjection : IBrokerRuntimeStateProjection
{
    private readonly RuntimeKernelLoop loop;

    public RuntimeKernelStateProjection(RuntimeKernelLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
    }

    public RuntimeKernelState CurrentState => loop.CurrentState;
}

/// <summary>
/// Resolves an opaque PreparedPlanId to the already-validated broker-side
/// start context. #19 supplies the staging implementation; the IPC contract
/// never carries a path, argv, or executable authority.
/// </summary>
public interface IPreparedRuntimePlanResolver
{
    bool TryResolve(
        PreparedPlanId preparedPlanId,
        out RuntimeProcessStartContext? startContext);
}

/// <summary>
/// Fail-closed placeholder until #19 owns bundle/plan staging.
/// </summary>
public sealed class RejectingPreparedRuntimePlanResolver : IPreparedRuntimePlanResolver
{
    public bool TryResolve(
        PreparedPlanId preparedPlanId,
        out RuntimeProcessStartContext? startContext)
    {
        startContext = null;
        return false;
    }
}

/// <summary>
/// Admits authenticated, semantically validated broker requests and maps
/// them onto the existing RuntimeSupervisor/RuntimeKernelLoop authority.
/// The dispatcher owns replay/admission correlation only; it is not a
/// lifecycle state machine and never increments RuntimeGeneration itself.
/// </summary>
public sealed class BrokerRuntimeDispatcher
{
    private readonly IRuntimeSupervisor supervisor;
    private readonly IBrokerRuntimeStateProjection projection;
    private readonly IPreparedRuntimePlanResolver preparedPlans;
    private readonly BrokerOperationLedger operationLedger;
    private readonly BrokerConcurrencyGate concurrencyGate;
    private readonly IBrokerLifetimeController lifetimeController;
    private readonly object correlationSync = new();

    private PreparedPlanId? activePlanId;
    private BrokerOperationId? activeOperationId;

    public BrokerRuntimeDispatcher(
        IRuntimeSupervisor supervisor,
        IBrokerRuntimeStateProjection projection,
        IPreparedRuntimePlanResolver preparedPlans,
        BrokerOperationLedger operationLedger,
        BrokerConcurrencyGate concurrencyGate,
        IBrokerLifetimeController lifetimeController)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(preparedPlans);
        ArgumentNullException.ThrowIfNull(operationLedger);
        ArgumentNullException.ThrowIfNull(concurrencyGate);
        ArgumentNullException.ThrowIfNull(lifetimeController);

        this.supervisor = supervisor;
        this.projection = projection;
        this.preparedPlans = preparedPlans;
        this.operationLedger = operationLedger;
        this.concurrencyGate = concurrencyGate;
        this.lifetimeController = lifetimeController;
    }

    public async Task<BrokerResponseEnvelope> DispatchAsync(
        BrokerRequestEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Sha256Digest fingerprint = Sha256Digest.Compute(
            BrokerProtocolCodec.EncodeRequest(request));

        BrokerOperationRegistration registration = operationLedger.Register(
            request.OperationId,
            request.Sequence,
            fingerprint);

        if (registration != BrokerOperationRegistration.New)
        {
            return CreateReplayResponse(request, registration);
        }

        bool mutation = request.Request is IBrokerMutationRequest;
        BrokerConcurrencyLease? lease = mutation
            ? concurrencyGate.TryAcquireMutation()
            : concurrencyGate.TryAcquireQuery();

        if (lease is null)
        {
            _ = operationLedger.Complete(request.OperationId);
            return CreateErrorResponse(
                request,
                BrokerResponseStatus.Busy,
                "BrokerConcurrencyLimit",
                mutation
                    ? "Another broker mutation is already in flight."
                    : "The broker query concurrency limit is reached.");
        }

        using (lease)
        {
            try
            {
                return await DispatchAdmittedAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _ = operationLedger.Complete(request.OperationId);
            }
        }
    }

    private async Task<BrokerResponseEnvelope> DispatchAdmittedAsync(
        BrokerRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        return request.Request switch
        {
            GetCapabilitiesRequest => CreateCapabilitiesResponse(request),
            GetRuntimeSnapshotRequest => CreateSnapshotResponse(request),
            StartPreparedPlanRequest start => await DispatchStartAsync(
                request,
                start,
                cancellationToken).ConfigureAwait(false),
            StopGenerationRequest stop => await DispatchStopAsync(
                request,
                stop,
                cancellationToken).ConfigureAwait(false),
            PrepareBundleRequest or PreparePlanRequest => CreateErrorResponse(
                request,
                BrokerResponseStatus.Rejected,
                "BrokerStagingUnavailable",
                "Bundle and prepared-plan realization is gated on #19."),
            ShutdownBrokerRequest => await DispatchShutdownAsync(
                request,
                cancellationToken).ConfigureAwait(false),
            BrokerHelloRequest => CreateErrorResponse(
                request,
                BrokerResponseStatus.ProtocolError,
                "BrokerHelloAlreadyCompleted",
                "Hello is a pre-dispatch authentication message and cannot be dispatched as an authenticated request."),
            _ => CreateErrorResponse(
                request,
                BrokerResponseStatus.ProtocolError,
                "UnsupportedBrokerRequest",
                "The broker request kind is not supported by this dispatcher."),
        };
    }

    private async Task<BrokerResponseEnvelope> DispatchStartAsync(
        BrokerRequestEnvelope envelope,
        StartPreparedPlanRequest request,
        CancellationToken cancellationToken)
    {
        BrokerResponseEnvelope? generationFailure = ValidateGeneration(
            envelope,
            request.ExpectedGeneration);
        if (generationFailure is not null)
        {
            return generationFailure;
        }

        if (!preparedPlans.TryResolve(request.PreparedPlanId, out RuntimeProcessStartContext? startContext)
            || startContext is null)
        {
            return CreateErrorResponse(
                envelope,
                BrokerResponseStatus.Rejected,
                "PreparedPlanNotFound",
                "The requested prepared plan is not available in the broker staging set.");
        }

        SetActiveOperation(envelope.OperationId);

        try
        {
            Result<RuntimeProcessHostResult> result = await supervisor
                .StartAsync(startContext, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                return CreateErrorResponse(
                    envelope,
                    BrokerResponseStatus.Rejected,
                    result.Error.Code,
                    result.Error.Message);
            }

            lock (correlationSync)
            {
                activePlanId = request.PreparedPlanId;
            }

            return CreateMutationAcceptedResponse(envelope);
        }
        finally
        {
            ClearActiveOperation(envelope.OperationId);
        }
    }

    private async Task<BrokerResponseEnvelope> DispatchStopAsync(
        BrokerRequestEnvelope envelope,
        StopGenerationRequest request,
        CancellationToken cancellationToken)
    {
        BrokerResponseEnvelope? generationFailure = ValidateGeneration(
            envelope,
            request.Generation);
        if (generationFailure is not null)
        {
            return generationFailure;
        }

        SetActiveOperation(envelope.OperationId);

        try
        {
            Result<Unit> result = await supervisor
                .StopAsync(cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                return CreateErrorResponse(
                    envelope,
                    BrokerResponseStatus.Rejected,
                    result.Error.Code,
                    result.Error.Message);
            }

            lock (correlationSync)
            {
                activePlanId = null;
            }

            return CreateMutationAcceptedResponse(envelope);
        }
        finally
        {
            ClearActiveOperation(envelope.OperationId);
        }
    }

    private async Task<BrokerResponseEnvelope> DispatchShutdownAsync(
        BrokerRequestEnvelope envelope,
        CancellationToken cancellationToken)
    {
        SetActiveOperation(envelope.OperationId);

        try
        {
            Result<Unit> result = await lifetimeController
                .ShutdownAsync(cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                return CreateErrorResponse(
                    envelope,
                    BrokerResponseStatus.Rejected,
                    result.Error.Code,
                    result.Error.Message);
            }

            lock (correlationSync)
            {
                activePlanId = null;
            }

            return CreateMutationAcceptedResponse(envelope);
        }
        finally
        {
            ClearActiveOperation(envelope.OperationId);
        }
    }

    private BrokerResponseEnvelope? ValidateGeneration(
        BrokerRequestEnvelope envelope,
        ContractGeneration expected)
    {
        ContractGeneration current = ToContractGeneration(projection.CurrentState.Generation);
        BrokerGenerationDecision decision = BrokerGenerationGuard.Validate(expected, current);
        if (decision.Accepted)
        {
            return null;
        }

        return decision.RejectionReason switch
        {
            BrokerGenerationRejectionReason.StaleGeneration => CreateErrorResponse(
                envelope,
                BrokerResponseStatus.Stale,
                "StaleRuntimeGeneration",
                $"Runtime generation {expected.Value} is stale; current generation is {current.Value}."),
            BrokerGenerationRejectionReason.FutureGeneration => CreateErrorResponse(
                envelope,
                BrokerResponseStatus.Rejected,
                "FutureRuntimeGeneration",
                $"Runtime generation {expected.Value} is ahead of current generation {current.Value}."),
            _ => CreateErrorResponse(
                envelope,
                BrokerResponseStatus.Rejected,
                "RuntimeGenerationRejected",
                "The runtime generation was rejected."),
        };
    }

    private static BrokerResponseEnvelope CreateCapabilitiesResponse(
        BrokerRequestEnvelope request)
    {
        BrokerCapabilities capabilities = new(
            BrokerProtocolVersion.V1,
            [
                BrokerMessageKind.GetCapabilities,
                BrokerMessageKind.GetRuntimeSnapshot,
                BrokerMessageKind.PrepareBundle,
                BrokerMessageKind.PreparePlan,
                BrokerMessageKind.StartPreparedPlan,
                BrokerMessageKind.StopGeneration,
                BrokerMessageKind.ShutdownBroker,
            ],
            new BrokerLimitsSnapshot(
                BrokerProtocolLimits.MaxFrameBytes,
                BrokerProtocolLimits.MaxInFlightQueries,
                BrokerProtocolLimits.MaxConcurrentMutations,
                BrokerProtocolLimits.IngressQueueCapacity,
                BrokerProtocolLimits.ResponseQueueCapacity));

        return CreateResponse(
            request,
            BrokerResponseStatus.Ok,
            new BrokerCapabilitiesResponse(capabilities));
    }

    private BrokerResponseEnvelope CreateSnapshotResponse(
        BrokerRequestEnvelope request)
    {
        RuntimeKernelState state = projection.CurrentState;
        PreparedPlanId? planId;
        BrokerOperationId? operationId;
        lock (correlationSync)
        {
            planId = activePlanId;
            operationId = activeOperationId;
        }

        BrokerRuntimeSnapshot snapshot = new(
            ToContractGeneration(state.Generation),
            MapState(state.Status),
            planId,
            operationId);

        return CreateResponse(
            request,
            BrokerResponseStatus.Ok,
            new BrokerRuntimeSnapshotResponse(snapshot));
    }

    private BrokerResponseEnvelope CreateMutationAcceptedResponse(
        BrokerRequestEnvelope request)
    {
        ContractGeneration observedGeneration =
            ToContractGeneration(projection.CurrentState.Generation);

        return CreateResponse(
            request,
            BrokerResponseStatus.Accepted,
            new BrokerMutationAcceptedResponse(
                request.OperationId,
                observedGeneration));
    }

    private static BrokerResponseEnvelope CreateReplayResponse(
        BrokerRequestEnvelope request,
        BrokerOperationRegistration registration)
    {
        return registration switch
        {
            BrokerOperationRegistration.DuplicateInFlight => CreateErrorResponse(
                request,
                BrokerResponseStatus.DuplicateInFlight,
                "DuplicateInFlight",
                "The same broker operation is already in flight."),
            BrokerOperationRegistration.DuplicateCompleted => CreateErrorResponse(
                request,
                BrokerResponseStatus.DuplicateCompleted,
                "DuplicateCompleted",
                "The same broker operation has already completed."),
            BrokerOperationRegistration.Conflict => CreateErrorResponse(
                request,
                BrokerResponseStatus.Rejected,
                "BrokerOperationConflict",
                "The operation id was reused with a different sequence or request fingerprint."),
            BrokerOperationRegistration.Stale => CreateErrorResponse(
                request,
                BrokerResponseStatus.Stale,
                "StaleBrokerOperation",
                "The request sequence is not newer than the broker session high-water mark."),
            BrokerOperationRegistration.CapacityExceeded => CreateErrorResponse(
                request,
                BrokerResponseStatus.Busy,
                "BrokerOperationLedgerFull",
                "The bounded broker operation ledger is full."),
            _ => CreateErrorResponse(
                request,
                BrokerResponseStatus.Rejected,
                "BrokerOperationRejected",
                "The broker operation was rejected before dispatch."),
        };
    }

    private static BrokerResponseEnvelope CreateErrorResponse(
        BrokerRequestEnvelope request,
        BrokerResponseStatus status,
        string code,
        string message)
        => CreateResponse(
            request,
            status,
            new BrokerErrorResponse(code, message));

    private static BrokerResponseEnvelope CreateResponse(
        BrokerRequestEnvelope request,
        BrokerResponseStatus status,
        IBrokerResponse response)
        => new(
            request.Protocol,
            request.AppSessionId,
            request.BrokerSessionId,
            request.OperationId,
            status,
            response);

    private void SetActiveOperation(BrokerOperationId operationId)
    {
        lock (correlationSync)
        {
            activeOperationId = operationId;
        }
    }

    private void ClearActiveOperation(BrokerOperationId operationId)
    {
        lock (correlationSync)
        {
            if (activeOperationId == operationId)
            {
                activeOperationId = null;
            }
        }
    }

    private static ContractGeneration ToContractGeneration(KernelGeneration generation)
        => new(generation.Value);

    private static BrokerRuntimeState MapState(RuntimeKernelStatus status)
        => status switch
        {
            RuntimeKernelStatus.Stopped => BrokerRuntimeState.Stopped,
            RuntimeKernelStatus.Starting => BrokerRuntimeState.Starting,
            RuntimeKernelStatus.Running => BrokerRuntimeState.Running,
            RuntimeKernelStatus.Stopping => BrokerRuntimeState.Stopping,
            RuntimeKernelStatus.StartBlocked => BrokerRuntimeState.Faulted,
            _ => BrokerRuntimeState.Faulted,
        };
}

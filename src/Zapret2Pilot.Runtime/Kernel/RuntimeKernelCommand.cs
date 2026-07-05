using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// Closed hierarchy of commands accepted by
/// <see cref="RuntimeKernelLoop"/>. The kernel thread is the
/// only consumer of these commands and the only producer of the
/// corresponding <see cref="RuntimeReducerResult"/> /
/// <see cref="RuntimeEffectIntent"/>s; callers from the
/// Application or UI layer post commands into the loop's
/// bounded channel and observe the projected
/// <see cref="RuntimeKernelState"/> via
/// <see cref="RuntimeKernelLoop.StateChanged"/>.
/// </summary>
public abstract record RuntimeKernelCommand
{
    private RuntimeKernelCommand() { }

    /// <summary>
    /// Request to start the runtime. The reducer allocates a
    /// new <see cref="RuntimeOperationId"/>, increments the
    /// generation, consults the crash-loop guard and emits a
    /// <see cref="RuntimeEffectKind.StartProcess"/> intent.
    /// </summary>
    public sealed record Start(
        RuntimeProcessStartContext Context,
        AutomationOwner Owner) : RuntimeKernelCommand;

    // CA1716: 'Stop' is a reserved keyword in Visual Basic. The Kernel
    // API is consumed only by C# code, and the packet's design
    // intentionally uses 'Stop' for symmetry with the 'Start' command.
    // Suppress locally to keep the public API aligned with the
    // architecture plan rather than a VB-specific spelling.
#pragma warning disable CA1716 // Identifiers should not match keywords
    /// <summary>
    /// Request to stop the runtime. The reducer allocates a new
    /// <see cref="RuntimeOperationId"/>, increments the
    /// generation and emits a
    /// <see cref="RuntimeEffectKind.StopProcess"/> intent.
    /// </summary>
    /// <param name="OperationId">
    /// Identifier of the stop request. The supervisor allocates
    /// this when the user presses Stop; the loop uses it to
    /// match the eventual effect completion.
    /// </param>
    /// <param name="Reason">
    /// Short human-readable reason for the stop, used for
    /// observability and persisted diagnostics. Kept as a
    /// <c>string</c> for source compatibility with the previous
    /// shape; the typed cancellation reason lives on
    /// <see cref="CancellationReason"/>.
    /// </param>
    /// <param name="CancellationReason">
    /// Typed cancellation reason recorded with the operation.
    /// Defaults to <see cref="RuntimeCancellationReason.UserRequested"/>
    /// so existing call sites that only pass a
    /// <see cref="string"/> reason keep compiling and remain
    /// semantically valid.
    /// </param>
    public sealed record Stop(
        RuntimeOperationId OperationId,
        string Reason,
        RuntimeCancellationReason CancellationReason = RuntimeCancellationReason.UserRequested) : RuntimeKernelCommand;
#pragma warning restore CA1716

    /// <summary>
    /// Health snapshot observation reported by the supervisor
    /// when the <see cref="Health.IRuntimeHealthMonitor"/>
    /// publishes a new value. The reducer translates the
    /// snapshot into guard / state transitions.
    /// </summary>
    public sealed record Observation(
        RuntimeOperationId OperationId,
        Health.RuntimeHealthSnapshot Snapshot) : RuntimeKernelCommand;

    /// <summary>
    /// Asynchronous completion of an
    /// <see cref="RuntimeEffectIntent"/> dispatched by the loop
    /// to the <see cref="IRuntimeEffectRunner"/>. The loop
    /// posts one of these back into the command channel for
    /// every process effect, with the matching
    /// <see cref="OperationId"/> /
    /// <see cref="Generation"/> identity so the reducer can
    /// reject stale completions.
    /// </summary>
    /// <param name="OperationId">
    /// Operation id of the originating intent.
    /// </param>
    /// <param name="Generation">
    /// Generation of the kernel state when the originating
    /// intent was created. The reducer rejects completions
    /// whose generation is older than the current state.
    /// </param>
    /// <param name="Result">
    /// Success or failure result of the effect.
    /// </param>
    /// <param name="StartResult">
    /// For successful start completions the populated
    /// <see cref="RuntimeProcessHostResult"/> describing the
    /// launched process. <c>null</c> for every other kind.
    /// </param>
    /// <param name="CancellationReason">
    /// Set when the effect was cancelled before the host
    /// reported a terminal result. <c>null</c> when the effect
    /// ran to a normal success or failure.
    /// </param>
    /// <param name="CrossedIrreversibleBoundary">
    /// Set when the effect crossed the irreversible boundary
    /// (a successful process creation for a start effect, or
    /// the moment a stop effect invoked the host). The reducer
    /// uses this flag to distinguish ordinary cancellations
    /// from <c>RollbackRequired</c> /
    /// <c>RecoveryRequired</c> outcomes when reconciling the
    /// next state.
    /// </param>
    public sealed record EffectCompleted(
        RuntimeOperationId OperationId,
        RuntimeGeneration Generation,
        Result<Unit> Result,
        RuntimeProcessHostResult? StartResult = null,
        RuntimeCancellationReason? CancellationReason = null,
        bool CrossedIrreversibleBoundary = false) : RuntimeKernelCommand;

    /// <summary>
    /// Request to dispose the loop. The reducer drives the
    /// state machine to <see cref="RuntimeKernelStatus.Stopping"/>
    /// and emits a <see cref="RuntimeEffectKind.StopProcess"/>
    /// intent with a <see cref="RuntimeCancellationReason.HostShutdown"/>
    /// reason.
    /// </summary>
    public sealed record Dispose : RuntimeKernelCommand;
}

using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// Marker record emitted by <see cref="RuntimeKernelReducer"/>
/// when a <see cref="RuntimeKernelCommand.EffectCompleted"/>
/// is rejected as stale. The reducer never mutates state for
/// stale completions; the marker surfaces the rejection to
/// downstream consumers (publisher, tests) so the rejection is
/// observable and the operation identity of the dropped
/// completion is preserved for diagnostics.
/// </summary>
/// <param name="OperationId">
/// Operation id of the rejected completion.
/// </param>
/// <param name="Generation">
/// Generation of the rejected completion.
/// </param>
/// <param name="StateGeneration">
/// Generation of the kernel state at the moment the
/// completion was processed.
/// </param>
/// <param name="StatePendingOperationId">
/// Pending operation id of the kernel state at the moment the
/// completion was processed, or <c>null</c> if no operation
/// was in flight.
/// </param>
/// <param name="Reason">
/// Short human-readable reason for the rejection. Currently
/// one of <c>"StaleGeneration"</c> or
/// <c>"MismatchedOperationId"</c>.
/// </param>
public sealed record IgnoredStaleCompletion(
    RuntimeOperationId OperationId,
    RuntimeGeneration Generation,
    RuntimeGeneration StateGeneration,
    RuntimeOperationId? StatePendingOperationId,
    string Reason);

/// <summary>
/// Pure reducer for the Runtime Kernel state machine. The
/// reducer is deterministic, free of I/O and process APIs, and
/// is exercised through exhaustive property tests. The
/// <see cref="RuntimeKernelLoop"/> is the only caller.
/// </summary>
/// <remarks>
/// <para>
/// The reducer is intentionally time-agnostic: it uses the
/// supplied <see cref="TimeProvider"/> to stamp the new state's
/// <see cref="RuntimeKernelState.Timestamp"/> and to compute
/// the deadlines recorded with start / stop transitions. The
/// reducer NEVER performs any process, network, or
/// non-deterministic operation.
/// </para>
/// <para>
/// <see cref="RuntimeEffectIntent"/>s emitted by the reducer
/// carry the new <see cref="RuntimeEffectIntent.Deadline"/>
/// and <see cref="RuntimeEffectIntent.CancellationReason"/>
/// fields so the runner can enforce the deadline and classify
/// cancellations consistently with the
/// <see cref="RuntimeKernelState.CancellationReason"/>.
/// </para>
/// </remarks>
public static class RuntimeKernelReducer
{
    /// <summary>
    /// Default deadline applied to a <see cref="RuntimeEffectKind.StartProcess"/>
    /// effect when the caller does not supply an explicit one.
    /// Mirrors the start readiness budget used by the host.
    /// </summary>
    public static readonly TimeSpan DefaultStartDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default deadline applied to a <see cref="RuntimeEffectKind.StopProcess"/>
    /// effect when the caller does not supply an explicit one.
    /// Mirrors the graceful stop window used by the host.
    /// </summary>
    public static readonly TimeSpan DefaultStopDeadline = TimeSpan.FromSeconds(5);

    public static RuntimeReducerResult Reduce(
        RuntimeKernelState state,
        RuntimeKernelCommand command,
        ICrashLoopGuard guard,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return command switch
        {
            RuntimeKernelCommand.Start start => ReduceStart(state, start, guard, timeProvider),
            RuntimeKernelCommand.Stop stop => ReduceStop(state, stop, timeProvider),
            RuntimeKernelCommand.Observation observation => ReduceObservation(state, observation, guard, timeProvider),
            RuntimeKernelCommand.EffectCompleted completed => ReduceEffectCompleted(state, completed, timeProvider),
            RuntimeKernelCommand.Dispose => ReduceDispose(state, timeProvider),
            RuntimeKernelCommand.CancelOperation cancel => ReduceCancelOperation(state, cancel, timeProvider),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    private static RuntimeReducerResult ReduceStart(
        RuntimeKernelState state,
        RuntimeKernelCommand.Start command,
        ICrashLoopGuard guard,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        if (state.Status is not RuntimeKernelStatus.Stopped and not RuntimeKernelStatus.StartBlocked)
        {
            var error = new ErrorInfo(
                code: "RuntimeAlreadyRunning",
                message: "Cannot start the runtime: a start or stop transition is already in progress.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);

            return new RuntimeReducerResult(
                state with { Timestamp = now, LastError = error },
                Array.Empty<RuntimeEffectIntent>(),
                Array.Empty<object>(),
                Result.Failure<Unit>(error));
        }

        var guardResult = guard.Check();
        if (!guardResult.IsAllowed)
        {
            bool permanent = guardResult.ConsecutiveFailures
                > CrashLoopGuardOptions.DefaultMaxConsecutiveFailures;

            var error = permanent
                ? new ErrorInfo(
                    code: "RuntimePermanentLockout",
                    message: "Permanent lockout: the runtime has crashed too many times. Restart the application to recover.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime)
                : new ErrorInfo(
                    code: "RuntimeStartBlockedByCrashLoopGuard",
                    message: guardResult.BackoffRemaining is { } backoff
                        ? $"Start blocked by the crash-loop guard. Retry in {backoff.TotalSeconds:F0} s."
                        : "Start blocked by the crash-loop guard.",
                    severity: ErrorSeverity.Warning,
                    category: ErrorCategory.Runtime);

            var next = new RuntimeKernelState(
                RuntimeKernelStatus.StartBlocked,
                command.Owner,
                state.Generation.Next(),
                pendingOperationId: null,
                state.LastStartResult,
                guardResult,
                error,
                now,
                deadline: null,
                cancellationReason: null);

            return new RuntimeReducerResult(
                next,
                Array.Empty<RuntimeEffectIntent>(),
                Array.Empty<object>(),
                Result.Failure<Unit>(error));
        }

        var operationId = RuntimeOperationId.New();
        var deadline = now + DefaultStartDeadline;
        var cancellationReason = RuntimeCancellationReason.UserRequested;

        var nextState = new RuntimeKernelState(
            RuntimeKernelStatus.Starting,
            command.Owner,
            state.Generation.Next(),
            operationId,
            state.LastStartResult,
            guardResult,
            lastError: null,
            now,
            deadline: deadline,
            cancellationReason: cancellationReason);

        var effects = new List<RuntimeEffectIntent>(1)
        {
            new RuntimeEffectIntent(
                operationId,
                nextState.Generation,
                RuntimeEffectKind.StartProcess,
                command.Context,
                now,
                deadline,
                cancellationReason),
        };

        return new RuntimeReducerResult(
            nextState,
            effects,
            Array.Empty<object>(),
            Result.Success(Unit.Instance));
    }

    private static RuntimeReducerResult ReduceStop(
        RuntimeKernelState state,
        RuntimeKernelCommand.Stop command,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        if (state.Status is RuntimeKernelStatus.Stopped or RuntimeKernelStatus.StartBlocked)
        {
            return new RuntimeReducerResult(
                state with { Timestamp = now },
                Array.Empty<RuntimeEffectIntent>(),
                Array.Empty<object>(),
                Result.Success(Unit.Instance));
        }

        var operationId = command.OperationId;
        var deadline = now + DefaultStopDeadline;
        var cancellationReason = command.CancellationReason;

        var nextState = new RuntimeKernelState(
            RuntimeKernelStatus.Stopping,
            state.Owner,
            state.Generation.Next(),
            operationId,
            state.LastStartResult,
            state.GuardResult,
            state.LastError,
            now,
            deadline: deadline,
            cancellationReason: cancellationReason);

        var effects = new List<RuntimeEffectIntent>(1)
        {
            new RuntimeEffectIntent(
                operationId,
                nextState.Generation,
                RuntimeEffectKind.StopProcess,
                Payload: command.Reason,
                now,
                deadline,
                cancellationReason),
        };

        return new RuntimeReducerResult(
            nextState,
            effects,
            Array.Empty<object>(),
            Result.Success(Unit.Instance));
    }

    private static RuntimeReducerResult ReduceObservation(
        RuntimeKernelState state,
        RuntimeKernelCommand.Observation command,
        ICrashLoopGuard guard,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        if (command.Snapshot.State == RuntimeHealthState.Healthy
            && state.Status is RuntimeKernelStatus.Starting or RuntimeKernelStatus.Running)
        {
            var successEffect = new RuntimeEffectIntent(
                command.OperationId,
                state.Generation,
                RuntimeEffectKind.RecordGuardSuccess,
                Payload: null,
                RequestedAtUtc: now,
                Deadline: null,
                CancellationReason: state.CancellationReason ?? RuntimeCancellationReason.UserRequested);

            var next = state with
            {
                Status = RuntimeKernelStatus.Running,
                Timestamp = now,
                LastError = null,
                Deadline = null,
                CancellationReason = null,
            };

            return new RuntimeReducerResult(
                next,
                new List<RuntimeEffectIntent> { successEffect },
                Array.Empty<object>(),
                Result.Success(Unit.Instance));
        }

        if (command.Snapshot.State == RuntimeHealthState.Exited
            && state.Status is RuntimeKernelStatus.Starting or RuntimeKernelStatus.Running)
        {
            var failureEffect = new RuntimeEffectIntent(
                command.OperationId,
                state.Generation,
                RuntimeEffectKind.RecordGuardFailure,
                Payload: null,
                RequestedAtUtc: now,
                Deadline: null,
                CancellationReason: state.CancellationReason ?? RuntimeCancellationReason.SafetyAbort);

            var stopOperationId = RuntimeOperationId.New();
            var stopDeadline = now + DefaultStopDeadline;
            var stopReason = RuntimeCancellationReason.SafetyAbort;

            var stopEffect = new RuntimeEffectIntent(
                stopOperationId,
                state.Generation,
                RuntimeEffectKind.StopProcess,
                Payload: "Runtime process exited unexpectedly.",
                now,
                stopDeadline,
                stopReason);

            var error = new ErrorInfo(
                code: "RuntimeProcessExitedUnexpectedly",
                message: "The runtime process exited unexpectedly.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);

            var next = state with
            {
                Status = RuntimeKernelStatus.Stopping,
                PendingOperationId = stopOperationId,
                Timestamp = now,
                LastError = error,
                Deadline = stopDeadline,
                CancellationReason = stopReason,
            };

            return new RuntimeReducerResult(
                next,
                new List<RuntimeEffectIntent> { failureEffect, stopEffect },
                Array.Empty<object>(),
                Result.Success(Unit.Instance));
        }

        return new RuntimeReducerResult(
            state with { Timestamp = now },
            Array.Empty<RuntimeEffectIntent>(),
            Array.Empty<object>(),
            Result.Success(Unit.Instance));
    }

    private static RuntimeReducerResult ReduceEffectCompleted(
        RuntimeKernelState state,
        RuntimeKernelCommand.EffectCompleted command,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        if (command.Generation.Value < state.Generation.Value)
        {
            IgnoredStaleCompletion ignored = new(
                command.OperationId,
                command.Generation,
                state.Generation,
                state.PendingOperationId,
                "StaleGeneration");

            return new RuntimeReducerResult(
                state with { Timestamp = now },
                Array.Empty<RuntimeEffectIntent>(),
                new object[] { ignored },
                Result.Success(Unit.Instance));
        }

        if (state.PendingOperationId != command.OperationId)
        {
            IgnoredStaleCompletion ignored = new(
                command.OperationId,
                command.Generation,
                state.Generation,
                state.PendingOperationId,
                "MismatchedOperationId");

            return new RuntimeReducerResult(
                state with { Timestamp = now },
                Array.Empty<RuntimeEffectIntent>(),
                new object[] { ignored },
                Result.Success(Unit.Instance));
        }

        if (command.Result.IsFailure)
        {
            // Cancellation outcomes (RecoveryRequired / plain
            // Cancelled) follow the same shape as ordinary host
            // failures: the state transitions to Stopped and the
            // error is surfaced through LastError so the
            // supervisor can project it onto the public state.
            // The deadline and cancellation reason are cleared
            // because the operation is now terminal.
            var next = state with
            {
                Status = RuntimeKernelStatus.Stopped,
                PendingOperationId = null,
                Timestamp = now,
                LastError = command.Result.Error,
                Deadline = null,
                CancellationReason = null,
            };

            List<RuntimeEffectIntent> effects = new();
            if (state.Status == RuntimeKernelStatus.Starting)
            {
                effects.Add(new RuntimeEffectIntent(
                    command.OperationId,
                    command.Generation,
                    RuntimeEffectKind.RecordGuardFailure,
                    Payload: null,
                    RequestedAtUtc: now,
                    Deadline: null,
                    CancellationReason: command.CancellationReason
                        ?? state.CancellationReason
                        ?? RuntimeCancellationReason.HostShutdown));
            }

            return new RuntimeReducerResult(
                next,
                effects,
                Array.Empty<object>(),
                Result.Success(Unit.Instance));
        }

        RuntimeKernelStatus nextStatus = state.Status switch
        {
            RuntimeKernelStatus.Starting => RuntimeKernelStatus.Running,
            RuntimeKernelStatus.Stopping => RuntimeKernelStatus.Stopped,
            _ => state.Status,
        };

        RuntimeProcessHostResult? nextStartResult = state.LastStartResult;
        if (nextStatus == RuntimeKernelStatus.Running
            && command.StartResult is not null)
        {
            nextStartResult = command.StartResult;
        }

        var nextState = state with
        {
            Status = nextStatus,
            PendingOperationId = null,
            Timestamp = now,
            LastStartResult = nextStartResult,
            Deadline = null,
            CancellationReason = null,
        };

        return new RuntimeReducerResult(
            nextState,
            Array.Empty<RuntimeEffectIntent>(),
            Array.Empty<object>(),
            Result.Success(Unit.Instance));
    }

    private static RuntimeReducerResult ReduceDispose(
        RuntimeKernelState state,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        if (state.Status == RuntimeKernelStatus.Stopped)
        {
            return new RuntimeReducerResult(
                state with { Timestamp = now, Deadline = null, CancellationReason = null },
                Array.Empty<RuntimeEffectIntent>(),
                Array.Empty<object>(),
                Result.Success(Unit.Instance));
        }

        var operationId = RuntimeOperationId.New();
        var deadline = now + DefaultStopDeadline;
        var cancellationReason = RuntimeCancellationReason.HostShutdown;

        var nextState = new RuntimeKernelState(
            RuntimeKernelStatus.Stopping,
            state.Owner,
            state.Generation.Next(),
            operationId,
            state.LastStartResult,
            state.GuardResult,
            state.LastError,
            now,
            deadline: deadline,
            cancellationReason: cancellationReason);

        var effects = new List<RuntimeEffectIntent>(1)
        {
            new RuntimeEffectIntent(
                operationId,
                nextState.Generation,
                RuntimeEffectKind.StopProcess,
                Payload: "Disposing the runtime kernel.",
                now,
                deadline,
                cancellationReason),
        };

        return new RuntimeReducerResult(
            nextState,
            effects,
            Array.Empty<object>(),
            Result.Success(Unit.Instance));
    }

    /// <summary>
    /// Reducer entry point for a
    /// <see cref="RuntimeKernelCommand.CancelOperation"/>. The
    /// command is a control signal only: the loop has its own
    /// per-operation <see cref="CancellationTokenSource"/>
    /// registry and uses this command to flip the matching
    /// CTS. The reducer is therefore intentionally side-effect
    /// free here — it only validates the command and emits an
    /// <see cref="IgnoredStaleCompletion"/> event when the
    /// cancel is stale (mismatched generation or mismatched
    /// pending operation id).
    /// </summary>
    /// <remarks>
    /// The match contract is:
    /// <list type="bullet">
    ///   <item>If <c>state.PendingOperationId == command.OperationId</c>
    ///         AND <c>state.Generation == command.Generation</c>,
    ///         the command targets the currently in-flight
    ///         effect. The reducer stamps the timestamp on the
    ///         state to acknowledge the command but does not
    ///         transition out of <see cref="RuntimeKernelStatus.Starting"/>
    ///         / <see cref="RuntimeKernelStatus.Stopping"/>;
    ///         the actual state transition is driven by the
    ///         resulting <see cref="RuntimeKernelCommand.EffectCompleted"/>,
    ///         which now carries the supplied
    ///         <see cref="RuntimeKernelCommand.CancelOperation.Reason"/>
    ///         as its
    ///         <see cref="RuntimeKernelCommand.EffectCompleted.CancellationReason"/>.</item>
    ///   <item>Otherwise the command is treated as stale: an
    ///         <see cref="IgnoredStaleCompletion"/> event is
    ///         emitted with a <c>"CancelOperationStale"</c>
    ///         reason, the state is returned unchanged, and
    ///         the loop's <see cref="TryCancelOperation"/>
    ///         entry point still runs (a stale cancel must
    ///         never flip a CTS that has already been
    ///         removed).</item>
    /// </list>
    /// </remarks>
    private static RuntimeReducerResult ReduceCancelOperation(
        RuntimeKernelState state,
        RuntimeKernelCommand.CancelOperation command,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        bool matches = state.PendingOperationId == command.OperationId
            && state.Generation == command.Generation;

        if (matches)
        {
            return new RuntimeReducerResult(
                state with { Timestamp = now },
                Array.Empty<RuntimeEffectIntent>(),
                Array.Empty<object>(),
                Result.Success(Unit.Instance));
        }

        IgnoredStaleCompletion ignored = new(
            command.OperationId,
            command.Generation,
            state.Generation,
            state.PendingOperationId,
            "CancelOperationStale");

        return new RuntimeReducerResult(
            state with { Timestamp = now },
            Array.Empty<RuntimeEffectIntent>(),
            new object[] { ignored },
            Result.Success(Unit.Instance));
    }
}

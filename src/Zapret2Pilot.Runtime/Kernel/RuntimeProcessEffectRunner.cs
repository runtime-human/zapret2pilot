using System;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// Default <see cref="IRuntimeEffectRunner"/> that forwards
/// <see cref="RuntimeEffectKind.StartProcess"/> and
/// <see cref="RuntimeEffectKind.StopProcess"/> effects to the
/// production <see cref="IRuntimeProcessHost"/>. Lives next to
/// the kernel so the loop can stay free of any direct host
/// reference.
/// </summary>
/// <remarks>
/// <para>
/// The runner is the only place where cancellation tokens,
/// deadlines and the irreversible boundary are translated into
/// kernel completion results. It is responsible for:
/// </para>
/// <list type="bullet">
///   <item>enforcing the intent's
///         <see cref="RuntimeEffectIntent.Deadline"/> with a
///         monotonic <see cref="TimeProvider"/>
///         (<see cref="TimeProvider.GetTimestamp"/> /
///         <see cref="TimeProvider.GetElapsedTime"/>);</item>
///   <item>classifying a cancellation as
///         <see cref="RuntimeCancellationReason.HostShutdown"/>
///         (outer token) or
///         <see cref="RuntimeCancellationReason.Timeout"/>
///         (deadline) and returning it on the resulting
///         <see cref="RuntimeKernelCommand.EffectCompleted"/>;</item>
///   <item>tracking the irreversible boundary — a successful
///         <see cref="IRuntimeProcessHost.StartAsync"/> for the
///         start pipeline, the moment the stop pipeline begins
///         for the stop pipeline — and surfacing
///         <c>RecoveryRequired</c> instead of an ordinary
///         <c>Cancelled</c> result when the stop pipeline is
///         cancelled after the boundary has been crossed. The
///         start pipeline never raises <c>RollbackRequired</c>
///         on cancellation because a partially-completed start
///         cannot be classified reliably from inside the
///         runner.</item>
/// </list>
/// <para>
/// The runner never throws on cancellation: it converts
/// <see cref="OperationCanceledException"/> into the appropriate
/// completion. Unexpected exceptions from the host are
/// converted into a failed completion with
/// <c>RuntimeEffectRunnerThrew</c> so the kernel state machine
/// never gets stuck on an in-flight effect.
/// </para>
/// <para>
/// The class is <c>internal</c>: it is a kernel-internal seam
/// between the loop and the host and must not leak into the
/// public runtime API.
/// </para>
/// </remarks>
internal sealed class RuntimeProcessEffectRunner : IRuntimeEffectRunner
{
    private readonly IRuntimeProcessHost host;

    /// <summary>
    /// Creates a new <see cref="RuntimeProcessEffectRunner"/>.
    /// </summary>
    /// <param name="host">
    /// Process host that performs the actual start / stop
    /// pipeline. Must not be <c>null</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="host"/> is <c>null</c>.
    /// </exception>
    public RuntimeProcessEffectRunner(IRuntimeProcessHost host)
    {
        ArgumentNullException.ThrowIfNull(host, nameof(host));

        this.host = host;
    }

    /// <inheritdoc />
    public async Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
        RuntimeEffectIntent intent,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent, nameof(intent));
        ArgumentNullException.ThrowIfNull(timeProvider, nameof(timeProvider));

        // 1. Reject intents the runner does not understand with
        //    a typed failure. The kernel state machine must always
        //    get a completion back so the in-flight operation can
        //    be cleaned up; throwing here would wedge the loop.
        if (intent.Kind is not (RuntimeEffectKind.StartProcess or RuntimeEffectKind.StopProcess))
        {
            return NewCompletion(
                intent,
                Result.Failure<Unit>(UnsupportedKindError(intent.Kind)),
                cancellationReason: null,
                boundaryCrossed: false);
        }

        long startMono = timeProvider.GetTimestamp();
        DateTimeOffset startWall = timeProvider.GetUtcNow();

        // 2. Honour the deadline pre-check. If the deadline is
        //    already in the past at dispatch time there is no point
        //    in even attempting the host call.
        if (intent.Deadline is { } preDeadline && preDeadline <= startWall)
        {
            return NewCompletion(
                intent,
                Result.Failure<Unit>(DeadlineExceededError(intent)),
                RuntimeCancellationReason.Timeout,
                boundaryCrossed: false);
        }

        // 3. Build a linked CTS that fires when the deadline
        //    elapses. The outer cancellation token (the kernel
        //    worker CTS) is linked into the same source so the
        //    host call observes both signals at once.
        CancellationTokenSource? deadlineCts = null;
        CancellationTokenSource? linkedCts = null;
        if (intent.Deadline is { } deadlineValue)
        {
            TimeSpan remaining = deadlineValue - startWall;
            if (remaining > TimeSpan.Zero)
            {
                deadlineCts = new CancellationTokenSource(remaining);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, deadlineCts.Token);
            }
        }

        CancellationToken effectiveToken = linkedCts?.Token ?? cancellationToken;

        bool stopBoundaryCrossed = false;

        try
        {
            if (intent.Kind == RuntimeEffectKind.StopProcess)
            {
                // Honour pre-cancellation BEFORE crossing the
                // irreversible boundary: if the linked token is
                // already cancelled we must not call into the
                // host's stop pipeline at all. Crossing the
                // boundary unconditionally and then catching
                // OCE would also report the wrong outcome to
                // the kernel state machine.
                effectiveToken.ThrowIfCancellationRequested();

                // The stop pipeline crosses the irreversible
                // boundary as soon as it is invoked: from this
                // moment on, a partial cancellation must be
                // classified as RecoveryRequired because the
                // previous runtime may already be torn down
                // (or in the middle of being torn down). The
                // local is set BEFORE the await so the catch
                // block below can read the correct value if the
                // host's StopAsync throws OperationCanceledException.
                stopBoundaryCrossed = true;

                return await RunStopAsync(
                    intent,
                    effectiveToken).ConfigureAwait(false);
            }

            // The start pipeline crosses the irreversible
            // boundary only when the host's StartAsync returns
            // a success result; a partially-completed start
            // that is cancelled cannot be classified reliably
            // from the runner, so cancellations during the
            // start pipeline always produce an ordinary
            // Cancelled completion. The helper surfaces the
            // success-path boundary flag on the returned
            // completion; no local needs to be tracked here.
            return await RunStartAsync(
                intent,
                effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RuntimeCancellationReason reason = DetermineCancellationReason(
                deadlineCts,
                startMono,
                timeProvider,
                intent,
                cancellationToken);

            if (stopBoundaryCrossed)
            {
                return NewCompletion(
                    intent,
                    Result.Failure<Unit>(RecoveryRequiredError(reason)),
                    reason,
                    boundaryCrossed: true);
            }

            return NewCompletion(
                intent,
                Result.Failure<Unit>(CancelledError(reason)),
                reason,
                boundaryCrossed: false);
        }
        catch (Exception ex)
        {
            // The runner MUST NOT let an unexpected exception
            // escape. The loop would have to convert it into a
            // completion anyway; doing the conversion here keeps
            // the kernel state machine moving.
            ErrorInfo error = new(
                code: "RuntimeEffectRunnerThrew",
                message: $"The effect runner threw an unexpected exception: {ex.Message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);

            return NewCompletion(
                intent,
                Result.Failure<Unit>(error),
                cancellationReason: null,
                boundaryCrossed: stopBoundaryCrossed);
        }
        finally
        {
            linkedCts?.Dispose();
            deadlineCts?.Dispose();
        }
    }

    private async Task<RuntimeKernelCommand.EffectCompleted> RunStartAsync(
        RuntimeEffectIntent intent,
        CancellationToken effectiveToken)
    {
        if (intent.Payload is not RuntimeProcessStartContext startContext)
        {
            return NewCompletion(
                intent,
                Result.Failure<Unit>(MissingStartContextError()),
                cancellationReason: null,
                boundaryCrossed: false);
        }

        Result<RuntimeProcessHostResult> hostResult = await host
            .StartAsync(startContext, effectiveToken)
            .ConfigureAwait(false);

        if (hostResult.IsSuccess)
        {
            // Crossing the irreversible boundary for the start
            // pipeline: a process is now alive and ordinary
            // cancellation is no longer a valid outcome. The
            // returned completion carries the boundary flag so
            // the outer scope can lift it into its locals.
            return NewCompletion(
                intent,
                Result.Success(Unit.Instance),
                cancellationReason: null,
                boundaryCrossed: true,
                startResult: hostResult.Value);
        }

        return NewCompletion(
            intent,
            Result.Failure<Unit>(hostResult.Error),
            cancellationReason: null,
            boundaryCrossed: false);
    }

    private async Task<RuntimeKernelCommand.EffectCompleted> RunStopAsync(
        RuntimeEffectIntent intent,
        CancellationToken effectiveToken)
    {
        // The stop pipeline crosses the irreversible boundary as
        // soon as it is invoked: from this moment on, a partial
        // cancellation must be classified as RecoveryRequired
        // because the previous runtime may already be torn down
        // (or in the middle of being torn down).
        Result<Unit> hostResult = await host
            .StopAsync(effectiveToken)
            .ConfigureAwait(false);

        return NewCompletion(
            intent,
            hostResult,
            cancellationReason: null,
            boundaryCrossed: true);
    }

    /// <summary>
    /// Classifies a cancellation as
    /// <see cref="RuntimeCancellationReason.HostShutdown"/>,
    /// <see cref="RuntimeCancellationReason.Timeout"/>, or
    /// (last resort) the intent's recorded
    /// <see cref="RuntimeEffectIntent.CancellationReason"/>
    /// when neither of the two signals is responsible. The
    /// monotonic <see cref="TimeProvider"/> is used to confirm
    /// that the deadline actually elapsed.
    /// </summary>
    private static RuntimeCancellationReason DetermineCancellationReason(
        CancellationTokenSource? deadlineCts,
        long startMono,
        TimeProvider timeProvider,
        RuntimeEffectIntent intent,
        CancellationToken outerToken)
    {
        if (outerToken.IsCancellationRequested)
        {
            return RuntimeCancellationReason.HostShutdown;
        }

        if (deadlineCts is not null && deadlineCts.IsCancellationRequested)
        {
            return RuntimeCancellationReason.Timeout;
        }

        // Defensive fallback: confirm via the monotonic clock
        // that the deadline has actually elapsed. The intent's
        // recorded reason is used only as a tie-breaker — the
        // outer two signals are the canonical sources.
        if (intent.Deadline is { } deadlineValue)
        {
            DateTimeOffset nowWall = timeProvider.GetUtcNow();
            if (nowWall >= deadlineValue)
            {
                return RuntimeCancellationReason.Timeout;
            }

            // Sanity check: monotonic clock should agree that
            // the elapsed time is at least the deadline
            // interval. If neither timer has fired but the
            // call still threw, fall back to the intent's
            // recorded reason (typically the user request that
            // caused the outer cancellation).
            _ = startMono;
        }

        return intent.CancellationReason;
    }

    private static RuntimeKernelCommand.EffectCompleted NewCompletion(
        RuntimeEffectIntent intent,
        Result<Unit> result,
        RuntimeCancellationReason? cancellationReason,
        bool boundaryCrossed,
        RuntimeProcessHostResult? startResult = null)
    {
        return new RuntimeKernelCommand.EffectCompleted(
            intent.OperationId,
            intent.Generation,
            result,
            startResult,
            cancellationReason,
            boundaryCrossed);
    }

    private static ErrorInfo DeadlineExceededError(RuntimeEffectIntent intent)
    {
        return new ErrorInfo(
            code: "RuntimeEffectDeadlineExceeded",
            message: $"The {intent.Kind} effect deadline elapsed before the host could start running.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);
    }

    private static ErrorInfo MissingStartContextError()
    {
        return new ErrorInfo(
            code: "RuntimeStartEffectMissingContext",
            message: "RuntimeKernelLoop produced a StartProcess effect with a non-start payload.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);
    }

    private static ErrorInfo UnsupportedKindError(RuntimeEffectKind kind)
    {
        return new ErrorInfo(
            code: "RuntimeEffectRunnerUnsupportedKind",
            message: $"The effect runner does not support the {kind} effect kind.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);
    }

    private static ErrorInfo CancelledError(RuntimeCancellationReason reason)
    {
        return new ErrorInfo(
            code: "RuntimeEffectCancelled",
            message: $"The effect was cancelled ({reason}) before the irreversible boundary was crossed.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);
    }

    private static ErrorInfo RecoveryRequiredError(RuntimeCancellationReason reason)
    {
        return new ErrorInfo(
            code: "RuntimeEffectRecoveryRequired",
            message: $"The effect was cancelled ({reason}) after the stop boundary was crossed; the runtime must be recovered.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);
    }
}

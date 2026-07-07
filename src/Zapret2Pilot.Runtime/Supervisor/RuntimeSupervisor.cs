using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;

namespace Zapret2Pilot.Runtime.Supervisor;

/// <summary>
/// <see cref="IRuntimeSupervisor"/> implementation. As of milestone
/// 0.0.25 the supervisor is a pure façade over
/// <see cref="RuntimeKernelLoop"/>: the loop owns the state
/// machine, the guard and the lifecycle command queue; the
/// supervisor is a thin adapter that:
/// <list type="bullet">
///   <item>Translates the public <see cref="StartAsync"/> /
///         <see cref="StopAsync"/> calls into
///         <see cref="RuntimeKernelCommand"/>s and forwards them to
///         the loop.</item>
///   <item>Wraps every posted command in a
///         <see cref="RuntimeCommandReceipt"/> and awaits the
///         loop's projected <see cref="RuntimeKernelState"/>
///         observable until the terminal status
///         (<see cref="RuntimeSupervisorStatus.Running"/>,
///         <see cref="RuntimeSupervisorStatus.Stopped"/> or
///         <see cref="RuntimeSupervisorStatus.StartBlocked"/>) is
///         reached and returns the mapped result.</item>
///   <item>Implements <see cref="IHostedService"/> and bridges
///         <see cref="IRuntimeHealthMonitor.SnapshotChanged"/> into
///         <see cref="RuntimeKernelCommand.Observation"/> commands
///         so the kernel can react to health transitions.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>No own state machine.</b> The supervisor does not own a
/// <c>BehaviorSubject</c> or any other in-process state machine
/// of its own; every transition is sourced from the loop. The
/// <see cref="StateChanged"/> observable is a projection of
/// <see cref="RuntimeKernelLoop.StateChanged"/>.
/// </para>
/// <para>
/// <b>Concurrency model.</b> The supervisor serialises concurrent
/// <see cref="StartAsync"/> and <see cref="StopAsync"/> calls
/// through a single <c>pendingReceipt</c> slot guarded by
/// <see cref="Interlocked.CompareExchange(ref object, object, object)"/>.
/// A <see cref="SemaphoreSlim"/> is intentionally NOT used: the
/// stop path is allowed to supersede an in-flight start so the
/// caller pressing Stop does not have to wait for the start to
/// resolve. Concurrent start calls are rejected with a typed
/// <c>RuntimeSupervisorAlreadyRunning</c> failure result;
/// concurrent stop calls share the same receipt and observe the
/// same terminal outcome.
/// </para>
/// <para>
/// <b>Hosted-service contract.</b> The supervisor implements
/// <see cref="IHostedService"/>. <c>IHostedService.StartAsync</c>
/// subscribes to <see cref="IRuntimeHealthMonitor.SnapshotChanged"/>
/// and forwards every snapshot as an
/// <see cref="RuntimeKernelCommand.Observation"/> command.
/// <c>IHostedService.StopAsync</c> disposes the subscription and
/// then performs a final
/// <see cref="StopAsync(CancellationToken)"/>. <see cref="Dispose"/>
/// is idempotent and tears down the supervisor even if the hosted
/// service was never started.
/// </para>
/// </remarks>
public sealed class RuntimeSupervisor : IRuntimeSupervisor, IHostedService, IDisposable
{
    /// <summary>
    /// Private lifecycle state machine. The supervisor is a
    /// façade over <see cref="RuntimeKernelLoop"/>; the loop owns
    /// the start / stop transition state. The supervisor's own
    /// state machine is the disposal gate: it tracks whether the
    /// supervisor is still serving requests
    /// (<see cref="LifecycleState.Active"/>), in the middle of
    /// teardown (<see cref="LifecycleState.Disposing"/>) or fully
    /// torn down (<see cref="LifecycleState.Disposed"/>). The
    /// three-state design lets <see cref="Dispose"/> perform a
    /// graceful stop (the previous binary
    /// <c>int disposed</c> flag forced the stop path to observe
    /// itself and short-circuit with
    /// <see cref="ObjectDisposedException"/>).
    /// </summary>
    private enum LifecycleState
    {
        /// <summary>
        /// The supervisor is fully operational.
        /// </summary>
        Active = 0,

        /// <summary>
        /// A single <see cref="Dispose"/> call is in the middle
        /// of tearing the supervisor down. The stop pipeline is
        /// still expected to run.
        /// </summary>
        Disposing = 1,

        /// <summary>
        /// The supervisor has been fully torn down. Any future
        /// <c>StartAsync</c> or <c>StopAsync</c> call must throw
        /// <see cref="ObjectDisposedException"/>.
        /// </summary>
        Disposed = 2,
    }

    private readonly RuntimeKernelLoop loop;
    private readonly IRuntimeHealthMonitor healthMonitor;
    private readonly ILogger<RuntimeSupervisor> logger;
    private readonly TimeProvider timeProvider;

    private IDisposable? healthSubscription;
    private int lifecycleState; // stores a LifecycleState value

    // CS0420: a reference to a volatile field is not treated as
    // volatile when passed by ref to Interlocked.CompareExchange.
    // The Interlocked operations provide the necessary memory
    // barrier, so the warning is informational and the field can
    // remain volatile for plain Volatile.Read/Write.
#pragma warning disable CS0420
    private volatile RuntimeCommandReceipt? pendingReceipt;
#pragma warning restore CS0420

    /// <summary>
    /// Creates a new <see cref="RuntimeSupervisor"/> façade.
    /// </summary>
    /// <param name="loop">
    /// Kernel loop that owns the lifecycle state machine, the
    /// guard and the command queue. Must not be <c>null</c>.
    /// </param>
    /// <param name="healthMonitor">
    /// Health monitor whose <see cref="IRuntimeHealthMonitor.SnapshotChanged"/>
    /// observable drives the supervisor's automatic
    /// success / failure recording. Must not be <c>null</c>.
    /// </param>
    /// <param name="logger">
    /// Logger that receives structured events for guard blocks,
    /// automatic stops and other state transitions. Must not be
    /// <c>null</c>.
    /// </param>
    /// <param name="timeProvider">
    /// Time provider that supplies timestamps for the published
    /// <see cref="RuntimeSupervisorState"/> snapshots. When
    /// <c>null</c>, <see cref="TimeProvider.System"/> is used.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="loop"/>,
    /// <paramref name="healthMonitor"/> or <paramref name="logger"/>
    /// is <c>null</c>.
    /// </exception>
    public RuntimeSupervisor(
        RuntimeKernelLoop loop,
        IRuntimeHealthMonitor healthMonitor,
        ILogger<RuntimeSupervisor> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(loop, nameof(loop));
        ArgumentNullException.ThrowIfNull(healthMonitor, nameof(healthMonitor));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        this.loop = loop;
        this.healthMonitor = healthMonitor;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public RuntimeSupervisorState CurrentState => MapToSupervisorState(loop.CurrentState);

    /// <inheritdoc />
    public IObservable<RuntimeSupervisorState> StateChanged =>
        loop.StateChanged.Select(MapToSupervisorState);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref lifecycleState) != (int)LifecycleState.Active, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (healthSubscription is null)
        {
            healthSubscription = healthMonitor.SnapshotChanged.Subscribe(OnHealthSnapshot);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="IHostedService"/> shutdown hook. Disposes the
    /// health-monitor subscription and then performs a final
    /// <see cref="StopAsync(CancellationToken)"/> so the supervisor
    /// is left in the <see cref="RuntimeSupervisorStatus.Stopped"/>
    /// state when the Generic Host tears down. Implemented as an
    /// explicit interface member so the public
    /// <see cref="IRuntimeSupervisor.StopAsync(CancellationToken)"/>
    /// surface stays the canonical stop entry point.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token observed while waiting for the in-flight
    /// stop pipeline to complete.
    /// </param>
    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        DisposeHealthSubscription();

        try
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // expected when the host cancels shutdown
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Atomic transition Active -> Disposing. The first
        // caller wins; concurrent Dispose calls observe a
        // non-Active state and return immediately so the
        // teardown sequence runs exactly once.
        if (Interlocked.CompareExchange(
                ref lifecycleState,
                (int)LifecycleState.Disposing,
                (int)LifecycleState.Active)
            != (int)LifecycleState.Active)
        {
            return;
        }

        DisposeHealthSubscription();

        // Cancel any in-flight receipt so the StartAsync (or
        // StopAsync) caller that is awaiting it resumes with
        // OperationCanceledException. The receipt's terminal
        // callback also clears the pendingReceipt slot via
        // CompareExchange, so the subsequent StopCoreAsync
        // call observes an empty slot and posts a fresh stop
        // command rather than blocking on a cancelled one.
        RuntimeCommandReceipt? active = pendingReceipt;
        if (active is not null && !active.IsCompleted)
        {
            active.TryCancel();
        }

        try
        {
            // ignoreDisposed: true — the supervisor IS being
            // disposed, but the in-flight stop pipeline must
            // still run to completion. The receipt-based
            // design intentionally does not take a semaphore
            // here: the cancel above unblocks the in-flight
            // start, the slot is cleared, and StopCoreAsync
            // installs its own stop receipt and waits for
            // the kernel to publish the terminal Stopped
            // snapshot.
            StopCoreAsync(ignoreDisposed: true, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeSupervisor: Dispose triggered a failed stop.");
        }
        finally
        {
            try
            {
                loop.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RuntimeSupervisor: loop disposal failed.");
            }

            // The teardown sequence is complete. Subsequent
            // StartAsync / StopAsync callers must observe
            // ObjectDisposedException.
            Volatile.Write(ref lifecycleState, (int)LifecycleState.Disposed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<RuntimeProcessHostResult>> StartAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref lifecycleState) != (int)LifecycleState.Active, this);
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        cancellationToken.ThrowIfCancellationRequested();

        RuntimeKernelState initial = loop.CurrentState;
        RuntimeSupervisorStatus current = MapStatus(initial.Status);
        if (current is not (RuntimeSupervisorStatus.Stopped or RuntimeSupervisorStatus.StartBlocked))
        {
            ErrorInfo alreadyRunning = new(
                code: "RuntimeSupervisorAlreadyRunning",
                message: "Cannot start the runtime: a start or stop transition is already in progress.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);
            return Result.Failure<RuntimeProcessHostResult>(alreadyRunning);
        }

        RuntimeKernelCommand command = new RuntimeKernelCommand.Start(context, AutomationOwner.User);
        RuntimeCommandReceipt receipt = new(
            command: command,
            baselineGeneration: initial.Generation,
            stateChanged: loop.StateChanged,
            isTerminalStatus: IsStartTerminal,
            onTerminal: ClearPendingReceipt,
            createdAt: timeProvider.GetUtcNow(),
            cancellationToken: cancellationToken);

        if (!TryBeginReceipt(receipt, out RuntimeCommandReceipt? existing))
        {
            // The slot is held by another active receipt. We
            // cannot proceed: a concurrent start would race
            // the in-flight kernel transition, and a
            // concurrent stop is the supervisor's concern
            // (StopAsync handles its own slot arbitration).
            ErrorInfo alreadyRunning = new(
                code: "RuntimeSupervisorAlreadyRunning",
                message: "Cannot start the runtime: a start or stop transition is already in progress.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);
            _ = existing;
            return Result.Failure<RuntimeProcessHostResult>(alreadyRunning);
        }

        bool accepted = await loop
            .PostCommandAsync(command, cancellationToken)
            .ConfigureAwait(false);

        if (!accepted)
        {
            // The loop rejected the command (typically because
            // it is being torn down). Release the slot and
            // surface a typed failure.
            receipt.TryCancel();
            ErrorInfo error = new(
                code: "RuntimeKernelLoopNotAcceptingCommands",
                message: "The runtime kernel loop is not accepting new commands.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);
            return Result.Failure<RuntimeProcessHostResult>(error);
        }

        return await AwaitStartResultAsync(receipt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
    {
        return StopCoreAsync(ignoreDisposed: false, cancellationToken);
    }

    /// <summary>
    /// Core stop pipeline shared by the public
    /// <see cref="StopAsync"/> entry point and the
    /// <see cref="IDisposable.Dispose"/> fallback. The
    /// <paramref name="ignoreDisposed"/> flag distinguishes the
    /// two callers:
    /// <list type="bullet">
    ///   <item>The public <c>StopAsync</c> passes
    ///         <c>false</c> and throws
    ///         <see cref="ObjectDisposedException"/> when the
    ///         supervisor is already torn down (lifecycle state
    ///         <see cref="LifecycleState.Disposed"/>).</item>
    ///   <item>The <see cref="IDisposable.Dispose"/> path
    ///         passes <c>true</c>: the supervisor IS being
    ///         disposed (lifecycle state
    ///         <see cref="LifecycleState.Disposing"/>), but the
    ///         stop pipeline must still run to completion so the
    ///         runtime is left in the terminal
    ///         <see cref="RuntimeSupervisorStatus.Stopped"/>
    ///         state.</item>
    /// </list>
    /// </summary>
    /// <param name="ignoreDisposed">
    /// When <c>true</c>, the disposed-state check is skipped so
    /// the stop pipeline can run during disposal. When
    /// <c>false</c>, the disposed-state check is enforced and
    /// an <see cref="ObjectDisposedException"/> is raised if
    /// the supervisor is fully torn down.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed while waiting for the
    /// in-flight stop pipeline to complete.
    /// </param>
    private async Task<Result<Unit>> StopCoreAsync(
        bool ignoreDisposed,
        CancellationToken cancellationToken)
    {
        if (!ignoreDisposed)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref lifecycleState) == (int)LifecycleState.Disposed,
                this);
        }

        RuntimeKernelState initial = loop.CurrentState;
        RuntimeSupervisorStatus current = MapStatus(initial.Status);
        if (current is RuntimeSupervisorStatus.Stopped or RuntimeSupervisorStatus.StartBlocked)
        {
            // Idempotent no-op for an idle / guard-blocked
            // supervisor. The loop has already published the
            // terminal Stopped snapshot, so the receipt
            // shortcut is not even attempted.
            return Result.Success(Unit.Instance);
        }

        RuntimeCancellationReason cancellationReason = ignoreDisposed
            ? RuntimeCancellationReason.HostShutdown
            : RuntimeCancellationReason.UserRequested;
        string reasonText = cancellationReason == RuntimeCancellationReason.HostShutdown
            ? "Host-initiated stop"
            : "User-initiated stop";

        RuntimeKernelCommand command = new RuntimeKernelCommand.Stop(
            RuntimeOperationId.New(),
            reasonText,
            cancellationReason);
        RuntimeCommandReceipt receipt = new(
            command: command,
            baselineGeneration: initial.Generation,
            stateChanged: loop.StateChanged,
            isTerminalStatus: IsStopTerminal,
            onTerminal: ClearPendingReceipt,
            createdAt: timeProvider.GetUtcNow(),
            cancellationToken: cancellationToken);

        if (!TryBeginReceipt(receipt, out RuntimeCommandReceipt? existing))
        {
            return await ResolveStopReceiptConflictAsync(receipt, existing, cancellationToken)
                .ConfigureAwait(false);
        }

        bool accepted = await loop
            .PostCommandAsync(command, cancellationToken)
            .ConfigureAwait(false);

        if (!accepted)
        {
            // The loop rejected the command (typically because
            // it is being torn down). Treat this as a
            // best-effort no-op: complete the receipt with the
            // current state so the awaiter maps the outcome to
            // Success and the slot is cleared.
            receipt.TryCompleteWithState(loop.CurrentState);
            return Result.Success(Unit.Instance);
        }

        return await AwaitStopResultAsync(receipt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the case where <see cref="TryBeginReceipt"/>
    /// refused the new stop receipt because the
    /// <c>pendingReceipt</c> slot is held by another active
    /// receipt. Three sub-cases are handled:
    /// <list type="bullet">
    ///   <item>The existing receipt is a
    ///         <see cref="RuntimeKernelCommand.Stop"/>: the new
    ///         caller shares the existing receipt's completion
    ///         and observes the same terminal outcome.</item>
    ///   <item>The existing receipt is a
    ///         <see cref="RuntimeKernelCommand.Start"/>: the
    ///         existing receipt is superseded (its
    ///         <see cref="RuntimeCommandReceipt.IsSuperseded"/>
    ///         flag is set and its TCS is cancelled so the
    ///         in-flight start caller gets a typed
    ///         <c>SupersededByStop</c> failure), the slot is
    ///         re-arbitrated, and the new stop receipt is
    ///         installed. If a second receipt slipped into
    ///         the slot in the meantime (a race), the new
    ///         caller falls back to sharing the new
    ///         stop receipt or to a typed
    ///         <c>RuntimeSupervisorStopConflict</c>
    ///         failure.</item>
    ///   <item>The slot is empty (the existing receipt
    ///         completed between the <see cref="TryBeginReceipt"/>
    ///         read and the handler): the new stop receipt
    ///         is installed and the post-and-await pipeline
    ///         runs as the normal path would have.</item>
    /// </list>
    /// </summary>
    private async Task<Result<Unit>> ResolveStopReceiptConflictAsync(
        RuntimeCommandReceipt receipt,
        RuntimeCommandReceipt? existing,
        CancellationToken cancellationToken)
    {
        // Case 1: empty slot — the existing receipt completed
        // between TryBeginReceipt's read and our handler.
        // Re-attempt the slot install.
        if (existing is null)
        {
            if (TryBeginReceipt(receipt, out RuntimeCommandReceipt? stillExisting))
            {
                return await PostAndAwaitStopAsync(receipt, cancellationToken).ConfigureAwait(false);
            }

            return await ResolveStopReceiptConflictAsync(receipt, stillExisting, cancellationToken)
                .ConfigureAwait(false);
        }

        // Case 2: another stop is in flight. Share its
        // completion.
        if (existing.Command is RuntimeKernelCommand.Stop)
        {
            return await AwaitSharedStopReceiptAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        // Case 3: a start is in flight. Supersede it and retry
        // the slot install.
        existing.TrySetSuperseded();
        if (TryBeginReceipt(receipt, out RuntimeCommandReceipt? afterSupersede))
        {
            return await PostAndAwaitStopAsync(receipt, cancellationToken).ConfigureAwait(false);
        }

        // A new receipt slipped into the slot between
        // TrySetSuperseded and the re-try. Recurse one level
        // to handle the new conflict.
        if (afterSupersede is not null && afterSupersede.Command is RuntimeKernelCommand.Stop)
        {
            return await AwaitSharedStopReceiptAsync(afterSupersede, cancellationToken).ConfigureAwait(false);
        }

        // Could not establish a stop receipt. The most likely
        // cause is a concurrent start that grabbed the slot
        // after the supersede; surface a typed failure.
        return Result.Failure<Unit>(new ErrorInfo(
            code: "RuntimeSupervisorStopConflict",
            message: "Failed to establish a stop receipt: another transition grabbed the slot after a supersede.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime));
    }

    /// <summary>
    /// Posts the stop command on the supplied receipt and awaits
    /// the terminal state. Used by the
    /// <see cref="ResolveStopReceiptConflictAsync"/> retry path.
    /// </summary>
    private async Task<Result<Unit>> PostAndAwaitStopAsync(
        RuntimeCommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        bool accepted = await loop
            .PostCommandAsync(receipt.Command, cancellationToken)
            .ConfigureAwait(false);

        if (!accepted)
        {
            receipt.TryCompleteWithState(loop.CurrentState);
            return Result.Success(Unit.Instance);
        }

        return await AwaitStopResultAsync(receipt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits the completion of an in-flight stop receipt shared
    /// with a concurrent <see cref="StopAsync"/> caller. Maps
    /// the terminal <see cref="RuntimeKernelState"/> to a
    /// <see cref="Result{T}"/> the same way
    /// <see cref="AwaitStopResultAsync(RuntimeCommandReceipt, CancellationToken)"/>
    /// does, but reuses the existing receipt's
    /// <see cref="TaskCompletionSource{TResult}"/> rather than
    /// installing a new one.
    /// </summary>
    private static async Task<Result<Unit>> AwaitSharedStopReceiptAsync(
        RuntimeCommandReceipt existing,
        CancellationToken cancellationToken)
    {
        RuntimeKernelState terminal;
        try
        {
            terminal = await existing.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User cancellation propagates; supersede
            // cancellations cannot happen here because the
            // existing receipt is a Stop receipt, and the
            // supersede path is only triggered for Start
            // receipts in ResolveStopReceiptConflictAsync.
            throw;
        }

        if (terminal.LastError is { } lastError)
        {
            return Result.Failure<Unit>(lastError);
        }

        return Result.Success(Unit.Instance);
    }

    /// <summary>
    /// Atomically installs <paramref name="receipt"/> into the
    /// <c>pendingReceipt</c> slot. The slot is overwritten when
    /// it is <c>null</c> or when the existing receipt has
    /// already completed (i.e. the existing receipt's terminal
    /// callback fired between the read and the CAS, which is a
    /// benign race because the completion callback is
    /// idempotent).
    /// </summary>
    /// <param name="receipt">
    /// Receipt to install. Must not be <c>null</c>.
    /// </param>
    /// <param name="existing">
    /// When the method returns <c>false</c>, the receipt that
    /// was holding the slot at the moment of the failed CAS.
    /// The supervisor reads <c>existing.Command</c> to decide
    /// between supersede (Start) and share (Stop).
    /// </param>
    /// <returns>
    /// <c>true</c> when the slot now holds
    /// <paramref name="receipt"/>. <c>false</c> when the slot
    /// was held by an active receipt at the moment of the CAS;
    /// <paramref name="existing"/> is set in that case.
    /// </returns>
    private bool TryBeginReceipt(
        RuntimeCommandReceipt receipt,
        out RuntimeCommandReceipt? existing)
    {
        ArgumentNullException.ThrowIfNull(receipt, nameof(receipt));

#pragma warning disable CS0420
        while (true)
        {
            existing = Volatile.Read(ref pendingReceipt);
            if (existing is not null && !existing.IsCompleted)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref pendingReceipt, receipt, existing) == existing)
            {
                return true;
            }
            // CAS failed because another thread changed the
            // slot; re-read and retry.
        }
#pragma warning restore CS0420
    }

    /// <summary>
    /// Terminal callback handed to every
    /// <see cref="RuntimeCommandReceipt"/> the supervisor
    /// creates. Clears the <c>pendingReceipt</c> slot via
    /// <see cref="Interlocked.CompareExchange(ref object, object, object)"/>
    /// so the slot is only cleared if it still points at
    /// <paramref name="receipt"/>. A different receipt that
    /// was installed in the meantime is preserved.
    /// </summary>
    /// <param name="receipt">
    /// Receipt that just completed. The slot is cleared only
    /// if it still points at this receipt.
    /// </param>
    private void ClearPendingReceipt(RuntimeCommandReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt, nameof(receipt));
        Interlocked.CompareExchange(ref pendingReceipt, null, receipt);
    }

    /// <summary>
    /// Health-snapshot handler. The handler runs on the
    /// <see cref="IRuntimeHealthMonitor"/>'s publishing thread,
    /// so the snapshot is forwarded to the loop as an
    /// <see cref="RuntimeKernelCommand.Observation"/> for
    /// processing on the kernel thread. The loop owns all
    /// guard and state transitions; the supervisor's only
    /// responsibility is to bridge the observable.
    /// </summary>
    /// <param name="snapshot">Newly published health snapshot.</param>
    private void OnHealthSnapshot(RuntimeHealthSnapshot snapshot)
    {
        if (Volatile.Read(ref lifecycleState) != (int)LifecycleState.Active)
        {
            return;
        }

        try
        {
            // Best-effort post: if the loop has stopped accepting
            // commands we silently drop the snapshot. The kernel
            // thread is the only state mutator, so we never
            // attempt to mutate state from this thread directly.
            // AsTask() converts the returned ValueTask to a Task
            // so the discard satisfies CA2012; the operation
            // itself still runs to completion on the kernel loop
            // thread regardless of whether the Task is observed.
            _ = loop.PostCommandAsync(
                new RuntimeKernelCommand.Observation(RuntimeOperationId.New(), snapshot),
                CancellationToken.None).AsTask();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "RuntimeSupervisor: failed to forward a health snapshot to the kernel loop.");
        }
    }

    /// <summary>
    /// Awaits the start <see cref="RuntimeCommandReceipt"/>
    /// until the supervisor reaches one of the start terminal
    /// states (<see cref="RuntimeSupervisorStatus.Running"/>,
    /// <see cref="RuntimeSupervisorStatus.Stopped"/> or
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/>) and
    /// returns the mapped result. The receipt's
    /// <see cref="RuntimeCommandReceipt.IsSuperseded"/> flag
    /// is consulted so a stop-during-start race is surfaced as
    /// a typed <c>RuntimeSupervisorStartSupersededByStop</c>
    /// failure rather than the natural terminal
    /// snapshot.
    /// </summary>
    /// <param name="receipt">Start receipt to await.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<Result<RuntimeProcessHostResult>> AwaitStartResultAsync(
        RuntimeCommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        RuntimeKernelState terminal;
        try
        {
            terminal = await receipt.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (receipt.IsSuperseded)
        {
            return Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "RuntimeSupervisorStartSupersededByStop",
                message: "The start was superseded by a stop request before the kernel reached a terminal state.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        if (receipt.IsSuperseded)
        {
            // The receipt completed via TrySetSuperseded's
            // TrySetCanceled (which the design clears the
            // receipt and then immediately supersedes with a
            // state). The race window between the two CAS
            // ops is benign: if the natural terminal
            // resolution won, the awaiter gets the state
            // and we still consult IsSuperseded here.
            return Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "RuntimeSupervisorStartSupersededByStop",
                message: "The start was superseded by a stop request before the kernel reached a terminal state.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        if (terminal.Status == RuntimeKernelStatus.Running)
        {
            if (terminal.LastStartResult is { } payload)
            {
                return Result.Success(payload);
            }

            // The reducer should always carry the start payload
            // through to a Running state, but defend against a
            // misshaped kernel state.
            ErrorInfo error = new(
                code: "RuntimeStartResultMissing",
                message: "The runtime reached Running without an attached start result.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);
            return Result.Failure<RuntimeProcessHostResult>(error);
        }

        // StartBlocked or Stopped (host failure) — surface the
        // kernel's LastError. The snapshot carries either a guard
        // block message or a host failure message verbatim.
        if (terminal.LastError is { } lastError)
        {
            return Result.Failure<RuntimeProcessHostResult>(lastError);
        }

        ErrorInfo unknown = new(
            code: "RuntimeStartFailed",
            message: "The runtime start did not reach Running and no error was reported.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);
        return Result.Failure<RuntimeProcessHostResult>(unknown);
    }

    /// <summary>
    /// Awaits the stop <see cref="RuntimeCommandReceipt"/> until
    /// the supervisor reaches one of the stop terminal states
    /// (<see cref="RuntimeSupervisorStatus.Stopped"/> or
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/>) and
    /// returns the mapped result.
    /// </summary>
    /// <param name="receipt">Stop receipt to await.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<Result<Unit>> AwaitStopResultAsync(
        RuntimeCommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        RuntimeKernelState terminal;
        try
        {
            terminal = await receipt.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User cancellation propagates. The stop
            // receipt's IsSuperseded flag is never set
            // (supersede is reserved for the start
            // receipt), so we rethrow unconditionally.
            throw;
        }

        if (terminal.LastError is { } lastError)
        {
            return Result.Failure<Unit>(lastError);
        }

        return Result.Success(Unit.Instance);
    }

    private static bool IsStartTerminal(RuntimeKernelStatus status) =>
        status is RuntimeKernelStatus.Running
            or RuntimeKernelStatus.Stopped
            or RuntimeKernelStatus.StartBlocked;

    private static bool IsStopTerminal(RuntimeKernelStatus status) =>
        status is RuntimeKernelStatus.Stopped
            or RuntimeKernelStatus.StartBlocked;

    /// <summary>
    /// Projects a <see cref="RuntimeKernelState"/> snapshot onto
    /// the <see cref="RuntimeSupervisorState"/> shape that the
    /// public <see cref="IRuntimeSupervisor"/> contract exposes.
    /// The mapping is a 1:1 lift of the status and a straight
    /// copy of <c>LastStartResult</c>, <c>GuardResult</c>,
    /// <c>LastError</c> and <c>Timestamp</c>.
    /// </summary>
    /// <param name="state">Kernel state to project.</param>
    private static RuntimeSupervisorState MapToSupervisorState(RuntimeKernelState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new RuntimeSupervisorState(
            status: MapStatus(state.Status),
            lastStartResult: state.LastStartResult,
            guardResult: state.GuardResult,
            lastError: state.LastError,
            timestamp: state.Timestamp);
    }

    /// <summary>
    /// Maps a <see cref="RuntimeKernelStatus"/> to the equivalent
    /// <see cref="RuntimeSupervisorStatus"/>. The two enums share
    /// the same numeric layout, so a straight cast is sufficient
    /// and the cast is documented here to keep the two enums
    /// from drifting in future refactors.
    /// </summary>
    /// <param name="status">Kernel status to lift.</param>
    private static RuntimeSupervisorStatus MapStatus(RuntimeKernelStatus status)
    {
        // The two enums are intentionally aligned; an
        // (int)-based cast avoids an Enum.IsDefined check and
        // keeps the mapping allocation-free.
        return (RuntimeSupervisorStatus)(int)status;
    }

    /// <summary>
    /// Disposes the health-monitor subscription exactly once and
    /// detaches the local field. Safe to call from any thread.
    /// </summary>
    private void DisposeHealthSubscription()
    {
        IDisposable? subscription = Interlocked.Exchange(ref healthSubscription, null);
        subscription?.Dispose();
    }

    /// <summary>
    /// Test-only seam: returns <c>true</c> when the
    /// <c>pendingReceipt</c> slot is currently holding an
    /// <see cref="RuntimeCommandReceipt"/>. Used by the
    /// supervisor's own test suite to verify that the slot is
    /// cleared after a terminal state is observed. Not part of
    /// the public <see cref="IRuntimeSupervisor"/> contract.
    /// </summary>
    internal bool HasPendingReceiptForTests
    {
        get
        {
#pragma warning disable CS0420
            return Volatile.Read(ref pendingReceipt) is not null;
#pragma warning restore CS0420
        }
    }
}

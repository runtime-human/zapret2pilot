using System;
using System.Reactive.Disposables;
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
/// 0.0.24 the supervisor is a pure façade over
/// <see cref="RuntimeKernelLoop"/>: the loop owns the state
/// machine, the guard and the lifecycle command queue; the
/// supervisor is a thin adapter that:
/// <list type="bullet">
///   <item>Translates the public <see cref="StartAsync"/> /
///         <see cref="StopAsync"/> calls into
///         <see cref="RuntimeKernelCommand"/>s and forwards them to
///         the loop.</item>
///   <item>Awaits the loop's projected
///         <see cref="RuntimeKernelState"/> observable until the
///         terminal status (<see cref="RuntimeSupervisorStatus.Running"/>,
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
/// <b>Concurrency.</b> The supervisor uses a private
/// <see cref="SemaphoreSlim"/> to serialise concurrent
/// <see cref="StartAsync"/> and <see cref="StopAsync"/> calls. The
/// semaphore is never held across the call to the loop's blocking
/// <c>await</c> on the projected observable, so the critical
/// section is short and deadlocks are structurally impossible.
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

    private readonly SemaphoreSlim startStopLock = new(1, 1);
    private IDisposable? healthSubscription;
    private int lifecycleState; // stores a LifecycleState value

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

        try
        {
            // ignoreDisposed: true — the supervisor IS being
            // disposed, but the in-flight stop pipeline must
            // still run to completion. The previous binary
            // `int disposed` flag forced this call to short
            // -circuit with ObjectDisposedException, which made
            // Dispose a logged failure rather than a graceful
            // shutdown.
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

            try
            {
                startStopLock.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RuntimeSupervisor: startStopLock disposal failed.");
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

        await startStopLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RuntimeKernelState initial = loop.CurrentState;
            RuntimeSupervisorStatus current = MapStatus(initial.Status);
            if (current is RuntimeSupervisorStatus.Running
                or RuntimeSupervisorStatus.Starting
                or RuntimeSupervisorStatus.Stopping)
            {
                ErrorInfo alreadyRunning = new(
                    code: "RuntimeSupervisorAlreadyRunning",
                    message: "Cannot start the runtime: a start or stop transition is already in progress.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return Result.Failure<RuntimeProcessHostResult>(alreadyRunning);
            }

            bool accepted = await loop
                .PostCommandAsync(
                    new RuntimeKernelCommand.Start(context, AutomationOwner.User),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!accepted)
            {
                ErrorInfo error = new(
                    code: "RuntimeKernelLoopNotAcceptingCommands",
                    message: "The runtime kernel loop is not accepting new commands.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return Result.Failure<RuntimeProcessHostResult>(error);
            }

            return await AwaitStartResultAsync(initial, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            startStopLock.Release();
        }
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

        await startStopLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RuntimeKernelState initial = loop.CurrentState;
            RuntimeSupervisorStatus current = MapStatus(initial.Status);
            if (current is RuntimeSupervisorStatus.Stopped or RuntimeSupervisorStatus.StartBlocked)
            {
                // Idempotent no-op for an idle / guard-blocked
                // supervisor. The loop has already published the
                // terminal Stopped snapshot.
                return Result.Success(Unit.Instance);
            }

            bool accepted = await loop
                .PostCommandAsync(
                    new RuntimeKernelCommand.Stop(
                        RuntimeOperationId.New(),
                        "User-initiated stop"),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!accepted)
            {
                // The loop rejected the command (typically because
                // it is being torn down). Treat this as a
                // best-effort no-op.
                return Result.Success(Unit.Instance);
            }

            return await AwaitStopResultAsync(initial, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            startStopLock.Release();
        }
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
    /// Awaits the projected kernel observable until the
    /// supervisor reaches one of the start terminal states
    /// (<see cref="RuntimeSupervisorStatus.Running"/>,
    /// <see cref="RuntimeSupervisorStatus.Stopped"/> or
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/>) and
    /// returns the mapped result.
    /// </summary>
    /// <param name="initial">
    /// State captured before the start command was posted; used
    /// as the baseline so the projection does not race against
    /// a stale in-flight terminal state.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<Result<RuntimeProcessHostResult>> AwaitStartResultAsync(
        RuntimeKernelState initial,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<RuntimeKernelState> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // The kernel's StateChanged is backed by a BehaviorSubject
        // observed on the task pool, so a new subscription
        // immediately receives the latest published state. The
        // initial state of a fresh loop is Stopped (which is in
        // IsStartTerminal), so without the generation filter the
        // subscription would resolve the TCS with the baseline
        // snapshot before the kernel has even processed the
        // Start command. Restricting to states with a strictly
        // newer generation than the captured baseline makes the
        // await deterministic.
        using IDisposable subscription = loop.StateChanged
            .Where(state =>
                state.Generation.Value > initial.Generation.Value
                && IsStartTerminal(state.Status))
            .Subscribe(
                state => tcs.TrySetResult(state),
                ex => tcs.TrySetException(ex));

        RuntimeKernelState terminal;
        try
        {
            terminal = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
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
    /// Awaits the projected kernel observable until the
    /// supervisor reaches one of the stop terminal states
    /// (<see cref="RuntimeSupervisorStatus.Stopped"/> or
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/>) and
    /// returns the mapped result.
    /// </summary>
    /// <param name="initial">
    /// State captured before the stop command was posted; used as
    /// the generation baseline so the BehaviorSubject's replay of
    /// the in-flight Stopping snapshot does not prematurely
    /// resolve the await.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<Result<Unit>> AwaitStopResultAsync(
        RuntimeKernelState initial,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<RuntimeKernelState> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = loop.StateChanged
            .Where(state =>
                state.Generation.Value > initial.Generation.Value
                && IsStopTerminal(state.Status))
            .Subscribe(
                state => tcs.TrySetResult(state),
                ex => tcs.TrySetException(ex));

        RuntimeKernelState terminal;
        try
        {
            terminal = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
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
}

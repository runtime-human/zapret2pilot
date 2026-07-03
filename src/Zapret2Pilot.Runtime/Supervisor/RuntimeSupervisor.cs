using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Supervisor;

/// <summary>
/// <see cref="IRuntimeSupervisor"/> implementation. Owns the
/// runtime start / stop state machine, integrates
/// <see cref="ICrashLoopGuard"/> with
/// <see cref="IRuntimeProcessHost"/> and
/// <see cref="IRuntimeHealthMonitor"/>, and publishes an immutable
/// <see cref="RuntimeSupervisorState"/> on every transition through
/// <see cref="StateChanged"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>State machine.</b> The supervisor walks through
/// <see cref="RuntimeSupervisorStatus"/> values as follows:
/// <c>Stopped</c> → <c>Starting</c> → <c>Running</c> →
/// <c>Stopping</c> → <c>Stopped</c>, with two off-path states:
/// <c>StartBlocked</c> (the guard refused the restart) and
/// <c>Stopping</c> triggered by a <see cref="RuntimeHealthState.Exited"/>
/// health snapshot.
/// </para>
/// <para>
/// <b>Guard integration.</b> Every <see cref="StartAsync"/> call
/// is preceded by <see cref="ICrashLoopGuard.Check"/>. A guard
/// block returns a typed failure with a code of
/// <c>RuntimePermanentLockout</c> or
/// <c>RuntimeStartBlockedByCrashLoopGuard</c>. A failed start calls
/// <see cref="ICrashLoopGuard.RecordFailure"/>. A transition into
/// <see cref="RuntimeHealthState.Healthy"/> calls
/// <see cref="ICrashLoopGuard.RecordSuccess"/>; a transition into
/// <see cref="RuntimeHealthState.Exited"/> calls
/// <see cref="ICrashLoopGuard.RecordFailure"/> and triggers an
/// automatic <see cref="StopAsync"/>.
/// </para>
/// <para>
/// <b>Concurrency.</b> The supervisor uses a
/// <see cref="SemaphoreSlim"/> to serialise
/// <see cref="StartAsync"/> and <see cref="StopAsync"/>. The health
/// snapshot handler also takes the semaphore, briefly, before
/// releasing it and (when needed) calling
/// <see cref="StopAsync"/>. The semaphore is never held across an
/// <c>await</c> of a public state-mutating method, which keeps
/// deadlocks structurally impossible.
/// </para>
/// <para>
/// <b>Hosted-service contract.</b> The supervisor implements
/// <see cref="IHostedService"/>. <see cref="StartAsync"/>
/// subscribes to <see cref="IRuntimeHealthMonitor.SnapshotChanged"/>;
/// <see cref="StopAsync"/> unsubscribes and then performs a final
/// <see cref="StopAsync(CancellationToken)"/>. <see cref="Dispose"/>
/// is idempotent and tears down the supervisor even if the hosted
/// service was never started.
/// </para>
/// </remarks>
public sealed class RuntimeSupervisor : IRuntimeSupervisor, IHostedService, IDisposable
{
    private readonly IRuntimeProcessHost host;
    private readonly IRuntimeHealthMonitor healthMonitor;
    private readonly ICrashLoopGuard guard;
    private readonly ILogger<RuntimeSupervisor> logger;
    private readonly TimeProvider timeProvider;

    private readonly BehaviorSubject<RuntimeSupervisorState> stateSubject;
    private readonly SemaphoreSlim startStopLock = new(1, 1);
    private readonly object subscriptionLock = new();
    private IDisposable? healthSubscription;
    private RuntimeHealthState lastHealthState = RuntimeHealthState.Unknown;
    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="RuntimeSupervisor"/>.
    /// </summary>
    /// <param name="host">
    /// Process host that performs the actual start / stop pipeline.
    /// Must not be <c>null</c>.
    /// </param>
    /// <param name="healthMonitor">
    /// Health monitor whose <see cref="IRuntimeHealthMonitor.SnapshotChanged"/>
    /// observable drives the supervisor's automatic
    /// success / failure recording. Must not be <c>null</c>.
    /// </param>
    /// <param name="guard">
    /// Crash-loop guard consulted on every <see cref="StartAsync"/>
    /// call. Must not be <c>null</c>.
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
    /// Thrown when <paramref name="host"/>,
    /// <paramref name="healthMonitor"/>, <paramref name="guard"/>
    /// or <paramref name="logger"/> is <c>null</c>.
    /// </exception>
    public RuntimeSupervisor(
        IRuntimeProcessHost host,
        IRuntimeHealthMonitor healthMonitor,
        ICrashLoopGuard guard,
        ILogger<RuntimeSupervisor> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host, nameof(host));
        ArgumentNullException.ThrowIfNull(healthMonitor, nameof(healthMonitor));
        ArgumentNullException.ThrowIfNull(guard, nameof(guard));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        this.host = host;
        this.healthMonitor = healthMonitor;
        this.guard = guard;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.stateSubject = new BehaviorSubject<RuntimeSupervisorState>(
            new RuntimeSupervisorState(
                status: RuntimeSupervisorStatus.Stopped,
                lastStartResult: null,
                guardResult: null,
                lastError: null,
                timestamp: this.timeProvider.GetUtcNow()));
    }

    /// <inheritdoc />
    public RuntimeSupervisorState CurrentState => stateSubject.Value;

    /// <inheritdoc />
    public IObservable<RuntimeSupervisorState> StateChanged => stateSubject.AsObservable();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (subscriptionLock)
        {
            if (healthSubscription is null)
            {
                healthSubscription = healthMonitor.SnapshotChanged.Subscribe(OnHealthSnapshot);
            }
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
        IDisposable? subscription;
        lock (subscriptionLock)
        {
            subscription = healthSubscription;
            healthSubscription = null;
        }

        subscription?.Dispose();

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
        if (disposed)
        {
            return;
        }

        disposed = true;

        IDisposable? subscription;
        lock (subscriptionLock)
        {
            subscription = healthSubscription;
            healthSubscription = null;
        }

        subscription?.Dispose();

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeSupervisor: Dispose triggered a failed stop.");
        }
        finally
        {
            startStopLock.Dispose();
            stateSubject.OnCompleted();
            stateSubject.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<Result<RuntimeProcessHostResult>> StartAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(context, nameof(context));

        await startStopLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool lockHeld = true;
        try
        {
            RuntimeSupervisorStatus current = CurrentState.Status;
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

            // Guard check: a guard block short-circuits the start
            // before any work is forwarded to the process host. The
            // published state carries the guard verdict so the UI
            // can show the right message.
            CrashLoopGuardResult guardResult = guard.Check();
            if (!guardResult.IsAllowed)
            {
                bool isPermanentLockout = guardResult.ConsecutiveFailures
                    > CurrentGuardMaxConsecutiveFailures();
                ErrorInfo guardError = isPermanentLockout
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

                logger.LogWarning(
                    "RuntimeSupervisor: start blocked by the crash-loop guard. Code={Code} ConsecutiveFailures={ConsecutiveFailures} BackoffRemainingSeconds={BackoffSeconds}",
                    guardError.Code,
                    guardResult.ConsecutiveFailures,
                    guardResult.BackoffRemaining?.TotalSeconds ?? -1);

                PublishState(new RuntimeSupervisorState(
                    status: RuntimeSupervisorStatus.StartBlocked,
                    lastStartResult: CurrentState.LastStartResult,
                    guardResult: guardResult,
                    lastError: guardError,
                    timestamp: timeProvider.GetUtcNow()));

                return Result.Failure<RuntimeProcessHostResult>(guardError);
            }

            // Move into Starting before releasing the lock. The
            // actual host.StartAsync runs without the lock to keep
            // the critical section short and to allow other
            // observers (e.g. the health monitor) to see the
            // transition into Starting promptly.
            PublishState(new RuntimeSupervisorState(
                status: RuntimeSupervisorStatus.Starting,
                lastStartResult: CurrentState.LastStartResult,
                guardResult: guardResult,
                lastError: null,
                timestamp: timeProvider.GetUtcNow()));
        }
        finally
        {
            if (lockHeld)
            {
                startStopLock.Release();
                lockHeld = false;
            }
        }

        // Outside the lock: the host call can take arbitrary time.
        Result<RuntimeProcessHostResult> hostResult = await host
            .StartAsync(context, cancellationToken)
            .ConfigureAwait(false);

        await startStopLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        lockHeld = true;
        try
        {
            if (hostResult.IsFailure)
            {
                guard.RecordFailure();
                ErrorInfo hostError = hostResult.Error;
                logger.LogError(
                    "RuntimeSupervisor: host start failed. Code={Code} Message={Message}",
                    hostError.Code,
                    hostError.Message);

                PublishState(new RuntimeSupervisorState(
                    status: RuntimeSupervisorStatus.Stopped,
                    lastStartResult: null,
                    guardResult: CurrentState.GuardResult,
                    lastError: hostError,
                    timestamp: timeProvider.GetUtcNow()));

                return hostResult;
            }

            PublishState(new RuntimeSupervisorState(
                status: RuntimeSupervisorStatus.Running,
                lastStartResult: hostResult.Value,
                guardResult: CurrentState.GuardResult,
                lastError: null,
                timestamp: timeProvider.GetUtcNow()));

            return hostResult;
        }
        finally
        {
            if (lockHeld)
            {
                startStopLock.Release();
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await startStopLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool lockHeld = true;
        bool alreadyStopping = false;
        ErrorInfo? preservedLastError = null;
        RuntimeSupervisorStatus current;
        try
        {
            current = CurrentState.Status;
            if (current is RuntimeSupervisorStatus.Stopped or RuntimeSupervisorStatus.StartBlocked)
            {
                // Idempotent no-op for an idle / guard-blocked
                // supervisor. The published state is re-asserted
                // only when the carryover values changed.
                RuntimeSupervisorState resolved = new(
                    status: RuntimeSupervisorStatus.Stopped,
                    lastStartResult: null,
                    guardResult: CurrentState.GuardResult,
                    lastError: null,
                    timestamp: timeProvider.GetUtcNow());
                PublishState(resolved);
                return Result.Success(Unit.Instance);
            }

            if (current is RuntimeSupervisorStatus.Stopping)
            {
                // The status is already Stopping — typically because
                // the Exited health-snapshot handler published the
                // transition and queued this StopAsync call. Do not
                // re-publish Stopping and do not short-circuit: the
                // host-stop pipeline and the terminal Stopped
                // snapshot must still be driven so callers waiting
                // for the final transition can complete. The
                // startStopLock serialises concurrent callers, so
                // by the time a second StopAsync acquires the
                // semaphore the first one will have already
                // published Stopped and the idempotent branch above
                // will handle it.
                alreadyStopping = true;
                preservedLastError = CurrentState.LastError;
            }
            else
            {
                PublishState(new RuntimeSupervisorState(
                    status: RuntimeSupervisorStatus.Stopping,
                    lastStartResult: CurrentState.LastStartResult,
                    guardResult: CurrentState.GuardResult,
                    lastError: null,
                    timestamp: timeProvider.GetUtcNow()));
            }
        }
        finally
        {
            if (lockHeld)
            {
                startStopLock.Release();
                lockHeld = false;
            }
        }

        Result<Unit> stopResult = await host.StopAsync(cancellationToken).ConfigureAwait(false);

        await startStopLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        lockHeld = true;
        try
        {
            // When the stop was initiated automatically from the
            // Exited handler the Stopping snapshot already carried a
            // meaningful LastError. Preserve it on the terminal
            // Stopped publish so the reason for the unexpected
            // exit is not lost. For user-initiated stops the normal
            // host-stop result wins.
            ErrorInfo? lastError = alreadyStopping && preservedLastError is not null
                ? preservedLastError
                : (stopResult.IsFailure ? stopResult.Error : null);
            RuntimeSupervisorState resolved = new(
                status: RuntimeSupervisorStatus.Stopped,
                lastStartResult: null,
                guardResult: CurrentState.GuardResult,
                lastError: lastError,
                timestamp: timeProvider.GetUtcNow());
            PublishState(resolved);
            return stopResult;
        }
        finally
        {
            if (lockHeld)
            {
                startStopLock.Release();
            }
        }
    }

    /// <summary>
    /// Health-snapshot handler. The handler runs on the
    /// <see cref="IRuntimeHealthMonitor"/>'s publishing thread, so
    /// the supervisor must serialise access to its own state and
    /// release the lock before invoking <see cref="StopAsync"/>
    /// (re-entering the semaphore would deadlock).
    /// </summary>
    /// <param name="snapshot">Newly published health snapshot.</param>
    private void OnHealthSnapshot(RuntimeHealthSnapshot snapshot)
    {
        if (disposed)
        {
            return;
        }

        startStopLock.Wait();
        try
        {
            RuntimeSupervisorState current = CurrentState;
            RuntimeHealthState previous = lastHealthState;
            lastHealthState = snapshot.State;

            if (snapshot.State == RuntimeHealthState.Healthy
                && previous != RuntimeHealthState.Healthy)
            {
                guard.RecordSuccess();
                if (current.Status is RuntimeSupervisorStatus.Starting
                    or RuntimeSupervisorStatus.Running)
                {
                    PublishState(current with
                    {
                        Status = RuntimeSupervisorStatus.Running,
                        Timestamp = timeProvider.GetUtcNow(),
                        LastError = null,
                    });
                }
                return;
            }

            if (snapshot.State == RuntimeHealthState.Exited
                && previous != RuntimeHealthState.Exited
                && (current.Status is RuntimeSupervisorStatus.Running
                    or RuntimeSupervisorStatus.Starting))
            {
                guard.RecordFailure();
                logger.LogWarning(
                    "RuntimeSupervisor: health monitor reported Exited; recording a failure and stopping the runtime.");

                PublishState(current with
                {
                    Status = RuntimeSupervisorStatus.Stopping,
                    Timestamp = timeProvider.GetUtcNow(),
                    LastError = new ErrorInfo(
                        code: "RuntimeProcessExitedUnexpectedly",
                        message: "The runtime process exited unexpectedly.",
                        severity: ErrorSeverity.Error,
                        category: ErrorCategory.Runtime),
                });
            }
        }
        finally
        {
            startStopLock.Release();
        }

        // After releasing the lock, drive the automatic stop when
        // the health snapshot reported an unexpected exit. Calling
        // StopAsync while holding the semaphore would deadlock, so
        // the trigger is queued up here for execution outside the
        // critical section.
        if (!disposed && CurrentState.Status == RuntimeSupervisorStatus.Stopping)
        {
            _ = StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Publishes a new <see cref="RuntimeSupervisorState"/> snapshot
    /// to subscribers. The publish is best-effort: a slow or
    /// throwing subscriber cannot fault the supervisor, because the
    /// <see cref="BehaviorSubject{T}"/> catches observer
    /// exceptions in the same way every other Rx helper does.
    /// </summary>
    /// <param name="state">Snapshot to publish.</param>
    private void PublishState(RuntimeSupervisorState state)
    {
        stateSubject.OnNext(state);
    }

    /// <summary>
    /// Returns the configured
    /// <see cref="CrashLoopGuardOptions.MaxConsecutiveFailures"/>
    /// when the guard exposes the option, or
    /// <see cref="CrashLoopGuardOptions.DefaultMaxConsecutiveFailures"/>
    /// as a safe fallback. Used to discriminate a temporary backoff
    /// from a permanent lockout without having to introspect the
    /// guard's private counter.
    /// </summary>
    private static int CurrentGuardMaxConsecutiveFailures()
    {
        // The guard interface deliberately does not expose
        // MaxConsecutiveFailures, so the supervisor relies on the
        // documented default. The CrashLoopGuardOptions type lives
        // in the same assembly as the guard and is therefore
        // available through this internal helper.
        return CrashLoopGuardOptions.DefaultMaxConsecutiveFailures;
    }
}

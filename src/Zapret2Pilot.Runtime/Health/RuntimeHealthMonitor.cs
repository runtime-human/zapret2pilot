using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.State;

namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Generic-Host <see cref="IHostedService"/> that periodically
/// probes the runtime process owned by <see cref="RuntimeProcessHost"/>
/// and publishes immutable <see cref="RuntimeHealthSnapshot"/>
/// values. The probe body itself runs on the dedicated
/// <see cref="RuntimeKernelWorker"/> thread so every read of the
/// host's process state happens on the canonical kernel thread.
/// </summary>
/// <remarks>
/// <para>
/// The monitor owns a <see cref="PeriodicTimer"/> driven by an
/// injected <see cref="TimeProvider"/>. The loop runs on a
/// dedicated <see cref="Task"/> scheduled via
/// <see cref="Task.Run(Action)"/>; on every tick the loop enqueues
/// a probe onto the worker, awaits the worker's task, and only then
/// pushes the freshly computed snapshot through the
/// <see cref="BehaviorSubject{T}"/>. Because the
/// <c>OnNext</c> call happens on the monitor's loop task, a slow or
/// throwing subscriber cannot fault the kernel worker.
/// </para>
/// <para>
/// Probes are coalesced: only one probe is allowed to be in flight
/// at a time. If a tick fires while a probe is pending, the loop
/// drops the tick (it does not enqueue a second probe). The
/// coalescing flag is manipulated via
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> so
/// the loop is safe under any interleaving with the worker thread.
/// </para>
/// <para>
/// A transition into <see cref="RuntimeHealthState.Exited"/> is
/// the single signal the monitor uses to mark a session as
/// <see cref="RuntimeSessionState.Failed"/>. The transition is
/// detected by comparing the freshly built snapshot's
/// <see cref="RuntimeHealthSnapshot.State"/> against the
/// <see cref="BehaviorSubject{T}.Value"/> before the new snapshot is
/// published. Healthy and Exited probes that do not change the
/// state do not re-record the session.
/// </para>
/// <para>
/// <see cref="StartAsync"/> starts the loop. <see cref="StopAsync"/>
/// cancels the loop, disposes the timer and awaits the loop task
/// (using the caller's <see cref="CancellationToken"/> as an upper
/// bound) so the monitor never publishes a snapshot after
/// <see cref="StopAsync"/> returns. <see cref="Dispose"/> is
/// idempotent and forwards to <see cref="StopAsync"/>.
/// </para>
/// </remarks>
public sealed class RuntimeHealthMonitor : IHostedService, IRuntimeHealthMonitor, IDisposable
{
    /// <summary>
    /// Default probe interval applied when the caller does not
    /// supply one. 500 ms is a reasonable balance between
    /// responsiveness and overhead for a hosted health probe.
    /// </summary>
    internal static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromMilliseconds(500);

    private readonly RuntimeProcessHost host;
    private readonly RuntimeKernelWorker worker;
    private readonly IRuntimeKernelStateStore stateStore;
    private readonly ILogger<RuntimeHealthMonitor> logger;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan probeInterval;
    private readonly BehaviorSubject<RuntimeHealthSnapshot> subject;

    private readonly object timerLock = new();
    private PeriodicTimer? timer;
    private CancellationTokenSource? loopCts;
    private Task? loopTask;
    private int probePending; // 0 = idle, 1 = probe in flight

    // Cross-thread handoff slot: the worker writes the freshly computed
    // snapshot, the loop task reads it after `await probeTask`. The
    // await provides the happens-before edge that makes the write
    // visible to the read without an explicit lock or barrier.
    private RuntimeHealthSnapshot? nextSnapshot;
    private int stoppingFlag;
    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="RuntimeHealthMonitor"/>.
    /// </summary>
    /// <param name="host">Runtime process host that owns the live
    /// process to probe.</param>
    /// <param name="worker">Dedicated kernel worker thread used to
    /// run the probe body.</param>
    /// <param name="stateStore">Persistent kernel state store used
    /// to mark sessions as <c>Failed</c>.</param>
    /// <param name="logger">Logger that receives structured events
    /// for unexpected errors and state-store failures.</param>
    /// <param name="timeProvider">Time provider that drives the
    /// probe timer. Defaults to <see cref="TimeProvider.System"/>
    /// when <c>null</c>.</param>
    /// <param name="probeInterval">Probe interval. Defaults to
    /// <see cref="DefaultProbeInterval"/> when <c>null</c>.</param>
    public RuntimeHealthMonitor(
        RuntimeProcessHost host,
        RuntimeKernelWorker worker,
        IRuntimeKernelStateStore stateStore,
        ILogger<RuntimeHealthMonitor> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? probeInterval = null)
    {
        ArgumentNullException.ThrowIfNull(host, nameof(host));
        ArgumentNullException.ThrowIfNull(worker, nameof(worker));
        ArgumentNullException.ThrowIfNull(stateStore, nameof(stateStore));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        TimeSpan effectiveInterval = probeInterval ?? DefaultProbeInterval;
        if (effectiveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probeInterval),
                effectiveInterval,
                "Probe interval must be a positive time span.");
        }

        this.host = host;
        this.worker = worker;
        this.stateStore = stateStore;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.probeInterval = effectiveInterval;
        this.subject = new BehaviorSubject<RuntimeHealthSnapshot>(
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Unknown,
                processId: null,
                observedAtUtc: DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public IObservable<RuntimeHealthSnapshot> SnapshotChanged => subject.AsObservable();

    /// <inheritdoc />
    public RuntimeHealthSnapshot LatestSnapshot => subject.Value;

    /// <summary>
    /// Starts the probe loop. Returns immediately; the first
    /// scheduled probe fires after <see cref="probeInterval"/>.
    /// </summary>
    /// <param name="cancellationToken">Observed before the loop
    /// task is scheduled.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (timerLock)
        {
            if (loopTask is not null)
            {
                return Task.CompletedTask;
            }

            CancellationTokenSource newCts = new();
            PeriodicTimer newTimer = new(probeInterval, timeProvider);
            loopCts = newCts;
            timer = newTimer;
            loopTask = Task.Run(() => RunLoopAsync(newCts.Token), cancellationToken);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the probe loop. Idempotent. Cancels the loop's
    /// <see cref="CancellationTokenSource"/>, disposes the
    /// <see cref="PeriodicTimer"/> and awaits the loop task (using
    /// the caller's <see cref="CancellationToken"/> as an upper
    /// bound) so the monitor never publishes a snapshot after
    /// <see cref="StopAsync"/> returns.
    /// </summary>
    /// <param name="cancellationToken">Observed while waiting for
    /// the loop task to complete.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? toCancel;
        PeriodicTimer? toDispose;
        Task? toAwait;

        lock (timerLock)
        {
            toCancel = loopCts;
            toDispose = timer;
            toAwait = loopTask;
            loopCts = null;
            timer = null;
            loopTask = null;
        }

        Interlocked.Exchange(ref stoppingFlag, 1);

        toCancel?.Cancel();
        // Disposing the PeriodicTimer unblocks any in-flight
        // WaitForNextTickAsync call by completing it with `false`,
        // so the loop returns from the await and exits.
        toDispose?.Dispose();

        if (toAwait is not null)
        {
            try
            {
                await Task.WhenAny(toAwait, Task.Delay(Timeout.Infinite, cancellationToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when the host cancels shutdown
            }
        }
    }

    /// <summary>
    /// Disposes the monitor. Idempotent; calls
    /// <see cref="StopAsync"/> with <see cref="CancellationToken.None"/>
    /// and completes the <see cref="BehaviorSubject{T}"/>.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeHealthMonitor: StopAsync threw during Dispose.");
        }
        finally
        {
            subject.OnCompleted();
            subject.Dispose();
        }
    }

    /// <summary>
    /// Probe-loop body. Runs on a dedicated task scheduled by
    /// <see cref="StartAsync"/>; on every <see cref="PeriodicTimer"/>
    /// tick it enqueues a probe onto the worker, awaits the
    /// worker's task, and pushes the freshly computed snapshot
    /// through the <see cref="BehaviorSubject{T}"/>.
    /// </summary>
    /// <param name="token">Cancellation token that combines the
    /// monitor's internal <see cref="CancellationTokenSource"/>
    /// with the caller's <see cref="StopAsync"/> token.</param>
    private async Task RunLoopAsync(CancellationToken token)
    {
        PeriodicTimer? localTimer;
        lock (timerLock)
        {
            localTimer = timer;
        }

        if (localTimer is null)
        {
            return;
        }

        try
        {
            while (await localTimer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (Volatile.Read(ref stoppingFlag) != 0)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref probePending, 1, 0) != 0)
                {
                    // A probe is already in flight; coalesce this tick.
                    continue;
                }

                // Clear any stale snapshot from a previous iteration
                // before enqueuing the new probe. The probe will
                // overwrite the field synchronously, and the await
                // below provides the happens-before relationship
                // that makes the write visible to this loop.
                nextSnapshot = null;

                try
                {
                    Task probeTask = worker.Enqueue(ProbeOnWorkerAsync, token);
                    await probeTask.ConfigureAwait(false);

                    RuntimeHealthSnapshot? snapshot = nextSnapshot;
                    if (snapshot is not null)
                    {
                        subject.OnNext(snapshot);
                    }
                }
                catch (OperationCanceledException) when (IsLoopCancellationRequested())
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "RuntimeHealthMonitor: probe enqueue or execution failed.");
                }
                finally
                {
                    Interlocked.Exchange(ref probePending, 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    /// <summary>
    /// Probe body. Runs on the dedicated <see cref="RuntimeKernelWorker"/>
    /// thread. Computes the snapshot synchronously and records a
    /// failed session if the transition into <see cref="RuntimeHealthState.Exited"/>
    /// is observed. The snapshot is stored in <see cref="nextSnapshot"/>
    /// and the <c>OnNext</c> call is made by the monitor loop on its
    /// own task so a slow or throwing subscriber cannot block the
    /// worker.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token observed
    /// by the probe body.</param>
    /// <returns>A completed <see cref="Task"/>; the work is fully
    /// synchronous, so the method is not <c>async</c>.</returns>
    private Task ProbeOnWorkerAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        Process? process = host.RunningProcess;
        RuntimeHealthState newState;
        int? newProcessId;

        if (process is null)
        {
            newState = RuntimeHealthState.Unknown;
            newProcessId = null;
        }
        else
        {
            try
            {
                newProcessId = SafeGetProcessId(process);
                newState = process.HasExited
                    ? RuntimeHealthState.Exited
                    : RuntimeHealthState.Healthy;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // The host disposed the process handle concurrently with the
                // probe. Treat this as an Exited observation; the next probe
                // will see RunningProcess == null and settle to Unknown.
                logger.LogDebug(
                    ex,
                    "RuntimeHealthMonitor: process handle was disposed during the probe; treating as Exited.");
                newState = RuntimeHealthState.Exited;
                newProcessId = null;
            }
        }

        RuntimeHealthSnapshot next = new(
            state: newState,
            processId: newProcessId,
            observedAtUtc: DateTimeOffset.UtcNow);

        RuntimeHealthSnapshot previous = subject.Value;
        if (ShouldRecordFailedSession(previous.State, newState))
        {
            TryMarkActiveSessionAsFailed();
        }

        nextSnapshot = next;
        return Task.CompletedTask;
    }

    private bool IsLoopCancellationRequested()
    {
        CancellationTokenSource? captured;
        lock (timerLock)
        {
            captured = loopCts;
        }
        return captured is null || captured.IsCancellationRequested;
    }

    private static bool ShouldRecordFailedSession(
        RuntimeHealthState previous,
        RuntimeHealthState next)
    {
        // Only transitions INTO Exited trigger the failed-session
        // recording. A subsequent probe that observes Exited again
        // (because the host has not yet been stopped) does not
        // re-record the session.
        return next == RuntimeHealthState.Exited && previous != RuntimeHealthState.Exited;
    }

    private void TryMarkActiveSessionAsFailed()
    {
        try
        {
            RuntimeSessionRecord? current = stateStore.GetCurrentSession();
            if (current is null)
            {
                return;
            }

            stateStore.EndSession(current.Id, RuntimeSessionState.Failed);
            logger.LogWarning(
                "RuntimeHealthMonitor: marked active session {SessionId} as Failed after an unexpected process exit.",
                current.Id.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "RuntimeHealthMonitor: failed to mark active session as Failed.");
        }
    }

    /// <summary>
    /// Safely reads <see cref="Process.Id"/> from a process whose
    /// handle may have been disposed concurrently.
    /// </summary>
    /// <param name="process">Process whose id should be read.</param>
    /// <returns>The process id, or <c>null</c> if the id cannot
    /// be read (for example, because the handle has been disposed
    /// in a race with the probe).</returns>
    private static int? SafeGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

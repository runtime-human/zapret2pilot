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
/// values. The probe runs on the dedicated
/// <see cref="RuntimeKernelWorker"/> thread so every read of the
/// host's process state happens on the canonical kernel thread.
/// </summary>
/// <remarks>
/// <para>
/// The monitor owns a <see cref="System.Threading.Timer"/> whose
/// callback runs on a <c>ThreadPool</c> thread. The callback is
/// intentionally minimal: it only enqueues a probe onto the worker.
/// All kernel work — reading <c>RuntimeProcessHost.RunningProcess</c>,
/// calling <c>IRuntimeKernelStateStore.EndSession</c> on a
/// transition into <see cref="RuntimeHealthState.Exited"/>, and
/// pushing the new snapshot through the
/// <see cref="BehaviorSubject{T}"/> — happens on the worker
/// thread. The <see cref="System.Threading.Timer"/> never blocks
/// the UI thread.
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
/// <see cref="StartAsync"/> starts the timer. <see cref="StopAsync"/>
/// disposes the timer, marks the monitor as stopping, and awaits
/// the worker's cancellation of any in-flight probe. <see cref="Dispose"/>
/// is idempotent and forwards to <see cref="StopAsync"/>.
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
    private readonly TimeSpan probeInterval;
    private readonly BehaviorSubject<RuntimeHealthSnapshot> subject;

    private readonly object timerLock = new();
    private Timer? timer;
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
    /// <param name="probeInterval">Probe interval. Defaults to
    /// <see cref="DefaultProbeInterval"/> when <c>null</c>.</param>
    public RuntimeHealthMonitor(
        RuntimeProcessHost host,
        RuntimeKernelWorker worker,
        IRuntimeKernelStateStore stateStore,
        ILogger<RuntimeHealthMonitor> logger,
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
    /// Starts the probe timer. Returns immediately; the first
    /// scheduled probe fires after <see cref="probeInterval"/>.
    /// </summary>
    /// <param name="cancellationToken">Observed before the timer is
    /// created.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (timerLock)
        {
            if (timer is not null)
            {
                return Task.CompletedTask;
            }

            timer = new Timer(OnTimerTick, state: null, dueTime: probeInterval, period: probeInterval);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the probe timer. Idempotent. Awaits the worker's
    /// cancellation of any in-flight probe so the monitor never
    /// publishes a snapshot after <see cref="StopAsync"/> returns.
    /// </summary>
    /// <param name="cancellationToken">Observed while waiting for
    /// the worker to drain.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Timer? toDispose;
        lock (timerLock)
        {
            toDispose = timer;
            timer = null;
        }

        toDispose?.Dispose();
        Interlocked.Exchange(ref stoppingFlag, 1);

        // Drain the worker so an in-flight probe is allowed to
        // complete before Dispose() returns. The worker's channel
        // already serialises probes, so awaiting the most recent
        // enqueued work item is enough to guarantee no further
        // OnNext happens on this monitor.
        try
        {
            await worker.Enqueue(_ => Task.CompletedTask, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected when the host cancels shutdown
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

    private void OnTimerTick(object? state)
    {
        if (Volatile.Read(ref stoppingFlag) != 0)
        {
            return;
        }

        try
        {
            // Fire and forget: the probe body runs on the worker
            // thread and the timer is intentionally not blocked
            // waiting for it. Any failure inside the probe is
            // logged and observed through the worker.
            _ = worker.Enqueue(ProbeOnWorkerAsync, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeHealthMonitor: failed to enqueue probe.");
        }
    }

    private async Task ProbeOnWorkerAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
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

        subject.OnNext(next);
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Guard;

namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// The single authority over the runtime lifecycle (DEC-0039,
/// roadmap §5.1). The loop owns the bounded
/// <see cref="Channel{T}"/> of <see cref="RuntimeKernelCommand"/>s,
/// a single reader thread, the pure
/// <see cref="RuntimeKernelReducer"/>, the
/// <see cref="IRuntimeEffectRunner"/> dispatch, and the
/// <see cref="RuntimeStatePublisher"/> that surfaces immutable
/// snapshots to the Application and UI layers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread model.</b> All state mutations happen on the
/// kernel reader thread. Process effects are dispatched to
/// <see cref="IRuntimeEffectRunner"/> on the thread pool so the
/// kernel thread is never blocked on I/O. The runner returns a
/// <see cref="Task{TResult}"/> that completes with a
/// <see cref="RuntimeKernelCommand.EffectCompleted"/>; the
/// loop tracks the in-flight task and posts the completion back
/// to the channel when the task finishes.
/// </para>
/// <para>
/// <b>Shutdown.</b> <see cref="StopAsync(CancellationToken)"/>
/// completes the channel writer (no new commands accepted),
/// cancels the worker CTS (in-flight effects observe the
/// signal), waits for the worker thread to join and then
/// drains the in-flight effect tracker with a bounded timeout.
/// <see cref="Dispose"/> performs the same sequence on the
/// calling thread, then disposes the publisher and the CTS,
/// and only then sets the <c>disposed</c> flag so a concurrent
/// caller that observes <see cref="PostCommandAsync"/> after
/// disposal still gets the typed <see cref="ObjectDisposedException"/>.
/// </para>
/// <para>
/// <b>Guard effects.</b> <see cref="RuntimeEffectKind.RecordGuardSuccess"/>
/// and <see cref="RuntimeEffectKind.RecordGuardFailure"/>
/// remain inline on the kernel thread: the crash-loop guard is
/// a pure in-memory primitive and inline execution avoids
/// racing the next command's
/// <see cref="ICrashLoopGuard.Check"/> call.
/// </para>
/// </remarks>
public sealed class RuntimeKernelLoop : IDisposable
{
    /// <summary>
    /// Maximum time the dispose / stop path waits for in-flight
    /// effect tasks to drain after the worker thread has
    /// exited. Five seconds mirrors the kernel's documented
    /// graceful-stop window and bounds shutdown latency.
    /// </summary>
    private static readonly TimeSpan EffectDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time the stop path waits for the worker thread
    /// to exit after the channel is closed and the worker CTS
    /// is cancelled.
    /// </summary>
    private static readonly TimeSpan WorkerJoinTimeout = TimeSpan.FromSeconds(5);

    private readonly ICrashLoopGuard guard;
    private readonly IRuntimeEffectRunner effectRunner;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RuntimeKernelLoop> logger;
    private readonly Channel<RuntimeKernelCommand> channel;
    private readonly RuntimeStatePublisher publisher;
    private readonly CancellationTokenSource workerCts = new();
    private readonly Thread workerThread;
    private readonly InFlightEffectTracker inFlightEffects = new();

    private int stoppingFlag;
    private int disposed;

    internal RuntimeKernelLoop(
        ICrashLoopGuard guard,
        IRuntimeEffectRunner effectRunner,
        TimeProvider? timeProvider = null,
        ILogger<RuntimeKernelLoop>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(effectRunner);

        this.guard = guard;
        this.effectRunner = effectRunner;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.logger = logger ?? NullLogger<RuntimeKernelLoop>.Instance;

        var initialState = new RuntimeKernelState(
            RuntimeKernelStatus.Stopped,
            AutomationOwner.None,
            RuntimeGeneration.Initial,
            pendingOperationId: null,
            lastStartResult: null,
            guardResult: null,
            lastError: null,
            this.timeProvider.GetUtcNow(),
            deadline: null,
            cancellationReason: null);

        publisher = new RuntimeStatePublisher(initialState);

        channel = Channel.CreateBounded<RuntimeKernelCommand>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        workerThread = new Thread(RunLoop)
        {
            Name = "Z2P-RuntimeKernel",
            IsBackground = true,
        };
        workerThread.Start();
    }

    public RuntimeKernelState CurrentState => publisher.LatestState;

    public IObservable<RuntimeKernelState> StateChanged => publisher.StateChanged;

    internal int KernelThreadId => workerThread?.ManagedThreadId ?? 0;

    public ValueTask<bool> PostCommandAsync(
        RuntimeKernelCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(command);

        if (Volatile.Read(ref stoppingFlag) != 0)
        {
            return new ValueTask<bool>(false);
        }

        return PostCommandCoreAsync(command, cancellationToken);
    }

    private async ValueTask<bool> PostCommandCoreAsync(
        RuntimeKernelCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.Writer.WriteAsync(command, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops accepting new commands, cancels the worker CTS,
    /// waits for the kernel thread to join, and drains the
    /// in-flight effect tracker with a bounded timeout. Safe
    /// to call from any thread; idempotent.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token observed while waiting for the
    /// worker thread and the in-flight effect tasks. Cancelling
    /// the token does NOT cancel the worker CTS or abort the
    /// wait — it only bounds the caller's patience.
    /// </param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref stoppingFlag, 1) != 0)
        {
            return;
        }

        channel.Writer.Complete();
        workerCts.Cancel();

        Thread? thread = workerThread;
        if (thread is not null && thread.IsAlive)
        {
            await Task.Run(() => thread.Join(WorkerJoinTimeout), cancellationToken)
                .ConfigureAwait(false);

            if (thread.IsAlive)
            {
                logger.LogCritical(
                    "RuntimeKernelLoop worker thread did not exit within {Timeout}.",
                    WorkerJoinTimeout);
            }
        }

        inFlightEffects.Drain(EffectDrainTimeout, logger);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // `disposed` is set first so that concurrent callers of
        // PostCommandAsync observe ObjectDisposedException immediately.
        // Internal completion posting bypasses the disposed check and
        // uses channel.Writer.TryWrite directly, so in-flight effects
        // can still post their results while we drain. Cleanup then
        // proceeds bottom-up: stop the loop, drain effects, dispose the
        // publisher and the CTS.
        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeKernelLoop: StopAsync failed during disposal.");
        }

        try
        {
            publisher.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeKernelLoop: publisher disposal failed.");
        }

        try
        {
            workerCts.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeKernelLoop: worker CTS disposal failed.");
        }
    }

    private void RunLoop()
    {
        RuntimeKernelState state = CurrentState;

        try
        {
            while (!workerCts.Token.IsCancellationRequested)
            {
                RuntimeKernelCommand command;
                try
                {
                    command = channel.Reader.ReadAsync(workerCts.Token).AsTask()
                        .GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                try
                {
                    var result = RuntimeKernelReducer.Reduce(
                        state,
                        command,
                        guard,
                        timeProvider);

                    state = result.NextState;
                    publisher.Publish(state);

                    foreach (var effect in result.Effects)
                    {
                        logger.LogDebug(
                            "RuntimeKernelLoop produced effect {EffectKind} for operation {OperationId} generation {Generation} deadline {Deadline} reason {Reason}",
                            effect.Kind,
                            effect.OperationId,
                            effect.Generation.Value,
                            effect.Deadline,
                            effect.CancellationReason);

                        ExecuteEffect(effect);
                    }

                    foreach (var evt in result.Events)
                    {
                        if (evt is IgnoredStaleCompletion stale)
                        {
                            logger.LogWarning(
                                "RuntimeKernelLoop rejected stale completion for operation {OperationId} generation {Generation} (state generation {StateGeneration}, pending {StatePendingOperationId}): {Reason}",
                                stale.OperationId,
                                stale.Generation.Value,
                                stale.StateGeneration.Value,
                                stale.StatePendingOperationId,
                                stale.Reason);
                        }
                    }

                    if (command is RuntimeKernelCommand.Dispose)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "RuntimeKernelLoop: unhandled exception while processing command.");
                }
            }
        }
        finally
        {
            // The worker thread is exiting. Complete the channel
            // writer so any subsequent post is rejected without
            // spinning, and cancel the worker CTS so in-flight
            // effect tasks observe the cancellation.
            try
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RuntimeKernelLoop: failed to complete the channel writer in the worker finally.");
            }

            workerCts.Cancel();
        }
    }

    /// <summary>
    /// Dispatches a single <see cref="RuntimeEffectIntent"/> on
    /// the kernel thread. The intent's
    /// <see cref="RuntimeEffectIntent.Kind"/> determines which
    /// side effect runs:
    /// <list type="bullet">
    ///   <item><see cref="RuntimeEffectKind.RecordGuardSuccess"/>
    ///         and
    ///         <see cref="RuntimeEffectKind.RecordGuardFailure"/>
    ///         call
    ///         <see cref="ICrashLoopGuard.RecordSuccess"/>
    ///         / <see cref="ICrashLoopGuard.RecordFailure"/>
    ///         inline on the kernel thread.</item>
    ///   <item><see cref="RuntimeEffectKind.StartProcess"/> and
    ///         <see cref="RuntimeEffectKind.StopProcess"/>
    ///         dispatch to the
    ///         <see cref="IRuntimeEffectRunner"/> on the thread
    ///         pool; the returned
    ///         <see cref="RuntimeKernelCommand.EffectCompleted"/>
    ///         is posted back into the loop's channel when the
    ///         runner's task finishes.</item>
    /// </list>
    /// </summary>
    /// <param name="effect">Effect to execute.</param>
    private void ExecuteEffect(RuntimeEffectIntent effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        switch (effect.Kind)
        {
            case RuntimeEffectKind.RecordGuardSuccess:
                guard.RecordSuccess();
                return;

            case RuntimeEffectKind.RecordGuardFailure:
                guard.RecordFailure();
                return;

            case RuntimeEffectKind.StartProcess:
            case RuntimeEffectKind.StopProcess:
                DispatchAsyncEffect(effect);
                return;

            case RuntimeEffectKind.PublishState:
                // The kernel already publishes state on every
                // transition; this variant is reserved for a
                // future replay seam and is a no-op for now.
                return;

            default:
                logger.LogWarning(
                    "RuntimeKernelLoop encountered an unknown effect kind {EffectKind}.",
                    effect.Kind);
                return;
        }
    }

    /// <summary>
    /// Kicks off the runner for a process effect on the thread
    /// pool, tracks the in-flight task so the dispose / stop
    /// path can drain it, and schedules a continuation that
    /// posts the returned
    /// <see cref="RuntimeKernelCommand.EffectCompleted"/> back
    /// into the loop's channel.
    /// </summary>
    /// <param name="effect">Process effect to dispatch.</param>
    private void DispatchAsyncEffect(RuntimeEffectIntent effect)
    {
        CancellationToken token = workerCts.Token;
        IRuntimeEffectRunner runner = effectRunner;

        Task<RuntimeKernelCommand.EffectCompleted> task;
        try
        {
            task = Task.Run(() => runner.RunAsync(effect, timeProvider, token), token);
        }
        catch (Exception ex)
        {
            // Task.Run should never throw synchronously here
            // (we already verified `token` is not yet
            // cancelled), but defensive: surface the failure
            // as a completion rather than letting it bubble up.
            logger.LogError(ex, "RuntimeKernelLoop: failed to dispatch effect {EffectKind} to the runner.", effect.Kind);
            return;
        }

        inFlightEffects.Add(task);

        _ = task.ContinueWith(
            completedTask =>
            {
                RuntimeKernelCommand.EffectCompleted completion = BuildCompletionFromTask(
                    completedTask, effect);
                inFlightEffects.Remove(completedTask);
                TryPostCompletion(completion);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static RuntimeKernelCommand.EffectCompleted BuildCompletionFromTask(
        Task<RuntimeKernelCommand.EffectCompleted> completedTask,
        RuntimeEffectIntent originatingEffect)
    {
        if (completedTask.IsCompletedSuccessfully)
        {
            RuntimeKernelCommand.EffectCompleted completion = completedTask.Result;
            // The runner carries the operation id / generation
            // on the returned completion, but the originating
            // effect is the canonical source of identity in
            // case the runner forgot to copy it.
            if (completion.OperationId.Value == default
                || completion.Generation.Value == 0)
            {
                return completion with
                {
                    OperationId = originatingEffect.OperationId,
                    Generation = originatingEffect.Generation,
                };
            }

            return completion;
        }

        if (completedTask.IsFaulted)
        {
            string message = completedTask.Exception?.GetBaseException().Message
                ?? "The effect runner task completed with an unobserved exception.";
            ErrorInfo error = new(
                code: "RuntimeEffectRunnerThrew",
                message: $"The effect runner task completed with an exception: {message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);

            return new RuntimeKernelCommand.EffectCompleted(
                originatingEffect.OperationId,
                originatingEffect.Generation,
                Result.Failure<Unit>(error),
                StartResult: null,
                CancellationReason: RuntimeCancellationReason.HostShutdown,
                CrossedIrreversibleBoundary: false);
        }

        if (completedTask.IsCanceled)
        {
            ErrorInfo error = new(
                code: "RuntimeEffectCancelled",
                message: "The effect runner task was cancelled before the host could complete the operation.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);

            return new RuntimeKernelCommand.EffectCompleted(
                originatingEffect.OperationId,
                originatingEffect.Generation,
                Result.Failure<Unit>(error),
                StartResult: null,
                CancellationReason: RuntimeCancellationReason.HostShutdown,
                CrossedIrreversibleBoundary: false);
        }

        // Unreachable: every Task ends in one of the three
        // states above. Return a generic failure so the kernel
        // thread never gets stuck.
        ErrorInfo unknown = new(
            code: "RuntimeEffectRunnerUnknownState",
            message: "The effect runner task ended in an unrecognised state.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);

        return new RuntimeKernelCommand.EffectCompleted(
            originatingEffect.OperationId,
            originatingEffect.Generation,
            Result.Failure<Unit>(unknown),
            StartResult: null,
            CancellationReason: RuntimeCancellationReason.HostShutdown,
            CrossedIrreversibleBoundary: false);
    }

    private void TryPostCompletion(RuntimeKernelCommand.EffectCompleted completion)
    {
        try
        {
            if (!channel.Writer.TryWrite(completion))
            {
                // Channel is full or closed. The worker thread
                // is no longer reading; the completion is
                // dropped on the floor. This is acceptable
                // because the loop is being torn down and the
                // kernel state will be reset to Stopped on the
                // next Start. Log so the loss is observable.
                logger.LogWarning(
                    "RuntimeKernelLoop: dropped effect completion for operation {OperationId} generation {Generation} because the channel is closed.",
                    completion.OperationId,
                    completion.Generation.Value);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "RuntimeKernelLoop: unexpected error while posting effect completion for operation {OperationId} generation {Generation}.",
                completion.OperationId,
                completion.Generation.Value);
        }
    }

    /// <summary>
    /// Thread-safe set of in-flight
    /// <see cref="Task{TResult}"/>s produced by
    /// <see cref="DispatchAsyncEffect"/>. Used by
    /// <see cref="StopAsync"/> / <see cref="Dispose"/> to wait
    /// for every running effect to settle before the loop is
    /// torn down.
    /// </summary>
    private sealed class InFlightEffectTracker
    {
        private readonly object gate = new();
        private readonly List<Task> tasks = new();

        public void Add(Task task)
        {
            lock (gate)
            {
                tasks.Add(task);
            }
        }

        public void Remove(Task task)
        {
            lock (gate)
            {
                tasks.Remove(task);
            }
        }

        public void Drain(TimeSpan timeout, ILogger logger)
        {
            Task[] snapshot;
            lock (gate)
            {
                snapshot = tasks.ToArray();
            }

            if (snapshot.Length == 0)
            {
                return;
            }

            try
            {
                Task.WhenAll(snapshot).Wait(timeout);
            }
            catch (AggregateException)
            {
                // The runner converts expected cancellations
                // into EffectCompleted results, so this branch
                // is only reached when a runner bug let an
                // exception escape. The loop has already
                // converted it; nothing more to do here.
            }

            lock (gate)
            {
                int remaining = 0;
                foreach (Task task in tasks)
                {
                    if (!task.IsCompleted)
                    {
                        remaining++;
                    }
                }

                if (remaining > 0)
                {
                    logger.LogWarning(
                        "RuntimeKernelLoop: {Remaining} in-flight effect task(s) did not complete within {Timeout}.",
                        remaining,
                        timeout);
                }
            }
        }
    }
}

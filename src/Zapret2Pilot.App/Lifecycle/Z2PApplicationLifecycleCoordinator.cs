using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Default <see cref="IZ2PApplicationLifecycleCoordinator"/>
/// implementation. It is registered both as a singleton (so the
/// shell can observe <see cref="PhaseChanged"/>) and as an
/// <see cref="IHostedService"/> (so
/// <see cref="IHostedService.StartAsync"/> transitions the
/// coordinator to
/// <see cref="ApplicationLifecyclePhase.WaitingForShell"/> and
/// <see cref="IHostedService.StopAsync"/> cancels the in-flight
/// <see cref="IStartupStep"/> pipeline).
///
/// <para>
/// Per <c>docs/Z2P-PLAN-0.0.25.md</c> Scope B and 0.0.28 Packet 3
/// (P0-9), the coordinator waits for
/// <see cref="IZ2PApplicationLifecycleCoordinator.SignalShellVisible"/>
/// (invoked from the shell's <c>MainWindow.Opened</c> event in
/// <c>Program.Main</c>) and then runs the registered
/// <see cref="IStartupStep"/> implementations in registration
/// order:
/// </para>
///
/// <list type="number">
///   <item><c>StorageRecoveryStartupStep</c> → <see cref="ApplicationLifecyclePhase.StorageRecovery"/></item>
///   <item><c>DeploymentVerificationStartupStep</c> → <see cref="ApplicationLifecyclePhase.DeploymentVerification"/></item>
///   <item><c>OwnershipRecoveryStartupStep</c> → <see cref="ApplicationLifecyclePhase.OwnershipRecovery"/></item>
///   <item><c>CompatibilityPreflightStartupStep</c> → <see cref="ApplicationLifecyclePhase.CompatibilityPreflight"/></item>
/// </list>
///
/// <para>
/// The terminal phase is
/// <see cref="ApplicationLifecyclePhase.Ready"/> when all critical
/// steps succeed and no degradable step failed; a degradable-step
/// failure demotes the final phase to
/// <see cref="ApplicationLifecyclePhase.Degraded"/>; a critical-step
/// failure (or non-shutdown cancellation) publishes
/// <see cref="ApplicationLifecyclePhase.Blocked"/>.
/// <see cref="IHostedService.StopAsync"/> cancels the pipeline
/// task and transitions to
/// <see cref="ApplicationLifecyclePhase.Stopped"/>.
/// </para>
/// </summary>
public sealed class Z2PApplicationLifecycleCoordinator
    : IHostedService, IZ2PApplicationLifecycleCoordinator, IDisposable
{
    private readonly IReadOnlyList<IStartupStep> steps;
    private readonly ILogger<Z2PApplicationLifecycleCoordinator> logger;
    private readonly BehaviorSubject<ApplicationLifecyclePhase> phase =
        new(ApplicationLifecyclePhase.ProcessBootstrap);
    private readonly object pipelineLock = new();
    private readonly CancellationTokenSource pipelineCts = new();
    private readonly IServiceProvider services;
    private readonly ILoggerFactory loggerFactory;
    private Task? pipelineTask;
    private bool shellVisibleSignaled;

    public Z2PApplicationLifecycleCoordinator(
        IEnumerable<IStartupStep> steps,
        ILogger<Z2PApplicationLifecycleCoordinator> logger,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this.steps = steps.ToArray();
        this.logger = logger;
        this.services = services;
        this.loggerFactory = loggerFactory;
    }

    public ApplicationLifecyclePhase CurrentPhase => phase.Value;

    public bool IsReady => CurrentPhase == ApplicationLifecyclePhase.Ready;

    public IObservable<ApplicationLifecyclePhase> PhaseChanged => phase.AsObservable();

    /// <summary>
    /// Transitions the coordinator from
    /// <see cref="ApplicationLifecyclePhase.ProcessBootstrap"/> to
    /// <see cref="ApplicationLifecyclePhase.WaitingForShell"/>. The
    /// <see cref="IStartupStep"/> pipeline is NOT started here —
    /// the coordinator waits for the shell's
    /// <see cref="IZ2PApplicationLifecycleCoordinator.SignalShellVisible"/>
    /// signal so that runtime commands are never enabled before
    /// the shell exists. Returns <see cref="Task.CompletedTask"/>
    /// so the Generic Host can complete its startup handshake
    /// without blocking on step execution.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        TransitionTo(ApplicationLifecyclePhase.WaitingForShell);
        return Task.CompletedTask;
    }

    public void SignalShellVisible()
    {
        lock (pipelineLock)
        {
            if (shellVisibleSignaled)
            {
                return;
            }

            shellVisibleSignaled = true;
        }

        ApplicationLifecyclePhase current = CurrentPhase;

        if (current == ApplicationLifecyclePhase.Stopping || current == ApplicationLifecyclePhase.Stopped)
        {
            logger.LogWarning(
                "SignalShellVisible: shell became visible while the coordinator is in {Phase}. Ignoring.",
                current);
            return;
        }

        TransitionTo(ApplicationLifecyclePhase.ShellVisible);

        lock (pipelineLock)
        {
            pipelineTask ??= Task.Run(
                () => RunPipelineAsync(pipelineCts.Token),
                CancellationToken.None);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Move into the Stopping phase first so a late ShellVisible
        // signal (or a step mid-flight) can short-circuit out of the
        // pipeline. The transition is idempotent: re-entering
        // Stopping is a no-op.
        TransitionTo(ApplicationLifecyclePhase.Stopping);

        try
        {
            pipelineCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed (e.g. Dispose was called concurrently
            // with StopAsync); nothing to cancel.
        }

        Task? task;
        lock (pipelineLock)
        {
            task = pipelineTask;
        }

        if (task is not null)
        {
            try
            {
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The host-imposed deadline elapsed; the pipeline
                // task will still complete in the background and
                // publish Stopped when it observes the inner
                // cancellation. Re-throw so the host's shutdown
                // timeout is honoured.
                throw;
            }
            catch (OperationCanceledException)
            {
                // Pipeline task was cancelled by the coordinator's
                // own CTS (not the host's deadline). Expected.
            }
        }

        TransitionTo(ApplicationLifecyclePhase.Stopped);
    }

    public void Dispose()
    {
        try
        {
            pipelineCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        pipelineCts.Dispose();
        phase.Dispose();
    }

    private async Task RunPipelineAsync(CancellationToken cancellationToken)
    {
        bool hasDegradableFailure = false;

        foreach (IStartupStep step in steps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Lifecycle pipeline cancelled before step {Step}. Treating as a shutdown signal.",
                    step.Name);
                return;
            }

            ApplicationLifecyclePhase stepPhase = MapStepToPhase(step);
            if (stepPhase != CurrentPhase)
            {
                TransitionTo(stepPhase);
            }

            StartupStepResult result;
            try
            {
                StartupStepContext context = new(services, loggerFactory.CreateLogger(step.Name));
                result = await step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Lifecycle pipeline cancelled during step {Step}.",
                    step.Name);
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lifecycle step {Step} threw an unhandled exception.", step.Name);
                result = new StartupStepResult(
                    StartupStepStatus.Failed,
                    ErrorCode: $"{step.Name}Threw",
                    Message: ex.Message);
            }

            switch (result.Status)
            {
                case StartupStepStatus.Succeeded:
                case StartupStepStatus.NotApplicable:
                    continue;

                case StartupStepStatus.Degraded:
                    // Degraded success does not stop the pipeline but
                    // is a soft signal; the final Ready-vs-Degraded
                    // decision is made below.
                    logger.LogWarning(
                        "Lifecycle step {Step} reported Degraded: {Message}",
                        step.Name,
                        result.Message);
                    continue;

                case StartupStepStatus.Failed:
                    if (step.Criticality == StartupStepCriticality.Critical)
                    {
                        logger.LogError(
                            "Lifecycle step {Step} (Critical) failed: {Code} {Message}",
                            step.Name,
                            result.ErrorCode,
                            result.Message);
                        TransitionTo(ApplicationLifecyclePhase.Blocked);
                        return;
                    }

                    logger.LogError(
                        "Lifecycle step {Step} (Degradable) failed: {Code} {Message}. Pipeline continues.",
                        step.Name,
                        result.ErrorCode,
                        result.Message);
                    hasDegradableFailure = true;
                    continue;

                case StartupStepStatus.Cancelled:
                    if (cancellationToken.IsCancellationRequested)
                    {
                        logger.LogInformation(
                            "Lifecycle step {Step} cancelled because the host is shutting down.",
                            step.Name);
                        return;
                    }

                    logger.LogError(
                        "Lifecycle step {Step} reported Cancelled outside of host shutdown: {Message}",
                        step.Name,
                        result.Message);
                    TransitionTo(ApplicationLifecyclePhase.Blocked);
                    return;

                default:
                    logger.LogError(
                        "Lifecycle step {Step} returned an unrecognised status {Status}.",
                        step.Name,
                        result.Status);
                    TransitionTo(ApplicationLifecyclePhase.Blocked);
                    return;
            }
        }

        TransitionTo(hasDegradableFailure
            ? ApplicationLifecyclePhase.Degraded
            : ApplicationLifecyclePhase.Ready);
    }

    private void TransitionTo(ApplicationLifecyclePhase nextPhase)
    {
        ApplicationLifecyclePhase current = CurrentPhase;
        if (current == nextPhase)
        {
            return;
        }

        logger.LogInformation(
            "Lifecycle phase transition: {Previous} -> {Next}",
            current,
            nextPhase);
        phase.OnNext(nextPhase);
    }

    private static ApplicationLifecyclePhase MapStepToPhase(IStartupStep step)
    {
        // The pipeline drives the phase transitions to make the
        // observable surface reflect which step is currently
        // running. Steps and phases are coupled by name to keep
        // the coordinator free of per-step if/else chains.
        return step.Name switch
        {
            "StorageRecovery" => ApplicationLifecyclePhase.StorageRecovery,
            "DeploymentVerification" => ApplicationLifecyclePhase.DeploymentVerification,
            "OwnershipRecovery" => ApplicationLifecyclePhase.OwnershipRecovery,
            "CompatibilityPreflight" => ApplicationLifecyclePhase.CompatibilityPreflight,
            _ => ApplicationLifecyclePhase.ShellVisible,
        };
    }
}

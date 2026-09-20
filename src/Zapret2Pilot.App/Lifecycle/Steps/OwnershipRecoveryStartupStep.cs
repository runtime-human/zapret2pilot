using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Zapret2Pilot.App.Lifecycle.Steps;

/// <summary>
/// Control-Plane placeholder for the historical ownership-recovery phase.
///
/// In v7 the unelevated App must not reconcile runtime mutexes, lock files,
/// process ownership or recovery state. Those operations belong exclusively
/// to the session-scoped Runtime Broker. Until the broker session/lifetime
/// handshake is wired into the startup pipeline, this step is explicitly
/// not applicable rather than reflectively discovering a Runtime service.
/// </summary>
public sealed class OwnershipRecoveryStartupStep : IStartupStep
{
    /// <inheritdoc />
    public string Name => "OwnershipRecovery";

    /// <inheritdoc />
    public StartupStepCriticality Criticality => StartupStepCriticality.Degradable;

    /// <inheritdoc />
    public Task<StartupStepResult> ExecuteAsync(
        StartupStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        context.Logger.LogInformation(
            "OwnershipRecoveryStartupStep: runtime ownership recovery is broker-owned in v7; Control Plane performs no local reconciliation.");

        return Task.FromResult(
            new StartupStepResult(StartupStepStatus.NotApplicable));
    }
}

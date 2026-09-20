using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.App.Runtime;

namespace Zapret2Pilot.App.Lifecycle.Steps;

/// <summary>
/// v7 ownership boundary step.
///
/// The unelevated Control Plane performs no runtime mutex/process recovery.
/// Instead it establishes the authenticated session-scoped Runtime Broker;
/// all ownership/recovery primitives remain broker-side.
/// </summary>
public sealed class OwnershipRecoveryStartupStep : IStartupStep
{
    public string Name => "OwnershipRecovery";

    // Declining UAC or a missing Broker degrades runtime functionality without
    // taking storage/navigation diagnostics away from the Control Plane.
    public StartupStepCriticality Criticality =>
        StartupStepCriticality.Degradable;

    public async Task<StartupStepResult> ExecuteAsync(
        StartupStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IRuntimeBrokerBootstrapper bootstrapper =
            context.Services.GetRequiredService<IRuntimeBrokerBootstrapper>();

        RuntimeBrokerBootstrapResult result =
            await bootstrapper.StartBrokerAsync(cancellationToken)
                .ConfigureAwait(false);

        if (result.Succeeded)
        {
            context.Logger.LogInformation(
                "OwnershipRecoveryStartupStep: authenticated Runtime Broker session established.");
            return new StartupStepResult(
                StartupStepStatus.Succeeded);
        }

        context.Logger.LogWarning(
            "OwnershipRecoveryStartupStep: Runtime Broker unavailable ({Code}): {Message}",
            result.ErrorCode,
            result.Message);

        return new StartupStepResult(
            StartupStepStatus.Failed,
            result.ErrorCode ?? "RuntimeBrokerUnavailable",
            result.Message ?? "Runtime Broker bootstrap failed.");
    }
}

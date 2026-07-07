using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zapret2Pilot.App.Hosting;

namespace Zapret2Pilot.App.Lifecycle.Steps;

/// <summary>
/// Second step of the application startup pipeline: validate the
/// resolved <see cref="Z2PApplicationOptions.DeploymentFlavor"/>
/// against the actual deployment on disk.
///
/// <para>
/// <see cref="DeploymentFlavor.Development"/> is the only flavor
/// fully wired in 0.0.28; the step simply validates the
/// <see cref="Z2PApplicationOptions.StorageDatabasePath"/> is
/// non-empty and reports
/// <see cref="StartupStepStatus.Succeeded"/>.
/// <see cref="DeploymentFlavor.Installed"/> and
/// <see cref="DeploymentFlavor.Portable"/> are explicit
/// <see cref="StartupStepStatus.Failed"/> outcomes with error code
/// <c>DeploymentFlavorNotYetSupported</c> — the runtime support
/// ships in a later milestone.
/// </para>
/// </summary>
public sealed class DeploymentVerificationStartupStep : IStartupStep
{
    /// <summary>
    /// Stable error code emitted when the configured deployment
    /// flavor does not yet have runtime support.
    /// </summary>
    public const string ErrorCode = "DeploymentFlavorNotYetSupported";

    /// <inheritdoc />
    public string Name => "DeploymentVerification";

    /// <inheritdoc />
    public StartupStepCriticality Criticality => StartupStepCriticality.Critical;

    /// <inheritdoc />
    public Task<StartupStepResult> ExecuteAsync(StartupStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IOptions<Z2PApplicationOptions>? optionsAccessor;
        try
        {
            optionsAccessor = context.Services.GetService(typeof(IOptions<Z2PApplicationOptions>)) as IOptions<Z2PApplicationOptions>;
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "DeploymentVerificationStartupStep: failed to resolve IOptions<Z2PApplicationOptions>.");
            return Task.FromResult(new StartupStepResult(StartupStepStatus.Failed, ErrorCode, ex.Message));
        }

        if (optionsAccessor is null)
        {
            // No options registered — treat as a development
            // workspace. The host's startup will have already
            // validated the rest of the configuration through
            // IValidateOptions<Z2PApplicationOptions>.
            context.Logger.LogWarning(
                "DeploymentVerificationStartupStep: IOptions<Z2PApplicationOptions> is not registered. Assuming Development flavor.");
            return Task.FromResult(new StartupStepResult(StartupStepStatus.Succeeded));
        }

        Z2PApplicationOptions options = optionsAccessor.Value;

        switch (options.DeploymentFlavor)
        {
            case DeploymentFlavor.Development:
                if (string.IsNullOrWhiteSpace(options.StorageDatabasePath))
                {
                    return Task.FromResult(new StartupStepResult(
                        StartupStepStatus.Failed,
                        ErrorCode,
                        "StorageDatabasePath is required for the Development flavor."));
                }

                context.Logger.LogInformation(
                    "DeploymentVerificationStartupStep: Development flavor accepted (database path: {DatabasePath}).",
                    options.StorageDatabasePath);
                return Task.FromResult(new StartupStepResult(StartupStepStatus.Succeeded));

            case DeploymentFlavor.Installed:
            case DeploymentFlavor.Portable:
                context.Logger.LogError(
                    "DeploymentVerificationStartupStep: flavor {Flavor} is not yet supported in 0.0.28.",
                    options.DeploymentFlavor);
                return Task.FromResult(new StartupStepResult(
                    StartupStepStatus.Failed,
                    ErrorCode,
                    $"Deployment flavor '{options.DeploymentFlavor}' is not yet supported."));

            default:
                return Task.FromResult(new StartupStepResult(
                    StartupStepStatus.Failed,
                    ErrorCode,
                    $"Unknown deployment flavor '{options.DeploymentFlavor}'."));
        }
    }
}

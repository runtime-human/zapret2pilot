using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Zapret2Pilot.App.Lifecycle.Steps;

/// <summary>
/// Fourth (and last) step of the application startup pipeline:
/// assert that the host process is running on a supported
/// platform. The Avalonia shell is Windows-only (TFM
/// <c>net10.0-windows10.0.26100.0</c>), and the underlying
/// Runtime Kernel uses Windows Job Objects for process
/// containment, so any non-Windows or non-x64 host is a hard
/// failure.
/// </summary>
public sealed class CompatibilityPreflightStartupStep : IStartupStep
{
    /// <summary>
    /// Stable error code emitted when the host OS / architecture
    /// is not in the supported matrix.
    /// </summary>
    public const string ErrorCode = "PlatformNotSupported";

    /// <inheritdoc />
    public string Name => "CompatibilityPreflight";

    /// <inheritdoc />
    public StartupStepCriticality Criticality => StartupStepCriticality.Critical;

    /// <inheritdoc />
    public Task<StartupStepResult> ExecuteAsync(StartupStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            const string message = "Zapret2Pilot requires Windows. The current host OS is not supported.";
            context.Logger.LogError("CompatibilityPreflightStartupStep: {Message}", message);
            return Task.FromResult(new StartupStepResult(StartupStepStatus.Failed, ErrorCode, message));
        }

        if (!Environment.Is64BitOperatingSystem)
        {
            const string message = "Zapret2Pilot requires a 64-bit Windows installation. The current host is 32-bit.";
            context.Logger.LogError("CompatibilityPreflightStartupStep: {Message}", message);
            return Task.FromResult(new StartupStepResult(StartupStepStatus.Failed, ErrorCode, message));
        }

        context.Logger.LogInformation("CompatibilityPreflightStartupStep: Windows x64 host accepted.");
        return Task.FromResult(new StartupStepResult(StartupStepStatus.Succeeded));
    }
}

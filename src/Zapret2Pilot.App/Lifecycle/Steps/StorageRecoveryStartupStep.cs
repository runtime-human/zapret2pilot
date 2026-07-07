using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.App.Lifecycle.Steps;

/// <summary>
/// First step of the application startup pipeline: run SQLite
/// schema migrations through <see cref="SqliteDbInitializer"/>.
///
/// <para>
/// Migrations are idempotent (the initializer skips already-applied
/// migration IDs), so re-running on a healthy database is a no-op.
/// A genuine failure (e.g. disk full, locked database, missing
/// directory) is reported as
/// <see cref="StartupStepStatus.Failed"/> with error code
/// <c>StorageRecoveryFailed</c>. Because this step is
/// <see cref="StartupStepCriticality.Critical"/>, such a failure
/// publishes <see cref="ApplicationLifecyclePhase.Blocked"/>.
/// </para>
/// </summary>
public sealed class StorageRecoveryStartupStep : IStartupStep
{
    /// <summary>
    /// Stable error code emitted when the underlying
    /// <see cref="SqliteDbInitializer"/> throws.
    /// </summary>
    public const string ErrorCode = "StorageRecoveryFailed";

    /// <inheritdoc />
    public string Name => "StorageRecovery";

    /// <inheritdoc />
    public StartupStepCriticality Criticality => StartupStepCriticality.Critical;

    /// <inheritdoc />
    public async Task<StartupStepResult> ExecuteAsync(StartupStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        SqliteDbInitializer? initializer;
        try
        {
            initializer = context.Services.GetService(typeof(SqliteDbInitializer)) as SqliteDbInitializer;
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "StorageRecoveryStartupStep: failed to resolve SqliteDbInitializer.");
            return new StartupStepResult(StartupStepStatus.Failed, ErrorCode, ex.Message);
        }

        if (initializer is null)
        {
            // Storage is optional from the coordinator's point of view:
            // if the host composition root did not register the
            // initializer, treat the step as a no-op success so the
            // pipeline can still reach Ready in a degraded environment
            // (e.g. unit tests that build a service collection without
            // the runtime composition root).
            context.Logger.LogWarning(
                "StorageRecoveryStartupStep: SqliteDbInitializer is not registered. Skipping storage recovery.");
            return new StartupStepResult(StartupStepStatus.Succeeded);
        }

        try
        {
            // The initializer's API is synchronous; run it on the
            // thread pool so the pipeline task remains cancellable
            // and never blocks the coordinator.
            await Task.Run(initializer.Initialize, cancellationToken).ConfigureAwait(false);
            return new StartupStepResult(StartupStepStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "StorageRecoveryStartupStep: SqliteDbInitializer.Initialize threw.");
            return new StartupStepResult(StartupStepStatus.Failed, ErrorCode, ex.Message);
        }
    }
}

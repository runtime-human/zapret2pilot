using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Zapret2Pilot.App.Lifecycle.Steps;

/// <summary>
/// Third step of the application startup pipeline: reconcile
/// global runtime ownership (mutex + lock files) with the
/// process that just started.
///
/// <para>
/// The reconciliation primitive
/// (<c>IRuntimeOwnershipReconciler</c>) is not implemented in
/// 0.0.28 — the Runtime project exposes the lower-level
/// <c>RuntimeOwnershipMutex</c> primitives but no facade for
/// recovery yet. This step therefore attempts to resolve the
/// reconciler and either delegates to it (when present) or
/// reports <see cref="StartupStepStatus.Succeeded"/> with a
/// warning log. The real reconciliation is tracked as a
/// follow-up packet.
/// </para>
/// </summary>
public sealed class OwnershipRecoveryStartupStep : IStartupStep
{
    /// <summary>
    /// Stable error code emitted when an
    /// <c>IRuntimeOwnershipReconciler</c> is present but its
    /// recovery call throws.
    /// </summary>
    public const string ErrorCode = "OwnershipRecoveryFailed";

    /// <inheritdoc />
    public string Name => "OwnershipRecovery";

    /// <inheritdoc />
    public StartupStepCriticality Criticality => StartupStepCriticality.Degradable;

    /// <inheritdoc />
    public async Task<StartupStepResult> ExecuteAsync(StartupStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // The reconciler contract is not defined yet; resolve by
        // reflection on a type-name match so this step stays
        // compilable without a hard reference to a type that may
        // be added in a follow-up packet.
        Type? reconcilerType = FindReconcilerType(context.Services);
        object? reconciler = reconcilerType is null
            ? null
            : context.Services.GetService(reconcilerType);

        if (reconciler is null)
        {
            context.Logger.LogWarning(
                "OwnershipRecoveryStartupStep: IRuntimeOwnershipReconciler is not registered. Skipping real reconciliation; this is a known 0.0.28 gap.");
            return new StartupStepResult(StartupStepStatus.Succeeded);
        }

        try
        {
            // The reconciler contract is intentionally not pinned
            // here; the recovery call is invoked through a known
            // method-name "ReconcileAsync" so that any future
            // implementation can satisfy the step without touching
            // the coordinator.
            System.Reflection.MethodInfo? method = reconcilerType!.GetMethod(
                "ReconcileAsync",
                bindingAttr: System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(CancellationToken) },
                modifiers: null);

            if (method is null || method.ReturnType != typeof(Task))
            {
                context.Logger.LogWarning(
                    "OwnershipRecoveryStartupStep: reconciler type {Type} does not expose ReconcileAsync(CancellationToken). Treating as a no-op success.",
                    reconcilerType.FullName);
                return new StartupStepResult(StartupStepStatus.Succeeded);
            }

            Task? task = (Task?)method.Invoke(reconciler, new object[] { cancellationToken });
            if (task is null)
            {
                return new StartupStepResult(StartupStepStatus.Succeeded);
            }

            await task.ConfigureAwait(false);
            return new StartupStepResult(StartupStepStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reflective invocation can wrap the original exception
            // in a TargetInvocationException; unwrap it for the log
            // and the step result.
            Exception inner = ex is System.Reflection.TargetInvocationException tie && tie.InnerException is not null
                ? tie.InnerException
                : ex;
            context.Logger.LogError(inner, "OwnershipRecoveryStartupStep: reconciler threw.");
            return new StartupStepResult(StartupStepStatus.Failed, ErrorCode, inner.Message);
        }
    }

    private static Type? FindReconcilerType(IServiceProvider services)
    {
        // IServiceProvider does not expose its IServiceCollection,
        // so the step relies on a try-resolve against a list of
        // candidate type names. In 0.0.28 no reconciler is
        // registered, so this returns null and the step is a
        // logged no-op.
        string[] candidates =
        [
            "Zapret2Pilot.Runtime.Ownership.IRuntimeOwnershipReconciler",
            "Zapret2Pilot.Runtime.IRuntimeOwnershipReconciler",
        ];

        foreach (string candidate in candidates)
        {
            Type? type = Type.GetType(candidate, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }
}

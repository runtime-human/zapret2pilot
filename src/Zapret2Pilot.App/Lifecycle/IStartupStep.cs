using System.Threading;
using System.Threading.Tasks;

namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// A single step in the application startup pipeline. The
/// coordinator (<see cref="Z2PApplicationLifecycleCoordinator"/>)
/// runs registered <see cref="IStartupStep"/> implementations in
/// registration order after the shell becomes visible, and uses
/// <see cref="Criticality"/> + <see cref="ExecuteAsync"/>'s
/// <see cref="StartupStepResult"/> to decide whether to advance,
/// demote to <see cref="ApplicationLifecyclePhase.Degraded"/>, or
/// publish <see cref="ApplicationLifecyclePhase.Blocked"/>.
/// </summary>
public interface IStartupStep
{
    /// <summary>
    /// Human-readable name of the step, used for log output and
    /// error codes (e.g. <c>StorageRecovery</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether a failure of this step must block the application
    /// (<see cref="StartupStepCriticality.Critical"/>) or merely
    /// degrade it (<see cref="StartupStepCriticality.Degradable"/>).
    /// </summary>
    StartupStepCriticality Criticality { get; }

    /// <summary>
    /// Executes the step. Implementations must honour the supplied
    /// <paramref name="cancellationToken"/> and either:
    /// <list type="bullet">
    ///   <item>return a <see cref="StartupStepResult"/> with
    ///         <see cref="StartupStepStatus.Succeeded"/>,
    ///         <see cref="StartupStepStatus.Degraded"/>, or
    ///         <see cref="StartupStepStatus.NotApplicable"/>;</item>
    ///   <item>return a <see cref="StartupStepResult"/> with
    ///         <see cref="StartupStepStatus.Failed"/> and a
    ///         stable <c>ErrorCode</c>;</item>
    ///   <item>throw <see cref="System.OperationCanceledException"/>
    ///         on cancellation, or any other exception which the
    ///         coordinator will translate into a
    ///         <see cref="StartupStepStatus.Failed"/> result.</item>
    /// </list>
    /// </summary>
    /// <param name="context">The per-step execution context
    /// (services + logger).</param>
    /// <param name="cancellationToken">Token cancelled when the
    /// host shuts down.</param>
    Task<StartupStepResult> ExecuteAsync(
        StartupStepContext context,
        CancellationToken cancellationToken);
}

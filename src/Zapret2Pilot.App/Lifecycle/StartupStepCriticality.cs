namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Indicates how the application lifecycle coordinator must react
/// to the outcome of an <see cref="IStartupStep"/>:
/// <list type="bullet">
///   <item><see cref="Critical"/>: a failure (or cancellation) must
///         stop the pipeline and publish
///         <see cref="ApplicationLifecyclePhase.Blocked"/>.</item>
///   <item><see cref="Degradable"/>: a failure is logged and the
///         pipeline continues; the final phase is demoted to
///         <see cref="ApplicationLifecyclePhase.Degraded"/> instead
///         of <see cref="ApplicationLifecyclePhase.Ready"/>.</item>
/// </list>
/// Used by <see cref="Z2PApplicationLifecycleCoordinator"/> when
/// classifying <see cref="StartupStepResult.Status"/> values
/// produced by <see cref="IStartupStep.ExecuteAsync"/>.
/// </summary>
public enum StartupStepCriticality
{
    Critical,
    Degradable,
}

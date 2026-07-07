namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Terminal status reported by an <see cref="IStartupStep"/> via
/// <see cref="StartupStepResult"/>. The coordinator maps these
/// statuses (combined with
/// <see cref="StartupStepCriticality"/>) into the next
/// <see cref="ApplicationLifecyclePhase"/>.
/// </summary>
public enum StartupStepStatus
{
    /// <summary>
    /// The step completed its work and the pipeline may advance
    /// to the next step.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The step completed with a reduced capability (e.g. an
    /// optional feature was skipped). Pipeline continues; the
    /// coordinator remembers the degradation and may demote the
    /// final phase to <see cref="ApplicationLifecyclePhase.Degraded"/>.
    /// </summary>
    Degraded,

    /// <summary>
    /// The step failed. Combined with the step's
    /// <see cref="StartupStepCriticality"/>, this either publishes
    /// <see cref="ApplicationLifecyclePhase.Blocked"/> (critical
    /// step) or <see cref="ApplicationLifecyclePhase.Degraded"/>
    /// (degradable step).
    /// </summary>
    Failed,

    /// <summary>
    /// The step was cancelled. The coordinator treats cancellation
    /// as a hard failure (publishes
    /// <see cref="ApplicationLifecyclePhase.Blocked"/>) unless the
    /// host is shutting down, in which case the pipeline is allowed
    /// to stop and the phase is demoted to
    /// <see cref="ApplicationLifecyclePhase.Stopping"/>.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The step is not applicable in the current configuration
    /// (e.g. a Windows-only check on a non-Windows build). Treated
    /// as a successful no-op by the coordinator.
    /// </summary>
    NotApplicable,
}

namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Phases of the application startup lifecycle, surfaced by
/// <see cref="IZ2PApplicationLifecycleCoordinator"/>.
///
/// <para>
/// The order matches the canonical pipeline declared in
/// <c>docs/Z2P-PLAN-0.0.25.md</c> (Scope B):
/// <c>ProcessBootstrap</c> → <c>WaitingForShell</c> →
/// <c>ShellVisible</c> → <c>StorageRecovery</c> →
/// <c>DeploymentVerification</c> → <c>OwnershipRecovery</c> →
/// <c>CompatibilityPreflight</c> → <c>Ready</c> (or
/// <c>Degraded</c> / <c>Blocked</c> on a step failure) →
/// <c>Stopping</c> → <c>Stopped</c>.
/// </para>
///
/// <para>
/// Runtime commands in the shell remain disabled until the
/// coordinator reaches <see cref="Ready"/>. The
/// <see cref="Degraded"/> and <see cref="Blocked"/> phases are
/// terminal-error variants and never transition to
/// <see cref="Ready"/>.
/// </para>
/// </summary>
public enum ApplicationLifecyclePhase
{
    /// <summary>
    /// Process is alive; the Generic Host has started but the
    /// Avalonia shell has not yet been created. Steps do not
    /// run in this phase.
    /// </summary>
    ProcessBootstrap,

    /// <summary>
    /// Process is alive and the host has been started; the
    /// coordinator is waiting for the shell's
    /// <c>MainWindow.Opened</c> signal before running any
    /// <see cref="IStartupStep"/>.
    /// </summary>
    WaitingForShell,

    /// <summary>
    /// The Avalonia shell has been constructed and the
    /// <c>MainWindow.Opened</c> event has been observed. The
    /// coordinator will now start the <see cref="IStartupStep"/>
    /// pipeline.
    /// </summary>
    ShellVisible,

    /// <summary>
    /// The <c>StorageRecoveryStartupStep</c> is running.
    /// </summary>
    StorageRecovery,

    /// <summary>
    /// The <c>DeploymentVerificationStartupStep</c> is running.
    /// </summary>
    DeploymentVerification,

    /// <summary>
    /// The <c>OwnershipRecoveryStartupStep</c> is running.
    /// </summary>
    OwnershipRecovery,

    /// <summary>
    /// The <c>CompatibilityPreflightStartupStep</c> is running.
    /// </summary>
    CompatibilityPreflight,

    /// <summary>
    /// All critical <see cref="IStartupStep"/>s succeeded and no
    /// degradable step failed; runtime commands are enabled.
    /// </summary>
    Ready,

    /// <summary>
    /// All critical <see cref="IStartupStep"/>s succeeded but at
    /// least one degradable step reported
    /// <see cref="StartupStepStatus.Failed"/>. Runtime commands
    /// may be enabled with reduced capability; downstream
    /// features must check the phase before relying on the
    /// degraded subsystem.
    /// </summary>
    Degraded,

    /// <summary>
    /// A critical <see cref="IStartupStep"/> failed or was
    /// cancelled outside of host shutdown. Runtime commands
    /// remain disabled.
    /// </summary>
    Blocked,

    /// <summary>
    /// The host has begun graceful shutdown; the coordinator is
    /// cancelling the step pipeline.
    /// </summary>
    Stopping,

    /// <summary>
    /// The coordinator has been disposed.
    /// </summary>
    Stopped,
}

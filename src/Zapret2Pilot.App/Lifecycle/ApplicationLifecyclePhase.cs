namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Phases of the application startup lifecycle, surfaced by
/// <see cref="IZ2PApplicationLifecycleCoordinator"/>. The order
/// matches the canonical pipeline declared in
/// <c>docs/Z2P-PLAN-0.0.25.md</c> (Scope B):
/// <c>ProcessBootstrap</c> → <c>ShellVisible</c> →
/// <c>StorageRecovery</c> → <c>DeploymentVerification</c> →
/// <c>OwnershipRecovery</c> → <c>CompatibilityPreflight</c> →
/// <c>Ready</c> → <c>Stopping</c>. Runtime commands must remain
/// disabled until the coordinator reaches <see cref="Ready"/>.
/// </summary>
public enum ApplicationLifecyclePhase
{
    ProcessBootstrap,
    ShellVisible,
    StorageRecovery,
    DeploymentVerification,
    OwnershipRecovery,
    CompatibilityPreflight,
    Ready,
    Stopping,
}

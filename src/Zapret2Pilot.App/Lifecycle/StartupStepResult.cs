namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Outcome produced by <see cref="IStartupStep.ExecuteAsync"/>.
/// The coordinator reads <see cref="Status"/> to advance the
/// application lifecycle phase and uses <see cref="ErrorCode"/> and
/// <see cref="Message"/> for structured logging and user-facing
/// reporting.
/// </summary>
/// <param name="Status">Terminal status of the step.</param>
/// <param name="ErrorCode">Stable, machine-readable code
/// (e.g. <c>StorageRecoveryFailed</c>) used in logs and tests.
/// Optional; reserved for <see cref="StartupStepStatus.Failed"/>
/// outcomes.</param>
/// <param name="Message">Human-readable description suitable for
/// log output. Optional.</param>
public sealed record StartupStepResult(
    StartupStepStatus Status,
    string? ErrorCode = null,
    string? Message = null);

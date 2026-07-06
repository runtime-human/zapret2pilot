namespace Zapret2Pilot.Application.UseCases;

// ---------------------------------------------------------------------------
// Runtime use-case contracts.
// ---------------------------------------------------------------------------

/// <summary>
/// Request to start a runtime session.
/// </summary>
/// <param name="OperationId">Correlation id for the start call.</param>
public sealed record StartRuntimeRequest(OperationId OperationId);

/// <summary>
/// Request to stop a running runtime session.
/// </summary>
/// <param name="OperationId">Correlation id for the stop call.</param>
public sealed record StopRuntimeRequest(OperationId OperationId);

/// <summary>
/// Request to read the current runtime status.
/// </summary>
/// <param name="OperationId">Correlation id for the read call.</param>
public sealed record GetRuntimeStatusRequest(OperationId OperationId);

/// <summary>
/// Read-model describing a runtime session.
/// </summary>
/// <param name="OperationId">Correlation id for the originating call.</param>
/// <param name="IsActive">Whether the session is currently active.</param>
public sealed record RuntimeSessionReadModel(OperationId OperationId, bool IsActive);

/// <summary>
/// Read-model describing the runtime status snapshot.
/// </summary>
/// <param name="OperationId">Correlation id for the originating call.</param>
/// <param name="Status">Human-readable status string.</param>
public sealed record RuntimeStatusReadModel(OperationId OperationId, string Status);

// ---------------------------------------------------------------------------
// Profile use-case contracts.
// ---------------------------------------------------------------------------

/// <summary>
/// Request to list all profiles.
/// </summary>
/// <param name="OperationId">Correlation id for the list call.</param>
public sealed record ListProfilesRequest(OperationId OperationId);

/// <summary>
/// Request to import a profile from disk.
/// </summary>
/// <param name="OperationId">Correlation id for the import call.</param>
/// <param name="FilePath">Absolute path to the profile file to import.</param>
public sealed record ImportProfileRequest(OperationId OperationId, string FilePath);

/// <summary>
/// Read-model describing the set of known profiles.
/// </summary>
/// <param name="OperationId">Correlation id for the originating call.</param>
/// <param name="ProfileIds">Identifiers of the profiles.</param>
public sealed record ProfileReadModel(OperationId OperationId, IReadOnlyList<string> ProfileIds);

// ---------------------------------------------------------------------------
// Rules use-case contracts.
// ---------------------------------------------------------------------------

/// <summary>
/// Request to list all rules.
/// </summary>
/// <param name="OperationId">Correlation id for the list call.</param>
public sealed record ListRulesRequest(OperationId OperationId);

/// <summary>
/// Read-model describing the set of known rules.
/// </summary>
/// <param name="OperationId">Correlation id for the originating call.</param>
/// <param name="RuleIds">Identifiers of the rules.</param>
public sealed record RulesetReadModel(OperationId OperationId, IReadOnlyList<string> RuleIds);

// ---------------------------------------------------------------------------
// AutoDoctor use-case contracts.
// ---------------------------------------------------------------------------

/// <summary>
/// Request to run a quick AutoDoctor check.
/// </summary>
/// <param name="OperationId">Correlation id for the run call.</param>
public sealed record RunQuickCheckRequest(OperationId OperationId);

/// <summary>
/// Request to run a full AutoDoctor check.
/// </summary>
/// <param name="OperationId">Correlation id for the run call.</param>
public sealed record RunFullCheckRequest(OperationId OperationId);

/// <summary>
/// Read-model summarising the outcome of an AutoDoctor run.
/// </summary>
/// <param name="OperationId">Correlation id for the originating call.</param>
/// <param name="Completed">Whether the AutoDoctor run finished.</param>
public sealed record AutoDoctorSummaryReadModel(OperationId OperationId, bool Completed);

// ---------------------------------------------------------------------------
// Diagnostics use-case contracts.
// ---------------------------------------------------------------------------

/// <summary>
/// Request to export a diagnostics bundle.
/// </summary>
/// <param name="OperationId">Correlation id for the export call.</param>
/// <param name="OutputDirectory">Target directory for the bundle.</param>
public sealed record ExportDiagnosticsRequest(OperationId OperationId, string OutputDirectory);

/// <summary>
/// Reference to a diagnostics bundle on disk.
/// </summary>
/// <param name="OperationId">Correlation id for the originating call.</param>
/// <param name="BundlePath">Path to the produced bundle.</param>
public sealed record DiagnosticsBundleReference(OperationId OperationId, string BundlePath);

// ---------------------------------------------------------------------------
// Runtime update use-case contracts.
// ---------------------------------------------------------------------------

/// <summary>
/// Request to check for a runtime update.
/// </summary>
/// <param name="OperationId">Correlation id for the check call.</param>
public sealed record CheckForUpdateRequest(OperationId OperationId);

/// <summary>
/// Request to activate an update candidate.
/// </summary>
/// <param name="OperationId">Correlation id for the activation call.</param>
/// <param name="BundlePath">Path to the candidate bundle.</param>
public sealed record ActivateCandidateRequest(OperationId OperationId, string BundlePath);

/// <summary>
/// Reference to a runtime bundle (either a freshly downloaded
/// update or a re-activated candidate).
/// </summary>
/// <param name="OperationId">Correlation id for the originating call.</param>
/// <param name="BundlePath">Path to the bundle.</param>
public sealed record RuntimeBundleReference(OperationId OperationId, string BundlePath);

// ---------------------------------------------------------------------------
// Data management use-case contracts.
// ---------------------------------------------------------------------------

/// <summary>
/// Request to clean up old data.
/// </summary>
/// <param name="OperationId">Correlation id for the cleanup call.</param>
public sealed record CleanupOldDataRequest(OperationId OperationId);

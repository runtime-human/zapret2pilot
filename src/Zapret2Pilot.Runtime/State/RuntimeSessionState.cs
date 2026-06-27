namespace Zapret2Pilot.Runtime.State;

/// <summary>
/// Lifecycle state of a runtime session row in
/// <c>runtime_sessions</c>.
///
/// Persisted as the literal string values
/// <see cref="Active"/> = "Active",
/// <see cref="Stopped"/> = "Stopped",
/// <see cref="Failed"/> = "Failed".
/// </summary>
public enum RuntimeSessionState
{
    Active,
    Stopped,
    Failed
}

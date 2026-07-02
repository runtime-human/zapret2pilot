using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;

namespace Zapret2Pilot.Runtime.State;

/// <summary>
/// Persistent store for the Runtime Kernel's session and singleton
/// runtime state.
///
/// Backed by the SQLite tables created by migration
/// <c>0002_runtime_state_store</c> in
/// <see cref="Zapret2Pilot.Storage.Sqlite.SqliteDbInitializer"/>:
/// <list type="bullet">
///   <item><c>runtime_sessions</c> — append-only history of every
///         session, with its <c>profile</c>, <c>plan</c> and plan
///         cache key.</item>
///   <item><c>runtime_state</c> — singleton row tracking the
///         currently active session (if any) and the
///         <c>is_running</c> flag.</item>
/// </list>
///
/// All operations are synchronous to match the existing
/// <c>Zapret2Pilot.Storage</c> repositories. The store is intended
/// to be called from the Runtime Kernel's single-writer thread
/// (the same thread that drives
/// <c>RuntimeProcessHost</c>/<c>RuntimeTransactionManager</c>); no
/// internal locking is performed.
/// </summary>
public interface IRuntimeKernelStateStore
{
    /// <summary>
    /// Opens a new active session and updates the singleton state
    /// row to point at it. Any previously active session is
    /// automatically closed with <c>EndedAtUtc = now</c> and
    /// <see cref="RuntimeSessionState.Stopped"/> before the new row
    /// is inserted, so the store never holds more than one active
    /// session at a time.
    /// </summary>
    /// <param name="profileId">Profile that owns the session.</param>
    /// <param name="planId">Compiled plan installed for the session.</param>
    /// <param name="planCacheKey">Content-addressed cache key of the
    /// compiled plan.</param>
    /// <returns>The newly created session record, as persisted.</returns>
    RuntimeSessionRecord StartSession(
        ProfileId profileId,
        RuntimePlanId planId,
        RuntimePlanCacheKey planCacheKey);

    /// <summary>
    /// Closes the session identified by <paramref name="sessionId"/>
    /// as <see cref="RuntimeSessionState.Stopped"/> and resets the
    /// singleton state row to <c>is_running = 0</c>,
    /// <c>session_id = NULL</c>.
    /// </summary>
    /// <param name="sessionId">Identifier of the session to close.</param>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when no session with the given
    /// <paramref name="sessionId"/> exists.
    /// </exception>
    void EndSession(RuntimeSessionId sessionId);

    /// <summary>
    /// Closes the session identified by <paramref name="sessionId"/>
    /// with the supplied terminal <paramref name="finalState"/> and
    /// resets the singleton state row to <c>is_running = 0</c>,
    /// <c>session_id = NULL</c>. Used by the health monitor to mark
    /// a session as <see cref="RuntimeSessionState.Failed"/> on an
    /// unexpected process exit; the single-argument overload closes
    /// a session as <see cref="RuntimeSessionState.Stopped"/>.
    /// </summary>
    /// <param name="sessionId">Identifier of the session to close.</param>
    /// <param name="finalState">Terminal state to record
    /// (<see cref="RuntimeSessionState.Stopped"/> or
    /// <see cref="RuntimeSessionState.Failed"/>).</param>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="finalState"/> is not a terminal
    /// state (only <c>Stopped</c> and <c>Failed</c> are accepted).
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when no session with the given
    /// <paramref name="sessionId"/> exists.
    /// </exception>
    void EndSession(RuntimeSessionId sessionId, RuntimeSessionState finalState);

    /// <summary>
    /// Returns the session currently tracked by the singleton
    /// <c>runtime_state</c> row, or <c>null</c> if the runtime is
    /// not running (no active session).
    /// </summary>
    /// <returns>The current session record, or <c>null</c>.</returns>
    RuntimeSessionRecord? GetCurrentSession();

    /// <summary>
    /// Returns the most recent session records ordered by
    /// <c>started_utc DESC</c> (newest first), capped at
    /// <paramref name="count"/> rows.
    /// </summary>
    /// <param name="count">Maximum number of records to return.</param>
    IReadOnlyList<RuntimeSessionRecord> GetRecentSessions(int count);
}

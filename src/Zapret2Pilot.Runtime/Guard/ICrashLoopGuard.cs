namespace Zapret2Pilot.Runtime.Guard;

/// <summary>
/// Public contract for the in-memory crash-loop guard. The guard
/// tracks consecutive runtime crash failures and enforces an
/// exponential backoff before allowing a restart attempt. The
/// counter is reset only after a <see cref="RecordSuccess"/> call
/// observes a stable, failure-free window of
/// <see cref="CrashLoopGuardOptions.StabilityWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// The guard is intentionally pure: it owns no I/O, no process
/// handles and no P/Invoke. A real supervisor (currently
/// out-of-scope) is expected to call
/// <see cref="Check"/> before every restart attempt, then
/// <see cref="RecordFailure"/> on a failed start and
/// <see cref="RecordSuccess"/> after the runtime has been healthy
/// for at least the stability window.
/// </para>
/// <para>
/// Implementations must be thread-safe: a restart supervisor may
/// call the public methods from the kernel worker thread while a
/// UI or telemetry thread reads the current state through
/// <see cref="Check"/>.
/// </para>
/// </remarks>
public interface ICrashLoopGuard
{
    /// <summary>
    /// Returns the current guard verdict. The call is read-only
    /// and does not mutate the guard state, except for the
    /// counter reset that happens when the stability window has
    /// elapsed since the last recorded success.
    /// </summary>
    /// <returns>A <see cref="CrashLoopGuardResult"/> describing
    /// whether a restart is allowed right now, the time the
    /// caller must still wait, and the current
    /// consecutive-failure counter.</returns>
    CrashLoopGuardResult Check();

    /// <summary>
    /// Records a failed restart attempt. Increments the
    /// consecutive-failure counter and updates the timestamp used
    /// to compute the next backoff window.
    /// </summary>
    void RecordFailure();

    /// <summary>
    /// Records a successful start. The guard does not reset the
    /// consecutive-failure counter immediately — the counter is
    /// cleared the next time <see cref="Check"/> runs after the
    /// <see cref="CrashLoopGuardOptions.StabilityWindow"/> has
    /// elapsed since the most recent failure (or since this
    /// call, whichever is later).
    /// </summary>
    void RecordSuccess();

    /// <summary>
    /// Clears the guard back to its initial state. Useful for
    /// tests and for explicit recovery actions (for example, when
    /// a user manually overrides a permanent lockout).
    /// </summary>
    void Reset();
}

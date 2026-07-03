namespace Zapret2Pilot.Runtime.Guard;

/// <summary>
/// Public contract for the in-memory crash-loop guard. The guard
/// tracks consecutive runtime crash failures and enforces an
/// exponential backoff before allowing a restart attempt. The
/// counter is reset only when BOTH conditions are met at the
/// time of a <see cref="Check"/> call: a <see cref="RecordSuccess"/>
/// has been observed, and at least
/// <see cref="CrashLoopGuardOptions.StabilityWindow"/> has elapsed
/// since that success (and the success is at or after the most
/// recent failure). Time passing after a failure, on its own,
/// is not enough to clear the counter.
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
    /// counter reset that happens when BOTH a success has been
    /// recorded through <see cref="RecordSuccess"/> AND the
    /// <see cref="CrashLoopGuardOptions.StabilityWindow"/> has
    /// elapsed since that success (with the success recorded at
    /// or after the most recent failure).
    /// </summary>
    /// <returns>A <see cref="CrashLoopGuardResult"/> describing
    /// whether a restart is allowed right now, the time the
    /// caller must still wait, and the current
    /// consecutive-failure counter.</returns>
    CrashLoopGuardResult Check();

    /// <summary>
    /// Records a failed restart attempt. Increments the
    /// consecutive-failure counter (saturating at
    /// <c>MaxConsecutiveFailures + 1</c> to avoid integer
    /// overflow) and updates the timestamp used to compute the
    /// next backoff window.
    /// </summary>
    void RecordFailure();

    /// <summary>
    /// Records a successful start. The guard does not reset the
    /// consecutive-failure counter on this call — the counter is
    /// only cleared the next time <see cref="Check"/> runs while
    /// BOTH of the following hold: this <see cref="RecordSuccess"/>
    /// call (or a later one) has been observed, and at least
    /// <see cref="CrashLoopGuardOptions.StabilityWindow"/> has
    /// elapsed since the most recent success that was recorded at
    /// or after the most recent failure. Simply waiting around
    /// after a failure without ever calling
    /// <see cref="RecordSuccess"/> is not enough to clear the
    /// counter.
    /// </summary>
    void RecordSuccess();

    /// <summary>
    /// Clears the guard back to its initial state. Useful for
    /// tests and for explicit recovery actions (for example, when
    /// a user manually overrides a permanent lockout).
    /// </summary>
    void Reset();
}

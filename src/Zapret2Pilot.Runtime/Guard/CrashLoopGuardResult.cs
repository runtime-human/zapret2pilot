using System;

namespace Zapret2Pilot.Runtime.Guard;

/// <summary>
/// Outcome of a <see cref="ICrashLoopGuard.Check"/> call.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="IsAllowed"/> is <c>true</c> the caller is free to
/// attempt another restart. <see cref="BackoffRemaining"/> is
/// <c>null</c> in that case because no further wait is required.
/// </para>
/// <para>
/// When <see cref="IsAllowed"/> is <c>false</c>, either
/// <see cref="BackoffRemaining"/> reports the time the caller must
/// still wait, or the guard is in permanent lockout
/// (more than <see cref="CrashLoopGuardOptions.MaxConsecutiveFailures"/>
/// failures in a row) and <see cref="BackoffRemaining"/> is
/// <c>null</c> because the wait is unbounded.
/// </para>
/// <para>
/// <see cref="ConsecutiveFailures"/> always reflects the current
/// guard state, including when the call returned
/// <see cref="IsAllowed"/> = <c>true</c>. A non-zero counter on an
/// allowed result means the guard is still "warming" — the counter
/// will be reset by the next successful <c>RecordSuccess</c> that
/// occurs after the <see cref="CrashLoopGuardOptions.StabilityWindow"/>
/// has elapsed.
/// </para>
/// </remarks>
public sealed record class CrashLoopGuardResult
{
    /// <summary>
    /// Creates a new <see cref="CrashLoopGuardResult"/>.
    /// </summary>
    /// <param name="isAllowed"><c>true</c> when the caller is
    /// allowed to attempt another restart.</param>
    /// <param name="backoffRemaining">Time the caller must still
    /// wait before a restart is allowed. <c>null</c> when
    /// <paramref name="isAllowed"/> is <c>true</c> or when the
    /// guard is in permanent lockout.</param>
    /// <param name="consecutiveFailures">Current value of the
    /// consecutive-failure counter at the time of the check.
    /// Must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="consecutiveFailures"/> is
    /// negative.
    /// </exception>
    public CrashLoopGuardResult(
        bool isAllowed,
        TimeSpan? backoffRemaining,
        int consecutiveFailures)
    {
        if (consecutiveFailures < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveFailures),
                consecutiveFailures,
                "Consecutive failures must be non-negative.");
        }

        IsAllowed = isAllowed;
        BackoffRemaining = backoffRemaining;
        ConsecutiveFailures = consecutiveFailures;
    }

    /// <summary>
    /// <c>true</c> when the caller is allowed to attempt another
    /// restart right now. <c>false</c> when the guard is still
    /// inside the backoff window or has reached permanent
    /// lockout.
    /// </summary>
    public bool IsAllowed { get; }

    /// <summary>
    /// Time the caller must still wait before a restart is
    /// allowed. <c>null</c> when <see cref="IsAllowed"/> is
    /// <c>true</c> or when the guard is in permanent lockout.
    /// </summary>
    public TimeSpan? BackoffRemaining { get; }

    /// <summary>
    /// Current value of the consecutive-failure counter at the
    /// time of the check. Zero means the guard is in its
    /// initial / recovered state.
    /// </summary>
    public int ConsecutiveFailures { get; }
}

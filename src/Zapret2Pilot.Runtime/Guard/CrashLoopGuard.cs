using System;
using System.Threading;

namespace Zapret2Pilot.Runtime.Guard;

/// <summary>
/// In-memory crash-loop guard. Tracks consecutive runtime crash
/// failures and enforces an exponential backoff between restart
/// attempts. Once the consecutive-failure counter exceeds
/// <see cref="CrashLoopGuardOptions.MaxConsecutiveFailures"/> the
/// guard enters a permanent lockout that has to be cleared
/// explicitly through <see cref="Reset"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>State machine.</b> The guard tracks three pieces of state:
/// the current consecutive-failure counter, the timestamp of the
/// last recorded failure and the timestamp of the last recorded
/// success. All three live behind a single private monitor lock
/// so every public method is thread-safe.
/// </para>
/// <para>
/// <b>Backoff formula.</b> After the <c>n</c>-th consecutive
/// failure the backoff is
/// <c>min(BaseBackoff * 2^(n - 1), MaxBackoff)</c>. The doubling
/// saturates at <see cref="CrashLoopGuardOptions.MaxBackoff"/>.
/// </para>
/// <para>
/// <b>Lockout rule.</b> The guard enters permanent lockout when
/// <c>consecutiveFailures &gt; MaxConsecutiveFailures</c>; with
/// the default of 10, the 11th consecutive failure is the first
/// to be rejected as a permanent lockout rather than a bounded
/// backoff.
/// </para>
/// <para>
/// <b>Stability window.</b> A successful start does not reset the
/// counter immediately. The next <see cref="Check"/> call that
/// runs more than <see cref="CrashLoopGuardOptions.StabilityWindow"/>
/// after the last failure (or the last success, whichever is
/// later) is the one that actually clears the counter. This
/// keeps a "barely-survived" runtime from looking healthy.
/// </para>
/// <para>
/// <b>Purity.</b> The guard performs no I/O, no process work and
/// no P/Invoke. The optional <c>clock</c> constructor parameter
/// exists purely so tests can drive time deterministically.
/// </para>
/// </remarks>
public sealed class CrashLoopGuard : ICrashLoopGuard
{
    private readonly CrashLoopGuardOptions options;
    private readonly Func<DateTimeOffset> clock;
    private readonly object syncRoot = new();

    private int consecutiveFailures;
    private DateTimeOffset? lastFailureUtc;
    private DateTimeOffset? lastSuccessUtc;

    /// <summary>
    /// Creates a new <see cref="CrashLoopGuard"/> with the supplied
    /// options and a default clock that returns
    /// <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    /// <param name="options">Guard configuration. Must not be
    /// <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <c>null</c>.
    /// </exception>
    public CrashLoopGuard(CrashLoopGuardOptions options)
        : this(options, clock: null)
    {
    }

    /// <summary>
    /// Creates a new <see cref="CrashLoopGuard"/> with the supplied
    /// options and an injectable clock. Tests use the
    /// <paramref name="clock"/> seam to drive time deterministically
    /// without sleeping.
    /// </summary>
    /// <param name="options">Guard configuration. Must not be
    /// <c>null</c>.</param>
    /// <param name="clock">Clock used to read "now". When
    /// <c>null</c>, the guard uses
    /// <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <c>null</c>.
    /// </exception>
    public CrashLoopGuard(CrashLoopGuardOptions options, Func<DateTimeOffset>? clock)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        this.options = options;
        this.clock = clock ?? DefaultClock;
    }

    /// <inheritdoc />
    public CrashLoopGuardResult Check()
    {
        lock (syncRoot)
        {
            DateTimeOffset now = clock();

            // Permanent lockout is checked BEFORE the stability
            // reset: once the guard has decided the runtime is
            // irrecoverable, the only escape is an explicit
            // Reset() call. Time passing on its own must not be
            // able to forgive a runaway crash loop.
            //
            // Strictly greater than: with the default of 10, the
            // 11th consecutive failure is the first to be rejected
            // as a permanent lockout rather than a bounded backoff.
            if (consecutiveFailures > options.MaxConsecutiveFailures)
            {
                return new CrashLoopGuardResult(
                    isAllowed: false,
                    backoffRemaining: null,
                    consecutiveFailures: consecutiveFailures);
            }

            // Reset the counter when the runtime has been stable
            // long enough. The "last event" we measure against is
            // whichever of (last failure, last success) is the most
            // recent — a success recorded after a failure is the
            // signal we want to honour. A failure recorded after a
            // success restarts the stability window.
            DateTimeOffset? lastEventUtc = LastEventUtcLocked();
            if (consecutiveFailures > 0
                && lastEventUtc is { } last
                && (now - last) >= options.StabilityWindow)
            {
                consecutiveFailures = 0;
                lastFailureUtc = null;
                // lastSuccessUtc is intentionally preserved; it is older than any future failure and LastEventUtcLocked() will correctly prefer the newer timestamp.
            }

            if (consecutiveFailures == 0)
            {
                return new CrashLoopGuardResult(
                    isAllowed: true,
                    backoffRemaining: null,
                    consecutiveFailures: 0);
            }

            // The counter is in [1, MaxConsecutiveFailures] and
            // lastFailureUtc is guaranteed non-null because
            // consecutiveFailures > 0 only happens through
            // RecordFailure, which always sets the timestamp.
            DateTimeOffset failureTime = lastFailureUtc
                ?? throw new InvalidOperationException(
                    "CrashLoopGuard invariant violated: consecutive failures > 0 without a recorded failure timestamp.");

            TimeSpan backoff = ComputeBackoff(options, consecutiveFailures);
            TimeSpan elapsed = now - failureTime;

            if (elapsed >= backoff)
            {
                // The caller is allowed to attempt a restart, but
                // the counter is NOT cleared here — only a stable
                // run clears it.
                return new CrashLoopGuardResult(
                    isAllowed: true,
                    backoffRemaining: null,
                    consecutiveFailures: consecutiveFailures);
            }

            return new CrashLoopGuardResult(
                isAllowed: false,
                backoffRemaining: backoff - elapsed,
                consecutiveFailures: consecutiveFailures);
        }
    }

    /// <inheritdoc />
    public void RecordFailure()
    {
        lock (syncRoot)
        {
            consecutiveFailures++;
            lastFailureUtc = clock();
        }
    }

    /// <inheritdoc />
    public void RecordSuccess()
    {
        lock (syncRoot)
        {
            lastSuccessUtc = clock();
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (syncRoot)
        {
            consecutiveFailures = 0;
            lastFailureUtc = null;
            lastSuccessUtc = null;
        }
    }

    private DateTimeOffset? LastEventUtcLocked()
    {
        if (lastFailureUtc is null && lastSuccessUtc is null)
        {
            return null;
        }

        if (lastFailureUtc is null)
        {
            return lastSuccessUtc;
        }

        if (lastSuccessUtc is null)
        {
            return lastFailureUtc;
        }

        return lastFailureUtc > lastSuccessUtc ? lastFailureUtc : lastSuccessUtc;
    }

    private static TimeSpan ComputeBackoff(in CrashLoopGuardOptions options, int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        // backoff = min(BaseBackoff * 2^(consecutiveFailures - 1), MaxBackoff)
        //
        // We double the TimeSpan one step at a time and bail out
        // as soon as the next doubling would exceed MaxBackoff.
        // The cap on iterations is purely a defensive bound: the
        // doubling reaches MaxBackoff in at most
        // ceil(log2(MaxBackoff / BaseBackoff)) steps, which for
        // any realistic configuration is below 64.
        TimeSpan current = options.BaseBackoff;
        int remaining = consecutiveFailures - 1;
        const int maxIterations = 64;
        int iterations = 0;

        while (remaining > 0 && iterations < maxIterations)
        {
            if (current >= options.MaxBackoff)
            {
                return options.MaxBackoff;
            }

            TimeSpan doubled = current + current;
            if (doubled > options.MaxBackoff)
            {
                return options.MaxBackoff;
            }

            current = doubled;
            remaining--;
            iterations++;
        }

        if (current > options.MaxBackoff)
        {
            return options.MaxBackoff;
        }

        return current;
    }

    private static DateTimeOffset DefaultClock() => DateTimeOffset.UtcNow;
}

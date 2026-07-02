using System;

namespace Zapret2Pilot.Runtime.Guard;

/// <summary>
/// Configuration for <see cref="CrashLoopGuard"/>. The guard
/// enforces exponential backoff between restart attempts and a
/// permanent lockout once <see cref="MaxConsecutiveFailures"/> is
/// exceeded. <see cref="StabilityWindow"/> controls how long the
/// runtime must remain healthy before a single <c>RecordSuccess</c>
/// resets the consecutive-failure counter.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure configuration record: it has no I/O, no process
/// dependencies and no clock access. The guard itself remains the
/// only place that mutates runtime state.
/// </para>
/// <para>
/// Defaults are chosen so the guard is meaningful for a long-running
/// process supervisor: a 2-second base, a 5-minute cap, a 60-second
/// stability window and a 10-failure lockout threshold. Tests may
/// pass smaller values to keep scenarios fast and deterministic.
/// </para>
/// </remarks>
public sealed record CrashLoopGuardOptions
{
    /// <summary>
    /// Default backoff applied after the first failure.
    /// </summary>
    public static readonly TimeSpan DefaultBaseBackoff = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Default upper bound on the backoff duration.
    /// </summary>
    public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Default stability window: a single <c>RecordSuccess</c>
    /// only resets the consecutive-failure counter if the runtime
    /// has been healthy for at least this long.
    /// </summary>
    public static readonly TimeSpan DefaultStabilityWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Default cap on consecutive failures before the guard
    /// switches to a permanent lockout.
    /// </summary>
    public const int DefaultMaxConsecutiveFailures = 10;

    /// <summary>
    /// Creates a new <see cref="CrashLoopGuardOptions"/> using the
    /// documented defaults. Equivalent to passing every default
    /// value to the canonical constructor.
    /// </summary>
    public CrashLoopGuardOptions()
        : this(
            baseBackoff: DefaultBaseBackoff,
            maxBackoff: DefaultMaxBackoff,
            stabilityWindow: DefaultStabilityWindow,
            maxConsecutiveFailures: DefaultMaxConsecutiveFailures)
    {
    }

    /// <summary>
    /// Creates a new <see cref="CrashLoopGuardOptions"/> with the
    /// supplied values. Every argument is validated; the canonical
    /// constructor is the only path that mutates the
    /// configuration record.
    /// </summary>
    /// <param name="baseBackoff">Backoff applied after the first
    /// consecutive failure. Must be strictly positive.</param>
    /// <param name="maxBackoff">Upper bound on the backoff
    /// duration. Must be strictly positive and at least as large
    /// as <paramref name="baseBackoff"/>.</param>
    /// <param name="stabilityWindow">Minimum time the runtime
    /// must remain healthy for a <c>RecordSuccess</c> to reset the
    /// consecutive-failure counter. Must be strictly positive.</param>
    /// <param name="maxConsecutiveFailures">Maximum number of
    /// consecutive failures before the guard enters permanent
    /// lockout. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any argument is non-positive or when
    /// <paramref name="maxBackoff"/> is smaller than
    /// <paramref name="baseBackoff"/>.
    /// </exception>
    public CrashLoopGuardOptions(
        TimeSpan baseBackoff,
        TimeSpan maxBackoff,
        TimeSpan stabilityWindow,
        int maxConsecutiveFailures)
    {
        if (baseBackoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseBackoff),
                baseBackoff,
                "Base backoff must be a positive time span.");
        }

        if (maxBackoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBackoff),
                maxBackoff,
                "Max backoff must be a positive time span.");
        }

        if (maxBackoff < baseBackoff)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBackoff),
                maxBackoff,
                "Max backoff must be greater than or equal to base backoff.");
        }

        if (stabilityWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stabilityWindow),
                stabilityWindow,
                "Stability window must be a positive time span.");
        }

        if (maxConsecutiveFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConsecutiveFailures),
                maxConsecutiveFailures,
                "Max consecutive failures must be positive.");
        }

        BaseBackoff = baseBackoff;
        MaxBackoff = maxBackoff;
        StabilityWindow = stabilityWindow;
        MaxConsecutiveFailures = maxConsecutiveFailures;
    }

    /// <summary>
    /// Backoff applied after the first consecutive failure. Each
    /// subsequent failure doubles the backoff up to
    /// <see cref="MaxBackoff"/>.
    /// </summary>
    public TimeSpan BaseBackoff { get; }

    /// <summary>
    /// Upper bound on the backoff duration. The exponential backoff
    /// never exceeds this value, regardless of the number of
    /// consecutive failures.
    /// </summary>
    public TimeSpan MaxBackoff { get; }

    /// <summary>
    /// Minimum time the runtime must remain healthy before a
    /// successful check resets the consecutive-failure counter.
    /// </summary>
    public TimeSpan StabilityWindow { get; }

    /// <summary>
    /// Maximum number of consecutive failures before the guard
    /// enters permanent lockout and refuses every further restart
    /// attempt.
    /// </summary>
    public int MaxConsecutiveFailures { get; }
}

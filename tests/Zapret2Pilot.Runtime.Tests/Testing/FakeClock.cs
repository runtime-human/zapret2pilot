using System;

namespace Zapret2Pilot.Runtime.Tests.Testing;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> for tests. The clock
/// starts at a fixed instant and only moves forward when a test
/// calls <see cref="Advance(TimeSpan)"/>, so any code that asks the
/// provider for "now" sees a fully controllable timestamp instead
/// of wall-clock time.
/// </summary>
/// <remarks>
/// <para>
/// Two complementary surfaces are exposed so the fake can drive
/// every kind of consumer in the runtime:
/// </para>
/// <list type="bullet">
///   <item><see cref="Now"/> returns a <see cref="DateTimeOffset"/>
///         that can be passed where a <see cref="Func{TResult}"/>
///         clock is expected (for example,
///         <c>new CrashLoopGuard(options, clock.Now)</c>).</item>
///   <item><see cref="GetUtcNow"/> is the
///         <see cref="TimeProvider.GetUtcNow"/> override used by
///         code that takes a <see cref="TimeProvider"/> (for
///         example, <c>RuntimeSupervisor</c>).</item>
/// </list>
/// <para>
/// Both surfaces read from the same internal field, so the fake is
/// a single source of truth regardless of which surface a
/// collaborator happens to use.
/// </para>
/// </remarks>
public sealed class FakeClock : TimeProvider
{
    private static readonly DateTimeOffset DefaultInitial =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private DateTimeOffset now;

    /// <summary>
    /// Creates a new <see cref="FakeClock"/> rooted at
    /// <paramref name="initial"/>, or at
    /// <c>2026-01-01T00:00:00Z</c> when no value is supplied.
    /// </summary>
    /// <param name="initial">
    /// Optional starting timestamp. <c>null</c> selects the
    /// default root used by the runtime's test suite so tests
    /// stay aligned with the project's documented clock
    /// convention.
    /// </param>
    public FakeClock(DateTimeOffset? initial = null)
    {
        now = initial ?? DefaultInitial;
    }

    /// <summary>
    /// Returns the current fake time. The method is exposed as a
    /// plain instance method so it can be used as a
    /// <see cref="Func{TResult}"/> clock source (for example,
    /// <c>clock.Now</c>).
    /// </summary>
    public DateTimeOffset Now() => now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>
    /// Moves the fake clock forward by <paramref name="delta"/>.
    /// Negative deltas are rejected because time only flows
    /// forward in the runtime's state machine; allowing the test
    /// to go backwards would silently rewind every consumer that
    /// reads the clock.
    /// </summary>
    /// <param name="delta">
    /// Amount of time to add to the current fake time. Must be a
    /// non-negative <see cref="TimeSpan"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="delta"/> is negative.
    /// </exception>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "Clock advance must be a non-negative time span.");
        }

        now += delta;
    }
}

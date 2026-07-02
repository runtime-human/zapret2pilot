using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Runtime.Guard;

namespace Zapret2Pilot.Runtime.Tests.Guard;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// Focused xUnit tests for <see cref="CrashLoopGuard"/> (milestone
/// 0.0.22). All scenarios use a deterministic
/// <see cref="FakeClock"/> so the tests stay fast and do not rely
/// on real time. The guard is exercised in memory only — no real
/// process is launched and no filesystem is touched.
/// </summary>
public sealed class CrashLoopGuardTests
{
    private static readonly TimeSpan SmallBaseBackoff = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan SmallMaxBackoff = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan SmallStabilityWindow = TimeSpan.FromMilliseconds(50);
    private const int SmallMaxFailures = 3;

    [Fact]
    public static void Check_InitiallyAllowed()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        CrashLoopGuardResult result = guard.Check();

        Assert.True(result.IsAllowed);
        Assert.Null(result.BackoffRemaining);
        Assert.Equal(0, result.ConsecutiveFailures);
    }

    [Fact]
    public static void RecordFailure_SingleFailure_BackoffApplied()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();

        CrashLoopGuardResult result = guard.Check();

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.BackoffRemaining);
        Assert.True(result.BackoffRemaining > TimeSpan.Zero);
        Assert.Equal(1, result.ConsecutiveFailures);
    }

    [Fact]
    public static void RecordFailure_MultipleFailures_ExponentialBackoff()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        // First failure: backoff = BaseBackoff.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();
        CrashLoopGuardResult after1 = guard.Check();
        Assert.False(after1.IsAllowed);
        Assert.NotNull(after1.BackoffRemaining);
        TimeSpan firstBackoff = after1.BackoffRemaining!.Value;
        Assert.True(
            firstBackoff <= SmallBaseBackoff,
            $"First backoff {firstBackoff} must be <= BaseBackoff {SmallBaseBackoff}");

        // Second failure: backoff = 2 * BaseBackoff.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();
        CrashLoopGuardResult after2 = guard.Check();
        Assert.False(after2.IsAllowed);
        Assert.NotNull(after2.BackoffRemaining);
        TimeSpan secondBackoff = after2.BackoffRemaining!.Value;
        Assert.True(
            secondBackoff >= firstBackoff,
            $"Second backoff {secondBackoff} must be >= first backoff {firstBackoff}");

        // Third failure: backoff = 4 * BaseBackoff.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();
        CrashLoopGuardResult after3 = guard.Check();
        Assert.False(after3.IsAllowed);
        Assert.NotNull(after3.BackoffRemaining);
        TimeSpan thirdBackoff = after3.BackoffRemaining!.Value;
        Assert.True(
            thirdBackoff >= secondBackoff,
            $"Third backoff {thirdBackoff} must be >= second backoff {secondBackoff}");
    }

    [Fact]
    public static void RecordFailure_ExceedsMaxBackoff_CappedAtMax()
    {
        // A configuration where the doubling would explode past
        // the cap after just a couple of failures.
        CrashLoopGuardOptions options = new(
            baseBackoff: TimeSpan.FromMilliseconds(50),
            maxBackoff: TimeSpan.FromMilliseconds(70),
            stabilityWindow: TimeSpan.FromMilliseconds(50),
            maxConsecutiveFailures: 100);
        FakeClock clock = new();
        CrashLoopGuard guard = new(options, clock.Now);

        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            guard.RecordFailure();
        }

        CrashLoopGuardResult result = guard.Check();

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.BackoffRemaining);
        Assert.True(
            result.BackoffRemaining <= options.MaxBackoff,
            $"Backoff {result.BackoffRemaining} must be <= MaxBackoff {options.MaxBackoff}");
    }

    [Fact]
    public static void RecordFailure_ExactBackoffValues_AreExponentialAndCapped()
    {
        // A small, deterministic configuration that proves the
        // exact backoff schedule: doubling on each consecutive
        // failure, then saturating at MaxBackoff. With FakeClock
        // the elapsed time since the last failure is zero, so the
        // BackoffRemaining returned by Check equals the backoff
        // value computed from the current counter.
        CrashLoopGuardOptions options = new(
            baseBackoff: TimeSpan.FromMilliseconds(20),
            maxBackoff: TimeSpan.FromMilliseconds(100),
            stabilityWindow: TimeSpan.FromMilliseconds(50),
            maxConsecutiveFailures: 5);
        FakeClock clock = new();
        CrashLoopGuard guard = new(options, clock.Now);

        // Failure #1 → backoff = 20ms = BaseBackoff.
        guard.RecordFailure();
        CrashLoopGuardResult after1 = guard.Check();
        Assert.False(after1.IsAllowed);
        Assert.Equal(TimeSpan.FromMilliseconds(20), after1.BackoffRemaining);
        Assert.Equal(1, after1.ConsecutiveFailures);

        // Failure #2 → backoff = 40ms = 2 * BaseBackoff.
        guard.RecordFailure();
        CrashLoopGuardResult after2 = guard.Check();
        Assert.False(after2.IsAllowed);
        Assert.Equal(TimeSpan.FromMilliseconds(40), after2.BackoffRemaining);
        Assert.Equal(2, after2.ConsecutiveFailures);

        // Failure #3 → backoff = 80ms = 4 * BaseBackoff.
        guard.RecordFailure();
        CrashLoopGuardResult after3 = guard.Check();
        Assert.False(after3.IsAllowed);
        Assert.Equal(TimeSpan.FromMilliseconds(80), after3.BackoffRemaining);
        Assert.Equal(3, after3.ConsecutiveFailures);

        // Failure #4 → backoff = 160ms saturated to MaxBackoff = 100ms.
        guard.RecordFailure();
        CrashLoopGuardResult after4 = guard.Check();
        Assert.False(after4.IsAllowed);
        Assert.Equal(TimeSpan.FromMilliseconds(100), after4.BackoffRemaining);
        Assert.Equal(4, after4.ConsecutiveFailures);
    }

    [Fact]
    public static void RecordFailure_ExceedsMaxConsecutiveFailures_PermanentLockout()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        // With SmallMaxFailures=3, the 4th consecutive failure
        // pushes the counter strictly above the threshold and
        // the guard enters permanent lockout.
        for (int i = 0; i < SmallMaxFailures + 1; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            guard.RecordFailure();
        }

        // Advance well past any backoff window — the lockout
        // must persist regardless of how much time passes.
        clock.Advance(TimeSpan.FromMinutes(10));

        CrashLoopGuardResult result = guard.Check();

        Assert.False(result.IsAllowed);
        Assert.Null(result.BackoffRemaining);
        Assert.Equal(SmallMaxFailures + 1, result.ConsecutiveFailures);
    }

    [Fact]
    public static void RecordSuccess_WithinStabilityWindow_DoesNotReset()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();
        guard.RecordSuccess();

        // Advance LESS than both the backoff window AND the
        // stability window: the counter must NOT reset and the
        // guard must still apply a backoff.
        clock.Advance(TimeSpan.FromMilliseconds(5));

        CrashLoopGuardResult result = guard.Check();

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.BackoffRemaining);
        Assert.Equal(1, result.ConsecutiveFailures);
    }

    [Fact]
    public static void RecordSuccess_AfterStabilityWindow_ResetsCounter()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();
        guard.RecordSuccess();

        // Advance past the stability window since the success:
        // the next Check must clear the counter.
        clock.Advance(SmallStabilityWindow + TimeSpan.FromMilliseconds(10));

        CrashLoopGuardResult result = guard.Check();

        Assert.True(result.IsAllowed);
        Assert.Null(result.BackoffRemaining);
        Assert.Equal(0, result.ConsecutiveFailures);
    }

    [Fact]
    public static void Reset_ClearsAllState()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        // Drive the guard into a deep failure state and then
        // reset. After Reset, Check must behave like a brand-new
        // guard.
        for (int i = 0; i < SmallMaxFailures; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            guard.RecordFailure();
        }

        guard.Reset();

        CrashLoopGuardResult result = guard.Check();

        Assert.True(result.IsAllowed);
        Assert.Null(result.BackoffRemaining);
        Assert.Equal(0, result.ConsecutiveFailures);

        // A subsequent failure recorded AFTER Reset starts a
        // fresh backoff schedule — not the schedule that existed
        // before Reset.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();
        CrashLoopGuardResult afterFreshFailure = guard.Check();
        Assert.False(afterFreshFailure.IsAllowed);
        Assert.NotNull(afterFreshFailure.BackoffRemaining);
        Assert.True(afterFreshFailure.BackoffRemaining <= SmallBaseBackoff);
    }

    [Fact]
    public static void Check_AfterBackoffElapses_Allowed()
    {
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        guard.RecordFailure();

        // First Check is blocked by the backoff.
        CrashLoopGuardResult blocked = guard.Check();
        Assert.False(blocked.IsAllowed);

        // Advance past the backoff window.
        clock.Advance(SmallBaseBackoff + TimeSpan.FromMilliseconds(10));

        CrashLoopGuardResult allowed = guard.Check();

        Assert.True(allowed.IsAllowed);
        Assert.Null(allowed.BackoffRemaining);
        // The counter is NOT cleared by Check — only a stable
        // success clears it.
        Assert.Equal(1, allowed.ConsecutiveFailures);
    }

    [Fact]
    public static async Task ConcurrentRecordFailureAndCheck_StateRemainsConsistent()
    {
        // A stress test: many threads call RecordFailure while
        // many other threads call Check. The guard must never
        // throw and the final counter must equal the number of
        // RecordFailure calls.
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FakeClock clock = new();
        CrashLoopGuard guard = CreateGuard(clock);

        const int writerThreads = 4;
        const int readerThreads = 4;
        const int failuresPerWriter = 500;

        Task[] writers = new Task[writerThreads];
        for (int w = 0; w < writerThreads; w++)
        {
            writers[w] = Task.Run(() =>
            {
                for (int i = 0; i < failuresPerWriter; i++)
                {
                    guard.RecordFailure();
                }
            }, cancellationToken);
        }

        Task[] readers = new Task[readerThreads];
        for (int r = 0; r < readerThreads; r++)
        {
            readers[r] = Task.Run(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    CrashLoopGuardResult result = guard.Check();
                    Assert.True(result.ConsecutiveFailures >= 0);
                }
            }, cancellationToken);
        }

        await Task.WhenAll(writers).WaitAsync(cancellationToken);
        await Task.WhenAll(readers).WaitAsync(cancellationToken);

        // The final counter must equal the total number of
        // failures, with no torn writes.
        CrashLoopGuardResult finalResult = guard.Check();
        Assert.Equal(writerThreads * failuresPerWriter, finalResult.ConsecutiveFailures);
    }

    [Fact]
    public static void CrashLoopGuardResult_RejectsNegativeConsecutiveFailures()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardResult(
                isAllowed: true,
                backoffRemaining: null,
                consecutiveFailures: -1));

        Assert.Equal("consecutiveFailures", exception.ParamName);
    }

    private static CrashLoopGuard CreateGuard(FakeClock clock)
    {
        CrashLoopGuardOptions options = new(
            baseBackoff: SmallBaseBackoff,
            maxBackoff: SmallMaxBackoff,
            stabilityWindow: SmallStabilityWindow,
            maxConsecutiveFailures: SmallMaxFailures);
        return new CrashLoopGuard(options, clock.Now);
    }

    /// <summary>
    /// Minimal fake clock that hands a <see cref="Func{T}"/> back
    /// to the guard and lets tests advance time deterministically.
    /// </summary>
    private sealed class FakeClock
    {
        private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Now() => now;

        public void Advance(TimeSpan delta)
        {
            now += delta;
        }
    }
}

#pragma warning restore CA1707 // Identifiers should not contain underscores

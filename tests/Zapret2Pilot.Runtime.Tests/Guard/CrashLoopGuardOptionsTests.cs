using System;
using Xunit;
using Zapret2Pilot.Runtime.Guard;

namespace Zapret2Pilot.Runtime.Tests.Guard;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// Focused xUnit tests for <see cref="CrashLoopGuardOptions"/>
/// validation. The guard's runtime correctness depends on the
/// options rejecting every illegal combination, so this file is
/// the contract gate for any future caller that tries to
/// misconfigure the guard.
/// </summary>
public sealed class CrashLoopGuardOptionsTests
{
    [Fact]
    public static void DefaultConstructor_AppliesDocumentedDefaults()
    {
        CrashLoopGuardOptions options = new();

        Assert.Equal(CrashLoopGuardOptions.DefaultBaseBackoff, options.BaseBackoff);
        Assert.Equal(CrashLoopGuardOptions.DefaultMaxBackoff, options.MaxBackoff);
        Assert.Equal(CrashLoopGuardOptions.DefaultStabilityWindow, options.StabilityWindow);
        Assert.Equal(CrashLoopGuardOptions.DefaultMaxConsecutiveFailures, options.MaxConsecutiveFailures);
    }

    [Fact]
    public static void Constructor_RejectsZeroBaseBackoff()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.Zero,
                maxBackoff: TimeSpan.FromSeconds(10),
                stabilityWindow: TimeSpan.FromSeconds(1),
                maxConsecutiveFailures: 5));

        Assert.Equal("baseBackoff", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNegativeBaseBackoff()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(-1),
                maxBackoff: TimeSpan.FromSeconds(10),
                stabilityWindow: TimeSpan.FromSeconds(1),
                maxConsecutiveFailures: 5));

        Assert.Equal("baseBackoff", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsZeroMaxBackoff()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(1),
                maxBackoff: TimeSpan.Zero,
                stabilityWindow: TimeSpan.FromSeconds(1),
                maxConsecutiveFailures: 5));

        Assert.Equal("maxBackoff", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNegativeMaxBackoff()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(1),
                maxBackoff: TimeSpan.FromSeconds(-1),
                stabilityWindow: TimeSpan.FromSeconds(1),
                maxConsecutiveFailures: 5));

        Assert.Equal("maxBackoff", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsMaxBackoffLessThanBaseBackoff()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(5),
                maxBackoff: TimeSpan.FromSeconds(2),
                stabilityWindow: TimeSpan.FromSeconds(1),
                maxConsecutiveFailures: 5));

        Assert.Equal("maxBackoff", exception.ParamName);
    }

    [Fact]
    public static void Constructor_AcceptsMaxBackoffEqualToBaseBackoff()
    {
        TimeSpan shared = TimeSpan.FromSeconds(2);

        CrashLoopGuardOptions options = new(
            baseBackoff: shared,
            maxBackoff: shared,
            stabilityWindow: TimeSpan.FromSeconds(1),
            maxConsecutiveFailures: 5);

        Assert.Equal(shared, options.BaseBackoff);
        Assert.Equal(shared, options.MaxBackoff);
    }

    [Fact]
    public static void Constructor_RejectsZeroStabilityWindow()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(1),
                maxBackoff: TimeSpan.FromSeconds(10),
                stabilityWindow: TimeSpan.Zero,
                maxConsecutiveFailures: 5));

        Assert.Equal("stabilityWindow", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNegativeStabilityWindow()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(1),
                maxBackoff: TimeSpan.FromSeconds(10),
                stabilityWindow: TimeSpan.FromSeconds(-1),
                maxConsecutiveFailures: 5));

        Assert.Equal("stabilityWindow", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsZeroMaxConsecutiveFailures()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(1),
                maxBackoff: TimeSpan.FromSeconds(10),
                stabilityWindow: TimeSpan.FromSeconds(1),
                maxConsecutiveFailures: 0));

        Assert.Equal("maxConsecutiveFailures", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNegativeMaxConsecutiveFailures()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLoopGuardOptions(
                baseBackoff: TimeSpan.FromSeconds(1),
                maxBackoff: TimeSpan.FromSeconds(10),
                stabilityWindow: TimeSpan.FromSeconds(1),
                maxConsecutiveFailures: -1));

        Assert.Equal("maxConsecutiveFailures", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNullOptionsViaCrashLoopGuard()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new CrashLoopGuard(options: null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNullOptionsWithClock()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new CrashLoopGuard(options: null!, clock: () => DateTimeOffset.UtcNow));

        Assert.Equal("options", exception.ParamName);
    }
}

#pragma warning restore CA1707 // Identifiers should not contain underscores

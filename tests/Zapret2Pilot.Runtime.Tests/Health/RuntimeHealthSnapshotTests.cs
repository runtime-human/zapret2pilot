using System;
using Xunit;
using Zapret2Pilot.Runtime.Health;

namespace Zapret2Pilot.Runtime.Tests.Health;

// snake_case test method names; suppress CA1707 for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

public sealed class RuntimeHealthSnapshotTests
{
    [Fact]
    public static void Constructor_RejectsUnknownEnumValue()
    {
        DateTimeOffset observed = DateTimeOffset.UtcNow;
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new RuntimeHealthSnapshot(
                state: (RuntimeHealthState)999,
                processId: null,
                observedAtUtc: observed));
        Assert.Equal("state", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNonPositiveProcessId()
    {
        DateTimeOffset observed = DateTimeOffset.UtcNow;
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Healthy,
                processId: 0,
                observedAtUtc: observed));
        Assert.Equal("processId", exception.ParamName);
    }

    [Fact]
    public static void Constructor_AcceptsNullProcessId()
    {
        DateTimeOffset observed = DateTimeOffset.UtcNow;
        RuntimeHealthSnapshot snapshot = new(
            state: RuntimeHealthState.Unknown,
            processId: null,
            observedAtUtc: observed);

        Assert.Equal(RuntimeHealthState.Unknown, snapshot.State);
        Assert.Null(snapshot.ProcessId);
        Assert.Equal(observed, snapshot.ObservedAtUtc);
    }

    [Fact]
    public static void RecordEquality_IsStructural()
    {
        DateTimeOffset observed = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        RuntimeHealthSnapshot first = new(
            state: RuntimeHealthState.Healthy,
            processId: 42,
            observedAtUtc: observed);
        RuntimeHealthSnapshot second = new(
            state: RuntimeHealthState.Healthy,
            processId: 42,
            observedAtUtc: observed);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public static void RecordEquality_DiffersWhenAnyFieldDiffers()
    {
        DateTimeOffset observed = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        RuntimeHealthSnapshot baseline = new(
            state: RuntimeHealthState.Healthy,
            processId: 42,
            observedAtUtc: observed);

        Assert.NotEqual(
            baseline,
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Exited,
                processId: 42,
                observedAtUtc: observed));
        Assert.NotEqual(
            baseline,
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Healthy,
                processId: 43,
                observedAtUtc: observed));
        Assert.NotEqual(
            baseline,
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Healthy,
                processId: 42,
                observedAtUtc: observed.AddSeconds(1)));
    }
}

#pragma warning restore CA1707 // Identifiers should not contain underscores

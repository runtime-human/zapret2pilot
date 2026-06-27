using System;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.State;

namespace Zapret2Pilot.Runtime.Tests.State;

public sealed class RuntimeKernelStateStoreTests
{
    [Fact]
    public static void StartSessionPersistsActiveSession()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        ProfileId profileId = new("profile-a");
        RuntimePlanId planId = new("plan-a");
        RuntimePlanCacheKey planCacheKey = new("a".PadRight(64, '0'));

        RuntimeSessionRecord started = store.StartSession(profileId, planId, planCacheKey);

        Assert.Equal(RuntimeSessionState.Active, started.State);
        Assert.Equal(profileId, started.ProfileId);
        Assert.Equal(planId, started.PlanId);
        Assert.Equal(planCacheKey, started.PlanCacheKey);
        Assert.Null(started.EndedAtUtc);

        RuntimeSessionRecord? current = store.GetCurrentSession();
        Assert.NotNull(current);
        Assert.Equal(started.Id, current!.Id);
        Assert.Equal(RuntimeSessionState.Active, current.State);
    }

    [Fact]
    public static void StartSessionEndsPreviouslyActiveSession()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        ProfileId profileId = new("profile-a");
        RuntimePlanId firstPlan = new("plan-a");
        RuntimePlanId secondPlan = new("plan-b");
        RuntimePlanCacheKey firstKey = new("1".PadRight(64, '1'));
        RuntimePlanCacheKey secondKey = new("2".PadRight(64, '2'));

        RuntimeSessionRecord first = store.StartSession(profileId, firstPlan, firstKey);
        RuntimeSessionRecord second = store.StartSession(profileId, secondPlan, secondKey);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(RuntimeSessionState.Active, second.State);

        IReadOnlyList<RuntimeSessionRecord> history = store.GetRecentSessions(10);

        RuntimeSessionRecord firstStored = Assert.Single(
            history,
            record => record.Id == first.Id);
        Assert.Equal(RuntimeSessionState.Stopped, firstStored.State);
        Assert.NotNull(firstStored.EndedAtUtc);
    }

    [Fact]
    public static void StartSessionRejectsNullArguments()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        ProfileId profileId = new("profile-a");
        RuntimePlanId planId = new("plan-a");
        RuntimePlanCacheKey planCacheKey = new("a".PadRight(64, 'a'));

        Assert.Throws<ArgumentNullException>(
            () => store.StartSession(null!, planId, planCacheKey));
        Assert.Throws<ArgumentNullException>(
            () => store.StartSession(profileId, null!, planCacheKey));
        Assert.Throws<ArgumentNullException>(
            () => store.StartSession(profileId, planId, null!));
    }

    [Fact]
    public static void EndSessionClosesSessionAndClearsSingleton()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        RuntimeSessionRecord started = store.StartSession(
            new ProfileId("profile-a"),
            new RuntimePlanId("plan-a"),
            new RuntimePlanCacheKey("a".PadRight(64, 'a')));

        store.EndSession(started.Id);

        Assert.Null(store.GetCurrentSession());

        IReadOnlyList<RuntimeSessionRecord> history = store.GetRecentSessions(10);
        RuntimeSessionRecord closed = Assert.Single(history);
        Assert.Equal(started.Id, closed.Id);
        Assert.Equal(RuntimeSessionState.Stopped, closed.State);
        Assert.NotNull(closed.EndedAtUtc);
    }

    [Fact]
    public static void EndSessionWithUnknownIdThrows()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        RuntimeSessionId unknown = new(Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(() => store.EndSession(unknown));
    }

    [Fact]
    public static void EndSessionWithNullSessionIdThrows()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());

        Assert.Throws<ArgumentNullException>(() => store.EndSession(null!));
    }

    [Fact]
    public static void GetCurrentSessionWithNoSessionReturnsNull()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());

        Assert.Null(store.GetCurrentSession());
    }

    [Fact]
    public static void GetRecentSessionsReturnsNewestFirst()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        ProfileId profileId = new("profile-a");
        RuntimePlanId planId = new("plan-a");
        RuntimePlanCacheKey planCacheKey = new("a".PadRight(64, 'a'));

        RuntimeSessionRecord first = store.StartSession(profileId, planId, planCacheKey);
        RuntimeSessionRecord second = store.StartSession(profileId, planId, planCacheKey);
        RuntimeSessionRecord third = store.StartSession(profileId, planId, planCacheKey);

        IReadOnlyList<RuntimeSessionRecord> history = store.GetRecentSessions(10);

        Assert.Equal(3, history.Count);
        Assert.Equal(third.Id, history[0].Id);
        Assert.Equal(second.Id, history[1].Id);
        Assert.Equal(first.Id, history[2].Id);
    }

    [Fact]
    public static void GetRecentSessionsRespectsLimit()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        ProfileId profileId = new("profile-a");
        RuntimePlanId planId = new("plan-a");
        RuntimePlanCacheKey planCacheKey = new("a".PadRight(64, 'a'));

        for (int i = 0; i < 5; i++)
        {
            store.StartSession(profileId, planId, planCacheKey);
        }

        IReadOnlyList<RuntimeSessionRecord> history = store.GetRecentSessions(2);

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public static void GetRecentSessionsRejectsNonPositiveCount()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());

        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetRecentSessions(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetRecentSessions(-1));
    }

    [Fact]
    public static void StartSessionPreservesUniqueIdAndTimestamp()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        ProfileId profileId = new("profile-a");
        RuntimePlanId planId = new("plan-a");
        RuntimePlanCacheKey planCacheKey = new("a".PadRight(64, 'a'));

        RuntimeSessionRecord first = store.StartSession(profileId, planId, planCacheKey);
        RuntimeSessionRecord second = store.StartSession(profileId, planId, planCacheKey);

        Assert.NotEqual(first.Id.Value, second.Id.Value);
        Assert.NotEqual(first.StartedAtUtc, second.StartedAtUtc);
    }
}

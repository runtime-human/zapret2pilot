using System;
using Xunit;
using Zapret2Pilot.Core.Primitives;

namespace Zapret2Pilot.Core.Tests;

public sealed class TypedIdsTests
{
    [Fact]
    public static void TypedIdsRejectNullValues()
    {
        Assert.Throws<ArgumentNullException>(() => new ProfileId(null!));
        Assert.Throws<ArgumentNullException>(() => new StrategyPackId(null!));
        Assert.Throws<ArgumentNullException>(() => new RuntimePlanId(null!));
        Assert.Throws<ArgumentNullException>(() => new RuntimeSessionId(null!));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTransactionId(null!));
        Assert.Throws<ArgumentNullException>(() => new HostlistId(null!));
        Assert.Throws<ArgumentNullException>(() => new ProbeSessionId(null!));
        Assert.Throws<ArgumentNullException>(() => new EventId(null!));
    }

    [Fact]
    public static void TypedIdsRejectEmptyValues()
    {
        Assert.Throws<ArgumentException>(() => new ProfileId(string.Empty));
        Assert.Throws<ArgumentException>(() => new StrategyPackId(string.Empty));
        Assert.Throws<ArgumentException>(() => new RuntimePlanId(string.Empty));
        Assert.Throws<ArgumentException>(() => new RuntimeSessionId(string.Empty));
        Assert.Throws<ArgumentException>(() => new RuntimeTransactionId(string.Empty));
        Assert.Throws<ArgumentException>(() => new HostlistId(string.Empty));
        Assert.Throws<ArgumentException>(() => new ProbeSessionId(string.Empty));
        Assert.Throws<ArgumentException>(() => new EventId(string.Empty));
    }

    [Fact]
    public static void TypedIdsRejectWhitespaceValues()
    {
        Assert.Throws<ArgumentException>(() => new ProfileId("   "));
        Assert.Throws<ArgumentException>(() => new StrategyPackId("   "));
        Assert.Throws<ArgumentException>(() => new RuntimePlanId("   "));
        Assert.Throws<ArgumentException>(() => new RuntimeSessionId("   "));
        Assert.Throws<ArgumentException>(() => new RuntimeTransactionId("   "));
        Assert.Throws<ArgumentException>(() => new HostlistId("   "));
        Assert.Throws<ArgumentException>(() => new ProbeSessionId("   "));
        Assert.Throws<ArgumentException>(() => new EventId("   "));
    }

    [Fact]
    public static void TypedIdsTrimOuterWhitespace()
    {
        Assert.Equal("profile", new ProfileId("  profile  ").Value);
        Assert.Equal("strategy-pack", new StrategyPackId("  strategy-pack  ").Value);
        Assert.Equal("runtime-plan", new RuntimePlanId("  runtime-plan  ").Value);
        Assert.Equal("runtime-session", new RuntimeSessionId("  runtime-session  ").Value);
        Assert.Equal("runtime-transaction", new RuntimeTransactionId("  runtime-transaction  ").Value);
        Assert.Equal("hostlist", new HostlistId("  hostlist  ").Value);
        Assert.Equal("probe-session", new ProbeSessionId("  probe-session  ").Value);
        Assert.Equal("event", new EventId("  event  ").Value);
    }

    [Fact]
    public static void SameTypedIdWithSameValueIsEqual()
    {
        Assert.Equal(new ProfileId("same"), new ProfileId("same"));
        Assert.Equal(new StrategyPackId("same"), new StrategyPackId("same"));
        Assert.Equal(new RuntimePlanId("same"), new RuntimePlanId("same"));
        Assert.Equal(new RuntimeSessionId("same"), new RuntimeSessionId("same"));
        Assert.Equal(new RuntimeTransactionId("same"), new RuntimeTransactionId("same"));
        Assert.Equal(new HostlistId("same"), new HostlistId("same"));
        Assert.Equal(new ProbeSessionId("same"), new ProbeSessionId("same"));
        Assert.Equal(new EventId("same"), new EventId("same"));
    }

    [Fact]
    public static void DifferentTypedIdWithSameValueIsNotEqual()
    {
        ProfileId profileId = new("same");
        HostlistId hostlistId = new("same");

        Assert.NotEqual<object>(profileId, hostlistId);
    }

    [Fact]
    public static void ToStringReturnsNormalizedValue()
    {
        Assert.Equal("profile", new ProfileId("  profile  ").ToString());
        Assert.Equal("strategy-pack", new StrategyPackId("  strategy-pack  ").ToString());
        Assert.Equal("runtime-plan", new RuntimePlanId("  runtime-plan  ").ToString());
        Assert.Equal("runtime-session", new RuntimeSessionId("  runtime-session  ").ToString());
        Assert.Equal("runtime-transaction", new RuntimeTransactionId("  runtime-transaction  ").ToString());
        Assert.Equal("hostlist", new HostlistId("  hostlist  ").ToString());
        Assert.Equal("probe-session", new ProbeSessionId("  probe-session  ").ToString());
        Assert.Equal("event", new EventId("  event  ").ToString());
    }
}

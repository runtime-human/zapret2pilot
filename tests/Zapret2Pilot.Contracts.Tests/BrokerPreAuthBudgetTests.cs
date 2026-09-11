using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerPreAuthBudgetTests
{
    [Fact]
    public void ChallengeIssuesHaveFiniteAppSessionBudgetThatDoesNotRefillWithTime()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), timeProvider);

        for (int i = 0; i < BrokerPreAuthenticationSession.MaxChallengeIssuesPerSession; i++)
        {
            BrokerChallengeIssueResult result = session.TryIssueChallenge(peer);
            Assert.Equal(BrokerChallengeIssueStatus.Issued, result.Status);
            session.AbandonChallenge(Assert.IsType<BrokerChallengeHandle>(result.Handle));
            timeProvider.Advance(BrokerProtocolLimits.PreAuthChallengeRetryAfter);
        }

        BrokerChallengeIssueResult exhausted = session.TryIssueChallenge(peer);
        Assert.Equal(BrokerChallengeIssueStatus.SessionBudgetExhausted, exhausted.Status);
        Assert.Null(exhausted.Challenge);
        Assert.Null(exhausted.RetryAfter);

        timeProvider.Advance(TimeSpan.FromHours(1));
        Assert.Equal(
            BrokerChallengeIssueStatus.SessionBudgetExhausted,
            session.TryIssueChallenge(peer).Status);
    }

    private static byte[] CreateSecret()
        => Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();

    private static BrokerClientBinding CreateExpected()
        => new(
            AppSessionId.New(),
            ProcessId: 4242,
            ProcessCreationTimeFileTime: 133_800_000_000_000_000,
            WindowsSessionId: 3,
            UserSid: "S-1-5-21-111-222-333-1001",
            LogonSessionId: new LogonSessionId(42, 7),
            IntegrityLevelRid: 0x2000);

    private static BrokerPeerIdentity CreatePeer()
        => new(
            ProcessId: 4242,
            ProcessCreationTimeFileTime: 133_800_000_000_000_000,
            WindowsSessionId: 3,
            UserSid: "S-1-5-21-111-222-333-1001",
            LogonSessionId: new LogonSessionId(42, 7),
            IntegrityLevelRid: 0x2000);
}

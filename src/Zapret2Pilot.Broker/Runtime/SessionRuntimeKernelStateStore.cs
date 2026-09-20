using System;
using System.Collections.Generic;
using System.Linq;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.State;

namespace Zapret2Pilot.Broker.Runtime;

/// <summary>
/// Session-local runtime history used by the v7-D Broker lifecycle proof.
///
/// The privileged Broker deliberately does not open the Control Plane's
/// general application SQLite database. Durable broker recovery storage, if
/// later proven necessary, must be introduced as a dedicated minimal store
/// under its own trust boundary rather than by reusing z2p.db.
/// </summary>
internal sealed class SessionRuntimeKernelStateStore : IRuntimeKernelStateStore
{
    private readonly object sync = new();
    private readonly List<RuntimeSessionRecord> sessions = [];
    private RuntimeSessionRecord? current;

    public RuntimeSessionRecord StartSession(
        ProfileId profileId,
        RuntimePlanId planId,
        RuntimePlanCacheKey planCacheKey)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(planCacheKey);

        lock (sync)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (current is not null)
            {
                ReplaceSession(Close(current, RuntimeSessionState.Stopped, now));
            }

            RuntimeSessionRecord started = new(
                new RuntimeSessionId(Guid.NewGuid().ToString("N")),
                now,
                endedAtUtc: null,
                profileId,
                planId,
                planCacheKey,
                RuntimeSessionState.Active);

            sessions.Add(started);
            current = started;
            return started;
        }
    }

    public void EndSession(RuntimeSessionId sessionId)
        => EndSession(sessionId, RuntimeSessionState.Stopped);

    public void EndSession(RuntimeSessionId sessionId, RuntimeSessionState finalState)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (finalState is not (RuntimeSessionState.Stopped or RuntimeSessionState.Failed))
        {
            throw new ArgumentException(
                "The terminal runtime session state must be Stopped or Failed.",
                nameof(finalState));
        }

        lock (sync)
        {
            int index = sessions.FindIndex(session => session.Id == sessionId);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"No runtime session with id '{sessionId.Value}' exists.");
            }

            RuntimeSessionRecord closed = Close(
                sessions[index],
                finalState,
                DateTimeOffset.UtcNow);
            sessions[index] = closed;

            if (current?.Id == sessionId)
            {
                current = null;
            }
        }
    }

    public RuntimeSessionRecord? GetCurrentSession()
    {
        lock (sync)
        {
            return current;
        }
    }

    public IReadOnlyList<RuntimeSessionRecord> GetRecentSessions(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        lock (sync)
        {
            return sessions
                .OrderByDescending(static session => session.StartedAtUtc)
                .ThenByDescending(static session => session.Id.Value, StringComparer.Ordinal)
                .Take(count)
                .ToArray();
        }
    }

    private void ReplaceSession(RuntimeSessionRecord replacement)
    {
        int index = sessions.FindIndex(session => session.Id == replacement.Id);
        if (index >= 0)
        {
            sessions[index] = replacement;
        }

        current = null;
    }

    private static RuntimeSessionRecord Close(
        RuntimeSessionRecord session,
        RuntimeSessionState finalState,
        DateTimeOffset endedAtUtc)
        => new(
            session.Id,
            session.StartedAtUtc,
            endedAtUtc,
            session.ProfileId,
            session.PlanId,
            session.PlanCacheKey,
            finalState);
}

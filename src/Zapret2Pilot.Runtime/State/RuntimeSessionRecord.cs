using System;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;

namespace Zapret2Pilot.Runtime.State;

/// <summary>
/// Immutable record representing a single row of the
/// <c>runtime_sessions</c> table. A session is the durable trace of
/// a runtime lifecycle: it is opened by
/// <see cref="IRuntimeKernelStateStore.StartSession"/> and closed by
/// <see cref="IRuntimeKernelStateStore.EndSession"/>.
/// </summary>
public sealed record class RuntimeSessionRecord
{
    public RuntimeSessionRecord(
        RuntimeSessionId id,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        ProfileId? profileId,
        RuntimePlanId? planId,
        RuntimePlanCacheKey? planCacheKey,
        RuntimeSessionState state)
    {
        ArgumentNullException.ThrowIfNull(id, nameof(id));

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentException(
                $"Unknown {nameof(RuntimeSessionState)} value: {state}.",
                nameof(state));
        }

        Id = id;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        ProfileId = profileId;
        PlanId = planId;
        PlanCacheKey = planCacheKey;
        State = state;
    }

    public RuntimeSessionId Id { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; }

    public ProfileId? ProfileId { get; }

    public RuntimePlanId? PlanId { get; }

    public RuntimePlanCacheKey? PlanCacheKey { get; }

    public RuntimeSessionState State { get; }
}

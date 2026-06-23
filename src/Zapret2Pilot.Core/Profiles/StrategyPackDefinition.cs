using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;

namespace Zapret2Pilot.Core.Profiles;

/// <summary>
/// Resolved strategy pack domain model: a stable pack identifier and the
/// fully-resolved strategies it contributes to a profile.
///
/// This is the domain form produced by the engine's profile mapper from
/// a <c>StrategyPackDocument</c>; the mapper (0.0.14) and the future
/// profile compiler (0.0.15+) operate on this form rather than on the
/// raw DTOs.
/// </summary>
public sealed record StrategyPackDefinition
{
    public StrategyPackDefinition(StrategyPackId id, IReadOnlyList<StrategyDefinition> strategies)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        Strategies = GuardStrategies(strategies);
    }

    /// <summary>
    /// Stable strategy pack identifier. Never null.
    /// </summary>
    public StrategyPackId Id { get; }

    /// <summary>
    /// Ordered, immutable list of resolved strategies. Never null;
    /// may be empty. Duplicate strategy names are not enforced here:
    /// the upstream <c>ProfileDocumentValidator</c> already rejects
    /// duplicate <c>(pack, strategy)</c> references.
    /// </summary>
    public IReadOnlyList<StrategyDefinition> Strategies { get; }

    private static IReadOnlyList<StrategyDefinition> GuardStrategies(IReadOnlyList<StrategyDefinition> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        return strategies;
    }
}

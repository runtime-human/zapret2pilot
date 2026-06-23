using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Core.Profiles;

/// <summary>
/// Validated, fully-resolved definition of a single Zapret2 strategy:
/// a stable name and the ordered list of raw command-line parameter tokens
/// passed to <c>winws2</c>.
///
/// This is the domain form produced by the engine's profile mapper from
/// a <c>StrategyDocument</c> carried by a strategy pack; it is also the
/// form accepted by the future profile compiler (0.0.15+).
/// </summary>
public sealed record StrategyDefinition
{
    public StrategyDefinition(string name, IReadOnlyList<string> parameters)
    {
        Name = GuardName(name);
        Parameters = GuardParameters(parameters);
    }

    /// <summary>
    /// Stable strategy name as it appears inside the strategy pack.
    /// Never null, empty or whitespace.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Ordered, immutable list of raw command-line parameter tokens.
    /// Never null; may be empty.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }

    private static string GuardName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Strategy name must not be empty or whitespace.", nameof(name));
        }

        return name;
    }

    private static IReadOnlyList<string> GuardParameters(IReadOnlyList<string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Element nullness is not enforced at construction time: the
        // strategy pack document is the DTO layer and the validator
        // already rejects null/blank pack-level payloads. A future
        // compiler step is the right place to reject individual null
        // tokens; doing it here would duplicate that work.
        return parameters;
    }
}

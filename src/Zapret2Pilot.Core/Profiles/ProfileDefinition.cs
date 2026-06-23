using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;

namespace Zapret2Pilot.Core.Profiles;

/// <summary>
/// Resolved assignment of a single <see cref="StrategyDefinition"/>
/// (coming from a referenced strategy pack) to a profile. Carries the
/// pack identifier as well so the profile compiler can later group or
/// reason about strategies that came from the same pack.
/// </summary>
public sealed record StrategyAssignment
{
    public StrategyAssignment(StrategyPackId packId, StrategyDefinition strategy)
    {
        ArgumentNullException.ThrowIfNull(packId);
        ArgumentNullException.ThrowIfNull(strategy);
        PackId = packId;
        Strategy = strategy;
    }

    /// <summary>
    /// Stable strategy pack identifier the strategy was resolved from.
    /// Never null.
    /// </summary>
    public StrategyPackId PackId { get; }

    /// <summary>
    /// Resolved strategy definition. Never null.
    /// </summary>
    public StrategyDefinition Strategy { get; }
}

/// <summary>
/// Resolved assignment of a single hostlist (identified by its
/// <see cref="HostlistId"/>) to a profile, with a path relative to the
/// configured profiles/hostlists root.
///
/// The path is intentionally not validated for path-traversal safety
/// here: the upstream <c>ProfileDocumentValidator</c> is the single
/// authority for that check (see <c>HostlistPathUnsafe</c>). The mapper
/// only guarantees the reference is structurally complete.
/// </summary>
public sealed record HostlistAssignment
{
    public HostlistAssignment(HostlistId hostlistId, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(hostlistId);
        HostlistId = hostlistId;
        RelativePath = GuardRelativePath(relativePath);
    }

    /// <summary>
    /// Stable hostlist identifier. Never null.
    /// </summary>
    public HostlistId HostlistId { get; }

    /// <summary>
    /// Path to the hostlist file, relative to the configured profiles
    /// root. Never null, empty or whitespace. Path-traversal safety is
    /// the validator's responsibility, not this record's.
    /// </summary>
    public string RelativePath { get; }

    private static string GuardRelativePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Hostlist relative path must not be empty or whitespace.", nameof(relativePath));
        }

        return relativePath;
    }
}

/// <summary>
/// Validated, fully-resolved definition of a single Zapret2 profile:
/// a stable identifier, a human-readable display name, an optional
/// description, and the resolved strategy and hostlist assignments that
/// make up the profile.
///
/// This is the domain model produced by
/// <c>Engine.Zapret2.Profiles.ProfileDocumentMapper</c> from a validated
/// <c>ProfileDocument</c> plus the supplied strategy packs. It is the
/// input to the future profile compiler (0.0.15+).
///
/// <para>
/// Following <c>DEC-0010</c> and Critical Review #18:
/// <c>ProfileDocument</c> is the storage/import/export DTO and
/// <see cref="ProfileDefinition"/> is the validated domain model. The
/// compiler accepts <see cref="ProfileDefinition"/> only.
/// </para>
/// </summary>
public sealed record ProfileDefinition
{
    public ProfileDefinition(
        ProfileId id,
        string displayName,
        string? description,
        IReadOnlyList<StrategyAssignment> strategies,
        IReadOnlyList<HostlistAssignment> hostlists)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        DisplayName = GuardDisplayName(displayName);
        Description = description; // explicitly nullable
        Strategies = GuardStrategies(strategies);
        Hostlists = GuardHostlists(hostlists);
    }

    /// <summary>
    /// Stable profile identifier. Never null.
    /// </summary>
    public ProfileId Id { get; }

    /// <summary>
    /// Human-readable display name. Never null, empty or whitespace.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Optional human-readable description. May be null.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Ordered, immutable list of resolved strategy assignments.
    /// Never null; may be empty.
    /// </summary>
    public IReadOnlyList<StrategyAssignment> Strategies { get; }

    /// <summary>
    /// Ordered, immutable list of resolved hostlist assignments.
    /// Never null; may be empty.
    /// </summary>
    public IReadOnlyList<HostlistAssignment> Hostlists { get; }

    private static string GuardDisplayName(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Profile display name must not be empty or whitespace.", nameof(displayName));
        }

        return displayName;
    }

    private static IReadOnlyList<StrategyAssignment> GuardStrategies(IReadOnlyList<StrategyAssignment> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        return strategies;
    }

    private static IReadOnlyList<HostlistAssignment> GuardHostlists(IReadOnlyList<HostlistAssignment> hostlists)
    {
        ArgumentNullException.ThrowIfNull(hostlists);
        return hostlists;
    }
}

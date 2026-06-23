using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;

namespace Zapret2Pilot.Engine.Zapret2.Profiles;

/// <summary>
/// Reference to a single strategy inside a referenced strategy pack.
/// </summary>
public sealed record StrategyReference(StrategyPackId PackId, string StrategyName);

/// <summary>
/// Reference to a hostlist file, identified by its <see cref="HostlistId"/>
/// and a path relative to the configured profiles root.
/// </summary>
public sealed record HostlistReference(HostlistId HostlistId, string RelativePath);

/// <summary>
/// Top-level profile document describing one Zapret2 profile: a stable
/// identifier, a human-readable display name, an optional description, and
/// the lists of referenced strategy packs and hostlists that make up the
/// profile.
/// </summary>
public sealed record ProfileDocument(
    ProfileId Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<StrategyReference> StrategyReferences,
    IReadOnlyList<HostlistReference> HostlistReferences);

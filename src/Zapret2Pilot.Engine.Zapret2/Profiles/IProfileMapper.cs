using System.Collections.Generic;
using Zapret2Pilot.Core.Profiles;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Engine.Zapret2.Profiles;

/// <summary>
/// Resolves a validated <see cref="ProfileDocument"/> together with a set
/// of supplied <see cref="StrategyPackDocument"/>s into a fully-resolved
/// <see cref="ProfileDefinition"/> domain model.
///
/// The mapper does not load files, does not perform path-traversal
/// checks (those are the <see cref="ProfileDocumentValidator"/>'s
/// responsibility) and does not produce a runtime plan. It is the
/// strict, side-effect-free step between the DTO layer and the domain
/// layer required by <c>DEC-0010</c> and Critical Review #18.
/// </summary>
public interface IProfileMapper
{
    /// <summary>
    /// Maps <paramref name="profile"/> plus the supplied
    /// <paramref name="strategyPacks"/> into a
    /// <see cref="ProfileDefinition"/>.
    /// </summary>
    /// <param name="profile">Validated profile document. May be null.</param>
    /// <param name="strategyPacks">All available strategy pack documents.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> carrying the resolved
    /// <see cref="ProfileDefinition"/>, or a failure with a typed
    /// <see cref="ErrorInfo"/> describing the first detected problem
    /// (null profile, missing pack, missing strategy, or structurally
    /// invalid hostlist reference).
    /// </returns>
    Result<ProfileDefinition> Map(
        ProfileDocument profile,
        IReadOnlyList<StrategyPackDocument> strategyPacks);
}

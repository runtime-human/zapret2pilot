using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;

namespace Zapret2Pilot.Engine.Zapret2.Profiles;

/// <summary>
/// A single strategy definition inside a strategy pack: a stable name and
/// the ordered list of raw command-line parameter tokens passed to winws2.
/// </summary>
public sealed record StrategyDocument(
    string Name,
    IReadOnlyList<string> Parameters);

/// <summary>
/// Strategy pack document: a stable pack identifier and the strategies it
/// contributes to a profile.
/// </summary>
public sealed record StrategyPackDocument(
    StrategyPackId Id,
    IReadOnlyList<StrategyDocument> Strategies);

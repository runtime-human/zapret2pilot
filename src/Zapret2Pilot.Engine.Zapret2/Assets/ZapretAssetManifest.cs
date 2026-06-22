using System.Collections.Generic;

namespace Zapret2Pilot.Engine.Zapret2.Assets;

/// <summary>
/// Manifest of Zapret2 runtime assets that must be present and intact on disk
/// before a Zapret2 profile can be considered verifiable by the engine.
/// </summary>
public sealed record ZapretAssetManifest(
    ZapretRuntimeAsset RuntimeExecutable,
    IReadOnlyList<ZapretRuntimeAsset> Hostlists,
    IReadOnlyList<ZapretRuntimeAsset> StrategyPacks);

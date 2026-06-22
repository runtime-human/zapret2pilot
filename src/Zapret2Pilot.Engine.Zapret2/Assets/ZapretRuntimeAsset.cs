namespace Zapret2Pilot.Engine.Zapret2.Assets;

/// <summary>
/// Single runtime asset entry: a relative path inside the configured assets
/// root, the expected SHA-256 hash (lowercase or uppercase hex) and the
/// asset kind.
/// </summary>
public sealed record ZapretRuntimeAsset(
    string RelativePath,
    string ExpectedHash,
    AssetKind Kind);

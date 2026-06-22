using System.Collections.Generic;

namespace Zapret2Pilot.Engine.Zapret2.Assets;

/// <summary>
/// Summary of a successful <see cref="ZapretAssetVerifier.VerifyAsync"/> run.
/// Lists the relative paths of the assets that were found on disk and matched
/// the expected SHA-256 hash.
/// </summary>
public sealed record ZapretAssetVerificationSummary(
    IReadOnlyList<string> VerifiedRelativePaths);

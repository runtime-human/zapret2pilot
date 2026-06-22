using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.FileSystem;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Engine.Zapret2.Assets;

/// <summary>
/// Verifies the integrity of Zapret2 runtime assets declared in a
/// <see cref="ZapretAssetManifest"/>: presence, path safety, and SHA-256 hash
/// match. Uses an injected <see cref="ISafePathResolver"/> so the engine
/// adapter stays free of Infrastructure and can be tested with a fake
/// resolver.
/// </summary>
public sealed class ZapretAssetVerifier
{
    private readonly ISafePathResolver pathResolver;

    public ZapretAssetVerifier(ISafePathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);

        this.pathResolver = pathResolver;
    }

    public async Task<Result<ZapretAssetVerificationSummary>> VerifyAsync(
        ZapretAssetManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        List<ZapretRuntimeAsset> assets = CollectAssets(manifest);

        List<string> verifiedRelativePaths = new(assets.Count);

        foreach (ZapretRuntimeAsset asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string resolvedPath;
            try
            {
                resolvedPath = pathResolver.ResolveFilePath(asset.RelativePath);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Result.Failure<ZapretAssetVerificationSummary>(
                    new ErrorInfo(
                        "AssetPathUnsafe",
                        $"Asset path is unsafe: {asset.RelativePath}.",
                        ErrorSeverity.Error,
                        ErrorCategory.Runtime));
            }

            if (!File.Exists(resolvedPath))
            {
                return Result.Failure<ZapretAssetVerificationSummary>(
                    new ErrorInfo(
                        "AssetMissing",
                        $"Asset is missing: {asset.RelativePath}.",
                        ErrorSeverity.Error,
                        ErrorCategory.Runtime));
            }

            string actualHash = await ComputeSha256HexAsync(
                resolvedPath,
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(actualHash, asset.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<ZapretAssetVerificationSummary>(
                    new ErrorInfo(
                        "AssetHashMismatch",
                        $"Asset hash mismatch: {asset.RelativePath}.",
                        ErrorSeverity.Error,
                        ErrorCategory.Runtime));
            }

            verifiedRelativePaths.Add(asset.RelativePath);
        }

        return Result.Success(new ZapretAssetVerificationSummary(verifiedRelativePaths));
    }

    private static List<ZapretRuntimeAsset> CollectAssets(ZapretAssetManifest manifest)
    {
        List<ZapretRuntimeAsset> assets = new();

        assets.Add(manifest.RuntimeExecutable);

        if (manifest.Hostlists is not null)
        {
            assets.AddRange(manifest.Hostlists);
        }

        if (manifest.StrategyPacks is not null)
        {
            assets.AddRange(manifest.StrategyPacks);
        }

        return assets;
    }

    private static async Task<string> ComputeSha256HexAsync(
        string resolvedPath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            resolvedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        byte[] hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Core.FileSystem;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Engine.Zapret2.Assets;

namespace Zapret2Pilot.Engine.Zapret2.Tests.Assets;

public sealed class ZapretAssetVerifierTests
{
    [Fact]
    public static async Task ValidManifestSucceeds()
    {
        using TempRoot tempRoot = new();

        string executableRelativePath = "bin/winws2.exe";
        string executableFullPath = tempRoot.WriteFile(
            executableRelativePath,
            "fake-winws2-binary");

        string hostlistRelativePath = "hostlists/default.txt";
        string hostlistFullPath = tempRoot.WriteFile(
            hostlistRelativePath,
            "example.com\n");

        string strategyPackRelativePath = "packs/base.txt";
        string strategyPackFullPath = tempRoot.WriteFile(
            strategyPackRelativePath,
            "strategy-1\n");

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                executableRelativePath,
                ComputeSha256Hex(executableFullPath),
                AssetKind.Executable),
            Hostlists: new[]
            {
                new ZapretRuntimeAsset(
                    hostlistRelativePath,
                    ComputeSha256Hex(hostlistFullPath),
                    AssetKind.Hostlist),
            },
            StrategyPacks: new[]
            {
                new ZapretRuntimeAsset(
                    strategyPackRelativePath,
                    ComputeSha256Hex(strategyPackFullPath),
                    AssetKind.StrategyPack),
            });

        FixedRootPathResolver pathResolver = new(tempRoot.DirectoryPath);
        ZapretAssetVerifier verifier = new(pathResolver);

        Result<ZapretAssetVerificationSummary> result = await verifier.VerifyAsync(
            manifest,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(executableRelativePath, result.Value.VerifiedRelativePaths);
        Assert.Contains(hostlistRelativePath, result.Value.VerifiedRelativePaths);
        Assert.Contains(strategyPackRelativePath, result.Value.VerifiedRelativePaths);
    }

    [Fact]
    public static async Task MissingExecutableReturnsFailure()
    {
        using TempRoot tempRoot = new();

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                "bin/missing.exe",
                "0000000000000000000000000000000000000000000000000000000000000000",
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        FixedRootPathResolver pathResolver = new(tempRoot.DirectoryPath);
        ZapretAssetVerifier verifier = new(pathResolver);

        Result<ZapretAssetVerificationSummary> result = await verifier.VerifyAsync(
            manifest,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("AssetMissing", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Fact]
    public static async Task HashMismatchReturnsFailure()
    {
        using TempRoot tempRoot = new();

        string executableRelativePath = "bin/winws2.exe";
        string executableFullPath = tempRoot.WriteFile(
            executableRelativePath,
            "real-content");

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                executableRelativePath,
                "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        FixedRootPathResolver pathResolver = new(tempRoot.DirectoryPath);
        ZapretAssetVerifier verifier = new(pathResolver);

        Result<ZapretAssetVerificationSummary> result = await verifier.VerifyAsync(
            manifest,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("AssetHashMismatch", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static async Task PathEscapeAttemptReturnsFailure()
    {
        using TempRoot tempRoot = new();

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                "../escape.exe",
                "0000000000000000000000000000000000000000000000000000000000000000",
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        ThrowingPathResolver pathResolver = new(
            relativePath => relativePath.Contains("..", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Parent traversal segments are not allowed.")
                : throw new InvalidOperationException("Only relative paths are allowed."));
        ZapretAssetVerifier verifier = new(pathResolver);

        Result<ZapretAssetVerificationSummary> result = await verifier.VerifyAsync(
            manifest,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("AssetPathUnsafe", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    private static string ComputeSha256Hex(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        byte[] hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private sealed class TempRoot : IDisposable
    {
        private bool disposed;

        public TempRoot()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "z2p-engine-zapret2-tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string WriteFile(string relativePath, string content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            string fullPath = Path.Combine(DirectoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(fullPath, content, Encoding.UTF8);

            return fullPath;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    private sealed class FixedRootPathResolver : ISafePathResolver
    {
        private readonly string rootDirectory;

        public FixedRootPathResolver(string rootDirectory)
        {
            this.rootDirectory = rootDirectory;
        }

        public string ResolveFilePath(string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            return Path.GetFullPath(
                relativePath.Replace('/', Path.DirectorySeparatorChar),
                rootDirectory);
        }
    }

    private sealed class ThrowingPathResolver : ISafePathResolver
    {
        private readonly Func<string, string> _resolve;

        public ThrowingPathResolver(Func<string, string> resolve)
        {
            ArgumentNullException.ThrowIfNull(resolve);

            _resolve = resolve;
        }

        public string ResolveFilePath(string relativePath)
        {
            return _resolve(relativePath);
        }
    }
}

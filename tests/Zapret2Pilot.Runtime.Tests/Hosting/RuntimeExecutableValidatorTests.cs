using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Tests.Hosting;

#pragma warning disable CA1707 // Identifiers should not contain underscores

public sealed class RuntimeExecutableValidatorTests
{
    [Fact]
    public static void VerifyAsync_ReturnsVerifiedManifestExecutablePath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string executablePath = WriteExecutable(temporaryDirectory.DirectoryPath, "bin/runtime-engine.exe", "fake runtime");
        ZapretAssetManifest manifest = CreateManifest("bin/runtime-engine.exe", ComputeSha256HexLower(executablePath));
        RuntimeExecutableValidator validator = new(temporaryDirectory.DirectoryPath);

        Result<VerifiedRuntimeExecutable> result = validator.VerifyAsync(manifest, CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        Assert.Equal(executablePath, result.Value.FullPath);
        Assert.Equal("bin/runtime-engine.exe", result.Value.ManifestRelativePath);
    }

    [Fact]
    public static void VerifyAsync_RejectsPathTraversalExecutable()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeExecutableValidator validator = new(temporaryDirectory.DirectoryPath);
        ZapretAssetManifest manifest = CreateManifest("../outside.exe", new string('0', 64));

        Result<VerifiedRuntimeExecutable> result = validator.VerifyAsync(manifest, CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeExecutableManifestVerificationFailed", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static void VerifyAsync_RejectsMissingExecutable()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeExecutableValidator validator = new(temporaryDirectory.DirectoryPath);
        ZapretAssetManifest manifest = CreateManifest("bin/missing.exe", new string('0', 64));

        Result<VerifiedRuntimeExecutable> result = validator.VerifyAsync(manifest, CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeExecutableManifestVerificationFailed", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static void VerifyAsync_RejectsHashMismatch()
    {
        using TemporaryDirectory temporaryDirectory = new();
        WriteExecutable(temporaryDirectory.DirectoryPath, "bin/runtime-engine.exe", "fake runtime");
        RuntimeExecutableValidator validator = new(temporaryDirectory.DirectoryPath);
        ZapretAssetManifest manifest = CreateManifest("bin/runtime-engine.exe", new string('0', 64));

        Result<VerifiedRuntimeExecutable> result = validator.VerifyAsync(manifest, CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeExecutableManifestVerificationFailed", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    private static ZapretAssetManifest CreateManifest(string executableRelativePath, string expectedHash)
    {
        return new ZapretAssetManifest(
            RuntimeExecutable: new ZapretRuntimeAsset(
                executableRelativePath,
                expectedHash,
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
    }

    private static string WriteExecutable(string rootDirectory, string relativePath, string content)
    {
        string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static string ComputeSha256HexLower(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        byte[] hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

#pragma warning restore CA1707 // Identifiers should not contain underscores

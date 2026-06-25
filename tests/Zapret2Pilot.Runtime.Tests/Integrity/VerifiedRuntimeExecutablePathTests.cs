using System;
using System.IO;
using System.Text;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Integrity;

namespace Zapret2Pilot.Runtime.Tests.Integrity;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// xUnit tests for <see cref="VerifiedRuntimeExecutablePath.TryCreate"/>.
/// Covers the happy path and every documented failure code.
/// </summary>
public sealed class VerifiedRuntimeExecutablePathTests
{
    private const string RuntimeExecutableRelativePath = "bin/winws2.exe";
    private const string ExecutableContent = "fake-winws2-binary";

    [Fact]
    public static void TryCreate_HappyPath_ReturnsSuccessAndCorrectAbsolutePath()
    {
        using TemporaryDirectory assetsRoot = new();
        string absolutePath = WriteFakeExecutable(assetsRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);

        ZapretAssetManifest manifest = CreateManifest(absolutePath);
        ZapretAssetVerificationSummary summary = CreateSummary(manifest.RuntimeExecutable.RelativePath);

        Result<VerifiedRuntimeExecutablePath> result = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        Assert.Equal(absolutePath, result.Value.AbsolutePath);
    }

    [Fact]
    public static void TryCreate_MatchIsCaseInsensitive()
    {
        using TemporaryDirectory assetsRoot = new();
        string absolutePath = WriteFakeExecutable(assetsRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);

        ZapretAssetManifest manifest = CreateManifest(absolutePath);
        ZapretAssetVerificationSummary summary = new(new[] { RuntimeExecutableRelativePath.ToUpperInvariant() });

        Result<VerifiedRuntimeExecutablePath> result = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        Assert.Equal(absolutePath, result.Value.AbsolutePath);
    }

    [Fact]
    public static void TryCreate_MissingExecutableRelativePath_ReturnsFailure()
    {
        using TemporaryDirectory assetsRoot = new();
        string absolutePath = WriteFakeExecutable(assetsRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                " ",
                "deadbeef",
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
        ZapretAssetVerificationSummary summary = CreateSummary(RuntimeExecutableRelativePath);

        Result<VerifiedRuntimeExecutablePath> result = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeExecutableRelativePathMissing", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Fact]
    public static void TryCreate_NullRuntimeExecutableAsset_ReturnsFailure()
    {
        using TemporaryDirectory assetsRoot = new();

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: null!,
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
        ZapretAssetVerificationSummary summary = CreateSummary(RuntimeExecutableRelativePath);

        Result<VerifiedRuntimeExecutablePath> result = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeExecutableRelativePathMissing", result.Error.Code);
    }

    [Fact]
    public static void TryCreate_PathNotInVerificationSummary_ReturnsFailure()
    {
        using TemporaryDirectory assetsRoot = new();
        string absolutePath = WriteFakeExecutable(assetsRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);

        ZapretAssetManifest manifest = CreateManifest(absolutePath);
        ZapretAssetVerificationSummary summary = CreateSummary("hostlists/other.txt");

        Result<VerifiedRuntimeExecutablePath> result = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeExecutableNotVerified", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Fact]
    public static void TryCreate_MissingFileOnDisk_ReturnsFailure()
    {
        using TemporaryDirectory assetsRoot = new();

        // Manifest claims the executable is at bin/winws2.exe but no
        // file is written. The summary is fabricated so we can isolate
        // the missing-file check.
        string nonExistentAbsolutePath = Path.Combine(
            assetsRoot.DirectoryPath,
            RuntimeExecutableRelativePath.Replace('/', Path.DirectorySeparatorChar));
        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                RuntimeExecutableRelativePath,
                "deadbeef",
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
        ZapretAssetVerificationSummary summary = CreateSummary(RuntimeExecutableRelativePath);

        Assert.False(File.Exists(nonExistentAbsolutePath));

        Result<VerifiedRuntimeExecutablePath> result = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeExecutableMissing", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public static void TryCreate_NullOrWhitespaceAssetsRoot_Throws(string? assetsRootDirectory)
    {
        using TemporaryDirectory realRoot = new();
        string absolutePath = WriteFakeExecutable(realRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);

        ZapretAssetManifest manifest = CreateManifest(absolutePath);
        ZapretAssetVerificationSummary summary = CreateSummary(manifest.RuntimeExecutable.RelativePath);

        if (assetsRootDirectory is null)
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                VerifiedRuntimeExecutablePath.TryCreate(
                    manifest,
                    summary,
                    assetsRootDirectory!));

            Assert.Equal("assetsRootDirectory", exception.ParamName);
        }
        else
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                VerifiedRuntimeExecutablePath.TryCreate(
                    manifest,
                    summary,
                    assetsRootDirectory));

            Assert.Equal("assetsRootDirectory", exception.ParamName);
        }
    }

    [Fact]
    public static void TryCreate_NullManifest_Throws()
    {
        using TemporaryDirectory assetsRoot = new();
        ZapretAssetVerificationSummary summary = CreateSummary(RuntimeExecutableRelativePath);

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            VerifiedRuntimeExecutablePath.TryCreate(
                manifest: null!,
                summary,
                assetsRoot.DirectoryPath));

        Assert.Equal("manifest", exception.ParamName);
    }

    [Fact]
    public static void TryCreate_NullSummary_Throws()
    {
        using TemporaryDirectory assetsRoot = new();
        string absolutePath = WriteFakeExecutable(assetsRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);
        ZapretAssetManifest manifest = CreateManifest(absolutePath);

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            VerifiedRuntimeExecutablePath.TryCreate(
                manifest,
                summary: null!,
                assetsRoot.DirectoryPath));

        Assert.Equal("summary", exception.ParamName);
    }

    private static ZapretAssetManifest CreateManifest(string executableFullPath)
    {
        string hash = ComputeSha256Hex(executableFullPath);
        return new ZapretAssetManifest(
            RuntimeExecutable: new ZapretRuntimeAsset(
                RuntimeExecutableRelativePath,
                hash,
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
    }

    private static ZapretAssetVerificationSummary CreateSummary(string relativePath)
    {
        return new ZapretAssetVerificationSummary(new[] { relativePath });
    }

    private static string WriteFakeExecutable(string root, string relativePath, string content)
    {
        string fullPath = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllBytes(fullPath, Encoding.UTF8.GetBytes(content));
        return fullPath;
    }

    private static string ComputeSha256Hex(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
#pragma warning restore CA1707 // Identifiers should not contain underscores

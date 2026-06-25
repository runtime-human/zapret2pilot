using System;
using System.IO;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;

namespace Zapret2Pilot.Runtime.Tests.Hosting;

/// <summary>
/// Focused xUnit tests for <see cref="RuntimeProcessStartContext"/>. The
/// type is a pure DTO: its constructor is the only behaviour under test.
/// All tests are hermetic and do not touch the filesystem or launch a
/// process.
/// </summary>
public sealed class RuntimeProcessStartContextTests
{
    private const string WorkspaceDirectory = @"C:\z2p\workspace";
    private const string RuntimeExecutableRelativePath = "bin/winws2.exe";
    private const string ExecutableContent = "fake-winws2-binary";

    [Fact]
    public static void ValidConstructionExposesAllInputs()
    {
        using TemporaryDirectory assetsRoot = new();
        string absoluteExecutablePath = WriteFakeExecutable(assetsRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);

        CompiledZapretPlan plan = CreatePlan();
        ZapretAssetManifest manifest = CreateManifestFromFile(absoluteExecutablePath);
        VerifiedRuntimeExecutablePath verifiedPath = BuildVerifiedPath(manifest, assetsRoot.DirectoryPath);

        RuntimeProcessStartContext context = new(
            plan: plan,
            manifest: manifest,
            workspaceDirectory: WorkspaceDirectory,
            runtimeExecutablePath: verifiedPath);

        Assert.Same(plan, context.Plan);
        Assert.Same(manifest, context.Manifest);
        Assert.Equal(WorkspaceDirectory, context.WorkspaceDirectory);
        Assert.Same(verifiedPath, context.RuntimeExecutablePath);
        Assert.Equal(absoluteExecutablePath, context.RuntimeExecutablePath.AbsolutePath);
    }

    [Fact]
    public static void NullPlanThrowsArgumentNullException()
    {
        ZapretAssetManifest manifest = CreateFakeManifest();
        VerifiedRuntimeExecutablePath verifiedPath = CreateFakeVerifiedPath();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new RuntimeProcessStartContext(
                plan: null!,
                manifest: manifest,
                workspaceDirectory: WorkspaceDirectory,
                runtimeExecutablePath: verifiedPath));

        Assert.Equal("plan", exception.ParamName);
    }

    [Fact]
    public static void NullManifestThrowsArgumentNullException()
    {
        CompiledZapretPlan plan = CreatePlan();
        VerifiedRuntimeExecutablePath verifiedPath = CreateFakeVerifiedPath();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new RuntimeProcessStartContext(
                plan: plan,
                manifest: null!,
                workspaceDirectory: WorkspaceDirectory,
                runtimeExecutablePath: verifiedPath));

        Assert.Equal("manifest", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public static void NullOrWhitespaceWorkspaceDirectoryThrows(string? workspaceDirectory)
    {
        CompiledZapretPlan plan = CreatePlan();
        ZapretAssetManifest manifest = CreateFakeManifest();
        VerifiedRuntimeExecutablePath verifiedPath = CreateFakeVerifiedPath();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null and ArgumentException for empty/whitespace. Both are
        // acceptable signals for an invalid workspace directory.
        if (workspaceDirectory is null)
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                new RuntimeProcessStartContext(
                    plan: plan,
                    manifest: manifest,
                    workspaceDirectory: workspaceDirectory!,
                    runtimeExecutablePath: verifiedPath));

            Assert.Equal("workspaceDirectory", exception.ParamName);
        }
        else
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new RuntimeProcessStartContext(
                    plan: plan,
                    manifest: manifest,
                    workspaceDirectory: workspaceDirectory,
                    runtimeExecutablePath: verifiedPath));

            Assert.Equal("workspaceDirectory", exception.ParamName);
        }
    }

    [Fact]
    public static void NullRuntimeExecutablePathThrowsArgumentNullException()
    {
        CompiledZapretPlan plan = CreatePlan();
        ZapretAssetManifest manifest = CreateFakeManifest();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new RuntimeProcessStartContext(
                plan: plan,
                manifest: manifest,
                workspaceDirectory: WorkspaceDirectory,
                runtimeExecutablePath: null!));

        Assert.Equal("runtimeExecutablePath", exception.ParamName);
    }

    private static CompiledZapretPlan CreatePlan()
    {
        return new CompiledZapretPlan(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());
    }

    /// <summary>
    /// Builds a manifest for tests that don't actually exercise the
    /// verifier (the constructor never reads the file). The expected
    /// hash is a placeholder.
    /// </summary>
    private static ZapretAssetManifest CreateFakeManifest()
    {
        return new ZapretAssetManifest(
            RuntimeExecutable: new ZapretRuntimeAsset(
                RuntimeExecutableRelativePath,
                new string('0', 64),
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
    }

    private static ZapretAssetManifest CreateManifestFromFile(string executableFullPath)
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

    private static VerifiedRuntimeExecutablePath CreateFakeVerifiedPath()
    {
        using TemporaryDirectory assetsRoot = new();
        string absolutePath = WriteFakeExecutable(assetsRoot.DirectoryPath, RuntimeExecutableRelativePath, ExecutableContent);
        ZapretAssetManifest manifest = CreateManifestFromFile(absolutePath);
        return BuildVerifiedPath(manifest, assetsRoot.DirectoryPath);
    }

    private static VerifiedRuntimeExecutablePath BuildVerifiedPath(
        ZapretAssetManifest manifest,
        string assetsRootDirectory)
    {
        ZapretAssetVerificationSummary summary = new(new[] { manifest.RuntimeExecutable.RelativePath });
        Result<VerifiedRuntimeExecutablePath> result = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRootDirectory);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
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

        File.WriteAllBytes(fullPath, System.Text.Encoding.UTF8.GetBytes(content));
        return fullPath;
    }

    private static string ComputeSha256Hex(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

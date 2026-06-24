using System;
using System.Collections.Generic;
using Xunit;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Hosting;

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
    private const string RuntimeExecutablePath = @"C:\z2p\bin\winws2.exe";

    [Fact]
    public static void ValidConstructionExposesAllInputs()
    {
        CompiledZapretPlan plan = CreatePlan();
        ZapretAssetManifest manifest = CreateManifest();

        RuntimeProcessStartContext context = new(
            plan: plan,
            manifest: manifest,
            workspaceDirectory: WorkspaceDirectory,
            runtimeExecutablePath: RuntimeExecutablePath);

        Assert.Same(plan, context.Plan);
        Assert.Same(manifest, context.Manifest);
        Assert.Equal(WorkspaceDirectory, context.WorkspaceDirectory);
        Assert.Equal(RuntimeExecutablePath, context.RuntimeExecutablePath);
    }

    [Fact]
    public static void NullPlanThrowsArgumentNullException()
    {
        ZapretAssetManifest manifest = CreateManifest();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new RuntimeProcessStartContext(
                plan: null!,
                manifest: manifest,
                workspaceDirectory: WorkspaceDirectory,
                runtimeExecutablePath: RuntimeExecutablePath));

        Assert.Equal("plan", exception.ParamName);
    }

    [Fact]
    public static void NullManifestThrowsArgumentNullException()
    {
        CompiledZapretPlan plan = CreatePlan();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new RuntimeProcessStartContext(
                plan: plan,
                manifest: null!,
                workspaceDirectory: WorkspaceDirectory,
                runtimeExecutablePath: RuntimeExecutablePath));

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
        ZapretAssetManifest manifest = CreateManifest();

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
                    runtimeExecutablePath: RuntimeExecutablePath));

            Assert.Equal("workspaceDirectory", exception.ParamName);
        }
        else
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new RuntimeProcessStartContext(
                    plan: plan,
                    manifest: manifest,
                    workspaceDirectory: workspaceDirectory,
                    runtimeExecutablePath: RuntimeExecutablePath));

            Assert.Equal("workspaceDirectory", exception.ParamName);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public static void NullOrWhitespaceRuntimeExecutablePathThrows(string? runtimeExecutablePath)
    {
        CompiledZapretPlan plan = CreatePlan();
        ZapretAssetManifest manifest = CreateManifest();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null and ArgumentException for empty/whitespace. Both are
        // acceptable signals for an invalid runtime executable path.
        if (runtimeExecutablePath is null)
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                new RuntimeProcessStartContext(
                    plan: plan,
                    manifest: manifest,
                    workspaceDirectory: WorkspaceDirectory,
                    runtimeExecutablePath: runtimeExecutablePath!));

            Assert.Equal("runtimeExecutablePath", exception.ParamName);
        }
        else
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new RuntimeProcessStartContext(
                    plan: plan,
                    manifest: manifest,
                    workspaceDirectory: WorkspaceDirectory,
                    runtimeExecutablePath: runtimeExecutablePath));

            Assert.Equal("runtimeExecutablePath", exception.ParamName);
        }
    }

    private static CompiledZapretPlan CreatePlan()
    {
        return new CompiledZapretPlan(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());
    }

    private static ZapretAssetManifest CreateManifest()
    {
        return new ZapretAssetManifest(
            RuntimeExecutable: new ZapretRuntimeAsset(
                "bin/winws2.exe",
                new string('0', 64),
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
    }
}

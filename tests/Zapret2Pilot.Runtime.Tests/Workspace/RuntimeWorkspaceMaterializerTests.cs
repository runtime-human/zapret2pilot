using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Workspace;

namespace Zapret2Pilot.Runtime.Tests.Workspace;

public sealed class RuntimeWorkspaceMaterializerTests
{
    [Fact]
    public static async Task HappyPathWritesConfigArgsAndHostlists()
    {
        using TemporaryDirectory workspaceRoot = new();

        const string ExecutableRelativePath = "bin/winws2.exe";
        const string ExecutableContent = "fake-winws2-binary";
        const string HostlistRelativePath = "hostlists/default.txt";
        const string HostlistContent = "example.com\n";

        string executableFullPath = WriteAssetFile(workspaceRoot, ExecutableRelativePath, ExecutableContent);
        string hostlistFullPath = WriteAssetFile(workspaceRoot, HostlistRelativePath, HostlistContent);

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                ExecutableRelativePath,
                ComputeSha256Hex(executableFullPath),
                AssetKind.Executable),
            Hostlists: new[]
            {
                new ZapretRuntimeAsset(
                    HostlistRelativePath,
                    ComputeSha256Hex(hostlistFullPath),
                    AssetKind.Hostlist),
            },
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        CompiledZapretPlan plan = new(
            generatedConfigContent: "# generated config\n",
            argsContent: "--new\n--hostlists=hostlists/default.txt\n",
            hostlists: new[]
            {
                new CompiledZapretPlan.HostlistContent(
                    relativePath: "default.txt",
                    content: HostlistContent),
            });

        RuntimeWorkspaceMaterializer materializer = RuntimeWorkspaceMaterializer.CreateForRoot(workspaceRoot.DirectoryPath);

        Result<RuntimeWorkspaceMaterializeResult> result = await materializer.MaterializeAsync(
            plan,
            manifest,
            workspaceRoot.DirectoryPath,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        RuntimeWorkspaceMaterializeResult value = result.Value;

        Assert.Equal(Path.GetFullPath(workspaceRoot.DirectoryPath), value.WorkspaceDirectory);

        string expectedArgsPath = Path.Combine(workspaceRoot.DirectoryPath, "args.txt");
        string expectedConfigPath = Path.Combine(workspaceRoot.DirectoryPath, "generated.cfg");
        string expectedHostlistPath = Path.Combine(workspaceRoot.DirectoryPath, "hostlists", "default.txt");

        Assert.Equal(expectedArgsPath, value.ArgsFilePath);
        Assert.Equal(expectedConfigPath, value.GeneratedConfigPath);
        Assert.Single(value.WrittenHostlistPaths);
        Assert.Equal(expectedHostlistPath, value.WrittenHostlistPaths[0]);

        Assert.True(File.Exists(expectedArgsPath));
        Assert.True(File.Exists(expectedConfigPath));
        Assert.True(File.Exists(expectedHostlistPath));

        Assert.Equal(plan.ArgsContent, File.ReadAllText(expectedArgsPath));
        Assert.Equal(plan.GeneratedConfigContent, File.ReadAllText(expectedConfigPath));
        Assert.Equal(HostlistContent, File.ReadAllText(expectedHostlistPath));
    }

    [Fact]
    public static async Task PathTraversalInHostlistRelativePathIsRejected()
    {
        using TemporaryDirectory workspaceRoot = new();

        const string ExecutableRelativePath = "bin/winws2.exe";
        const string ExecutableContent = "fake-winws2-binary";
        const string HostlistRelativePath = "hostlists/default.txt";
        const string HostlistContent = "example.com\n";

        string executableFullPath = WriteAssetFile(workspaceRoot, ExecutableRelativePath, ExecutableContent);
        string hostlistFullPath = WriteAssetFile(workspaceRoot, HostlistRelativePath, HostlistContent);

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                ExecutableRelativePath,
                ComputeSha256Hex(executableFullPath),
                AssetKind.Executable),
            Hostlists: new[]
            {
                new ZapretRuntimeAsset(
                    HostlistRelativePath,
                    ComputeSha256Hex(hostlistFullPath),
                    AssetKind.Hostlist),
            },
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        CompiledZapretPlan plan = new(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: new[]
            {
                new CompiledZapretPlan.HostlistContent(
                    relativePath: "../escape.txt",
                    content: "escape"),
            });

        RuntimeWorkspaceMaterializer materializer = RuntimeWorkspaceMaterializer.CreateForRoot(workspaceRoot.DirectoryPath);

        Result<RuntimeWorkspaceMaterializeResult> result = await materializer.MaterializeAsync(
            plan,
            manifest,
            workspaceRoot.DirectoryPath,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("WorkspacePathUnsafe", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Fact]
    public static async Task AtomicReplaceReplacesExistingFiles()
    {
        using TemporaryDirectory workspaceRoot = new();

        const string ExecutableRelativePath = "bin/winws2.exe";
        const string ExecutableContent = "fake-winws2-binary";
        const string HostlistRelativePath = "hostlists/default.txt";
        const string HostlistContent = "example.com\n";

        string executableFullPath = WriteAssetFile(workspaceRoot, ExecutableRelativePath, ExecutableContent);
        string hostlistFullPath = WriteAssetFile(workspaceRoot, HostlistRelativePath, HostlistContent);

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                ExecutableRelativePath,
                ComputeSha256Hex(executableFullPath),
                AssetKind.Executable),
            Hostlists: new[]
            {
                new ZapretRuntimeAsset(
                    HostlistRelativePath,
                    ComputeSha256Hex(hostlistFullPath),
                    AssetKind.Hostlist),
            },
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        string argsFilePath = Path.Combine(workspaceRoot.DirectoryPath, "args.txt");
        string configFilePath = Path.Combine(workspaceRoot.DirectoryPath, "generated.cfg");
        string hostlistFilePath = Path.Combine(workspaceRoot.DirectoryPath, "hostlists", "default.txt");

        // Pre-create the generated files so we can prove they are replaced
        // atomically. The hostlist already exists from the manifest setup, so
        // its pre-existing content matches the manifest hash and survives the
        // asset verification step.
        File.WriteAllText(argsFilePath, "old-args", Encoding.UTF8);
        File.WriteAllText(configFilePath, "old-config", Encoding.UTF8);

        CompiledZapretPlan plan = new(
            generatedConfigContent: "new-config",
            argsContent: "new-args",
            hostlists: new[]
            {
                new CompiledZapretPlan.HostlistContent(
                    relativePath: "default.txt",
                    content: "new-hostlist"),
            });

        RuntimeWorkspaceMaterializer materializer = RuntimeWorkspaceMaterializer.CreateForRoot(workspaceRoot.DirectoryPath);

        Result<RuntimeWorkspaceMaterializeResult> result = await materializer.MaterializeAsync(
            plan,
            manifest,
            workspaceRoot.DirectoryPath,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);

        Assert.Equal("new-args", File.ReadAllText(argsFilePath));
        Assert.Equal("new-config", File.ReadAllText(configFilePath));
        Assert.Equal("new-hostlist", File.ReadAllText(hostlistFilePath));

        IEnumerable<string> leftoverTempFiles = Directory
            .EnumerateFiles(workspaceRoot.DirectoryPath, ".args.txt.*.tmp", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(workspaceRoot.DirectoryPath, ".generated.cfg.*.tmp", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(workspaceRoot.DirectoryPath, ".default.txt.*.tmp", SearchOption.AllDirectories));

        Assert.Empty(leftoverTempFiles);
    }

    [Fact]
    public static async Task MissingAssetReturnsFailure()
    {
        using TemporaryDirectory workspaceRoot = new();

        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                "bin/missing.exe",
                "0000000000000000000000000000000000000000000000000000000000000000",
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        CompiledZapretPlan plan = new(
            generatedConfigContent: "config",
            argsContent: "args",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

        RuntimeWorkspaceMaterializer materializer = RuntimeWorkspaceMaterializer.CreateForRoot(workspaceRoot.DirectoryPath);

        Result<RuntimeWorkspaceMaterializeResult> result = await materializer.MaterializeAsync(
            plan,
            manifest,
            workspaceRoot.DirectoryPath,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("WorkspaceAssetVerificationFailed", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);

        Assert.False(File.Exists(Path.Combine(workspaceRoot.DirectoryPath, "args.txt")));
        Assert.False(File.Exists(Path.Combine(workspaceRoot.DirectoryPath, "generated.cfg")));
    }

    private static string WriteAssetFile(TemporaryDirectory root, string relativePath, string content)
    {
        string fullPath = Path.Combine(
            root.DirectoryPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(fullPath, content, Encoding.UTF8);

        return fullPath;
    }

    private static string ComputeSha256Hex(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        byte[] hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

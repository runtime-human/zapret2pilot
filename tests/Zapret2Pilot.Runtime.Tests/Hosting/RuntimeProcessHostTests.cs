using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Locking;
using Zapret2Pilot.Runtime.Ownership;
using Zapret2Pilot.Runtime.Recovery;
using Zapret2Pilot.Runtime.Transactions;
using Zapret2Pilot.Runtime.Windows;
using Zapret2Pilot.Runtime.Workspace;

namespace Zapret2Pilot.Runtime.Tests.Hosting;

#pragma warning disable CA1707 // Identifiers should not contain underscores

public sealed class RuntimeProcessHostTests
{
    private const string FakeRuntimeExecutableName = "Zapret2Pilot.Testing.FakeRuntime.exe";
    private const string ManifestExecutableRelativePath = "bin/fake-runtime.exe";

    [Fact]
    public static void StartAsync_NullContext_Fails()
    {
        HostFixture fixture = HostFixture.Create();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            host_StartAsync(fixture.Host, context: null!));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public static void RuntimeProcessStartContext_DoesNotExposeIndependentExecutablePath()
    {
        PropertyInfo? property = typeof(RuntimeProcessStartContext).GetProperty("RuntimeExecutablePath");

        Assert.Null(property);
    }

    [Fact]
    public static void StartAsync_PlanWithNullCacheKey_Fails()
    {
        HostFixture fixture = HostFixture.Create();

        CompiledZapretPlan planWithoutCacheKey = new(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

        RuntimeProcessStartContext context = new(
            plan: planWithoutCacheKey,
            manifest: HostFixture.CreateMissingAssetManifest(),
            workspaceDirectory: fixture.WorkspaceDirectory);

        Result<RuntimeProcessHostResult> result = host_StartAsync(fixture.Host, context);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimePlanCacheKeyMissing", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static void StopAsync_NotRunning_Fails()
    {
        HostFixture fixture = HostFixture.Create();
        Result<Unit> result = host_StopAsync(fixture.Host);
        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeNotRunning", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static void StartAsync_MaterializationFailure_Fails()
    {
        HostFixture fixture = HostFixture.Create();
        RuntimeProcessStartContext context = new(
            plan: HostFixture.CreatePlanWithCacheKey(),
            manifest: HostFixture.CreateMissingAssetManifest(),
            workspaceDirectory: fixture.WorkspaceDirectory);

        Result<RuntimeProcessHostResult> result = host_StartAsync(fixture.Host, context);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeWorkspaceMaterializationFailed", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.False(fixture.TransactionManager.IsRunning);
    }

    [Fact]
    public static void StartAsync_RefusesExecutableOutsideWorkspaceRoot()
    {
        HostFixture fixture = HostFixture.Create();
        RuntimeProcessStartContext context = new(
            plan: HostFixture.CreatePlanWithCacheKey(),
            manifest: HostFixture.CreateUnsafeManifest(),
            workspaceDirectory: fixture.WorkspaceDirectory);

        Result<RuntimeProcessHostResult> result = host_StartAsync(fixture.Host, context);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeWorkspaceMaterializationFailed", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.False(fixture.TransactionManager.IsRunning);
    }

    [Fact]
    public static void StartAsync_RefusesMissingExecutableBeforeProcessStart()
    {
        HostFixture fixture = HostFixture.Create();
        RuntimeProcessStartContext context = new(
            plan: HostFixture.CreatePlanWithCacheKey(),
            manifest: HostFixture.CreateMissingAssetManifest(),
            workspaceDirectory: fixture.WorkspaceDirectory);

        Result<RuntimeProcessHostResult> result = host_StartAsync(fixture.Host, context);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeWorkspaceMaterializationFailed", result.Error.Code);
        Assert.False(fixture.TransactionManager.IsRunning);
        Assert.False(File.Exists(fixture.LockFileStore.LockFilePath));
    }

    [Fact]
    public static void StartAsync_AlreadyRunning_Fails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HostFixture fixture = HostFixture.Create();
        fixture.PrepareFakeRuntimeInWorkspace();

        RuntimeProcessStartContext context = fixture.CreateStartContextForFakeRuntime();

        Result<RuntimeProcessHostResult> firstStart = host_StartAsync(fixture.Host, context);
        Assert.True(firstStart.IsSuccess, firstStart.IsFailure ? firstStart.Error.ToString() : string.Empty);

        try
        {
            Result<RuntimeProcessHostResult> secondStart = host_StartAsync(fixture.Host, context);
            Assert.True(secondStart.IsFailure);
            Assert.Equal("RuntimeAlreadyRunning", secondStart.Error.Code);
            Assert.Equal(ErrorCategory.Runtime, secondStart.Error.Category);
        }
        finally
        {
            Result<Unit> stop = host_StopAsync(fixture.Host);
            Assert.True(stop.IsSuccess, stop.IsFailure ? stop.Error.ToString() : string.Empty);
        }
    }

    [Fact]
    public static void StartAsync_StopAsync_FakeRuntime_Success()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HostFixture fixture = HostFixture.Create();
        string expectedExecutablePath = fixture.PrepareFakeRuntimeInWorkspace();

        RuntimeProcessStartContext context = fixture.CreateStartContextForFakeRuntime();

        Result<RuntimeProcessHostResult> startResult = host_StartAsync(fixture.Host, context);
        Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.ToString() : string.Empty);

        RuntimeProcessHostResult hostResult = startResult.Value;
        Assert.True(hostResult.ProcessId > 0);
        Assert.Equal(FakeRuntimeExecutableName[..^".exe".Length], hostResult.ProcessName, ignoreCase: true);
        Assert.Equal(expectedExecutablePath, hostResult.ExecutablePath);
        Assert.Same(context.Plan, hostResult.Plan);

        Assert.True(File.Exists(fixture.LockFileStore.LockFilePath), "Lock file should exist after a successful start.");

        using (Process runningProcess = Process.GetProcessById(hostResult.ProcessId))
        {
            Assert.False(runningProcess.HasExited, "Fake runtime should still be running after start.");
        }

        Result<Unit> stopResult = host_StopAsync(fixture.Host);
        Assert.True(stopResult.IsSuccess, stopResult.IsFailure ? stopResult.Error.ToString() : string.Empty);

        Assert.False(File.Exists(fixture.LockFileStore.LockFilePath), "Lock file should be deleted after stop.");
        Assert.False(fixture.TransactionManager.IsRunning);
        Assert.True(IsProcessGone(hostResult.ProcessId), "Fake runtime should have exited after stop.");
    }

    [Fact]
    public static void StopAsync_ClearsHostStateWhenLockDeleteFailsAfterProcessStop()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HostFixture fixture = HostFixture.CreateWithLockDeleteFailure();
        fixture.PrepareFakeRuntimeInWorkspace();
        RuntimeProcessStartContext context = fixture.CreateStartContextForFakeRuntime();

        Result<RuntimeProcessHostResult> startResult = host_StartAsync(fixture.Host, context);
        Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.ToString() : string.Empty);

        int processId = startResult.Value.ProcessId;

        Result<Unit> stopResult = host_StopAsync(fixture.Host);

        Assert.True(stopResult.IsFailure);
        Assert.Equal("RuntimeLockDeleteFailed", stopResult.Error.Code);
        Assert.Equal(ErrorCategory.Storage, stopResult.Error.Category);
        Assert.False(fixture.TransactionManager.IsRunning);
        Assert.True(IsProcessGone(processId));

        Result<Unit> secondStop = host_StopAsync(fixture.Host);
        Assert.True(secondStop.IsFailure);
        Assert.Equal("RuntimeNotRunning", secondStop.Error.Code);
    }

    [Fact]
    public static void StopAsync_DoesNotRollbackTransactionManagerToRunningAfterIrreversibleKill()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HostFixture fixture = HostFixture.CreateWithLockDeleteFailure();
        fixture.PrepareFakeRuntimeInWorkspace();
        RuntimeProcessStartContext context = fixture.CreateStartContextForFakeRuntime();

        Result<RuntimeProcessHostResult> startResult = host_StartAsync(fixture.Host, context);
        Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.ToString() : string.Empty);

        Result<Unit> stopResult = host_StopAsync(fixture.Host);

        Assert.True(stopResult.IsFailure);
        Assert.Equal("RuntimeLockDeleteFailed", stopResult.Error.Code);
        Assert.False(fixture.TransactionManager.IsRunning);
        Assert.Null(fixture.TransactionManager.CurrentPlan);
    }

    private static Result<RuntimeProcessHostResult> host_StartAsync(
        RuntimeProcessHost host,
        RuntimeProcessStartContext context)
    {
        return host.StartAsync(context, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static Result<Unit> host_StopAsync(RuntimeProcessHost host)
    {
        return host.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static bool IsProcessGone(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private sealed class HostFixture : IDisposable
    {
        private readonly RuntimeProcessHost host;
        private bool disposed;

        private HostFixture(
            TemporaryDirectory tempDir,
            string workspaceDirectory,
            string runtimeDirectory,
            RuntimeOwnershipMutex ownershipMutex,
            IRuntimeLockFileStore lockFileStore,
            RuntimeTransactionManager transactionManager,
            RuntimeProcessHost host)
        {
            TempDir = tempDir;
            WorkspaceDirectory = workspaceDirectory;
            RuntimeDirectory = runtimeDirectory;
            OwnershipMutex = ownershipMutex;
            LockFileStore = lockFileStore;
            TransactionManager = transactionManager;
            this.host = host;
        }

        public TemporaryDirectory TempDir { get; }

        public string WorkspaceDirectory { get; }

        public string RuntimeDirectory { get; }

        public RuntimeOwnershipMutex OwnershipMutex { get; }

        public IRuntimeLockFileStore LockFileStore { get; }

        public RuntimeTransactionManager TransactionManager { get; }

        public RuntimeProcessHost Host => host;

        public static HostFixture Create()
        {
            return CreateCore(useFailingDeleteLockStore: false);
        }

        public static HostFixture CreateWithLockDeleteFailure()
        {
            return CreateCore(useFailingDeleteLockStore: true);
        }

        private static HostFixture CreateCore(bool useFailingDeleteLockStore)
        {
            TemporaryDirectory tempDir = new();

            string workspaceDirectory = Path.Combine(tempDir.DirectoryPath, "workspace");
            string runtimeDirectory = Path.Combine(tempDir.DirectoryPath, "runtime");

            string mutexName = RuntimeTestData.CreateUniqueMutexName();
            RuntimeOwnershipMutex ownershipMutex = new(mutexName);
            RuntimeLockFileStore realLockFileStore = new(runtimeDirectory);
            IRuntimeLockFileStore lockFileStore = useFailingDeleteLockStore
                ? new DeleteFailingRuntimeLockFileStore(realLockFileStore)
                : realLockFileStore;
            RuntimeStaleLockRecovery staleLockRecovery = new(lockFileStore);
            IRuntimeWorkspaceMaterializer materializer = RuntimeWorkspaceMaterializer.CreateForRoot(workspaceDirectory);
            RuntimeTransactionManager transactionManager = new();
            IRuntimeJobObjectProcessAssigner jobObjectAssigner = new RuntimeJobObjectProcessAssigner();

            RuntimeProcessHost host = new(
                ownershipMutex,
                staleLockRecovery,
                materializer,
                transactionManager,
                jobObjectAssigner,
                lockFileStore,
                stopTimeout: TimeSpan.FromSeconds(5));

            return new HostFixture(
                tempDir,
                workspaceDirectory,
                runtimeDirectory,
                ownershipMutex,
                lockFileStore,
                transactionManager,
                host);
        }

        public string PrepareFakeRuntimeInWorkspace()
        {
            string fakeRuntimeExePath = Path.Combine(AppContext.BaseDirectory, FakeRuntimeExecutableName);
            if (!File.Exists(fakeRuntimeExePath))
            {
                throw new FileNotFoundException(
                    $"Fake runtime not found at '{fakeRuntimeExePath}'. " +
                    "Ensure tests/Zapret2Pilot.Testing.FakeRuntime is referenced by the test project.",
                    fakeRuntimeExePath);
            }

            string workspaceExePath = Path.Combine(
                WorkspaceDirectory,
                ManifestExecutableRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string? workspaceExeDir = Path.GetDirectoryName(workspaceExePath);
            if (!string.IsNullOrEmpty(workspaceExeDir))
            {
                Directory.CreateDirectory(workspaceExeDir);
            }

            File.Copy(fakeRuntimeExePath, workspaceExePath, overwrite: true);
            return workspaceExePath;
        }

        public static CompiledZapretPlan CreatePlanWithCacheKey()
        {
            return new CompiledZapretPlan(
                generatedConfigContent: "# fake config\n",
                argsContent: "--new\n",
                hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>(),
                id: null,
                profileId: null,
                commandLine: null,
                arguments: null,
                cacheKey: new RuntimePlanCacheKey(new string('a', 64)));
        }

        public static ZapretAssetManifest CreateMissingAssetManifest()
        {
            return new ZapretAssetManifest(
                RuntimeExecutable: new ZapretRuntimeAsset(
                    "bin/missing.exe",
                    new string('0', 64),
                    AssetKind.Executable),
                Hostlists: Array.Empty<ZapretRuntimeAsset>(),
                StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
        }

        public static ZapretAssetManifest CreateUnsafeManifest()
        {
            return new ZapretAssetManifest(
                RuntimeExecutable: new ZapretRuntimeAsset(
                    "../outside.exe",
                    new string('0', 64),
                    AssetKind.Executable),
                Hostlists: Array.Empty<ZapretRuntimeAsset>(),
                StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
        }

        public ZapretAssetManifest CreateFakeRuntimeManifest()
        {
            string workspaceExePath = Path.Combine(
                WorkspaceDirectory,
                ManifestExecutableRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string hash = ComputeSha256HexLower(workspaceExePath);

            return new ZapretAssetManifest(
                RuntimeExecutable: new ZapretRuntimeAsset(
                    ManifestExecutableRelativePath,
                    hash,
                    AssetKind.Executable),
                Hostlists: Array.Empty<ZapretRuntimeAsset>(),
                StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
        }

        public RuntimeProcessStartContext CreateStartContextForFakeRuntime()
        {
            return new RuntimeProcessStartContext(
                plan: CreatePlanWithCacheKey(),
                manifest: CreateFakeRuntimeManifest(),
                workspaceDirectory: WorkspaceDirectory);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            host.Dispose();
            TempDir.Dispose();
        }

        private static string ComputeSha256HexLower(string fullPath)
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            byte[] hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }

    private sealed class DeleteFailingRuntimeLockFileStore : IRuntimeLockFileStore
    {
        private readonly RuntimeLockFileStore inner;

        public DeleteFailingRuntimeLockFileStore(RuntimeLockFileStore inner)
        {
            ArgumentNullException.ThrowIfNull(inner);

            this.inner = inner;
        }

        public string LockFilePath => inner.LockFilePath;

        public void Write(RuntimeLockMetadata metadata)
        {
            inner.Write(metadata);
        }

        public RuntimeLockFileReadResult Read()
        {
            return inner.Read();
        }

        public void Delete()
        {
            throw new IOException("Injected lock delete failure.");
        }
    }
}
#pragma warning restore CA1707 // Identifiers should not contain underscores

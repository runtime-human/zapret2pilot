using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
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

// Test method names deliberately use snake_case (e.g. StartAsync_NullContext_Fails)
// to make scenarios readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// xUnit tests for <see cref="RuntimeProcessHost"/> (milestone 0.0.17).
///
/// <para>
/// The non-integration cases (NullContext, PlanWithNullCacheKey,
/// MaterializationFailure) run on every platform. The integration
/// cases (AlreadyRunning, FakeRuntime_Success) launch a real child
/// process and therefore early-return on non-Windows so the suite
/// stays cross-platform-buildable.
/// </para>
/// </summary>
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
    public static void StartAsync_PlanWithNullCacheKey_Fails()
    {
        HostFixture fixture = HostFixture.Create();

        // Plan without a CacheKey — constructor used here leaves CacheKey = null.
        CompiledZapretPlan planWithoutCacheKey = new(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

        RuntimeProcessStartContext context = new(
            plan: planWithoutCacheKey,
            manifest: HostFixture.CreateMissingAssetManifest(),
            workspaceDirectory: fixture.WorkspaceDirectory,
            runtimeExecutablePath: Path.Combine(fixture.TempDir.DirectoryPath, "fake.exe"));

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
            workspaceDirectory: fixture.WorkspaceDirectory,
            runtimeExecutablePath: Path.Combine(fixture.TempDir.DirectoryPath, "fake.exe"));

        Result<RuntimeProcessHostResult> result = host_StartAsync(fixture.Host, context);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeWorkspaceMaterializationFailed", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
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
            // Make sure the launched fake runtime is reaped.
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
        fixture.PrepareFakeRuntimeInWorkspace();

        RuntimeProcessStartContext context = fixture.CreateStartContextForFakeRuntime();

        // 1. Start.
        Result<RuntimeProcessHostResult> startResult = host_StartAsync(fixture.Host, context);
        Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.ToString() : string.Empty);

        RuntimeProcessHostResult hostResult = startResult.Value;
        Assert.True(hostResult.ProcessId > 0);
        Assert.Equal(FakeRuntimeExecutableName[..^".exe".Length], hostResult.ProcessName, ignoreCase: true);
        Assert.Same(context.Plan, hostResult.Plan);

        // 2. Lock file exists, process is alive.
        Assert.True(File.Exists(fixture.LockFileStore.LockFilePath), "Lock file should exist after a successful start.");

        using (Process runningProcess = Process.GetProcessById(hostResult.ProcessId))
        {
            Assert.False(runningProcess.HasExited, "Fake runtime should still be running after start.");
        }

        // 3. Stop.
        Result<Unit> stopResult = host_StopAsync(fixture.Host);
        Assert.True(stopResult.IsSuccess, stopResult.IsFailure ? stopResult.Error.ToString() : string.Empty);

        // 4. Lock file is gone and the launched process is no longer running.
        Assert.False(File.Exists(fixture.LockFileStore.LockFilePath), "Lock file should be deleted after stop.");

        Assert.True(IsProcessGone(hostResult.ProcessId), "Fake runtime should have exited after stop.");
    }

    /// <summary>
    /// Calls <see cref="RuntimeProcessHost.StartAsync"/> synchronously
    /// on the calling thread so the ownership-mutex thread affinity
    /// invariants are preserved.
    /// </summary>
    private static Result<RuntimeProcessHostResult> host_StartAsync(
        RuntimeProcessHost host,
        RuntimeProcessStartContext context)
    {
        return host.StartAsync(context, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Calls <see cref="RuntimeProcessHost.StopAsync"/> synchronously
    /// on the calling thread so the ownership-lease disposal happens on
    /// the same thread that acquired the lease.
    /// </summary>
    private static Result<Unit> host_StopAsync(RuntimeProcessHost host)
    {
        return host.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Returns <c>true</c> when the given PID is no longer an active
    /// process. Handles both "PID not found" and "process has exited"
    /// outcomes so the test is robust against PID reuse timing.
    /// </summary>
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

    /// <summary>
    /// Per-test fixture that owns a <see cref="TemporaryDirectory"/>, a
    /// fully wired <see cref="RuntimeProcessHost"/>, and a unique
    /// ownership mutex name. The host and the temp directory are
    /// disposed together.
    /// </summary>
    private sealed class HostFixture : IDisposable
    {
        private readonly RuntimeProcessHost host;
        private bool disposed;

        private HostFixture(
            TemporaryDirectory tempDir,
            string workspaceDirectory,
            string runtimeDirectory,
            RuntimeOwnershipMutex ownershipMutex,
            RuntimeLockFileStore lockFileStore,
            RuntimeProcessHost host)
        {
            TempDir = tempDir;
            WorkspaceDirectory = workspaceDirectory;
            RuntimeDirectory = runtimeDirectory;
            OwnershipMutex = ownershipMutex;
            LockFileStore = lockFileStore;
            this.host = host;
        }

        public TemporaryDirectory TempDir { get; }

        public string WorkspaceDirectory { get; }

        public string RuntimeDirectory { get; }

        public RuntimeOwnershipMutex OwnershipMutex { get; }

        public RuntimeLockFileStore LockFileStore { get; }

        public RuntimeProcessHost Host => host;

        public static HostFixture Create()
        {
            TemporaryDirectory tempDir = new();

            string workspaceDirectory = Path.Combine(tempDir.DirectoryPath, "workspace");
            string runtimeDirectory = Path.Combine(tempDir.DirectoryPath, "runtime");

            string mutexName = RuntimeTestData.CreateUniqueMutexName();
            RuntimeOwnershipMutex ownershipMutex = new(mutexName);
            RuntimeLockFileStore lockFileStore = new(runtimeDirectory);
            RuntimeStaleLockRecovery staleLockRecovery = new(lockFileStore);
            IRuntimeWorkspaceMaterializer materializer = RuntimeWorkspaceMaterializer.CreateForRoot(workspaceDirectory);
            IRuntimeTransactionManager transactionManager = new RuntimeTransactionManager();
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
                host);
        }

        /// <summary>
        /// Locates the bundled fake runtime, copies it into the
        /// workspace under the manifest-relative path, and returns the
        /// absolute path of the original executable that the host
        /// should launch.
        /// </summary>
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
            return fakeRuntimeExePath;
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

        /// <summary>
        /// Manifest that points at a non-existent asset. Used by the
        /// pure validation tests so the materializer fails without
        /// needing any file to be present.
        /// </summary>
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

        /// <summary>
        /// Manifest that references the fake runtime copied into the
        /// workspace. The SHA-256 is computed from the actual file so
        /// the verifier accepts it.
        /// </summary>
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
            string fakeRuntimeExePath = Path.Combine(AppContext.BaseDirectory, FakeRuntimeExecutableName);
            return new RuntimeProcessStartContext(
                plan: CreatePlanWithCacheKey(),
                manifest: CreateFakeRuntimeManifest(),
                workspaceDirectory: WorkspaceDirectory,
                runtimeExecutablePath: fakeRuntimeExePath);
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
}
#pragma warning restore CA1707 // Identifiers should not contain underscores

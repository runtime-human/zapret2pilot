using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
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
            runtimeExecutablePath: fixture.CreatePlaceholderVerifiedPath());

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
            runtimeExecutablePath: fixture.CreatePlaceholderVerifiedPath());

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
        // The verified path resolves to the workspace copy
        // "<workspace>/bin/fake-runtime.exe", so the OS-reported
        // process name is the file's base name ("fake-runtime"), not
        // the apphost's source assembly name.
        string expectedProcessName = Path.GetFileNameWithoutExtension(ManifestExecutableRelativePath);
        Assert.Equal(expectedProcessName, hostResult.ProcessName, ignoreCase: true);
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
    /// P0-6 contract: after a successful stop, a subsequent start
    /// must succeed. This proves that <see cref="RuntimeProcessHost.StopAsync"/>
    /// does not roll the transaction back to <c>Running</c> after the
    /// runtime has been killed, and that the host's local state
    /// (lease, process, job object) is cleared unconditionally.
    ///
    /// <para>
    /// The test exercises the full start → stop → start cycle on
    /// Windows. It is the lightweight companion to
    /// <see cref="StopAsync_LockFileDeleteFails_AllowsRestart"/>
    /// (which forces a cleanup failure): together they document that
    /// the stop pipeline is irreversible from the caller's point of
    /// view and that a restart is always allowed afterwards.
    /// </para>
    /// </summary>
    [Fact]
    public static void StopAsync_AfterSuccess_AllowsRestart()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HostFixture fixture = HostFixture.Create();
        fixture.PrepareFakeRuntimeInWorkspace();

        RuntimeProcessStartContext context = fixture.CreateStartContextForFakeRuntime();

        // 1. First start.
        Result<RuntimeProcessHostResult> firstStart = host_StartAsync(fixture.Host, context);
        Assert.True(firstStart.IsSuccess, firstStart.IsFailure ? firstStart.Error.ToString() : string.Empty);

        // 2. Stop. The host commits the stop transaction and clears
        //    its local state, so the manager considers the runtime
        //    stopped and the host has no leftover lease / process /
        //    job object references.
        Result<Unit> firstStop = host_StopAsync(fixture.Host);
        Assert.True(firstStop.IsSuccess, firstStop.IsFailure ? firstStop.Error.ToString() : string.Empty);

        Assert.False(File.Exists(fixture.LockFileStore.LockFilePath),
            "Lock file should be deleted after stop.");

        // 3. Second start must succeed. Pre-0.0.18, the stop path
        //    could call Rollback on the stop transaction, which
        //    restored isRunning = true in the manager and would have
        //    made the second start fail with "RuntimeAlreadyRunning".
        //    After 0.0.18-B, no rollback happens after kill, so the
        //    second start goes through cleanly.
        Result<RuntimeProcessHostResult> secondStart = host_StartAsync(fixture.Host, context);
        try
        {
            Assert.True(secondStart.IsSuccess,
                $"Second start must succeed after a stop; got: {(secondStart.IsFailure ? secondStart.Error.ToString() : string.Empty)}");
            Assert.True(secondStart.Value.ProcessId > 0);
        }
        finally
        {
            // Clean up the second instance regardless of assertion outcome.
            host_StopAsync(fixture.Host);
        }
    }

    /// <summary>
    /// P0-6 contract under a forced lock-file delete failure. The
    /// lock file is marked read-only so that the stop pipeline's
    /// delete step throws <see cref="UnauthorizedAccessException"/>.
    /// The host must still commit the stop transaction and clear
    /// its local state; the only consequence of the failed cleanup
    /// is a logged warning. A subsequent start must succeed, which
    /// proves that no rollback to <c>Running</c> happened.
    ///
    /// <para>
    /// We deliberately do not assert the specific
    /// <see cref="Result{T}"/> returned by <see cref="RuntimeProcessHost.StopAsync"/>:
    /// the 0.0.18 refactor (P0-6) makes the lock-file delete a
    /// best-effort cleanup step, so the stop can return success
    /// with a logged warning, or surface the cleanup error as a
    /// failure result, depending on the implementation choice. The
    /// load-bearing contract — "no rollback, a restart must
    /// succeed" — is what this test pins down.
    /// </para>
    /// </summary>
    [Fact]
    public static void StopAsync_LockFileDeleteFails_AllowsRestart()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HostFixture fixture = HostFixture.Create();
        fixture.PrepareFakeRuntimeInWorkspace();

        RuntimeProcessStartContext context = fixture.CreateStartContextForFakeRuntime();

        // 1. First start.
        Result<RuntimeProcessHostResult> firstStart = host_StartAsync(fixture.Host, context);
        Assert.True(firstStart.IsSuccess, firstStart.IsFailure ? firstStart.Error.ToString() : string.Empty);

        // 2. Make the lock file read-only so File.Delete will throw
        //    UnauthorizedAccessException during the stop pipeline.
        string lockFilePath = fixture.LockFileStore.LockFilePath;
        Assert.True(File.Exists(lockFilePath), "Lock file should exist after a successful start.");
        File.SetAttributes(lockFilePath, FileAttributes.ReadOnly);

        try
        {
            // 3. Stop. The delete step will fail; the host must
            //    log the failure, commit the stop transaction and
            //    clear its local state (P0-6).
            Result<Unit> stop = host_StopAsync(fixture.Host);
            // The Result may be success or failure; we do not assert
            // its IsSuccess value. The P0-6 contract is verified
            // below by the second start.

            // 4. Reset the read-only attribute so the next start's
            //    stale-lock recovery can delete the leftover file.
            if (File.Exists(lockFilePath))
            {
                File.SetAttributes(lockFilePath, FileAttributes.Normal);
            }

            // 5. Second start must succeed. If the host had rolled
            //    the stop transaction back, the transaction manager
            //    would still report isRunning = true and the start
            //    would fail with "RuntimeAlreadyRunning". After
            //    0.0.18-B, the manager is in the stopped state and
            //    the start goes through (and clears any leftover
            //    lock file via stale-lock recovery).
            Result<RuntimeProcessHostResult> secondStart = host_StartAsync(fixture.Host, context);
            try
            {
                Assert.True(secondStart.IsSuccess,
                    $"Second start must succeed after a stop with failed lock delete; got: {(secondStart.IsFailure ? secondStart.Error.ToString() : string.Empty)}");
                Assert.True(secondStart.Value.ProcessId > 0);
            }
            finally
            {
                host_StopAsync(fixture.Host);
            }
        }
        finally
        {
            // Best-effort cleanup: ensure the lock file attribute is
            // reset so the temp directory cleanup does not fail.
            if (File.Exists(lockFilePath))
            {
                try
                {
                    File.SetAttributes(lockFilePath, FileAttributes.Normal);
                }
                catch
                {
                    // best effort
                }
            }
        }
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
                NullLogger<RuntimeProcessHost>.Instance,
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
        /// Locates the bundled fake runtime, copies it (together with
        /// its .dll, .deps.json and .runtimeconfig.json) into the
        /// workspace under the manifest-relative path, and returns the
        /// absolute path of the workspace copy that the host should
        /// launch.
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

            // Copy the apphost's adjacent files (.dll, .deps.json,
            // .runtimeconfig.json) so the workspace copy can actually
            // start. Without these the apphost exits immediately
            // because it cannot resolve the managed assembly or the
            // framework configuration.
            string fakeRuntimeBaseName = "Zapret2Pilot.Testing.FakeRuntime";
            string[] siblingExtensions = new[] { ".dll", ".deps.json", ".runtimeconfig.json" };
            foreach (string ext in siblingExtensions)
            {
                string source = Path.Combine(AppContext.BaseDirectory, fakeRuntimeBaseName + ext);
                string destination = Path.Combine(workspaceExeDir ?? WorkspaceDirectory, fakeRuntimeBaseName + ext);
                if (File.Exists(source))
                {
                    File.Copy(source, destination, overwrite: true);
                }
            }

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
            ZapretAssetManifest manifest = CreateFakeRuntimeManifest();
            string workspaceExePath = Path.Combine(
                WorkspaceDirectory,
                ManifestExecutableRelativePath.Replace('/', Path.DirectorySeparatorChar));
            ZapretAssetVerificationSummary summary = new(
                new[] { ManifestExecutableRelativePath });
            Result<VerifiedRuntimeExecutablePath> verifiedResult = VerifiedRuntimeExecutablePath.TryCreate(
                manifest,
                summary,
                WorkspaceDirectory);
            Assert.True(
                verifiedResult.IsSuccess,
                verifiedResult.IsFailure ? verifiedResult.Error.ToString() : string.Empty);
            return new RuntimeProcessStartContext(
                plan: CreatePlanWithCacheKey(),
                manifest: manifest,
                workspaceDirectory: WorkspaceDirectory,
                runtimeExecutablePath: verifiedResult.Value);
        }

        /// <summary>
        /// Creates a <see cref="VerifiedRuntimeExecutablePath"/> pointing
        /// at a placeholder file outside the workspace. Used by tests
        /// that need a syntactically valid verified path but never
        /// reach the point where the host actually launches the
        /// executable (e.g. cache-key validation, materialization
        /// failure).
        /// </summary>
        public VerifiedRuntimeExecutablePath CreatePlaceholderVerifiedPath()
        {
            const string RelativePath = "bin/placeholder.exe";
            string assetsRoot = Path.Combine(TempDir.DirectoryPath, "placeholder-assets");
            string absolutePath = Path.Combine(
                assetsRoot,
                RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string? parent = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllBytes(absolutePath, Encoding.UTF8.GetBytes("placeholder"));

            string hash = ComputeSha256HexLower(absolutePath);
            ZapretAssetManifest placeholderManifest = new(
                RuntimeExecutable: new ZapretRuntimeAsset(RelativePath, hash, AssetKind.Executable),
                Hostlists: Array.Empty<ZapretRuntimeAsset>(),
                StrategyPacks: Array.Empty<ZapretRuntimeAsset>());
            ZapretAssetVerificationSummary summary = new(new[] { RelativePath });
            Result<VerifiedRuntimeExecutablePath> verifiedResult = VerifiedRuntimeExecutablePath.TryCreate(
                placeholderManifest,
                summary,
                assetsRoot);
            Assert.True(
                verifiedResult.IsSuccess,
                verifiedResult.IsFailure ? verifiedResult.Error.ToString() : string.Empty);
            return verifiedResult.Value;
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

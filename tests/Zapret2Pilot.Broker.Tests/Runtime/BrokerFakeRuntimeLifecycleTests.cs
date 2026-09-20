using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Infrastructure.FileSystem;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Ownership;
using ContractGeneration = Zapret2Pilot.Contracts.Identity.RuntimeGeneration;

namespace Zapret2Pilot.Broker.Tests.Runtime;

public sealed class BrokerFakeRuntimeLifecycleTests
{
    [Fact]
    public static async Task StartThenStop_UsesBrokerKernelAndLeavesNoFakeRuntime()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using BrokerRuntimeHarness harness =
            await BrokerRuntimeHarness.CreateAsync();

        BrokerResponseEnvelope start =
            await harness.StartAsync(sequence: 1);

        Assert.Equal(BrokerResponseStatus.Accepted, start.Status);
        int processId = harness.RunningProcessId;
        Assert.True(processId > 0);
        Assert.True(IsProcessAlive(processId));

        BrokerResponseEnvelope stop =
            await harness.StopAsync(sequence: 2);

        Assert.Equal(BrokerResponseStatus.Accepted, stop.Status);
        await WaitForProcessGoneAsync(
            processId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            RuntimeKernelStatus.Stopped,
            harness.Kernel.CurrentState.Status);
    }

    [Fact]
    public static async Task BrokerGracefulShutdown_StopsFakeRuntimeBeforeTermination()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using BrokerRuntimeHarness harness =
            await BrokerRuntimeHarness.CreateAsync();

        _ = await harness.StartAsync(sequence: 1);
        int processId = harness.RunningProcessId;

        IBrokerLifetimeController lifetime =
            harness.Services.GetRequiredService<IBrokerLifetimeController>();
        IHostApplicationLifetime applicationLifetime =
            harness.Services.GetRequiredService<IHostApplicationLifetime>();

        Result<Unit> cleanup = await lifetime.StopRuntimeAsync(
            TestContext.Current.CancellationToken);

        Assert.True(
            cleanup.IsSuccess,
            cleanup.IsFailure ? cleanup.Error.ToString() : string.Empty);

        await WaitForProcessGoneAsync(
            processId,
            TestContext.Current.CancellationToken);

        Assert.False(
            applicationLifetime.ApplicationStopping.IsCancellationRequested,
            "Runtime cleanup is phase one; Broker termination must remain explicit.");

        lifetime.TerminateBroker();

        Assert.True(
            applicationLifetime.ApplicationStopping.WaitHandle.WaitOne(
                TimeSpan.FromSeconds(5)),
            "Broker termination signal was not observed.");
    }

    [Fact]
    public static async Task AppProcessDeath_StopsFakeRuntimeAndTerminatesBroker()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using FakeAppSessionLease appLease = new();
        await using BrokerRuntimeHarness harness =
            await BrokerRuntimeHarness.CreateAsync(appLease);

        _ = await harness.StartAsync(sequence: 1);
        int processId = harness.RunningProcessId;

        IHostApplicationLifetime applicationLifetime =
            harness.Services.GetRequiredService<IHostApplicationLifetime>();

        appLease.SignalExit();

        Assert.True(
            applicationLifetime.ApplicationStopping.WaitHandle.WaitOne(
                TimeSpan.FromSeconds(10)),
            "Broker did not terminate after the retained App process lease exited.");

        await WaitForProcessGoneAsync(
            processId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            RuntimeKernelStatus.Stopped,
            harness.Kernel.CurrentState.Status);
    }

    [Fact]
    public static async Task UnexpectedFakeRuntimeExit_ConvergesKernelBackToStopped()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using BrokerRuntimeHarness harness =
            await BrokerRuntimeHarness.CreateAsync();

        _ = await harness.StartAsync(sequence: 1);
        int processId = harness.RunningProcessId;

        using (Process process = Process.GetProcessById(processId))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(
                TestContext.Current.CancellationToken);
        }

        await WaitForKernelStatusAsync(
            harness.Kernel,
            RuntimeKernelStatus.Stopped,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.False(IsProcessAlive(processId));
    }

    private static async Task WaitForKernelStatusAsync(
        RuntimeKernelLoop kernel,
        RuntimeKernelStatus expected,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (kernel.CurrentState.Status == expected)
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
        }

        Assert.Equal(
            expected,
            kernel.CurrentState.Status);
    }

    private static async Task WaitForProcessGoneAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsProcessAlive(processId))
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
        }

        Assert.False(
            IsProcessAlive(processId),
            $"FakeRuntime process {processId} remained alive.");
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process =
                Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class BrokerRuntimeHarness :
        IAsyncDisposable
    {
        private const string FakeRuntimeExecutableName =
            "Zapret2Pilot.Testing.FakeRuntime.exe";
        private const string ManifestExecutableRelativePath =
            "bin/fake-runtime.exe";

        private readonly string root;
        private readonly PreparedPlanId preparedPlanId;
        private readonly IHost host;
        private long nextSequence;

        private BrokerRuntimeHarness(
            string root,
            PreparedPlanId preparedPlanId,
            IHost host)
        {
            this.root = root;
            this.preparedPlanId = preparedPlanId;
            this.host = host;

            Services = host.Services;
            Dispatcher =
                Services.GetRequiredService<BrokerRuntimeDispatcher>();
            Kernel =
                Services.GetRequiredService<RuntimeKernelLoop>();
        }

        public IServiceProvider Services { get; }

        public BrokerRuntimeDispatcher Dispatcher { get; }

        public RuntimeKernelLoop Kernel { get; }

        public int RunningProcessId =>
            Kernel.CurrentState.LastStartResult?.ProcessId
            ?? throw new InvalidOperationException(
                "Kernel does not expose a running process id.");

        public static async Task<BrokerRuntimeHarness> CreateAsync(
            IBrokerAppSessionLease? appSessionLease = null)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "z2p-broker-fakeruntime",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(root);

            try
            {
                AppDataLayout layout = new(root);
                layout.EnsureCreated();

                RuntimeProcessStartContext context =
                    PrepareFakeRuntime(layout.RuntimeDirectory);
                PreparedPlanId planId = PreparedPlanId.New();

                HostApplicationBuilder builder =
                    BrokerHostBuilder.CreateBuilder([]);

                builder.Services.Replace(
                    ServiceDescriptor.Singleton(layout));
                builder.Services.Replace(
                    ServiceDescriptor.Singleton(
                        new RuntimeOwnershipMutex(
                            $"Z2P_BROKER_TEST_{Guid.NewGuid():N}")));
                builder.Services.Replace(
                    ServiceDescriptor.Singleton<IPreparedRuntimePlanResolver>(
                        new SinglePreparedPlanResolver(
                            planId,
                            context)));

                if (appSessionLease is not null)
                {
                    BrokerAppSessionLeaseHolder holder = new();
                    if (!holder.TryBind(appSessionLease))
                    {
                        holder.Dispose();
                        throw new InvalidOperationException(
                            "Failed to bind the test AppSession lease.");
                    }

                    builder.Services.Replace(
                        ServiceDescriptor.Singleton(holder));
                }

                IHost host = builder.Build();
                await host.StartAsync(
                    TestContext.Current.CancellationToken);

                return new(
                    root,
                    planId,
                    host);
            }
            catch
            {
                TryDelete(root);
                throw;
            }
        }

        public Task<BrokerResponseEnvelope> StartAsync(
            long sequence)
        {
            ContractGeneration generation = new(
                Kernel.CurrentState.Generation.Value);

            return Dispatcher.DispatchAsync(
                CreateRequest(
                    sequence,
                    new StartPreparedPlanRequest(
                        preparedPlanId,
                        generation)),
                TestContext.Current.CancellationToken);
        }

        public Task<BrokerResponseEnvelope> StopAsync(
            long sequence)
        {
            ContractGeneration generation = new(
                Kernel.CurrentState.Generation.Value);

            return Dispatcher.DispatchAsync(
                CreateRequest(
                    sequence,
                    new StopGenerationRequest(
                        generation,
                        BrokerStopReason.UserRequested)),
                TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using CancellationTokenSource shutdown =
                    new(TimeSpan.FromSeconds(10));
                await host.StopAsync(shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                // Host disposal below still owns RuntimeProcessHost/Job cleanup.
            }
            finally
            {
                host.Dispose();
                TryDelete(root);
            }
        }

        private BrokerRequestEnvelope CreateRequest(
            long sequence,
            IBrokerRequest request)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            return new(
                BrokerProtocolVersion.V1,
                AppSessionId.New(),
                BrokerSessionId.New(),
                BrokerOperationId.New(),
                new RequestSequence(sequence),
                now,
                now + BrokerProtocolLimits.MaxRequestLifetime,
                request);
        }

        private static RuntimeProcessStartContext PrepareFakeRuntime(
            string runtimeDirectory)
        {
            string sourceExecutable = Path.Combine(
                AppContext.BaseDirectory,
                FakeRuntimeExecutableName);

            if (!File.Exists(sourceExecutable))
            {
                throw new FileNotFoundException(
                    "FakeRuntime apphost is missing from the Broker test output.",
                    sourceExecutable);
            }

            string binDirectory =
                Path.Combine(runtimeDirectory, "bin");
            Directory.CreateDirectory(binDirectory);

            string destinationExecutable = Path.Combine(
                runtimeDirectory,
                ManifestExecutableRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            File.Copy(
                sourceExecutable,
                destinationExecutable,
                overwrite: true);

            string fakeRuntimeBaseName =
                "Zapret2Pilot.Testing.FakeRuntime";
            foreach (string extension in new[]
                     {
                         ".dll",
                         ".deps.json",
                         ".runtimeconfig.json",
                     })
            {
                string source = Path.Combine(
                    AppContext.BaseDirectory,
                    fakeRuntimeBaseName + extension);

                if (File.Exists(source))
                {
                    File.Copy(
                        source,
                        Path.Combine(
                            binDirectory,
                            fakeRuntimeBaseName + extension),
                        overwrite: true);
                }
            }

            string hash = Convert.ToHexString(
                SHA256.HashData(
                    File.ReadAllBytes(destinationExecutable)))
                .ToLowerInvariant();

            ZapretAssetManifest manifest = new(
                new ZapretRuntimeAsset(
                    ManifestExecutableRelativePath,
                    hash,
                    AssetKind.Executable),
                Array.Empty<ZapretRuntimeAsset>(),
                Array.Empty<ZapretRuntimeAsset>());

            ZapretAssetVerificationSummary summary = new(
                [ManifestExecutableRelativePath]);

            Result<VerifiedRuntimeExecutablePath> verified =
                VerifiedRuntimeExecutablePath.TryCreate(
                    manifest,
                    summary,
                    runtimeDirectory);

            Assert.True(
                verified.IsSuccess,
                verified.IsFailure
                    ? verified.Error.ToString()
                    : string.Empty);

            CompiledZapretPlan plan = new(
                generatedConfigContent: "# fake config\n",
                argsContent: "--new\n",
                hostlists:
                    Array.Empty<CompiledZapretPlan.HostlistContent>(),
                id: null,
                profileId: null,
                commandLine: null,
                arguments: null,
                cacheKey: new RuntimePlanCacheKey(
                    new string('a', 64)));

            return new(
                plan,
                manifest,
                runtimeDirectory,
                verified.Value);
        }

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(
                        directory,
                        recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort after Job/process cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort after Job/process cleanup.
            }
        }
    }

    private sealed class SinglePreparedPlanResolver :
        IPreparedRuntimePlanResolver
    {
        private readonly PreparedPlanId planId;
        private readonly RuntimeProcessStartContext context;

        public SinglePreparedPlanResolver(
            PreparedPlanId planId,
            RuntimeProcessStartContext context)
        {
            this.planId = planId;
            this.context = context;
        }

        public bool TryResolve(
            PreparedPlanId preparedPlanId,
            out RuntimeProcessStartContext? startContext)
        {
            if (preparedPlanId == planId)
            {
                startContext = context;
                return true;
            }

            startContext = null;
            return false;
        }
    }

    private sealed class FakeAppSessionLease :
        IBrokerAppSessionLease
    {
        private readonly TaskCompletionSource<bool> exited = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForExitAsync(
            CancellationToken cancellationToken)
            => exited.Task.WaitAsync(cancellationToken);

        public void SignalExit()
            => exited.TrySetResult(true);

        public void Dispose()
            => exited.TrySetCanceled();
    }
}

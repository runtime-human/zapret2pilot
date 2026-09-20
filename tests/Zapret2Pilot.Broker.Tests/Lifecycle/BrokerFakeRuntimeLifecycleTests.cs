using System.Diagnostics;
using System.Security.Cryptography;
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

namespace Zapret2Pilot.Broker.Tests.Lifecycle;

/// <summary>
/// Windows-only end-to-end lifecycle evidence for #17.
///
/// These tests intentionally enter through <see cref="BrokerRuntimeDispatcher"/>
/// and launch only the test FakeRuntime. They prove that the Broker-composed
/// RuntimeKernelLoop remains the sole mutation authority and that terminal
/// stop paths leave no owned FakeRuntime process behind.
/// </summary>
public sealed class BrokerFakeRuntimeLifecycleTests
{
    [Fact]
    public static async Task StartThenStopThroughBrokerReapsFakeRuntime()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using BrokerFakeRuntimeFixture fixture = BrokerFakeRuntimeFixture.Create();
        using IHost host = fixture.BuildHost();

        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));

        await host.StartAsync(budget.Token);

        BrokerRuntimeDispatcher dispatcher =
            host.Services.GetRequiredService<BrokerRuntimeDispatcher>();
        RuntimeKernelLoop loop =
            host.Services.GetRequiredService<RuntimeKernelLoop>();

        SessionIds session = SessionIds.Create();

        BrokerResponseEnvelope startResponse = await dispatcher.DispatchAsync(
            CreateRequest(
                session,
                sequence: 1,
                new StartPreparedPlanRequest(
                    fixture.PreparedPlanId,
                    new ContractGeneration(loop.CurrentState.Generation.Value))),
            budget.Token);

        Assert.Equal(BrokerResponseStatus.Accepted, startResponse.Status);
        Assert.Equal(RuntimeKernelStatus.Running, loop.CurrentState.Status);
        Assert.NotNull(loop.CurrentState.LastStartResult);

        int processId = loop.CurrentState.LastStartResult!.ProcessId;
        Assert.True(IsProcessAlive(processId));

        BrokerResponseEnvelope stopResponse = await dispatcher.DispatchAsync(
            CreateRequest(
                session,
                sequence: 2,
                new StopGenerationRequest(
                    new ContractGeneration(loop.CurrentState.Generation.Value),
                    BrokerStopReason.UserRequested)),
            budget.Token);

        Assert.Equal(BrokerResponseStatus.Accepted, stopResponse.Status);
        Assert.Equal(RuntimeKernelStatus.Stopped, loop.CurrentState.Status);
        Assert.True(
            IsProcessGone(processId),
            $"FakeRuntime process {processId} remained alive after Broker stop.");
    }

    [Fact]
    public static async Task GracefulBrokerHostStopReapsRunningFakeRuntime()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using BrokerFakeRuntimeFixture fixture = BrokerFakeRuntimeFixture.Create();
        using IHost host = fixture.BuildHost();

        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));

        await host.StartAsync(budget.Token);

        BrokerRuntimeDispatcher dispatcher =
            host.Services.GetRequiredService<BrokerRuntimeDispatcher>();
        RuntimeKernelLoop loop =
            host.Services.GetRequiredService<RuntimeKernelLoop>();

        SessionIds session = SessionIds.Create();

        BrokerResponseEnvelope startResponse = await dispatcher.DispatchAsync(
            CreateRequest(
                session,
                sequence: 1,
                new StartPreparedPlanRequest(
                    fixture.PreparedPlanId,
                    new ContractGeneration(loop.CurrentState.Generation.Value))),
            budget.Token);

        Assert.Equal(BrokerResponseStatus.Accepted, startResponse.Status);
        Assert.Equal(RuntimeKernelStatus.Running, loop.CurrentState.Status);
        Assert.NotNull(loop.CurrentState.LastStartResult);

        int processId = loop.CurrentState.LastStartResult!.ProcessId;
        Assert.True(IsProcessAlive(processId));

        await host.StopAsync(budget.Token);

        Assert.True(
            IsProcessGone(processId),
            $"FakeRuntime process {processId} remained alive after graceful Broker host shutdown.");
    }

    private static BrokerRequestEnvelope CreateRequest(
        SessionIds session,
        long sequence,
        IBrokerRequest request)
    {
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
        return new BrokerRequestEnvelope(
            BrokerProtocolVersion.V1,
            session.AppSessionId,
            session.BrokerSessionId,
            BrokerOperationId.New(),
            new RequestSequence(sequence),
            issuedAt,
            issuedAt + BrokerProtocolLimits.MaxRequestLifetime,
            request);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsProcessGone(int processId)
        => !IsProcessAlive(processId);

    private sealed record SessionIds(
        AppSessionId AppSessionId,
        BrokerSessionId BrokerSessionId)
    {
        public static SessionIds Create()
            => new(AppSessionId.New(), BrokerSessionId.New());
    }

    private sealed class BrokerFakeRuntimeFixture : IDisposable
    {
        private const string FakeRuntimeExecutableName =
            "Zapret2Pilot.Testing.FakeRuntime.exe";
        private const string FakeRuntimeBaseName =
            "Zapret2Pilot.Testing.FakeRuntime";
        private const string ManifestExecutableRelativePath =
            "bin/fake-runtime.exe";

        private readonly string rootDirectory;
        private bool disposed;

        private BrokerFakeRuntimeFixture(
            string rootDirectory,
            AppDataLayout layout,
            PreparedPlanId preparedPlanId,
            RuntimeProcessStartContext startContext)
        {
            this.rootDirectory = rootDirectory;
            Layout = layout;
            PreparedPlanId = preparedPlanId;
            StartContext = startContext;
        }

        public AppDataLayout Layout { get; }

        public PreparedPlanId PreparedPlanId { get; }

        public RuntimeProcessStartContext StartContext { get; }

        public static BrokerFakeRuntimeFixture Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "z2p-broker-lifecycle",
                Guid.NewGuid().ToString("N"));

            AppDataLayout layout =
                AppDataPathProvider.CreateLayout(root);
            Directory.CreateDirectory(layout.RuntimeDirectory);

            string executablePath = CopyFakeRuntime(layout.RuntimeDirectory);
            string hash = ComputeSha256HexLower(executablePath);

            ZapretAssetManifest manifest = new(
                RuntimeExecutable: new ZapretRuntimeAsset(
                    ManifestExecutableRelativePath,
                    hash,
                    AssetKind.Executable),
                Hostlists: Array.Empty<ZapretRuntimeAsset>(),
                StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

            ZapretAssetVerificationSummary summary = new(
                [ManifestExecutableRelativePath]);

            Result<VerifiedRuntimeExecutablePath> verified =
                VerifiedRuntimeExecutablePath.TryCreate(
                    manifest,
                    summary,
                    layout.RuntimeDirectory);

            Assert.True(
                verified.IsSuccess,
                verified.IsFailure
                    ? verified.Error.ToString()
                    : string.Empty);

            CompiledZapretPlan plan = new(
                generatedConfigContent: "# FakeRuntime lifecycle proof\n",
                argsContent: "--fake-runtime\n",
                hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>(),
                id: null,
                profileId: null,
                commandLine: null,
                arguments: null,
                cacheKey: new RuntimePlanCacheKey(new string('b', 64)));

            RuntimeProcessStartContext context = new(
                plan,
                manifest,
                layout.RuntimeDirectory,
                verified.Value);

            return new BrokerFakeRuntimeFixture(
                root,
                layout,
                PreparedPlanId.New(),
                context);
        }

        public IHost BuildHost()
        {
            HostApplicationBuilder builder =
                BrokerHostBuilder.CreateBuilder([]);

            builder.Services.RemoveAll<AppDataLayout>();
            builder.Services.AddSingleton(Layout);

            builder.Services.RemoveAll<RuntimeOwnershipMutex>();
            builder.Services.AddSingleton(
                new RuntimeOwnershipMutex(CreateUniqueMutexName()));

            builder.Services.RemoveAll<IPreparedRuntimePlanResolver>();
            builder.Services.AddSingleton<IPreparedRuntimePlanResolver>(
                new FixedPreparedRuntimePlanResolver(
                    PreparedPlanId,
                    StartContext));

            return builder.Build();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                if (Directory.Exists(rootDirectory))
                {
                    Directory.Delete(rootDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // A failing lifecycle assertion is more useful than masking it
                // with best-effort temporary-directory cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Same rationale as the IOException branch above.
            }
        }

        private static string CopyFakeRuntime(string runtimeDirectory)
        {
            string sourceExecutable = Path.Combine(
                AppContext.BaseDirectory,
                FakeRuntimeExecutableName);

            if (!File.Exists(sourceExecutable))
            {
                throw new FileNotFoundException(
                    "FakeRuntime apphost is not present in the Broker test output.",
                    sourceExecutable);
            }

            string destinationExecutable = Path.Combine(
                runtimeDirectory,
                ManifestExecutableRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            string destinationDirectory =
                Path.GetDirectoryName(destinationExecutable)
                ?? throw new InvalidOperationException(
                    "FakeRuntime destination directory is unavailable.");

            Directory.CreateDirectory(destinationDirectory);
            File.Copy(
                sourceExecutable,
                destinationExecutable,
                overwrite: true);

            foreach (string extension in new[]
                     {
                         ".dll",
                         ".deps.json",
                         ".runtimeconfig.json",
                     })
            {
                string source = Path.Combine(
                    AppContext.BaseDirectory,
                    FakeRuntimeBaseName + extension);
                if (!File.Exists(source))
                {
                    continue;
                }

                File.Copy(
                    source,
                    Path.Combine(
                        destinationDirectory,
                        FakeRuntimeBaseName + extension),
                    overwrite: true);
            }

            return destinationExecutable;
        }

        private static string ComputeSha256HexLower(string path)
        {
            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string CreateUniqueMutexName()
            => $@"Global\Z2P_BROKER_LIFECYCLE_{Guid.NewGuid():N}";
    }

    private sealed class FixedPreparedRuntimePlanResolver :
        IPreparedRuntimePlanResolver
    {
        private readonly PreparedPlanId preparedPlanId;
        private readonly RuntimeProcessStartContext context;

        public FixedPreparedRuntimePlanResolver(
            PreparedPlanId preparedPlanId,
            RuntimeProcessStartContext context)
        {
            this.preparedPlanId = preparedPlanId;
            this.context = context;
        }

        public bool TryResolve(
            PreparedPlanId requestedPlanId,
            out RuntimeProcessStartContext? startContext)
        {
            if (requestedPlanId == preparedPlanId)
            {
                startContext = context;
                return true;
            }

            startContext = null;
            return false;
        }
    }
}

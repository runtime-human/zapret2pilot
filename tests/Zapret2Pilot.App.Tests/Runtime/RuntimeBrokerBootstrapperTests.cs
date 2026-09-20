using System;
using System.ComponentModel;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.App.Runtime;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.App.Tests.Runtime;

public sealed class RuntimeBrokerBootstrapperTests
{
    [Fact]
    public static async Task SuccessfulBootstrapTransfersSecretAttachesSessionAndShutsDownBroker()
    {
        AppSessionId sessionId = AppSessionId.New();
        BrokerClientBinding binding = CreateBinding(sessionId);
        FakeBindingProvider bindingProvider = new(binding);
        FakeExecutableLocator locator = new();
        FakeElevatedBrokerProcess process = new();
        FakeLauncher launcher = new(process);
        FakeBootstrapServer bootstrapServer = new();
        FakeBootstrapServerFactory bootstrapFactory = new(bootstrapServer);
        FakeSession brokerSession = new();
        FakeSessionFactory sessionFactory = new(brokerSession);
        using BrokerRuntimeClient runtimeClient = new();

        using RuntimeBrokerBootstrapper bootstrapper = new(
            bindingProvider,
            locator,
            launcher,
            bootstrapFactory,
            sessionFactory,
            runtimeClient,
            NullLogger<RuntimeBrokerBootstrapper>.Instance);

        RuntimeBrokerBootstrapResult result =
            await bootstrapper.StartBrokerAsync(
                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(runtimeClient.IsConnected);
        Assert.True(bootstrapServer.Verified);
        Assert.True(bootstrapServer.Sent);
        Assert.NotNull(bootstrapServer.CapturedBootstrap);
        Assert.NotNull(bindingProvider.LastBinding);
        Assert.Equal(
            bindingProvider.LastBinding,
            bootstrapServer.CapturedBootstrap!.ClientBinding);
        Assert.Equal(
            BrokerAuthenticator.SecretSizeBytes,
            bootstrapServer.CapturedSecret!.Length);
        Assert.Equal(binding.ProcessId, launcher.LastAppProcessId);
        Assert.StartsWith(
            "z2p-bootstrap-",
            launcher.LastBootstrapPipeName,
            StringComparison.Ordinal);
        Assert.Equal(1, brokerSession.ConnectCallCount);

        await bootstrapper.StopAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(1, brokerSession.ShutdownCallCount);
        Assert.True(brokerSession.Disposed);
        Assert.Equal(1, process.WaitForExitCallCount);
        Assert.True(process.Disposed);
    }

    [Fact]
    public static async Task UacCancellationLeavesRuntimeClientDisconnected()
    {
        BrokerClientBinding binding =
            CreateBinding(AppSessionId.New());
        using BrokerRuntimeClient runtimeClient = new();

        using RuntimeBrokerBootstrapper bootstrapper = new(
            new FakeBindingProvider(binding),
            new FakeExecutableLocator(),
            new CancellingLauncher(),
            new FakeBootstrapServerFactory(
                new FakeBootstrapServer()),
            new FakeSessionFactory(new FakeSession()),
            runtimeClient,
            NullLogger<RuntimeBrokerBootstrapper>.Instance);

        RuntimeBrokerBootstrapResult result =
            await bootstrapper.StartBrokerAsync(
                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "BrokerElevationCancelled",
            result.ErrorCode);
        Assert.False(runtimeClient.IsConnected);
    }

    [Fact]
    public static async Task PostLaunchBootstrapFailureTerminatesBrokerAndDoesNotAttach()
    {
        BrokerClientBinding binding =
            CreateBinding(AppSessionId.New());
        FakeElevatedBrokerProcess process = new();
        FakeBootstrapServer bootstrapServer = new()
        {
            FailVerification = true,
        };
        using BrokerRuntimeClient runtimeClient = new();

        using RuntimeBrokerBootstrapper bootstrapper = new(
            new FakeBindingProvider(binding),
            new FakeExecutableLocator(),
            new FakeLauncher(process),
            new FakeBootstrapServerFactory(bootstrapServer),
            new FakeSessionFactory(new FakeSession()),
            runtimeClient,
            NullLogger<RuntimeBrokerBootstrapper>.Instance);

        RuntimeBrokerBootstrapResult result =
            await bootstrapper.StartBrokerAsync(
                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "RuntimeBrokerBootstrapFailed",
            result.ErrorCode);
        Assert.False(runtimeClient.IsConnected);
        Assert.Equal(1, process.TerminateCallCount);
        Assert.True(process.Disposed);
    }

    private static BrokerClientBinding CreateBinding(
        AppSessionId appSessionId)
        => new(
            appSessionId,
            ProcessId: 4242,
            ProcessCreationTimeFileTime: 123456789,
            WindowsSessionId: 3,
            UserSid: "S-1-5-21-1000",
            LogonSessionId: new LogonSessionId(11, 22),
            IntegrityLevelRid: 0x2000);

    private sealed class FakeBindingProvider :
        IAppProcessBindingProvider
    {
        private readonly BrokerClientBinding binding;

        public FakeBindingProvider(
            BrokerClientBinding binding)
        {
            this.binding = binding;
        }

        public BrokerClientBinding? LastBinding { get; private set; }

        public BrokerClientBinding Create(
            AppSessionId appSessionId)
        {
            LastBinding = binding with
            {
                AppSessionId = appSessionId,
            };
            return LastBinding;
        }
    }

    private sealed class FakeExecutableLocator :
        IBrokerExecutableLocator
    {
        public string ResolveBrokerExecutablePath()
            => @"C:\fake\z2p-broker.exe";
    }

    private sealed class FakeLauncher :
        IElevatedBrokerLauncher
    {
        private readonly IElevatedBrokerProcess process;

        public FakeLauncher(
            IElevatedBrokerProcess process)
        {
            this.process = process;
        }

        public string? LastBootstrapPipeName { get; private set; }

        public int LastAppProcessId { get; private set; }

        public IElevatedBrokerProcess Launch(
            string executablePath,
            string bootstrapPipeName,
            int appProcessId)
        {
            Assert.Equal(
                @"C:\fake\z2p-broker.exe",
                executablePath);
            LastBootstrapPipeName = bootstrapPipeName;
            LastAppProcessId = appProcessId;
            return process;
        }
    }

    private sealed class CancellingLauncher :
        IElevatedBrokerLauncher
    {
        public IElevatedBrokerProcess Launch(
            string executablePath,
            string bootstrapPipeName,
            int appProcessId)
            => throw new Win32Exception(1223);
    }

    private sealed class FakeElevatedBrokerProcess :
        IElevatedBrokerProcess
    {
        public int ProcessId => 5555;

        public long ProcessCreationTimeFileTime =>
            987654321;

        public bool HasExited { get; set; }

        public int WaitForExitCallCount { get; private set; }

        public int TerminateCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitForExitCallCount++;
            HasExited = true;
            return Task.CompletedTask;
        }

        public bool TryTerminate()
        {
            TerminateCallCount++;
            HasExited = true;
            return true;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class FakeBootstrapServerFactory :
        IBrokerBootstrapServerFactory
    {
        private readonly FakeBootstrapServer server;

        public FakeBootstrapServerFactory(
            FakeBootstrapServer server)
        {
            this.server = server;
        }

        public IBrokerBootstrapServer Create(
            string pipeName,
            string expectedUserSid)
        {
            Assert.StartsWith(
                "z2p-bootstrap-",
                pipeName,
                StringComparison.Ordinal);
            Assert.StartsWith(
                "S-",
                expectedUserSid,
                StringComparison.Ordinal);
            return server;
        }
    }

    private sealed class FakeBootstrapServer :
        IBrokerBootstrapServer
    {
        public bool FailVerification { get; set; }

        public bool Verified { get; private set; }

        public bool Sent { get; private set; }

        public BrokerBootstrapMessage? CapturedBootstrap { get; private set; }

        public byte[]? CapturedSecret { get; private set; }

        public Task WaitForVerifiedConnectionAsync(
            IElevatedBrokerProcess launchedBroker,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FailVerification)
            {
                throw new UnauthorizedAccessException(
                    "simulated peer mismatch");
            }

            Verified = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(
            BrokerBootstrapMessage bootstrap,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent = true;
            CapturedBootstrap = bootstrap;
            CapturedSecret =
                bootstrap.BootstrapSecret.ToArray();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class FakeSessionFactory :
        IRuntimeBrokerSessionFactory
    {
        private readonly FakeSession session;

        public FakeSessionFactory(
            FakeSession session)
        {
            this.session = session;
        }

        public IConnectableRuntimeBrokerSession Create(
            BrokerClientBinding clientBinding,
            string pipeName,
            byte[] bootstrapSecret)
        {
            Assert.StartsWith(
                "z2p-runtime-",
                pipeName,
                StringComparison.Ordinal);
            Assert.Equal(
                BrokerAuthenticator.SecretSizeBytes,
                bootstrapSecret.Length);
            return session;
        }
    }

    private sealed class FakeSession :
        IConnectableRuntimeBrokerSession
    {
        private readonly BehaviorSubject<BrokerRuntimeSnapshot> snapshots =
            new(
                new BrokerRuntimeSnapshot(
                    new RuntimeGeneration(1),
                    BrokerRuntimeState.Stopped,
                    ActivePlanId: null,
                    ActiveOperationId: null));

        public BrokerRuntimeSnapshot CurrentSnapshot =>
            snapshots.Value;

        public IObservable<BrokerRuntimeSnapshot> SnapshotChanged =>
            snapshots;

        public int ConnectCallCount { get; private set; }

        public int ShutdownCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task ConnectAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCallCount++;
            return Task.CompletedTask;
        }

        public Task<BrokerRuntimeSnapshot> GetRuntimeSnapshotAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(CurrentSnapshot);

        public Task<BrokerRuntimeSnapshot> StartPreparedPlanAsync(
            PreparedPlanId preparedPlanId,
            RuntimeGeneration expectedGeneration,
            CancellationToken cancellationToken)
            => Task.FromResult(CurrentSnapshot);

        public Task<BrokerRuntimeSnapshot> StopGenerationAsync(
            RuntimeGeneration generation,
            BrokerStopReason reason,
            CancellationToken cancellationToken)
            => Task.FromResult(CurrentSnapshot);

        public Task ShutdownBrokerAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShutdownCallCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            snapshots.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

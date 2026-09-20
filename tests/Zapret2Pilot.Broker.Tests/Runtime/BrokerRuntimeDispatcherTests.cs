using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Supervisor;
using ContractGeneration = Zapret2Pilot.Contracts.Identity.RuntimeGeneration;
using KernelGeneration = Zapret2Pilot.Runtime.Kernel.RuntimeGeneration;

namespace Zapret2Pilot.Broker.Tests.Runtime;

public sealed class BrokerRuntimeDispatcherTests
{
    [Fact]
    public static async Task SnapshotQueryProjectsAuthoritativeKernelState()
    {
        using FakeRuntimeSupervisor supervisor = new();
        FakeRuntimeStateProjection projection = new(CreateKernelState(RuntimeKernelStatus.Running, 12));
        FakePreparedRuntimePlanResolver resolver = new();
        BrokerRuntimeDispatcher dispatcher = CreateDispatcher(supervisor, projection, resolver);

        BrokerResponseEnvelope response = await dispatcher.DispatchAsync(
            CreateRequest(1, new GetRuntimeSnapshotRequest()),
            CancellationToken.None);

        Assert.Equal(BrokerResponseStatus.Ok, response.Status);
        BrokerRuntimeSnapshotResponse body = Assert.IsType<BrokerRuntimeSnapshotResponse>(response.Response);
        Assert.Equal(12, body.Snapshot.Generation.Value);
        Assert.Equal(BrokerRuntimeState.Running, body.Snapshot.State);
        Assert.Equal(0, supervisor.StartCallCount);
        Assert.Equal(0, supervisor.StopCallCount);
    }

    [Theory]
    [InlineData(6, BrokerResponseStatus.Stale, "StaleRuntimeGeneration")]
    [InlineData(8, BrokerResponseStatus.Rejected, "FutureRuntimeGeneration")]
    public static async Task StartRejectsNonCurrentGenerationBeforeRuntimeDispatch(
        long expectedGeneration,
        BrokerResponseStatus expectedStatus,
        string expectedCode)
    {
        using FakeRuntimeSupervisor supervisor = new();
        FakeRuntimeStateProjection projection = new(CreateKernelState(RuntimeKernelStatus.Stopped, 7));
        using RuntimeContextFixture context = RuntimeContextFixture.Create();
        FakePreparedRuntimePlanResolver resolver = new(context.Context);
        BrokerRuntimeDispatcher dispatcher = CreateDispatcher(supervisor, projection, resolver);

        BrokerResponseEnvelope response = await dispatcher.DispatchAsync(
            CreateRequest(
                1,
                new StartPreparedPlanRequest(
                    PreparedPlanId.New(),
                    new ContractGeneration(expectedGeneration))),
            CancellationToken.None);

        Assert.Equal(expectedStatus, response.Status);
        BrokerErrorResponse error = Assert.IsType<BrokerErrorResponse>(response.Response);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(0, supervisor.StartCallCount);
    }

    [Fact]
    public static async Task DuplicateInFlightAndCompletedStartNeverDispatchTwice()
    {
        TaskCompletionSource<bool> releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using FakeRuntimeSupervisor supervisor = new(startGate: releaseStart.Task);
        FakeRuntimeStateProjection projection = new(CreateKernelState(RuntimeKernelStatus.Stopped, 4));
        using RuntimeContextFixture context = RuntimeContextFixture.Create();
        FakePreparedRuntimePlanResolver resolver = new(context.Context);
        BrokerRuntimeDispatcher dispatcher = CreateDispatcher(supervisor, projection, resolver);

        PreparedPlanId planId = PreparedPlanId.New();
        BrokerRequestEnvelope request = CreateRequest(
            1,
            new StartPreparedPlanRequest(planId, new ContractGeneration(4)));

        Task<BrokerResponseEnvelope> first = dispatcher.DispatchAsync(request, CancellationToken.None);
        await supervisor.StartEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        BrokerResponseEnvelope duplicateInFlight = await dispatcher.DispatchAsync(request, CancellationToken.None);
        Assert.Equal(BrokerResponseStatus.DuplicateInFlight, duplicateInFlight.Status);
        Assert.Equal(1, supervisor.StartCallCount);

        releaseStart.TrySetResult(true);
        BrokerResponseEnvelope firstResponse = await first;
        Assert.Equal(BrokerResponseStatus.Accepted, firstResponse.Status);

        BrokerResponseEnvelope duplicateCompleted = await dispatcher.DispatchAsync(request, CancellationToken.None);
        Assert.Equal(BrokerResponseStatus.DuplicateCompleted, duplicateCompleted.Status);
        Assert.Equal(1, supervisor.StartCallCount);
    }

    [Fact]
    public static async Task ConcurrentMutationIsRejectedBusyWithoutSecondRuntimeDispatch()
    {
        TaskCompletionSource<bool> releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using FakeRuntimeSupervisor supervisor = new(startGate: releaseStart.Task);
        FakeRuntimeStateProjection projection = new(CreateKernelState(RuntimeKernelStatus.Stopped, 5));
        using RuntimeContextFixture context = RuntimeContextFixture.Create();
        FakePreparedRuntimePlanResolver resolver = new(context.Context);
        BrokerRuntimeDispatcher dispatcher = CreateDispatcher(supervisor, projection, resolver);

        Task<BrokerResponseEnvelope> first = dispatcher.DispatchAsync(
            CreateRequest(
                1,
                new StartPreparedPlanRequest(PreparedPlanId.New(), new ContractGeneration(5))),
            CancellationToken.None);
        await supervisor.StartEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        BrokerResponseEnvelope second = await dispatcher.DispatchAsync(
            CreateRequest(
                2,
                new StartPreparedPlanRequest(PreparedPlanId.New(), new ContractGeneration(5))),
            CancellationToken.None);

        Assert.Equal(BrokerResponseStatus.Busy, second.Status);
        Assert.Equal(1, supervisor.StartCallCount);

        releaseStart.TrySetResult(true);
        _ = await first;
    }

    [Fact]
    public static async Task StopIsAdmittedWhileStartMutationIsInFlight()
    {
        TaskCompletionSource<bool> releaseStart = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using FakeRuntimeSupervisor supervisor = new(
            startGate: releaseStart.Task);
        FakeRuntimeStateProjection projection = new(
            CreateKernelState(RuntimeKernelStatus.Starting, 6));
        using RuntimeContextFixture context =
            RuntimeContextFixture.Create();
        FakePreparedRuntimePlanResolver resolver =
            new(context.Context);
        BrokerRuntimeDispatcher dispatcher =
            CreateDispatcher(supervisor, projection, resolver);

        Task<BrokerResponseEnvelope> start = dispatcher.DispatchAsync(
            CreateRequest(
                1,
                new StartPreparedPlanRequest(
                    PreparedPlanId.New(),
                    new ContractGeneration(6))),
            CancellationToken.None);

        await supervisor.StartEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        BrokerResponseEnvelope stop =
            await dispatcher.DispatchAsync(
                CreateRequest(
                    2,
                    new StopGenerationRequest(
                        new ContractGeneration(6),
                        BrokerStopReason.UserRequested)),
                CancellationToken.None);

        Assert.Equal(BrokerResponseStatus.Accepted, stop.Status);
        Assert.Equal(1, supervisor.StartCallCount);
        Assert.Equal(1, supervisor.StopCallCount);

        releaseStart.TrySetResult(true);
        _ = await start;
    }

    [Fact]
    public static async Task ValidStartResolvesPreparedPlanAndDelegatesToSupervisor()
    {
        using FakeRuntimeSupervisor supervisor = new();
        FakeRuntimeStateProjection projection = new(CreateKernelState(RuntimeKernelStatus.Stopped, 2));
        using RuntimeContextFixture context = RuntimeContextFixture.Create();
        FakePreparedRuntimePlanResolver resolver = new(context.Context);
        BrokerRuntimeDispatcher dispatcher = CreateDispatcher(supervisor, projection, resolver);
        PreparedPlanId planId = PreparedPlanId.New();

        BrokerResponseEnvelope response = await dispatcher.DispatchAsync(
            CreateRequest(
                1,
                new StartPreparedPlanRequest(planId, new ContractGeneration(2))),
            CancellationToken.None);

        Assert.Equal(BrokerResponseStatus.Accepted, response.Status);
        Assert.Equal(1, supervisor.StartCallCount);
        Assert.Same(context.Context, supervisor.LastStartContext);
        Assert.Equal(planId, resolver.LastRequestedPlanId);
    }

    [Fact]
    public static async Task ValidStopDelegatesToSupervisor()
    {
        using FakeRuntimeSupervisor supervisor = new();
        FakeRuntimeStateProjection projection = new(CreateKernelState(RuntimeKernelStatus.Running, 9));
        FakePreparedRuntimePlanResolver resolver = new();
        BrokerRuntimeDispatcher dispatcher = CreateDispatcher(supervisor, projection, resolver);

        BrokerResponseEnvelope response = await dispatcher.DispatchAsync(
            CreateRequest(
                1,
                new StopGenerationRequest(new ContractGeneration(9), BrokerStopReason.UserRequested)),
            CancellationToken.None);

        Assert.Equal(BrokerResponseStatus.Accepted, response.Status);
        Assert.Equal(1, supervisor.StopCallCount);
        Assert.Equal(0, supervisor.StartCallCount);
    }

    [Fact]
    public static async Task ShutdownDelegatesToSingleLifetimeController()
    {
        using FakeRuntimeSupervisor supervisor = new();
        FakeRuntimeStateProjection projection = new(CreateKernelState(RuntimeKernelStatus.Running, 11));
        FakePreparedRuntimePlanResolver resolver = new();
        FakeBrokerLifetimeController lifetime = new();
        BrokerRuntimeDispatcher dispatcher = CreateDispatcher(
            supervisor,
            projection,
            resolver,
            lifetime);

        BrokerRequestEnvelope request = CreateRequest(
            1,
            new ShutdownBrokerRequest());

        BrokerResponseEnvelope response = await dispatcher.DispatchAsync(
            request,
            CancellationToken.None);

        Assert.Equal(BrokerResponseStatus.Accepted, response.Status);
        Assert.Equal(1, lifetime.StopRuntimeCallCount);

        BrokerResponseEnvelope duplicate = await dispatcher.DispatchAsync(
            request,
            CancellationToken.None);
        Assert.Equal(BrokerResponseStatus.DuplicateCompleted, duplicate.Status);
        Assert.Equal(1, lifetime.StopRuntimeCallCount);
    }

    private static BrokerRuntimeDispatcher CreateDispatcher(
        IRuntimeSupervisor supervisor,
        IBrokerRuntimeStateProjection projection,
        IPreparedRuntimePlanResolver resolver,
        IBrokerLifetimeController? lifetimeController = null) =>
        new(
            supervisor,
            projection,
            resolver,
            new BrokerOperationLedger(
                BrokerProtocolLimits.OperationLedgerCapacity,
                BrokerProtocolLimits.OperationLedgerTtl),
            new BrokerConcurrencyGate(
                BrokerProtocolLimits.MaxInFlightQueries,
                BrokerProtocolLimits.MaxConcurrentMutations),
            lifetimeController ?? new FakeBrokerLifetimeController());

    private static BrokerRequestEnvelope CreateRequest(long sequence, IBrokerRequest body)
    {
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
        return new BrokerRequestEnvelope(
            BrokerProtocolVersion.V1,
            AppSessionId.New(),
            BrokerSessionId.New(),
            BrokerOperationId.New(),
            new RequestSequence(sequence),
            issuedAt,
            issuedAt + BrokerProtocolLimits.MaxRequestLifetime,
            body);
    }

    private static RuntimeKernelState CreateKernelState(RuntimeKernelStatus status, long generation) =>
        new(
            status,
            AutomationOwner.None,
            new KernelGeneration(generation),
            pendingOperationId: null,
            lastStartResult: null,
            guardResult: null,
            lastError: null,
            DateTimeOffset.UtcNow);

    private sealed class FakeRuntimeStateProjection : IBrokerRuntimeStateProjection
    {
        public FakeRuntimeStateProjection(RuntimeKernelState currentState)
        {
            CurrentState = currentState;
        }

        public RuntimeKernelState CurrentState { get; set; }
    }

    private sealed class FakePreparedRuntimePlanResolver : IPreparedRuntimePlanResolver
    {
        private readonly RuntimeProcessStartContext? context;

        public FakePreparedRuntimePlanResolver(RuntimeProcessStartContext? context = null)
        {
            this.context = context;
        }

        public PreparedPlanId? LastRequestedPlanId { get; private set; }

        public bool TryResolve(PreparedPlanId preparedPlanId, out RuntimeProcessStartContext? startContext)
        {
            LastRequestedPlanId = preparedPlanId;
            startContext = context;
            return startContext is not null;
        }
    }

    private sealed class FakeBrokerLifetimeController : IBrokerLifetimeController
    {
        public int StopRuntimeCallCount { get; private set; }

        public Task<Result<Unit>> StopRuntimeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopRuntimeCallCount++;
            return Task.FromResult(Result.Success(Unit.Instance));
        }

        public void TerminateBroker()
        {
        }
    }

    private sealed class FakeRuntimeSupervisor : IRuntimeSupervisor, IDisposable
    {
        private readonly Task startGate;
        private readonly BehaviorSubject<RuntimeSupervisorState> states = new(
            new RuntimeSupervisorState(
                RuntimeSupervisorStatus.Stopped,
                lastStartResult: null,
                guardResult: null,
                lastError: null,
                DateTimeOffset.UtcNow));

        public FakeRuntimeSupervisor(Task? startGate = null)
        {
            this.startGate = startGate ?? Task.CompletedTask;
        }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public RuntimeProcessStartContext? LastStartContext { get; private set; }

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RuntimeSupervisorState CurrentState => states.Value;

        public IObservable<RuntimeSupervisorState> StateChanged => states.AsObservable();

        public async Task<Result<RuntimeProcessHostResult>> StartAsync(
            RuntimeProcessStartContext context,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            LastStartContext = context;
            StartEntered.TrySetResult(true);
            await startGate.WaitAsync(cancellationToken);

            return Result.Success(new RuntimeProcessHostResult(
                1,
                "fake-runtime",
                context.RuntimeExecutablePath.AbsolutePath,
                context.Plan));
        }

        public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCallCount++;
            return Task.FromResult(Result.Success(Unit.Instance));
        }

        public void Dispose()
        {
            states.Dispose();
        }
    }

    private sealed class RuntimeContextFixture : IDisposable
    {
        private RuntimeContextFixture(string root, RuntimeProcessStartContext context)
        {
            Root = root;
            Context = context;
        }

        public string Root { get; }

        public RuntimeProcessStartContext Context { get; }

        public static RuntimeContextFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "z2p-broker-tests", Guid.NewGuid().ToString("N"));
            string executableRelativePath = Path.Combine("bin", "fake-runtime.exe");
            string executablePath = Path.Combine(root, executableRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            File.WriteAllText(executablePath, "fake");

            ZapretAssetManifest manifest = new(
                new ZapretRuntimeAsset(
                    executableRelativePath,
                    new string('0', 64),
                    AssetKind.Executable),
                Array.Empty<ZapretRuntimeAsset>(),
                Array.Empty<ZapretRuntimeAsset>());

            ZapretAssetVerificationSummary summary = new([executableRelativePath]);
            Result<VerifiedRuntimeExecutablePath> verified =
                VerifiedRuntimeExecutablePath.TryCreate(manifest, summary, root);
            Assert.True(verified.IsSuccess);

            CompiledZapretPlan plan = new(
                generatedConfigContent: string.Empty,
                argsContent: string.Empty,
                hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

            return new RuntimeContextFixture(
                root,
                new RuntimeProcessStartContext(plan, manifest, root, verified.Value));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

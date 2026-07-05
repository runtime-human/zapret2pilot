#pragma warning disable CA1707 // Identifiers should not contain underscores

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Tests.Testing;

namespace Zapret2Pilot.Runtime.Tests.Kernel;

/// <summary>
/// xUnit tests for <see cref="RuntimeProcessEffectRunner"/>
/// (milestone 0.0.24, Packet 4). The tests exercise the
/// runner's cancellation classification, irreversible-boundary
/// tracking, deadline pre-check and exception-to-completion
/// conversion paths. The runner is internal and is reached via
/// <see cref="RuntimeKernelLoop"/> or directly with a
/// caller-supplied <see cref="IRuntimeProcessHost"/>; this
/// fixture uses the direct route so the runner's
/// behaviour can be asserted without the surrounding
/// loop's state machine.
/// </summary>
public sealed class RuntimeProcessEffectRunnerTests
{
    private const string RuntimeExecutableRelativePath = "bin/winws2.exe";
    private const string ExecutableContent = "fake-winws2-binary";
    private static readonly TimeSpan DeadlineWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShortDeadline = TimeSpan.FromMilliseconds(150);

    [Fact]
    public static async Task StartProcess_Success_CrossesBoundaryAndReturnsStartResult()
    {
        FakeHost host = new()
        {
            StartHandler = (_, _) => FakeHost.DefaultStartResult(),
        };
        RuntimeProcessEffectRunner runner = new(host);
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(1),
            Kind: RuntimeEffectKind.StartProcess,
            Payload: context,
            RequestedAtUtc: DateTimeOffset.UtcNow,
            Deadline: null,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        RuntimeKernelCommand.EffectCompleted completion = await runner.RunAsync(
            intent,
            new FakeClock(),
            TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsSuccess, completion.Result.IsFailure ? completion.Result.Error.ToString() : string.Empty);
        Assert.True(completion.CrossedIrreversibleBoundary);
        Assert.NotNull(completion.StartResult);
        Assert.Equal(1, host.StartCallCount);
        Assert.Equal(0, host.StopCallCount);
    }

    [Fact]
    public static async Task StopProcess_Success_CrossesBoundary()
    {
        FakeHost host = new()
        {
            StopHandler = _ => Task.FromResult(Result.Success(Unit.Instance)),
        };
        RuntimeProcessEffectRunner runner = new(host);
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(2),
            Kind: RuntimeEffectKind.StopProcess,
            Payload: "Test stop intent",
            RequestedAtUtc: DateTimeOffset.UtcNow,
            Deadline: null,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        RuntimeKernelCommand.EffectCompleted completion = await runner.RunAsync(
            intent,
            new FakeClock(),
            TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsSuccess, completion.Result.IsFailure ? completion.Result.Error.ToString() : string.Empty);
        Assert.True(completion.CrossedIrreversibleBoundary);
        Assert.Null(completion.StartResult);
        Assert.Equal(0, host.StartCallCount);
        Assert.Equal(1, host.StopCallCount);
    }

    [Fact]
    public static async Task UnsupportedKind_ReturnsFailureWithoutBoundary()
    {
        FakeHost host = new();
        RuntimeProcessEffectRunner runner = new(host);
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(3),
            Kind: RuntimeEffectKind.RecordGuardSuccess,
            Payload: null,
            RequestedAtUtc: DateTimeOffset.UtcNow,
            Deadline: null,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        RuntimeKernelCommand.EffectCompleted completion = await runner.RunAsync(
            intent,
            new FakeClock(),
            TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsFailure);
        Assert.Equal("RuntimeEffectRunnerUnsupportedKind", completion.Result.Error.Code);
        Assert.False(completion.CrossedIrreversibleBoundary);
        Assert.Null(completion.StartResult);
        Assert.Equal(0, host.StartCallCount);
        Assert.Equal(0, host.StopCallCount);
    }

    [Fact]
    public static async Task StartProcess_MissingContext_ReturnsFailure()
    {
        FakeHost host = new();
        RuntimeProcessEffectRunner runner = new(host);
        // Intent claims StartProcess but carries a non-start payload.
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(4),
            Kind: RuntimeEffectKind.StartProcess,
            Payload: "not-a-start-context",
            RequestedAtUtc: DateTimeOffset.UtcNow,
            Deadline: null,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        RuntimeKernelCommand.EffectCompleted completion = await runner.RunAsync(
            intent,
            new FakeClock(),
            TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsFailure);
        Assert.Equal("RuntimeStartEffectMissingContext", completion.Result.Error.Code);
        Assert.False(completion.CrossedIrreversibleBoundary);
        Assert.Equal(0, host.StartCallCount);
    }

    [Fact]
    public static async Task DeadlineAlreadyElapsed_ReturnsDeadlineExceededWithTimeout()
    {
        FakeHost host = new();
        RuntimeProcessEffectRunner runner = new(host);
        FakeClock clock = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        // The deadline is one minute in the past relative to the
        // fake clock so the runner's pre-check rejects the intent
        // before any host call.
        DateTimeOffset pastDeadline = clock.GetUtcNow() - TimeSpan.FromMinutes(1);
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(5),
            Kind: RuntimeEffectKind.StartProcess,
            Payload: context,
            RequestedAtUtc: clock.GetUtcNow(),
            Deadline: pastDeadline,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        RuntimeKernelCommand.EffectCompleted completion = await runner.RunAsync(
            intent,
            clock,
            TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsFailure);
        Assert.Equal("RuntimeEffectDeadlineExceeded", completion.Result.Error.Code);
        Assert.Equal(RuntimeCancellationReason.Timeout, completion.CancellationReason);
        Assert.False(completion.CrossedIrreversibleBoundary);
        Assert.Equal(0, host.StartCallCount);
    }

    [Fact]
    public static async Task OuterCancellation_BeforeBoundary_ReturnsCancelledHostShutdown()
    {
        // The fake host awaits the supplied token and throws
        // OperationCanceledException when it fires. The outer
        // token is cancelled externally, so the linked token (no
        // deadline configured) is also cancelled and the host
        // throws before any irreversible boundary is crossed.
        using CancellationTokenSource outer = new();
        FakeHost host = new()
        {
            StartHandler = (_, token) => WaitForCancellationAsync(token),
        };
        RuntimeProcessEffectRunner runner = new(host);
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(6),
            Kind: RuntimeEffectKind.StartProcess,
            Payload: context,
            RequestedAtUtc: DateTimeOffset.UtcNow,
            Deadline: null,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        Task<RuntimeKernelCommand.EffectCompleted> runTask = runner.RunAsync(
            intent,
            new FakeClock(),
            outer.Token);

        // Give the runner a moment to dispatch the host call
        // before we trigger the cancellation.
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.Equal(1, host.StartCallCount);

        outer.Cancel();

        RuntimeKernelCommand.EffectCompleted completion = await runTask
            .WaitAsync(DeadlineWaitTimeout, TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsFailure);
        Assert.Equal("RuntimeEffectCancelled", completion.Result.Error.Code);
        Assert.Equal(RuntimeCancellationReason.HostShutdown, completion.CancellationReason);
        Assert.False(completion.CrossedIrreversibleBoundary);
    }

    [Fact]
    public static async Task DeadlineExpires_BeforeBoundary_ReturnsCancelledTimeout()
    {
        // The fake host awaits the supplied token and throws
        // OperationCanceledException when it fires. The intent
        // carries a short deadline, so the deadline CTS fires
        // before the (uncancelled) outer token does, and the
        // host throws from the linked token's cancellation.
        FakeHost host = new()
        {
            StartHandler = (_, token) => WaitForCancellationAsync(token),
        };
        RuntimeProcessEffectRunner runner = new(host);
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        // We need a real wall-clock reading so the deadline CTS
        // can arm a timer; TimeProvider.System is fine because
        // the deadline is read once at intent construction.
        DateTimeOffset startWall = DateTimeOffset.UtcNow;
        DateTimeOffset deadline = startWall + ShortDeadline;
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(7),
            Kind: RuntimeEffectKind.StartProcess,
            Payload: context,
            RequestedAtUtc: startWall,
            Deadline: deadline,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        RuntimeKernelCommand.EffectCompleted completion = await runner.RunAsync(
            intent,
            TimeProvider.System,
            CancellationToken.None)
            .WaitAsync(DeadlineWaitTimeout, TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsFailure);
        Assert.Equal("RuntimeEffectCancelled", completion.Result.Error.Code);
        Assert.Equal(RuntimeCancellationReason.Timeout, completion.CancellationReason);
        Assert.False(completion.CrossedIrreversibleBoundary);
        Assert.Equal(1, host.StartCallCount);
    }

    [Fact]
    public static async Task HostThrowsUnexpectedException_ReturnsRunnerThrew()
    {
        // The fake host raises an exception the runner does not
        // expect (anything that is not OperationCanceledException
        // is treated as a bug in the host). The runner converts
        // the exception into a typed failure so the kernel state
        // machine can keep moving.
        InvalidOperationException boom = new("simulated host failure");
        FakeHost host = new()
        {
            StartHandler = (_, _) => throw boom,
        };
        RuntimeProcessEffectRunner runner = new(host);
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);
        RuntimeEffectIntent intent = new(
            OperationId: RuntimeOperationId.New(),
            Generation: new RuntimeGeneration(8),
            Kind: RuntimeEffectKind.StartProcess,
            Payload: context,
            RequestedAtUtc: DateTimeOffset.UtcNow,
            Deadline: null,
            CancellationReason: RuntimeCancellationReason.UserRequested);

        RuntimeKernelCommand.EffectCompleted completion = await runner.RunAsync(
            intent,
            new FakeClock(),
            TestContext.Current.CancellationToken);

        Assert.True(completion.Result.IsFailure);
        Assert.Equal("RuntimeEffectRunnerThrew", completion.Result.Error.Code);
        Assert.False(completion.CrossedIrreversibleBoundary);
    }

    private static async Task<Result<RuntimeProcessHostResult>> WaitForCancellationAsync(CancellationToken token)
    {
        // Suspend until the supplied token fires, then surface
        // the cancellation so the runner can classify it. Using
        // Task.Delay with the token guarantees the awaiter
        // throws OperationCanceledException carrying the same
        // token, which is the path the runner's catch block
        // exercises.
        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);

        // The line above throws; the unreachable return keeps
        // the compiler happy if the delay is ever refactored to
        // complete normally.
        return await Task.FromCanceled<Result<RuntimeProcessHostResult>>(token).ConfigureAwait(false);
    }

    private static RuntimeProcessStartContext CreateStartContext(TemporaryDirectory assetsRoot)
    {
        string absolutePath = WriteFakeExecutable(
            assetsRoot.DirectoryPath,
            RuntimeExecutableRelativePath,
            ExecutableContent);

        string hash = ComputeSha256HexLower(absolutePath);
        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                RuntimeExecutableRelativePath,
                hash,
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        ZapretAssetVerificationSummary summary = new(new[] { RuntimeExecutableRelativePath });
        Result<VerifiedRuntimeExecutablePath> verifiedResult = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);
        if (verifiedResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to build verified executable path: {verifiedResult.Error}");
        }

        CompiledZapretPlan plan = new(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

        return new RuntimeProcessStartContext(
            plan: plan,
            manifest: manifest,
            workspaceDirectory: assetsRoot.DirectoryPath,
            runtimeExecutablePath: verifiedResult.Value);
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

        File.WriteAllBytes(fullPath, Encoding.UTF8.GetBytes(content));
        return fullPath;
    }

    private static string ComputeSha256HexLower(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        byte[] hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// In-memory <see cref="IRuntimeProcessHost"/> that defers
    /// the start / stop result to caller-supplied delegates. The
    /// default handlers return a successful start result and a
    /// successful stop result, so tests that only need the
    /// happy path can construct an empty fake and exercise the
    /// runner unchanged. <see cref="StartCallCount"/> and
    /// <see cref="StopCallCount"/> record how many times the
    /// runner invoked the host so tests can assert that the
    /// runner short-circuited (for example, when the deadline is
    /// already elapsed).
    /// </summary>
    private sealed class FakeHost : IRuntimeProcessHost
    {
        public Func<RuntimeProcessStartContext, CancellationToken, Task<Result<RuntimeProcessHostResult>>>? StartHandler { get; set; }

        public Func<CancellationToken, Task<Result<Unit>>>? StopHandler { get; set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task<Result<RuntimeProcessHostResult>> StartAsync(
            RuntimeProcessStartContext context,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            if (StartHandler is not null)
            {
                return StartHandler(context, cancellationToken);
            }

            return DefaultStartResult();
        }

        public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            if (StopHandler is not null)
            {
                return StopHandler(cancellationToken);
            }

            return Task.FromResult(Result.Success(Unit.Instance));
        }

        public static Task<Result<RuntimeProcessHostResult>> DefaultStartResult()
        {
            CompiledZapretPlan plan = new(
                generatedConfigContent: "# config\n",
                argsContent: "--new\n",
                hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

            return Task.FromResult(Result.Success(new RuntimeProcessHostResult(
                processId: 4321,
                processName: "fake-runtime",
                executablePath: "/fake/runtime.exe",
                plan: plan)));
        }
    }
}

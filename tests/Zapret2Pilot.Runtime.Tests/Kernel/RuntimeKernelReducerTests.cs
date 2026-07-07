#pragma warning disable CA1707 // Identifiers should not contain underscores

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Tests.Testing;

namespace Zapret2Pilot.Runtime.Tests.Kernel;

/// <summary>
/// Pure-function tests for <see cref="RuntimeKernelReducer"/> (milestone
/// 0.0.24, Packet 3). The reducer is deterministic and side-effect
/// free, so every scenario uses a hand-rolled
/// <see cref="FakeCrashLoopGuard"/> and the shared
/// <see cref="FakeClock"/> as the <see cref="TimeProvider"/> seed.
/// No real <c>winws2</c> process is launched and no filesystem
/// mutation occurs outside the tiny temporary workspace that backs
/// the synthetic <see cref="RuntimeProcessStartContext"/> used by
/// the <see cref="RuntimeKernelCommand.Start"/> scenarios.
/// </summary>
public sealed class RuntimeKernelReducerTests
{
    private const string RuntimeExecutableRelativePath = "bin/winws2.exe";
    private const string ExecutableContent = "fake-winws2-binary";

    [Fact]
    public static void Start_FromStopped_AllowedByGuard_ReturnsStarting_WithStartProcessEffect()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeKernelState state = CreateInitialState(clock);
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess, result.Outcome.IsFailure ? result.Outcome.Error.ToString() : string.Empty);
        Assert.Equal(RuntimeKernelStatus.Starting, result.NextState.Status);
        Assert.Single(result.Effects);
        RuntimeEffectIntent effect = result.Effects[0];
        Assert.Equal(RuntimeEffectKind.StartProcess, effect.Kind);
        Assert.Same(context, effect.Payload);
        Assert.Equal(result.NextState.Generation, effect.Generation);
        Assert.Equal(result.NextState.PendingOperationId, effect.OperationId);
        Assert.NotNull(result.NextState.PendingOperationId);
        Assert.Null(result.NextState.LastError);
        // Packet 3 additions: the reducer records the deadline
        // and the cancellation reason on the new state and the
        // emitted intent.
        Assert.Equal(result.NextState.Deadline, effect.Deadline);
        Assert.Equal(RuntimeCancellationReason.UserRequested, result.NextState.CancellationReason);
        Assert.Equal(RuntimeCancellationReason.UserRequested, effect.CancellationReason);
        Assert.NotNull(effect.Deadline);
        Assert.True(effect.Deadline!.Value > clock.GetUtcNow());
    }

    [Fact]
    public static void Start_FromStopped_BlockedByGuard_ReturnsStartBlocked_WithError()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: false,
            backoffRemaining: TimeSpan.FromSeconds(3),
            consecutiveFailures: 1));
        RuntimeKernelState state = CreateInitialState(clock);
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            guard,
            clock);

        Assert.True(result.Outcome.IsFailure);
        Assert.Equal("RuntimeStartBlockedByCrashLoopGuard", result.Outcome.Error.Code);
        Assert.Equal(RuntimeKernelStatus.StartBlocked, result.NextState.Status);
        Assert.Empty(result.Effects);
        // Packet 3: StartBlocked clears the deadline and
        // cancellation reason because no operation is in flight.
        Assert.Null(result.NextState.Deadline);
        Assert.Null(result.NextState.CancellationReason);
    }

    [Fact]
    public static void Start_FromStarting_ReturnsAlreadyRunningError()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeOperationId pendingId = RuntimeOperationId.New();
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Starting,
            PendingOperationId = pendingId,
            Owner = AutomationOwner.User,
        };
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            guard,
            clock);

        Assert.True(result.Outcome.IsFailure);
        Assert.Equal("RuntimeAlreadyRunning", result.Outcome.Error.Code);
        Assert.Equal(RuntimeKernelStatus.Starting, result.NextState.Status);
        Assert.Equal(pendingId, result.NextState.PendingOperationId);
        Assert.Empty(result.Effects);
    }

    [Fact]
    public static void Stop_FromRunning_ReturnsStopping_WithStopProcessEffect()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Running,
            Owner = AutomationOwner.User,
        };
        RuntimeOperationId stopOperationId = RuntimeOperationId.New();

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Stop(stopOperationId, "user-requested"),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess, result.Outcome.IsFailure ? result.Outcome.Error.ToString() : string.Empty);
        Assert.Equal(RuntimeKernelStatus.Stopping, result.NextState.Status);
        Assert.Single(result.Effects);
        RuntimeEffectIntent effect = result.Effects[0];
        Assert.Equal(RuntimeEffectKind.StopProcess, effect.Kind);
        Assert.Equal(result.NextState.PendingOperationId, effect.OperationId);
        Assert.Equal(result.NextState.Generation, effect.Generation);
        // Packet 3: Stop populates the deadline and the
        // cancellation reason (defaults to UserRequested when
        // the caller only supplied a string).
        Assert.Equal(RuntimeCancellationReason.UserRequested, result.NextState.CancellationReason);
        Assert.Equal(RuntimeCancellationReason.UserRequested, effect.CancellationReason);
        Assert.NotNull(effect.Deadline);
    }

    [Fact]
    public static void Stop_FromStopped_IsIdempotent()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeKernelState state = CreateInitialState(clock);

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Stop(RuntimeOperationId.New(), "noop"),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Stopped, result.NextState.Status);
        Assert.Empty(result.Effects);
    }

    [Fact]
    public static void Stop_WithCancellationReason_PropagatesToStateAndIntent()
    {
        // The supervisor uses the Stop command with a
        // default cancellation reason; the reducer must carry
        // the typed reason through to the state and the
        // emitted intent so the runner can classify the
        // cancellation.
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Running,
            Owner = AutomationOwner.User,
        };
        RuntimeOperationId stopOperationId = RuntimeOperationId.New();

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Stop(
                stopOperationId,
                "host shutdown",
                RuntimeCancellationReason.HostShutdown),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Stopping, result.NextState.Status);
        Assert.Single(result.Effects);
        Assert.Equal(RuntimeCancellationReason.HostShutdown, result.NextState.CancellationReason);
        Assert.Equal(RuntimeCancellationReason.HostShutdown, result.Effects[0].CancellationReason);
    }

    [Fact]
    public static void Observation_Healthy_FromStarting_TransitionsToRunning()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeOperationId observationId = RuntimeOperationId.New();
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Starting,
            Owner = AutomationOwner.User,
        };
        RuntimeHealthSnapshot snapshot = new(
            RuntimeHealthState.Healthy,
            processId: 1234,
            observedAtUtc: clock.GetUtcNow());

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Observation(observationId, snapshot),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Running, result.NextState.Status);
        Assert.Single(result.Effects);
        Assert.Equal(RuntimeEffectKind.RecordGuardSuccess, result.Effects[0].Kind);
        Assert.Null(result.NextState.LastError);
        // Packet 3: Running clears the deadline and cancellation
        // reason because the operation is now stable.
        Assert.Null(result.NextState.Deadline);
        Assert.Null(result.NextState.CancellationReason);
    }

    [Fact]
    public static void Observation_Exited_FromRunning_TransitionsToStopping_AndRecordsFailure()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeOperationId observationId = RuntimeOperationId.New();
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Running,
            Owner = AutomationOwner.User,
        };
        RuntimeHealthSnapshot snapshot = new(
            RuntimeHealthState.Exited,
            processId: 1234,
            observedAtUtc: clock.GetUtcNow());

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Observation(observationId, snapshot),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Stopping, result.NextState.Status);
        Assert.Equal(2, result.Effects.Count);
        Assert.Contains(result.Effects, e => e.Kind == RuntimeEffectKind.RecordGuardFailure);
        Assert.Contains(result.Effects, e => e.Kind == RuntimeEffectKind.StopProcess);
        Assert.NotNull(result.NextState.LastError);
        Assert.Equal("RuntimeProcessExitedUnexpectedly", result.NextState.LastError!.Code);
        // Packet 3: the auto-stop uses SafetyAbort as the
        // cancellation reason and records a fresh deadline.
        Assert.Equal(RuntimeCancellationReason.SafetyAbort, result.NextState.CancellationReason);
        Assert.Equal(RuntimeCancellationReason.SafetyAbort,
            result.Effects.First(e => e.Kind == RuntimeEffectKind.StopProcess).CancellationReason);
        Assert.NotNull(result.NextState.Deadline);
    }

    [Fact]
    public static void EffectCompleted_MatchingOperationAndGeneration_FromStartingToRunning()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeOperationId pendingId = RuntimeOperationId.New();
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Starting,
            Owner = AutomationOwner.User,
            PendingOperationId = pendingId,
        };

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.EffectCompleted(
                pendingId,
                state.Generation,
                Result.Success(Unit.Instance)),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Running, result.NextState.Status);
        Assert.Null(result.NextState.PendingOperationId);
        // Packet 3: successful completion clears the deadline
        // and cancellation reason.
        Assert.Null(result.NextState.Deadline);
        Assert.Null(result.NextState.CancellationReason);
    }

    [Fact]
    public static void EffectCompleted_StaleGeneration_IsIgnored_AndEmitsEvent()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeOperationId pendingId = RuntimeOperationId.New();
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Starting,
            Owner = AutomationOwner.User,
            PendingOperationId = pendingId,
        };
        RuntimeGeneration stale = new(state.Generation.Value - 1);

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.EffectCompleted(
                pendingId,
                stale,
                Result.Success(Unit.Instance)),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Starting, result.NextState.Status);
        Assert.Equal(pendingId, result.NextState.PendingOperationId);
        Assert.Empty(result.Effects);
        // Packet 3: a stale completion emits a single
        // IgnoredStaleCompletion event without mutating state.
        IgnoredStaleCompletion? ignored = result.Events.OfType<IgnoredStaleCompletion>().SingleOrDefault();
        Assert.NotNull(ignored);
        Assert.Equal("StaleGeneration", ignored!.Reason);
        Assert.Equal(pendingId, ignored.OperationId);
    }

    [Fact]
    public static void EffectCompleted_MismatchedOperationId_IsIgnored_AndEmitsEvent()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeOperationId pendingId = RuntimeOperationId.New();
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Starting,
            Owner = AutomationOwner.User,
            PendingOperationId = pendingId,
        };
        RuntimeOperationId stranger = RuntimeOperationId.New();

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.EffectCompleted(
                stranger,
                state.Generation,
                Result.Success(Unit.Instance)),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Starting, result.NextState.Status);
        Assert.Equal(pendingId, result.NextState.PendingOperationId);
        Assert.Empty(result.Effects);
        IgnoredStaleCompletion? ignored = result.Events.OfType<IgnoredStaleCompletion>().SingleOrDefault();
        Assert.NotNull(ignored);
        Assert.Equal("MismatchedOperationId", ignored!.Reason);
    }

    [Fact]
    public static void EffectCompleted_CancellationFailure_DoesNotMutateCancellationReason()
    {
        // A failed completion with a cancellation reason still
        // surfaces the error and transitions the state to
        // Stopped, but the state's CancellationReason is
        // cleared because the operation is now terminal.
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeOperationId pendingId = RuntimeOperationId.New();
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Starting,
            Owner = AutomationOwner.User,
            PendingOperationId = pendingId,
            CancellationReason = RuntimeCancellationReason.HostShutdown,
            Deadline = clock.GetUtcNow() + TimeSpan.FromSeconds(5),
        };
        ErrorInfo syntheticError = new(
            code: "SyntheticEffectFailure",
            message: "Cancellation crossed the start boundary.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.EffectCompleted(
                pendingId,
                state.Generation,
                Result.Failure<Unit>(syntheticError),
                StartResult: null,
                CancellationReason: RuntimeCancellationReason.HostShutdown,
                CrossedIrreversibleBoundary: true),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Stopped, result.NextState.Status);
        Assert.NotNull(result.NextState.LastError);
        Assert.Equal("SyntheticEffectFailure", result.NextState.LastError!.Code);
        Assert.Null(result.NextState.Deadline);
        Assert.Null(result.NextState.CancellationReason);
    }

    [Fact]
    public static void Dispose_FromRunning_TransitionsToStopping()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeKernelState state = CreateInitialState(clock) with
        {
            Status = RuntimeKernelStatus.Running,
            Owner = AutomationOwner.User,
        };

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Dispose(),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Stopping, result.NextState.Status);
        Assert.Single(result.Effects);
        Assert.Equal(RuntimeEffectKind.StopProcess, result.Effects[0].Kind);
        // Packet 3: Dispose uses HostShutdown as the
        // cancellation reason and records a fresh deadline.
        Assert.Equal(RuntimeCancellationReason.HostShutdown, result.NextState.CancellationReason);
        Assert.Equal(RuntimeCancellationReason.HostShutdown, result.Effects[0].CancellationReason);
        Assert.NotNull(result.NextState.Deadline);
    }

    [Fact]
    public static void Dispose_FromStopped_IsIdempotent()
    {
        FakeClock clock = new();
        FakeCrashLoopGuard guard = new(new CrashLoopGuardResult(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0));
        RuntimeKernelState state = CreateInitialState(clock);

        RuntimeReducerResult result = RuntimeKernelReducer.Reduce(
            state,
            new RuntimeKernelCommand.Dispose(),
            guard,
            clock);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(RuntimeKernelStatus.Stopped, result.NextState.Status);
        Assert.Empty(result.Effects);
        // Packet 3: Dispose on a stopped kernel clears the
        // deadline and cancellation reason defensively.
        Assert.Null(result.NextState.Deadline);
        Assert.Null(result.NextState.CancellationReason);
    }

    private static RuntimeKernelState CreateInitialState(FakeClock clock)
    {
        return new RuntimeKernelState(
            RuntimeKernelStatus.Stopped,
            AutomationOwner.None,
            RuntimeGeneration.Initial,
            pendingOperationId: null,
            lastStartResult: null,
            guardResult: null,
            lastError: null,
            clock.GetUtcNow());
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
    /// Hand-rolled <see cref="ICrashLoopGuard"/> that returns a
    /// caller-supplied <see cref="CrashLoopGuardResult"/> on every
    /// <see cref="Check"/> call. Records are silently swallowed
    /// because the reducer never queries the counters.
    /// </summary>
    private sealed class FakeCrashLoopGuard : ICrashLoopGuard
    {
        private readonly CrashLoopGuardResult result;

        public FakeCrashLoopGuard(CrashLoopGuardResult result)
        {
            this.result = result;
        }

        public CrashLoopGuardResult Check() => result;

        public void RecordFailure()
        {
        }

        public void RecordSuccess()
        {
        }

        public void Reset()
        {
        }
    }
}

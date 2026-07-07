using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.App.Lifecycle;

namespace Zapret2Pilot.App.Tests.Lifecycle;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// 0.0.28 Packet 3 (P0-9) — real IStartupStep-based lifecycle
/// coordinator. The stub coordinator advanced phases via
/// <c>Task.Yield()</c> without doing any work; the real
/// implementation waits for <see cref="IZ2PApplicationLifecycleCoordinator.SignalShellVisible"/>
/// and runs a real <see cref="IStartupStep"/> pipeline.
/// </summary>
public sealed class Z2PApplicationLifecycleCoordinatorTests
{
    [Fact]
    public static async Task SignalShellVisible_RunsStepsAndReachesReady()
    {
        ConfigurableStartupStep[] steps =
        [
            new("StorageRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("DeploymentVerification", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("OwnershipRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("CompatibilityPreflight", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
        ];

        await using CoordinatorHarness harness = CoordinatorHarness.Create(steps);

        Assert.Equal(ApplicationLifecyclePhase.ProcessBootstrap, harness.Coordinator.CurrentPhase);

        harness.Coordinator.SignalShellVisible();

        await harness.WaitForPhaseAsync(ApplicationLifecyclePhase.Ready, TimeSpan.FromSeconds(2));

        Assert.Equal(ApplicationLifecyclePhase.Ready, harness.Coordinator.CurrentPhase);
        Assert.True(harness.Coordinator.IsReady);

        foreach (ConfigurableStartupStep step in steps)
        {
            Assert.Equal(1, step.ExecuteCallCount);
        }
    }

    [Fact]
    public static async Task CriticalStepFailure_PublishesBlocked()
    {
        ConfigurableStartupStep[] steps =
        [
            new("StorageRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("DeploymentVerification", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Failed, "DeploymentFlavorNotYetSupported")),
            new("OwnershipRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("CompatibilityPreflight", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
        ];

        await using CoordinatorHarness harness = CoordinatorHarness.Create(steps);

        harness.Coordinator.SignalShellVisible();

        await harness.WaitForPhaseAsync(ApplicationLifecyclePhase.Blocked, TimeSpan.FromSeconds(2));

        Assert.Equal(ApplicationLifecyclePhase.Blocked, harness.Coordinator.CurrentPhase);
        Assert.False(harness.Coordinator.IsReady);

        // StorageRecovery and DeploymentVerification ran; the remaining
        // critical steps must NOT have been executed because the pipeline
        // was short-circuited.
        Assert.Equal(1, steps[0].ExecuteCallCount);
        Assert.Equal(1, steps[1].ExecuteCallCount);
        Assert.Equal(0, steps[2].ExecuteCallCount);
        Assert.Equal(0, steps[3].ExecuteCallCount);
    }

    [Fact]
    public static async Task DegradableStepFailure_PublishesDegraded()
    {
        ConfigurableStartupStep[] steps =
        [
            new("StorageRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("DeploymentVerification", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("OwnershipRecovery", StartupStepCriticality.Degradable, new StartupStepResult(StartupStepStatus.Failed, "OwnershipReconcileUnavailable")),
            new("CompatibilityPreflight", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
        ];

        await using CoordinatorHarness harness = CoordinatorHarness.Create(steps);

        harness.Coordinator.SignalShellVisible();

        await harness.WaitForPhaseAsync(ApplicationLifecyclePhase.Degraded, TimeSpan.FromSeconds(2));

        Assert.Equal(ApplicationLifecyclePhase.Degraded, harness.Coordinator.CurrentPhase);
        Assert.False(harness.Coordinator.IsReady);

        // All four steps must have run; degradable failure must NOT
        // short-circuit the pipeline.
        foreach (ConfigurableStartupStep step in steps)
        {
            Assert.Equal(1, step.ExecuteCallCount);
        }
    }

    [Fact]
    public static async Task SignalShellVisible_IsIdempotent()
    {
        ConfigurableStartupStep[] steps =
        [
            new("StorageRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("DeploymentVerification", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("OwnershipRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new("CompatibilityPreflight", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
        ];

        await using CoordinatorHarness harness = CoordinatorHarness.Create(steps);

        harness.Coordinator.SignalShellVisible();
        harness.Coordinator.SignalShellVisible();
        harness.Coordinator.SignalShellVisible();

        await harness.WaitForPhaseAsync(ApplicationLifecyclePhase.Ready, TimeSpan.FromSeconds(2));

        Assert.Equal(ApplicationLifecyclePhase.Ready, harness.Coordinator.CurrentPhase);

        // Each step must have been executed exactly once even though
        // SignalShellVisible was called three times.
        foreach (ConfigurableStartupStep step in steps)
        {
            Assert.Equal(1, step.ExecuteCallCount);
        }
    }

    [Fact]
    public static async Task StopAsync_CancelsPipelineAndReachesStopped()
    {
        // A step that blocks on the supplied cancellation token
        // until StopAsync cancels it. After cancellation the
        // coordinator must transition to Stopping/Stopped without
        // leaving the pipeline running.
        BlockingStartupStep blocker = new("StorageRecovery");

        IStartupStep[] steps =
        [
            blocker,
            new ConfigurableStartupStep("DeploymentVerification", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new ConfigurableStartupStep("OwnershipRecovery", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
            new ConfigurableStartupStep("CompatibilityPreflight", StartupStepCriticality.Critical, new StartupStepResult(StartupStepStatus.Succeeded)),
        ];

        await using CoordinatorHarness harness = CoordinatorHarness.Create(steps);

        Z2PApplicationLifecycleCoordinator concrete =
            (Z2PApplicationLifecycleCoordinator)harness.Coordinator;

        harness.Coordinator.SignalShellVisible();

        // Wait until the blocking step is actually executing.
        await blocker.WaitUntilEnteredAsync(TimeSpan.FromSeconds(2));

        CancellationToken testContext = TestContext.Current.CancellationToken;

        // Run the host-stop on a background task so the test can
        // observe the pipeline cancellation. The test-context
        // cancellation token is used both as the Task.Run
        // cancellation and as the StopAsync deadline token so the
        // test responds to xunit's per-test timeout.
        Task stopTask = Task.Run(
            () => concrete.StopAsync(testContext),
            testContext);

        // Bound the assertion so a regression in the pipeline
        // cancellation does not hang the test forever. The 2s
        // deadline is well below xunit's default per-test timeout.
        await stopTask.WaitAsync(TimeSpan.FromSeconds(2), testContext);

        Assert.Equal(ApplicationLifecyclePhase.Stopped, harness.Coordinator.CurrentPhase);
    }
}

#pragma warning restore CA1707 // Identifiers should not contain underscores

/// <summary>
/// Lightweight harness that owns a service provider, a coordinator
/// instance, and an <see cref="IDisposable"/> contract for the
/// tests. Centralises the boilerplate so each test only describes
/// the steps and the expected phase.
/// </summary>
internal sealed class CoordinatorHarness : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    private CoordinatorHarness(IZ2PApplicationLifecycleCoordinator coordinator, ServiceProvider provider)
    {
        Coordinator = coordinator;
        this.provider = provider;
    }

    public IZ2PApplicationLifecycleCoordinator Coordinator { get; }

    public static CoordinatorHarness Create(IEnumerable<IStartupStep> steps)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ILogger<Z2PApplicationLifecycleCoordinator>>(
            static sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<Z2PApplicationLifecycleCoordinator>());

        foreach (IStartupStep step in steps)
        {
            services.AddSingleton(step);
        }

        services.AddSingleton<Z2PApplicationLifecycleCoordinator>();
        services.AddSingleton<IZ2PApplicationLifecycleCoordinator>(
            static sp => sp.GetRequiredService<Z2PApplicationLifecycleCoordinator>());

        ServiceProvider provider = services.BuildServiceProvider();
        IZ2PApplicationLifecycleCoordinator coordinator = provider
            .GetRequiredService<IZ2PApplicationLifecycleCoordinator>();

        return new CoordinatorHarness(coordinator, provider);
    }

    public async Task WaitForPhaseAsync(ApplicationLifecyclePhase target, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Coordinator.CurrentPhase == target)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"Coordinator did not reach phase {target} within {timeout}. Current phase: {Coordinator.CurrentPhase}.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Coordinator is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (Coordinator is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await provider.DisposeAsync();
    }
}

/// <summary>
/// Fake <see cref="IStartupStep"/> that returns a pre-configured
/// <see cref="StartupStepResult"/> and counts how many times its
/// <c>ExecuteAsync</c> was invoked. Used by the success / failure
/// tests to drive the coordinator's pipeline deterministically.
/// </summary>
internal sealed class ConfigurableStartupStep : IStartupStep
{
    private readonly StartupStepResult result;

    public ConfigurableStartupStep(string name, StartupStepCriticality criticality, StartupStepResult result)
    {
        Name = name;
        Criticality = criticality;
        this.result = result;
    }

    public string Name { get; }

    public StartupStepCriticality Criticality { get; }

    public int ExecuteCallCount { get; private set; }

    public Task<StartupStepResult> ExecuteAsync(StartupStepContext context, CancellationToken cancellationToken)
    {
        _ = context;
        ExecuteCallCount++;
        return Task.FromResult(result);
    }
}

/// <summary>
/// Fake <see cref="IStartupStep"/> that blocks on the supplied
/// cancellation token until the caller (or the coordinator's
/// <c>StopAsync</c>) cancels it. Used to assert that
/// <c>StopAsync</c> correctly cancels an in-flight pipeline.
/// </summary>
internal sealed class BlockingStartupStep : IStartupStep
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BlockingStartupStep(string name)
    {
        Name = name;
    }

    public string Name { get; } = "StorageRecovery";

    public StartupStepCriticality Criticality => StartupStepCriticality.Critical;

    public Task WaitUntilEnteredAsync(TimeSpan timeout)
    {
        return entered.Task.WaitAsync(timeout);
    }

    public async Task<StartupStepResult> ExecuteAsync(StartupStepContext context, CancellationToken cancellationToken)
    {
        _ = context;
        entered.TrySetResult();

        // Honour cancellation. The blocking semantics are sufficient
        // to prove that StopAsync's cancellation propagates into the
        // pipeline task.
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

        // Unreachable when cancellation is honoured.
        return new StartupStepResult(StartupStepStatus.Succeeded);
    }
}

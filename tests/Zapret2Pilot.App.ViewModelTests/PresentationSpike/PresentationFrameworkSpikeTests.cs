using System;
using System.Collections.Generic;
using Zapret2Pilot.App.Lifecycle;
using Zapret2Pilot.App.Threading;
using Zapret2Pilot.App.ViewModelTests.Fakes;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.ViewModelTests.PresentationSpike;

public sealed class PresentationFrameworkSpikeTests
{
    [Fact]
    public void ToolkitCandidate_LifecycleReadinessGatesDashboardCommand()
    {
        using var coordinator = new FakeLifecycleCoordinator();
        using var supervisor = new FakeRuntimeSupervisor();
        var scheduler = new RecordingUiScheduler();
        coordinator.SetPhase(ApplicationLifecyclePhase.WaitingForShell);

        using var candidate = new ToolkitDashboardFlowCandidate(coordinator, supervisor, scheduler);
        candidate.Activate();
        scheduler.Drain();

        Assert.False(candidate.CheckNowCommand.CanExecute(null));

        coordinator.SetPhase(ApplicationLifecyclePhase.Ready);
        scheduler.Drain();

        Assert.True(candidate.CheckNowCommand.CanExecute(null));
        candidate.CheckNowCommand.Execute(null);
        Assert.Equal("Демо: проверка сервисов использует mock-данные.", candidate.LastAction);
    }

    [Fact]
    public void ToolkitCandidate_RuntimeStateIsUiScheduledAndActivationOwnsSubscriptionLifetime()
    {
        using var coordinator = new FakeLifecycleCoordinator();
        using var supervisor = new FakeRuntimeSupervisor();
        var scheduler = new RecordingUiScheduler();
        using var candidate = new ToolkitDashboardFlowCandidate(coordinator, supervisor, scheduler);
        candidate.Activate();
        scheduler.Drain();

        Assert.Equal("Обход остановлен.", candidate.LastAction);

        supervisor.Publish(CreateState(RuntimeSupervisorStatus.Running));
        Assert.Equal("Обход остановлен.", candidate.LastAction);

        scheduler.Drain();
        Assert.Equal("Обход активен.", candidate.LastAction);

        candidate.Deactivate();
        supervisor.Publish(CreateState(RuntimeSupervisorStatus.Stopped));
        scheduler.Drain();

        Assert.Equal("Обход активен.", candidate.LastAction);
    }

    private static RuntimeSupervisorState CreateState(RuntimeSupervisorStatus status) => new(
        status: status,
        lastStartResult: null,
        guardResult: null,
        lastError: null,
        timestamp: DateTimeOffset.UtcNow);

    private sealed class RecordingUiScheduler : IUiScheduler
    {
        private readonly Queue<Action> pending = new();

        public void Schedule(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            pending.Enqueue(action);
        }

        public void Drain()
        {
            while (pending.Count > 0)
            {
                pending.Dequeue()();
            }
        }
    }
}

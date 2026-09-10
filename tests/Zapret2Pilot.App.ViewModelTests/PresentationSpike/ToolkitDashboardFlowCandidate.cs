using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zapret2Pilot.App.Lifecycle;
using Zapret2Pilot.App.Threading;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.ViewModelTests.PresentationSpike;

/// <summary>
/// Test-only CommunityToolkit.Mvvm candidate for the representative Dashboard
/// flow used by #15. It deliberately consumes the same lifecycle, runtime-state
/// and UI-scheduler boundaries as the production ReactiveUI view model while
/// remaining outside every production project.
/// </summary>
internal sealed partial class ToolkitDashboardFlowCandidate : ObservableObject, IDisposable
{
    private readonly IZ2PApplicationLifecycleCoordinator coordinator;
    private readonly IRuntimeSupervisor supervisor;
    private readonly IUiScheduler uiScheduler;
    private CompositeDisposable? activation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    private bool isReady;

    [ObservableProperty]
    private string lastAction = "Демо-режим: runtime ещё не подключён.";

    public ToolkitDashboardFlowCandidate(
        IZ2PApplicationLifecycleCoordinator coordinator,
        IRuntimeSupervisor supervisor,
        IUiScheduler uiScheduler)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(uiScheduler);

        this.coordinator = coordinator;
        this.supervisor = supervisor;
        this.uiScheduler = uiScheduler;
        isReady = coordinator.IsReady;
    }

    public void Activate()
    {
        if (activation is not null)
        {
            return;
        }

        var subscriptions = new CompositeDisposable();
        subscriptions.Add(coordinator.PhaseChanged.Subscribe(
            phase => uiScheduler.Schedule(() => IsReady = phase == ApplicationLifecyclePhase.Ready)));
        subscriptions.Add(supervisor.StateChanged.Subscribe(
            state => uiScheduler.Schedule(() => ApplyRuntimeState(state))));
        activation = subscriptions;
    }

    public void Deactivate()
    {
        activation?.Dispose();
        activation = null;
    }

    public void Dispose() => Deactivate();

    private bool CanCheckNow() => IsReady;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private void CheckNow()
    {
        LastAction = "Демо: проверка сервисов использует mock-данные.";
    }

    private void ApplyRuntimeState(RuntimeSupervisorState state)
    {
        LastAction = state.Status switch
        {
            RuntimeSupervisorStatus.Running => "Обход активен.",
            RuntimeSupervisorStatus.Stopped => "Обход остановлен.",
            RuntimeSupervisorStatus.Starting => "Запуск обхода...",
            RuntimeSupervisorStatus.Stopping => "Остановка обхода...",
            RuntimeSupervisorStatus.StartBlocked => "Запуск заблокирован.",
            _ => LastAction,
        };
    }
}

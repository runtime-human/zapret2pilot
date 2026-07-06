using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent; // Required for DisposeWith extension (System.Reactive 6.1.0).
using System.Reactive.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Zapret2Pilot.App.Diagnostics;
using Zapret2Pilot.App.Lifecycle;
using Zapret2Pilot.App.Navigation;
using Zapret2Pilot.App.Threading;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.Shell;

public sealed class MainWindowViewModel : ReactiveObject, IActivatableViewModel
{
    private readonly NavigationRouter navigationRouter;
    private readonly IUiScheduler uiScheduler;
    private NavigationPageViewModel currentPage;
    private string lastAction = "Демо-режим: runtime ещё не подключён.";

    /// <summary>
    /// Full constructor used by the Generic Host composition root.
    /// All reactive subscriptions (supervisor state and command
    /// <c>ThrownExceptions</c>) are wired inside
    /// <see cref="IActivatableViewModel.WhenActivated"/> and
    /// disposed with a <see cref="CompositeDisposable"/> when the
    /// view deactivates, so no observer leaks past the view's
    /// lifetime. <c>RxApp.DefaultExceptionHandler</c> is configured
    /// separately by the composition root
    /// (<c>Program.BuildAvaloniaApp</c>) to route ReactiveUI's
    /// global observer to <see cref="IExceptionPolicy"/>; this view
    /// model only forwards <c>ThrownExceptions</c> for the runtime
    /// commands it owns.
    /// </summary>
    /// <param name="navigationRouter">Navigation router shared with
    /// the rest of the shell.</param>
    /// <param name="uiScheduler">UI scheduler that marshals
    /// supervisor-state updates onto the UI thread.</param>
    /// <param name="supervisor">Optional runtime supervisor. When
    /// <c>null</c>, the view model falls back to the
    /// design-time defaults and never subscribes.</param>
    /// <param name="logger">Optional logger that receives
    /// structured warnings for the
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/>
    /// transition.</param>
    /// <param name="coordinator">Optional application lifecycle
    /// coordinator. When supplied, runtime commands stay disabled
    /// until the coordinator reaches
    /// <see cref="ApplicationLifecyclePhase.Ready"/>. When
    /// <c>null</c>, runtime commands are wired with a permanently
    /// disabled <c>canExecute</c> to enforce the same invariant at
    /// design time.</param>
    /// <param name="exceptionPolicy">Optional exception policy that
    /// receives the per-command <c>ThrownExceptions</c>. When
    /// <c>null</c>, exceptions are still observed (so they are
    /// never swallowed) but no policy is invoked. In production
    /// the policy is the same singleton wired into
    /// <c>RxApp.DefaultExceptionHandler</c> by
    /// <c>Program.BuildAvaloniaApp</c>.</param>
    public MainWindowViewModel(
        NavigationRouter navigationRouter,
        IUiScheduler uiScheduler,
        IRuntimeSupervisor? supervisor,
        ILogger<MainWindowViewModel>? logger,
        IZ2PApplicationLifecycleCoordinator? coordinator = null,
        IExceptionPolicy? exceptionPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(navigationRouter);
        ArgumentNullException.ThrowIfNull(uiScheduler);

        this.navigationRouter = navigationRouter;
        this.uiScheduler = uiScheduler;
        currentPage = navigationRouter.CurrentPage;

        IObservable<bool> isReadyObservable = coordinator is not null
            ? coordinator.PhaseChanged.Select(p => p == ApplicationLifecyclePhase.Ready).StartWith(coordinator.IsReady)
            : Observable.Return(false);

        SidebarItems =
        [
            CreateNavigationItem(RouteId.Dashboard, "Главная"),
            CreateNavigationItem(RouteId.Profiles, "Профили"),
            CreateNavigationItem(RouteId.Settings, "Настройки"),
        ];

        UpdateSidebarSelection();

        KeyServices =
        [
            new DashboardServiceStatusViewModel("YouTube", "Доступен", "124 мс"),
            new DashboardServiceStatusViewModel("Discord", "Доступен", "146 мс"),
            new DashboardServiceStatusViewModel("Telegram", "Доступен", "98 мс"),
        ];

        RecentEvents =
        [
            new RecentEventViewModel("12:43:15", "Проверка сервисов завершена — все доступны", "Успех"),
            new RecentEventViewModel("12:42:08", "Профиль “Сбалансированный” применён", "Инфо"),
            new RecentEventViewModel("12:41:22", "DNS проверка успешна", "Успех"),
            new RecentEventViewModel("12:40:55", "Резервный сценарий проверен — готов", "Инфо"),
            new RecentEventViewModel("12:40:12", "Обход запущен", "Успех"),
        ];

        StopCommand = ReactiveCommand.Create(
            () =>
            {
                LastAction = "Демо: остановка runtime будет подключена позже.";
            },
            canExecute: isReadyObservable);

        CheckNowCommand = ReactiveCommand.Create(
            () =>
            {
                LastAction = "Демо: проверка сервисов использует mock-данные.";
            },
            canExecute: isReadyObservable);

        OpenDiagnosticsCommand = ReactiveCommand.Create(
            () =>
            {
                LastAction = "Демо: экран диагностики будет добавлен позже.";
            },
            canExecute: isReadyObservable);

        PinProfileCommand = ReactiveCommand.Create(
            () =>
            {
                LastAction = "Демо: закрепление профиля будет добавлено после модели профилей.";
            },
            canExecute: isReadyObservable);

        OpenProfileDetailsCommand = ReactiveCommand.Create(
            () =>
            {
                LastAction = "Демо: детали профиля будут добавлены после ProfileDefinition.";
            },
            canExecute: isReadyObservable);

        this.WhenActivated(disposables =>
        {
            if (supervisor is not null)
            {
                supervisor.StateChanged
                    .Subscribe(state => uiScheduler.Schedule(() => HandleSupervisorState(state, logger)))
                    .DisposeWith(disposables);
            }

            ObserveCommandExceptions(StopCommand, exceptionPolicy)
                .DisposeWith(disposables);
            ObserveCommandExceptions(CheckNowCommand, exceptionPolicy)
                .DisposeWith(disposables);
            ObserveCommandExceptions(OpenDiagnosticsCommand, exceptionPolicy)
                .DisposeWith(disposables);
            ObserveCommandExceptions(PinProfileCommand, exceptionPolicy)
                .DisposeWith(disposables);
            ObserveCommandExceptions(OpenProfileDetailsCommand, exceptionPolicy)
                .DisposeWith(disposables);
        });
    }

    /// <inheritdoc />
    public ViewModelActivator Activator { get; } = new();

    private static IDisposable ObserveCommandExceptions(
        ReactiveCommand<Unit, Unit> command,
        IExceptionPolicy? policy)
    {
        // The Subscribe ensures the exception is observed and therefore
        // not flagged as unhandled. The policy then classifies and
        // logs it through the shared ExceptionContext.ReactiveUI
        // channel so it shows up alongside the global observer.
        return command.ThrownExceptions.Subscribe(ex => policy?.Handle(ex, ExceptionContext.ReactiveUI));
    }

    private void HandleSupervisorState(
        RuntimeSupervisorState state,
        ILogger<MainWindowViewModel>? logger)
    {
        switch (state.Status)
        {
            case RuntimeSupervisorStatus.StartBlocked:
                if (state.GuardResult?.BackoffRemaining is null)
                {
                    LastAction = "Запуск заблокирован: превышено число попыток. Перезапустите приложение.";
                }
                else
                {
                    TimeSpan remaining = state.GuardResult.BackoffRemaining.Value;
                    LastAction = $"Запуск отложен: слишком много падений. Повтор через {remaining.TotalSeconds:F0} с.";
                }

                // CA1848: structured warning is only used in the rare
                // StartBlocked transition; LoggerMessage source generator
                // migration is tracked separately, matching the Runtime
                // project's existing policy.
#pragma warning disable CA1848
                logger?.LogWarning(
                    "RuntimeSupervisor: start blocked. Code={Code} ConsecutiveFailures={ConsecutiveFailures} BackoffSeconds={BackoffSeconds}",
                    state.LastError?.Code,
                    state.GuardResult?.ConsecutiveFailures ?? 0,
                    state.GuardResult?.BackoffRemaining?.TotalSeconds ?? -1);
#pragma warning restore CA1848
                break;

            case RuntimeSupervisorStatus.Running:
                LastAction = "Обход активен.";
                break;

            case RuntimeSupervisorStatus.Stopped:
                LastAction = "Обход остановлен.";
                break;

            case RuntimeSupervisorStatus.Starting:
                LastAction = "Запуск обхода...";
                break;

            case RuntimeSupervisorStatus.Stopping:
                LastAction = "Остановка обхода...";
                break;
        }
    }

    public string AppName { get; } = "Zapret2Pilot";

    public string AppVersion { get; } = "v" + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    public string WindowTitle { get; } = "Zapret2Pilot";

    public string PageTitle => CurrentPage.Title;

    public string StatusTitle { get; } = "Обход активен";

    public string StatusSubtitle { get; } = "Текущий профиль работает стабильно.";

    public string StatusDescription { get; } = "Ключевые проверки пройдены.";

    public string UptimeText { get; } = "2 ч 47 мин";

    public string WorkMode { get; } = "Автопилот";

    public string CurrentProfile { get; } = "Сбалансированный";

    public string CurrentProfileBadge { get; } = "Сбалансированный    Рекомендуется";

    public string LastCheckText { get; } = "12:43";

    public string WorkModeDescription { get; } =
        "Z2P автоматически выбирает профиль, проверяет сеть и использует резервный сценарий при проблемах.";

    public string LastModeAction { get; } =
        "Последнее действие: проверил сервисы — профиль не менялся.";

    public string CurrentProfileDescription { get; } =
        "Оптимальный баланс скорости, стабильности и совместимости для большинства сетей. Выбран автоматически на основе диагностики сети и доступности сервисов.";

    public string ServicesUpdatedText { get; } = "Обновлено: 12:43";

    public NavigationPageViewModel CurrentPage
    {
        get => currentPage;
        private set
        {
            if (ReferenceEquals(currentPage, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref currentPage, value);
            this.RaisePropertyChanged(nameof(PageTitle));
        }
    }

    public IReadOnlyList<NavigationItemViewModel> SidebarItems { get; }

    public IReadOnlyList<DashboardServiceStatusViewModel> KeyServices { get; }

    public IReadOnlyList<RecentEventViewModel> RecentEvents { get; }

    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    public ReactiveCommand<Unit, Unit> CheckNowCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenDiagnosticsCommand { get; }

    public ReactiveCommand<Unit, Unit> PinProfileCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenProfileDetailsCommand { get; }

    public string LastAction
    {
        get => lastAction;
        private set => this.RaiseAndSetIfChanged(ref lastAction, value);
    }

    public bool NavigateTo(RouteId routeId)
    {
        ArgumentNullException.ThrowIfNull(routeId);

        bool navigated = false;

        uiScheduler.Schedule(() =>
        {
            navigated = navigationRouter.NavigateTo(routeId);

            if (!navigated)
            {
                LastAction = $"Демо: неизвестный маршрут '{routeId.Value}' не открыт.";

                return;
            }

            CurrentPage = navigationRouter.CurrentPage;
            UpdateSidebarSelection();
            LastAction = $"Демо: открыт раздел “{CurrentPage.Title}”.";
        });

        return navigated;
    }

    private NavigationItemViewModel CreateNavigationItem(RouteId routeId, string title)
    {
        return new NavigationItemViewModel(
            routeId,
            title,
            routeId.Equals(CurrentPage.RouteId),
            ReactiveCommand.Create(() =>
            {
                _ = NavigateTo(routeId);
            }));
    }

    private void UpdateSidebarSelection()
    {
        foreach (NavigationItemViewModel item in SidebarItems)
        {
            item.SetSelected(item.RouteId.Equals(CurrentPage.RouteId));
        }
    }
}

public sealed record DashboardServiceStatusViewModel(
    string Name,
    string Status,
    string Latency);

public sealed record RecentEventViewModel(
    string Time,
    string Message,
    string Severity);

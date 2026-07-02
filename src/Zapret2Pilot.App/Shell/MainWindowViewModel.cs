using System;
using System.Collections.Generic;
using System.Reactive;
using ReactiveUI;
using Zapret2Pilot.App.Navigation;
using Zapret2Pilot.App.Threading;

namespace Zapret2Pilot.App.Shell;

public sealed class MainWindowViewModel : ReactiveObject
{
    private readonly NavigationRouter navigationRouter;
    private readonly IUiScheduler uiScheduler;
    private NavigationPageViewModel currentPage;
    private string lastAction = "Демо-режим: runtime ещё не подключён.";

    public MainWindowViewModel()
        : this(CreateDefaultNavigationRouter(), new ImmediateUiScheduler())
    {
    }

    public MainWindowViewModel(NavigationRouter navigationRouter)
        : this(navigationRouter, new ImmediateUiScheduler())
    {
    }

    public MainWindowViewModel(
        NavigationRouter navigationRouter,
        IUiScheduler uiScheduler)
    {
        ArgumentNullException.ThrowIfNull(navigationRouter);
        ArgumentNullException.ThrowIfNull(uiScheduler);

        this.navigationRouter = navigationRouter;
        this.uiScheduler = uiScheduler;
        currentPage = navigationRouter.CurrentPage;

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

        StopCommand = ReactiveCommand.Create(() =>
        {
            LastAction = "Демо: остановка runtime будет подключена позже.";
        });

        CheckNowCommand = ReactiveCommand.Create(() =>
        {
            LastAction = "Демо: проверка сервисов использует mock-данные.";
        });

        OpenDiagnosticsCommand = ReactiveCommand.Create(() =>
        {
            LastAction = "Демо: экран диагностики будет добавлен позже.";
        });

        PinProfileCommand = ReactiveCommand.Create(() =>
        {
            LastAction = "Демо: закрепление профиля будет добавлено после модели профилей.";
        });

        OpenProfileDetailsCommand = ReactiveCommand.Create(() =>
        {
            LastAction = "Демо: детали профиля будут добавлены после ProfileDefinition.";
        });
    }

    public string AppName { get; } = "Zapret2Pilot";

    public string AppVersion { get; } = "v0.0.22";

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

    private static NavigationRouter CreateDefaultNavigationRouter()
    {
        return new NavigationRouter(
            new NavigationPageFactory(),
            RouteId.Dashboard);
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

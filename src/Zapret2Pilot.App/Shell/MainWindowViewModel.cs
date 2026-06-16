using System.Collections.Generic;
using System.Reactive;
using ReactiveUI;

namespace Zapret2Pilot.App.Shell;

public sealed class MainWindowViewModel : ReactiveObject
{
    private string lastAction = "Демо-режим: runtime ещё не подключён.";

    public MainWindowViewModel()
    {
        SidebarItems =
        [
            new SidebarItemViewModel("Главная", isSelected: true),
            new SidebarItemViewModel("Профили"),
            new SidebarItemViewModel("Правила"),
            new SidebarItemViewModel("Auto Doctor"),
            new SidebarItemViewModel("Диагностика"),
            new SidebarItemViewModel("Логи"),
            new SidebarItemViewModel("Модуль zapret2"),
            new SidebarItemViewModel("Настройки"),
        ];

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

    public string AppName => "Zapret2Pilot";

    public string AppVersion => "v0.0.1";

    public string WindowTitle => "Zapret2Pilot";

    public string PageTitle => "Главная";

    public string StatusTitle => "Обход активен";

    public string StatusSubtitle => "Текущий профиль работает стабильно.";

    public string StatusDescription => "Ключевые проверки пройдены.";

    public string UptimeText => "2 ч 47 мин";

    public string WorkMode => "Автопилот";

    public string CurrentProfile => "Сбалансированный";

    public string CurrentProfileBadge => "Сбалансированный    Рекомендуется";

    public string LastCheckText => "12:43";

    public string WorkModeDescription =>
        "Z2P автоматически выбирает профиль, проверяет сеть и использует резервный сценарий при проблемах.";

    public string LastModeAction =>
        "Последнее действие: проверил сервисы — профиль не менялся.";

    public string CurrentProfileDescription =>
        "Оптимальный баланс скорости, стабильности и совместимости для большинства сетей. Выбран автоматически на основе диагностики сети и доступности сервисов.";

    public string ServicesUpdatedText => "Обновлено: 12:43";

    public IReadOnlyList<SidebarItemViewModel> SidebarItems { get; }

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
}

public sealed record SidebarItemViewModel(string Title, bool IsSelected = false)
{
    public string Marker => IsSelected ? "●" : string.Empty;
}

public sealed record DashboardServiceStatusViewModel(
    string Name,
    string Status,
    string Latency);

public sealed record RecentEventViewModel(
    string Time,
    string Message,
    string Severity);

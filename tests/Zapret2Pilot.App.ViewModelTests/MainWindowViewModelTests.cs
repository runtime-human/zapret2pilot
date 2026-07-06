using System;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Xunit;
using Zapret2Pilot.App.Diagnostics;
using Zapret2Pilot.App.Lifecycle;
using Zapret2Pilot.App.Navigation;
using Zapret2Pilot.App.Shell;
using Zapret2Pilot.App.Threading;
using Zapret2Pilot.App.ViewModelTests.Fakes;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.ViewModelTests;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

public sealed class MainWindowViewModelTests
{
    [Fact]
    public static void InitialShellIdentityMatchesProjectCanon()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        Assert.Equal("Zapret2Pilot", viewModel.AppName);
        Assert.StartsWith("v0.0.25", viewModel.AppVersion);
        Assert.Equal("Zapret2Pilot", viewModel.WindowTitle);
        Assert.Equal("Главная", viewModel.PageTitle);
    }

    [Fact]
    public static void SidebarContainsNavigationPlaceholdersWithoutRuntimeStatusBlock()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        Assert.Collection(
            viewModel.SidebarItems,
            item => Assert.Equal("Главная", item.Title),
            item => Assert.Equal("Профили", item.Title),
            item => Assert.Equal("Настройки", item.Title));

        Assert.DoesNotContain(viewModel.SidebarItems, item => item.Title.Contains("Service", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.SidebarItems, item => item.Title.Contains("Runtime", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.SidebarItems, item => item.Title.Contains("Обход активен", StringComparison.Ordinal));
    }

    [Fact]
    public static void DashboardUsesMockDesignTimeStatus()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        Assert.Equal("Обход активен", viewModel.StatusTitle);
        Assert.Equal("Текущий профиль работает стабильно.", viewModel.StatusSubtitle);
        Assert.Equal("Ключевые проверки пройдены.", viewModel.StatusDescription);
        Assert.Equal("Автопилот", viewModel.WorkMode);
        Assert.Equal("Сбалансированный", viewModel.CurrentProfile);
    }

    [Fact]
    public static void KeyServicesUseLatencyInsteadOfExcellentCopy()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        Assert.Collection(
            viewModel.KeyServices,
            item =>
            {
                Assert.Equal("YouTube", item.Name);
                Assert.Equal("Доступен", item.Status);
                Assert.Equal("124 мс", item.Latency);
            },
            item =>
            {
                Assert.Equal("Discord", item.Name);
                Assert.Equal("Доступен", item.Status);
                Assert.Equal("146 мс", item.Latency);
            },
            item =>
            {
                Assert.Equal("Telegram", item.Name);
                Assert.Equal("Доступен", item.Status);
                Assert.Equal("98 мс", item.Latency);
            });

        Assert.DoesNotContain(viewModel.KeyServices, item => item.Status == "Отлично");
    }

    [Fact]
    public static void NavigateToProfilesUpdatesCurrentPageAndSelection()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        bool navigated = viewModel.NavigateTo(RouteId.Profiles);

        Assert.True(navigated);
        Assert.Equal("Профили", viewModel.PageTitle);
        Assert.Equal(RouteId.Profiles, viewModel.CurrentPage.RouteId);
        Assert.True(viewModel.CurrentPage.IsPlaceholder);
        Assert.False(viewModel.SidebarItems[0].IsSelected);
        Assert.True(viewModel.SidebarItems[1].IsSelected);
        Assert.False(viewModel.SidebarItems[2].IsSelected);
    }

    [Fact]
    public static void UnknownRouteDoesNotChangeCurrentPage()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        bool navigated = viewModel.NavigateTo(new RouteId("unknown"));

        Assert.False(navigated);
        Assert.Equal(RouteId.Dashboard, viewModel.CurrentPage.RouteId);
        Assert.Equal("Главная", viewModel.PageTitle);
    }

    [Fact]
    public static void MockCommandsOnlyUpdateUiState()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        using IDisposable subscription = viewModel.CheckNowCommand.Execute().Subscribe(_ => { });

        Assert.Equal("Демо: проверка сервисов использует mock-данные.", viewModel.LastAction);
    }

    [Fact]
    public static void StartBlocked_UpdatesLastAction()
    {
        NavigationRouter router = new(new NavigationPageFactory(), RouteId.Dashboard);
        ImmediateUiScheduler scheduler = new();
        using FakeRuntimeSupervisor fakeSupervisor = new();
        MainWindowViewModel viewModel = new(router, scheduler, fakeSupervisor, logger: null, coordinator: new FakeLifecycleCoordinator());

        // The supervisor subscription is wired inside WhenActivated
        // (Packet 6 — Scope F). Activate the view model so the
        // subscription becomes live before we publish, then deactivate
        // to dispose it cleanly at the end of the test.
        viewModel.Activator.Activate();
        try
        {
            RuntimeSupervisorState blockedState = new(
                status: RuntimeSupervisorStatus.StartBlocked,
                lastStartResult: null,
                guardResult: new CrashLoopGuardResult(
                    isAllowed: false,
                    backoffRemaining: TimeSpan.FromSeconds(5),
                    consecutiveFailures: 1),
                lastError: null,
                timestamp: DateTimeOffset.UtcNow);

            fakeSupervisor.Publish(blockedState);

            Assert.Equal(
                "Запуск отложен: слишком много падений. Повтор через 5 с.",
                viewModel.LastAction);
        }
        finally
        {
            viewModel.Activator.Deactivate();
        }
    }

    [Fact]
    public static void RuntimeCommandsDisabledWhenCoordinatorIsNotReady()
    {
        FakeLifecycleCoordinator coordinator = new();
        coordinator.SetPhase(ApplicationLifecyclePhase.ProcessBootstrap);
        MainWindowViewModel viewModel = CreateViewModel(coordinator: coordinator);

        ICommand stopCommand = viewModel.StopCommand;
        ICommand checkNowCommand = viewModel.CheckNowCommand;

        Assert.False(stopCommand.CanExecute(null));
        Assert.False(checkNowCommand.CanExecute(null));
    }

    [Fact]
    public static void RuntimeCommandsEnabledWhenCoordinatorReachesReady()
    {
        FakeLifecycleCoordinator coordinator = new();
        MainWindowViewModel viewModel = CreateViewModel(coordinator: coordinator);

        ICommand stopCommand = viewModel.StopCommand;
        ICommand checkNowCommand = viewModel.CheckNowCommand;

        Assert.True(stopCommand.CanExecute(null));
        Assert.True(checkNowCommand.CanExecute(null));
    }

    [Fact]
    public static void MainWindowViewModelIsActivatable()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        Assert.IsAssignableFrom<IActivatableViewModel>(viewModel);
        Assert.NotNull(((IActivatableViewModel)viewModel).Activator);
    }

    private static MainWindowViewModel CreateViewModel(
        NavigationRouter? router = null,
        IUiScheduler? scheduler = null,
        IRuntimeSupervisor? supervisor = null,
        IZ2PApplicationLifecycleCoordinator? coordinator = null,
        IExceptionPolicy? exceptionPolicy = null) =>
    new(
        router ?? new NavigationRouter(new NavigationPageFactory(), RouteId.Dashboard),
        scheduler ?? new ImmediateUiScheduler(),
        supervisor,
        logger: null,
        coordinator ?? new FakeLifecycleCoordinator(),
        exceptionPolicy);
}

#pragma warning restore CA1707 // Identifiers should not contain underscores

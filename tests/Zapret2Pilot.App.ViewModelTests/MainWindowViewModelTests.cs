using System;
using System.Reactive.Linq;
using Xunit;
using Zapret2Pilot.App.Navigation;
using Zapret2Pilot.App.Shell;

namespace Zapret2Pilot.App.ViewModelTests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public static void InitialShellIdentityMatchesProjectCanon()
    {
        MainWindowViewModel viewModel = new();

        Assert.Equal("Zapret2Pilot", viewModel.AppName);
        Assert.Equal("v0.0.23", viewModel.AppVersion);
        Assert.Equal("Zapret2Pilot", viewModel.WindowTitle);
        Assert.Equal("Главная", viewModel.PageTitle);
    }

    [Fact]
    public static void SidebarContainsNavigationPlaceholdersWithoutRuntimeStatusBlock()
    {
        MainWindowViewModel viewModel = new();

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
        MainWindowViewModel viewModel = new();

        Assert.Equal("Обход активен", viewModel.StatusTitle);
        Assert.Equal("Текущий профиль работает стабильно.", viewModel.StatusSubtitle);
        Assert.Equal("Ключевые проверки пройдены.", viewModel.StatusDescription);
        Assert.Equal("Автопилот", viewModel.WorkMode);
        Assert.Equal("Сбалансированный", viewModel.CurrentProfile);
    }

    [Fact]
    public static void KeyServicesUseLatencyInsteadOfExcellentCopy()
    {
        MainWindowViewModel viewModel = new();

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
        MainWindowViewModel viewModel = new();

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
        MainWindowViewModel viewModel = new();

        bool navigated = viewModel.NavigateTo(new RouteId("unknown"));

        Assert.False(navigated);
        Assert.Equal(RouteId.Dashboard, viewModel.CurrentPage.RouteId);
        Assert.Equal("Главная", viewModel.PageTitle);
    }

    [Fact]
    public static void MockCommandsOnlyUpdateUiState()
    {
        MainWindowViewModel viewModel = new();

        using IDisposable subscription = viewModel.CheckNowCommand.Execute().Subscribe(_ => { });

        Assert.Equal("Демо: проверка сервисов использует mock-данные.", viewModel.LastAction);
    }
}

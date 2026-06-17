using System;
using Xunit;
using Zapret2Pilot.App.Navigation;

namespace Zapret2Pilot.App.ViewModelTests;

public sealed class NavigationRouterTests
{
    [Fact]
    public static void RouterStartsAtInitialRoute()
    {
        NavigationRouter router = new(
            new NavigationPageFactory(),
            RouteId.Dashboard);

        Assert.Equal(RouteId.Dashboard, router.CurrentRoute);
        Assert.Equal("Главная", router.CurrentPage.Title);
        Assert.True(router.CurrentPage.IsDashboard);
    }

    [Fact]
    public static void RouterSelectsKnownRoute()
    {
        NavigationRouter router = new(
            new NavigationPageFactory(),
            RouteId.Dashboard);

        bool navigated = router.NavigateTo(RouteId.Settings);

        Assert.True(navigated);
        Assert.Equal(RouteId.Settings, router.CurrentRoute);
        Assert.Equal("Настройки", router.CurrentPage.Title);
        Assert.True(router.CurrentPage.IsPlaceholder);
    }

    [Fact]
    public static void RouterKeepsCurrentRouteWhenRouteIsUnknown()
    {
        NavigationRouter router = new(
            new NavigationPageFactory(),
            RouteId.Dashboard);

        bool navigated = router.NavigateTo(new RouteId("unknown"));

        Assert.False(navigated);
        Assert.Equal(RouteId.Dashboard, router.CurrentRoute);
        Assert.Equal("Главная", router.CurrentPage.Title);
    }

    [Fact]
    public static void RouterRejectsNullRoute()
    {
        NavigationRouter router = new(
            new NavigationPageFactory(),
            RouteId.Dashboard);

        Assert.Throws<ArgumentNullException>(() => router.NavigateTo(null!));
    }
}

using System;

namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// In-memory shell navigation router.
/// </summary>
public sealed class NavigationRouter : INavigationRouter
{
    private readonly INavigationPageFactory pageFactory;

    public NavigationRouter(
        INavigationPageFactory pageFactory,
        RouteId initialRoute)
    {
        ArgumentNullException.ThrowIfNull(pageFactory);
        ArgumentNullException.ThrowIfNull(initialRoute);

        this.pageFactory = pageFactory;

        NavigationPageViewModel? initialPage = pageFactory.CreatePage(initialRoute);

        CurrentPage = initialPage
            ?? throw new ArgumentException("Initial route is not known by the page factory.", nameof(initialRoute));
    }

    public RouteId CurrentRoute => CurrentPage.RouteId;

    public NavigationPageViewModel CurrentPage { get; private set; }

    public bool NavigateTo(RouteId routeId)
    {
        ArgumentNullException.ThrowIfNull(routeId);

        NavigationPageViewModel? page = pageFactory.CreatePage(routeId);

        if (page is null)
        {
            return false;
        }

        CurrentPage = page;

        return true;
    }
}

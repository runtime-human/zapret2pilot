namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// Routes the shell between known placeholder pages.
/// </summary>
public interface INavigationRouter
{
    RouteId CurrentRoute { get; }

    NavigationPageViewModel CurrentPage { get; }

    bool NavigateTo(RouteId routeId);
}

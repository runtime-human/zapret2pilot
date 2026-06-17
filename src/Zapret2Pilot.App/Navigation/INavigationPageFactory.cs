namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// Creates placeholder page view models for known shell routes.
/// </summary>
public interface INavigationPageFactory
{
    NavigationPageViewModel? CreatePage(RouteId routeId);
}

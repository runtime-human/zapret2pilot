using System;

namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// Default shell page factory for route placeholders.
/// </summary>
public sealed class NavigationPageFactory : INavigationPageFactory
{
    public NavigationPageViewModel? CreatePage(RouteId routeId)
    {
        ArgumentNullException.ThrowIfNull(routeId);

        if (routeId.Equals(RouteId.Dashboard))
        {
            return new NavigationPageViewModel(
                RouteId.Dashboard,
                "Главная",
                "Сводка состояния обхода и ключевых проверок.",
                isDashboard: true);
        }

        if (routeId.Equals(RouteId.Profiles))
        {
            return new NavigationPageViewModel(
                RouteId.Profiles,
                "Профили",
                "Здесь позже появится управление профилями обхода. Сейчас это placeholder без Profile compiler и storage.",
                isDashboard: false);
        }

        if (routeId.Equals(RouteId.Settings))
        {
            return new NavigationPageViewModel(
                RouteId.Settings,
                "Настройки",
                "Здесь позже появятся настройки приложения. Сейчас это placeholder без storage и runtime logic.",
                isDashboard: false);
        }

        return null;
    }
}

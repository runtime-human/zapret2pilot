using System;

namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// Placeholder page representation for shell-level navigation.
/// </summary>
public sealed class NavigationPageViewModel
{
    public NavigationPageViewModel(
        RouteId routeId,
        string title,
        string description,
        bool isDashboard)
    {
        ArgumentNullException.ThrowIfNull(routeId);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);

        RouteId = routeId;
        Title = title;
        Description = description;
        IsDashboard = isDashboard;
    }

    public RouteId RouteId { get; }

    public string Title { get; }

    public string Description { get; }

    public bool IsDashboard { get; }

    public bool IsPlaceholder => !IsDashboard;
}

using System;
using System.Reactive;
using FluentIcons.Common;
using ReactiveUI.Reactive;

namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// Sidebar navigation item view model.
/// </summary>
public sealed class NavigationItemViewModel : ReactiveObject
{
    private bool isSelected;

    public NavigationItemViewModel(
        RouteId routeId,
        string title,
        bool isSelected,
        ReactiveCommand<Unit, Unit> navigateCommand)
    {
        ArgumentNullException.ThrowIfNull(routeId);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(navigateCommand);

        RouteId = routeId;
        Title = title;
        this.isSelected = isSelected;
        NavigateCommand = navigateCommand;
        Icon = MapIcon(RouteId);
    }

    public RouteId RouteId { get; }

    public string Title { get; }

    public bool IsSelected => isSelected;

    /// <summary>
    /// FluentIcons symbol used to render the sidebar entry.
    /// Resolved eagerly from the route id; the mapping is a pure
    /// value-to-enum function with no Avalonia render dependency,
    /// so it is safe to use in headless view-model tests.
    /// </summary>
    public Icon Icon { get; }

    public ReactiveCommand<Unit, Unit> NavigateCommand { get; }

    internal void SetSelected(bool value)
    {
        if (isSelected == value)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref isSelected, value);
    }

    private static Icon MapIcon(RouteId routeId) => routeId.Value switch
    {
        "dashboard" => Icon.Home,
        "profiles" => Icon.People,
        "settings" => Icon.Settings,
        _ => Icon.Home,
    };
}

using System;
using System.Reactive;
using ReactiveUI;

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
    }

    public RouteId RouteId { get; }

    public string Title { get; }

    public bool IsSelected => isSelected;

    public string Marker => IsSelected ? "●" : string.Empty;

    public ReactiveCommand<Unit, Unit> NavigateCommand { get; }

    internal void SetSelected(bool value)
    {
        if (isSelected == value)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref isSelected, value);
        this.RaisePropertyChanged(nameof(Marker));
    }
}

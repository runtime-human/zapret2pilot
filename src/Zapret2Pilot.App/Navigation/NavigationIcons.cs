using FluentIcons.Common;

namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// Exposes the project's static <see cref="FluentIcons.Common.Icon"/>
/// values via XAML-friendly static properties. Use this from
/// markup (e.g. <c>{x:Static nav:NavigationIcons.Settings}</c>) so
/// icon values stay symbol-typed and refactor-safe.
/// </summary>
public static class NavigationIcons
{
    public static Icon Home { get; } = Icon.Home;

    public static Icon People { get; } = Icon.People;

    public static Icon Settings { get; } = Icon.Settings;

    public static Icon Checkmark { get; } = Icon.Checkmark;
}

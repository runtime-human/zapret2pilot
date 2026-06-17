using System;

namespace Zapret2Pilot.App.Navigation;

/// <summary>
/// Strongly typed identifier for shell navigation routes.
/// </summary>
public sealed record class RouteId
{
    public static RouteId Dashboard { get; } = new("dashboard");

    public static RouteId Profiles { get; } = new("profiles");

    public static RouteId Settings { get; } = new("settings");

    public RouteId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string normalized = value.Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Route id must not be empty or whitespace.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

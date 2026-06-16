using Zapret2Pilot.Core;

namespace Zapret2Pilot.Application;

/// <summary>
/// Marker for the Zapret2Pilot.Application assembly.
/// </summary>
public static class ApplicationAssemblyMarker
{
    public const string LayerName = "Application";

    public static string ProductName => CoreAssemblyMarker.ProductName;
}

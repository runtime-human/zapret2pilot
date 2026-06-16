using Xunit;
using Zapret2Pilot.Application;

namespace Zapret2Pilot.Application.Tests;

public sealed class ApplicationAssemblyMarkerTests
{
    [Fact]
    public void Application_layer_exposes_expected_identity_from_core()
    {
        Assert.Equal("Application", ApplicationAssemblyMarker.LayerName);
        Assert.Equal("Zapret2Pilot", ApplicationAssemblyMarker.ProductName);
    }
}

using Xunit;
using Zapret2Pilot.Application;

namespace Zapret2Pilot.Application.Tests;

public sealed class ApplicationAssemblyMarkerTests
{
    [Fact]
    public void ApplicationLayerExposesExpectedIdentityFromCore()
    {
        Assert.Equal("Application", ApplicationAssemblyMarker.LayerName);
        Assert.Equal("Zapret2Pilot", ApplicationAssemblyMarker.ProductName);
    }
}

using Xunit;
using Zapret2Pilot.Engine.Zapret2;

namespace Zapret2Pilot.Engine.Zapret2.Tests;

public sealed class EngineZapret2AssemblyMarkerTests
{
    [Fact]
    public void EngineZapret2LayerExposesExpectedIdentityFromCore()
    {
        Assert.Equal("Engine.Zapret2", EngineZapret2AssemblyMarker.LayerName);
        Assert.Equal("Zapret2Pilot", EngineZapret2AssemblyMarker.ProductName);
    }
}

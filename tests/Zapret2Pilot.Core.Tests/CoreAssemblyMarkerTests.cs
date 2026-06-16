using Xunit;
using Zapret2Pilot.Core;

namespace Zapret2Pilot.Core.Tests;

public sealed class CoreAssemblyMarkerTests
{
    [Fact]
    public void CoreIdentityMatchesProjectCanon()
    {
        Assert.Equal("Zapret2Pilot", CoreAssemblyMarker.ProductName);
        Assert.Equal("Z2P", CoreAssemblyMarker.ShortName);
        Assert.Equal("z2p.exe", CoreAssemblyMarker.MainExecutableName);
        Assert.Equal("Core", CoreAssemblyMarker.LayerName);
    }
}

using Xunit;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Tests.Ownership;

public sealed class RuntimeOwnershipNamesTests
{
    [Fact]
    public static void GlobalMutexNameUsesExpectedValue()
    {
        Assert.Equal(@"Global\Z2P_RUNTIME_OWNER_v1", RuntimeOwnershipNames.GlobalMutexName);
    }

    [Fact]
    public static void LockFileNameUsesExpectedValue()
    {
        Assert.Equal("z2p-runtime.lock", RuntimeOwnershipNames.LockFileName);
    }
}

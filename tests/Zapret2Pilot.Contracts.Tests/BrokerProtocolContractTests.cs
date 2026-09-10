using System.Reflection;
using Xunit;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerProtocolContractTests
{
    [Fact]
    public void Protocol_surface_is_closed_and_contains_no_generic_privileged_execution()
    {
        BrokerMessageKind[] expected =
        [
            BrokerMessageKind.Hello,
            BrokerMessageKind.GetCapabilities,
            BrokerMessageKind.GetRuntimeSnapshot,
            BrokerMessageKind.PrepareBundle,
            BrokerMessageKind.PreparePlan,
            BrokerMessageKind.StartPreparedPlan,
            BrokerMessageKind.StopGeneration,
            BrokerMessageKind.ShutdownBroker,
        ];

        Assert.Equal(expected, Enum.GetValues<BrokerMessageKind>());

        string[] forbiddenPropertyFragments =
        [
            "Executable",
            "Arguments",
            "CommandLine",
            "PowerShell",
            "Script",
            "Url",
            "FilePath",
            "Dll",
            "LuaPath",
        ];

        Type requestMarker = typeof(IBrokerRequest);
        Type[] requestTypes = requestMarker.Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && requestMarker.IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(requestTypes);
        foreach (Type requestType in requestTypes)
        {
            PropertyInfo[] properties = requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            foreach (PropertyInfo property in properties)
            {
                Assert.DoesNotContain(
                    forbiddenPropertyFragments,
                    fragment => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Contracts_layer_has_no_forbidden_product_dependencies()
    {
        string[] referencedAssemblies = typeof(IBrokerRequest).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Avalonia", referencedAssemblies);
        Assert.DoesNotContain("ReactiveUI", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", referencedAssemblies);
        Assert.DoesNotContain("Zapret2Pilot.Runtime", referencedAssemblies);
        Assert.DoesNotContain("Zapret2Pilot.Engine.Zapret2", referencedAssemblies);
    }

    [Fact]
    public void Transport_limits_are_explicit_and_bounded()
    {
        Assert.Equal(65_536, BrokerProtocolLimits.MaxFrameBytes);
        Assert.Equal(2, BrokerProtocolLimits.MaxConcurrentConnections);
        Assert.Equal(1, BrokerProtocolLimits.MaxAuthenticatedConnections);
        Assert.Equal(8, BrokerProtocolLimits.MaxInFlightQueries);
        Assert.Equal(1, BrokerProtocolLimits.MaxConcurrentMutations);
        Assert.Equal(32, BrokerProtocolLimits.IngressQueueCapacity);
        Assert.Equal(16, BrokerProtocolLimits.ResponseQueueCapacity);
        Assert.Equal(256, BrokerProtocolLimits.OperationLedgerCapacity);
        Assert.Equal(TimeSpan.FromMinutes(2), BrokerProtocolLimits.OperationLedgerTtl);
        Assert.Equal(TimeSpan.FromSeconds(3), BrokerProtocolLimits.HandshakeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), BrokerProtocolLimits.FrameHeaderTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), BrokerProtocolLimits.FrameBodyTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), BrokerProtocolLimits.QueryDeadline);
        Assert.Equal(TimeSpan.FromSeconds(15), BrokerProtocolLimits.MaxRequestLifetime);
        Assert.Equal(TimeSpan.FromSeconds(5), BrokerProtocolLimits.ResponseWriteTimeout);
    }

    [Fact]
    public void Version_negotiation_rejects_unknown_major_protocol()
    {
        BrokerProtocolRange server = BrokerProtocolRange.Current;
        BrokerProtocolRange client = new(new BrokerProtocolVersion(2, 0), new BrokerProtocolVersion(2, 1));

        BrokerProtocolNegotiationResult result = BrokerProtocolNegotiator.Negotiate(client, server);

        Assert.False(result.Accepted);
        Assert.Equal(BrokerProtocolRejectionReason.UnsupportedMajorVersion, result.RejectionReason);
    }

    [Fact]
    public void Version_negotiation_selects_highest_common_version()
    {
        BrokerProtocolRange server = new(new BrokerProtocolVersion(1, 0), new BrokerProtocolVersion(1, 2));
        BrokerProtocolRange client = new(new BrokerProtocolVersion(1, 0), new BrokerProtocolVersion(1, 1));

        BrokerProtocolNegotiationResult result = BrokerProtocolNegotiator.Negotiate(client, server);

        Assert.True(result.Accepted);
        Assert.Equal(new BrokerProtocolVersion(1, 1), result.SelectedVersion);
    }
}

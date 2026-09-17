using System.Reflection;
using System.Threading;
using Xunit;
using Zapret2Pilot.Contracts.Client;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class RuntimeClientContractTests
{
    [Fact]
    public static void RuntimeClientSurfaceIsBoundedToBrokerLifecycleProtocol()
    {
        Type contract = typeof(IRuntimeClient);
        string[] methodNames = contract.GetMethods()
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(IRuntimeClient.GetRuntimeSnapshotAsync),
                nameof(IRuntimeClient.ShutdownBrokerAsync),
                nameof(IRuntimeClient.StartPreparedPlanAsync),
                nameof(IRuntimeClient.StopGenerationAsync),
            ],
            methodNames);

        Assert.Equal(
            typeof(RuntimeClientSnapshot),
            contract.GetProperty(nameof(IRuntimeClient.CurrentSnapshot))?.PropertyType);
        Assert.Equal(
            typeof(IObservable<RuntimeClientSnapshot>),
            contract.GetProperty(nameof(IRuntimeClient.SnapshotChanged))?.PropertyType);

        ParameterInfo[] parameters = contract.GetMethods()
            .SelectMany(static method => method.GetParameters())
            .ToArray();

        Assert.DoesNotContain(parameters, static parameter => parameter.ParameterType == typeof(string));
        Assert.DoesNotContain(parameters, static parameter => parameter.ParameterType == typeof(string[]));
        Assert.DoesNotContain(parameters, static parameter => parameter.ParameterType == typeof(System.Diagnostics.ProcessStartInfo));

        Assert.Contains(parameters, static parameter => parameter.ParameterType == typeof(PreparedPlanId));
        Assert.Contains(parameters, static parameter => parameter.ParameterType == typeof(RuntimeGeneration));
        Assert.Contains(parameters, static parameter => parameter.ParameterType == typeof(BrokerStopReason));
        Assert.Contains(parameters, static parameter => parameter.ParameterType == typeof(CancellationToken));
    }
}

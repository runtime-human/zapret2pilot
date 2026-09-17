using System.Reflection;
using Xunit;
using Zapret2Pilot.App.Shell;
using Zapret2Pilot.Contracts.Client;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.ViewModelTests.Architecture;

public sealed class RuntimeClientPresentationBoundaryTests
{
    [Fact]
    public static void MainWindowViewModelDependsOnRuntimeClientNotRuntimeSupervisor()
    {
        ConstructorInfo constructor = Assert.Single(typeof(MainWindowViewModel).GetConstructors());
        ParameterInfo[] parameters = constructor.GetParameters();

        Assert.Contains(parameters, static parameter => parameter.ParameterType == typeof(IRuntimeClient));
        Assert.DoesNotContain(parameters, static parameter => parameter.ParameterType == typeof(IRuntimeSupervisor));
    }
}

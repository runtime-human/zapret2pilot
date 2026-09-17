using System.Reflection;
using Xunit;
using Zapret2Pilot.App.Shell;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.ViewModelTests.Architecture;

public sealed class RuntimeClientPresentationBoundaryTests
{
    private const string RuntimeClientTypeName = "Zapret2Pilot.Contracts.Client.IRuntimeClient";

    [Fact]
    public static void MainWindowViewModelDependsOnRuntimeClientNotRuntimeSupervisor()
    {
        ConstructorInfo constructor = Assert.Single(typeof(MainWindowViewModel).GetConstructors());
        ParameterInfo[] parameters = constructor.GetParameters();

        Assert.Contains(parameters, static parameter => parameter.ParameterType.FullName == RuntimeClientTypeName);
        Assert.DoesNotContain(parameters, static parameter => parameter.ParameterType == typeof(IRuntimeSupervisor));
    }
}

using System;
using System.IO;
using Xunit;

namespace Zapret2Pilot.App.ViewModelTests.Hosting;

public sealed class BrokerCompositionRootBoundaryTests
{
    [Fact]
    public static void SolutionDeclaresSessionScopedBrokerExecutable()
    {
        string repositoryRoot = FindRepositoryRoot();
        string solution = File.ReadAllText(Path.Combine(repositoryRoot, "Zapret2Pilot.slnx"));

        Assert.Contains(
            "<Project Path=\"src/Zapret2Pilot.Broker/Zapret2Pilot.Broker.csproj\" />",
            solution,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Zapret2Pilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Zapret2Pilot.slnx from the test output directory.");
    }
}

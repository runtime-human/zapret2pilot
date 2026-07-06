using System;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace Zapret2Pilot.App.ViewModelTests.Architecture;

// 0.0.25 Scope C — Platform TFM split architecture tests.
//
// The platform boundary from roadmap §28 / Scope C requires:
//   - App and Runtime to be built on a Windows-specific TFM
//     (net10.0-windows10.0.26100.0).
//   - Core / Application / Engine.Zapret2 / Storage to stay on the
//     cross-platform TFM (net10.0) and NOT advertise a
//     SupportedOSPlatformAttribute, so consumers can reuse them
//     from a non-Windows shell if that ever becomes a goal.
//
// These tests verify the split at runtime by inspecting the
// assembly-level attributes that the .NET compiler emits for
// platform-specific TFMs.

public sealed class TfmArchitectureTests
{
    // The .NET compiler emits the assembly-level
    // SupportedOSPlatformAttribute for a Windows TFM using the
    // canonical OS name "Windows" (capital W) followed by the
    // version. The TFM itself is written as "windows" in
    // csproj, but the generated attribute uses the official
    // capitalisation. See Roslyn's PlatformSymmetricAssembly
    // attribute generation (Microsoft.NET.HostModel / Roslyn).
    private const string ExpectedWindowsPlatformName = "Windows10.0.26100.0";

    [Fact]
    public static void AppAssemblyTargetsWindowsPlatform()
    {
        Assembly assembly = typeof(Zapret2Pilot.App.Shell.MainWindow).Assembly;

        SupportedOSPlatformAttribute? attribute = assembly
            .GetCustomAttribute<SupportedOSPlatformAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(ExpectedWindowsPlatformName, attribute!.PlatformName);
    }

    [Fact]
    public static void RuntimeAssemblyTargetsWindowsPlatform()
    {
        Assembly assembly = typeof(Zapret2Pilot.Runtime.Supervisor.RuntimeSupervisor).Assembly;

        SupportedOSPlatformAttribute? attribute = assembly
            .GetCustomAttribute<SupportedOSPlatformAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(ExpectedWindowsPlatformName, attribute!.PlatformName);
    }

    [Theory]
    [InlineData("Zapret2Pilot.Core")]
    [InlineData("Zapret2Pilot.Application")]
    [InlineData("Zapret2Pilot.Engine.Zapret2")]
    [InlineData("Zapret2Pilot.Storage")]
    public static void CrossPlatformAssembliesDoNotDeclareWindowsPlatform(string assemblyName)
    {
        Assembly assembly = Assembly.Load(new AssemblyName(assemblyName));

        SupportedOSPlatformAttribute? attribute = assembly
            .GetCustomAttribute<SupportedOSPlatformAttribute>();

        Assert.Null(attribute);
    }
}

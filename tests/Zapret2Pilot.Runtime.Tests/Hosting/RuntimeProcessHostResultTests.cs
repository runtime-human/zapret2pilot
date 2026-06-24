using System;
using Xunit;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Tests.Hosting;

/// <summary>
/// Focused xUnit tests for <see cref="RuntimeProcessHostResult"/>. The
/// type is a pure DTO populated by <c>RuntimeProcessHost</c> after a
/// successful launch; its constructor is the only behaviour under test.
/// All tests are hermetic and do not touch the filesystem or launch a
/// process.
/// </summary>
public sealed class RuntimeProcessHostResultTests
{
    private const int ProcessId = 4321;
    private const string ProcessName = "winws2";
    private const string ExecutablePath = @"C:\z2p\bin\winws2.exe";

    [Fact]
    public static void ValidConstructionExposesAllInputs()
    {
        CompiledZapretPlan plan = CreatePlan();

        RuntimeProcessHostResult result = new(
            processId: ProcessId,
            processName: ProcessName,
            executablePath: ExecutablePath,
            plan: plan);

        Assert.Equal(ProcessId, result.ProcessId);
        Assert.Equal(ProcessName, result.ProcessName);
        Assert.Equal(ExecutablePath, result.ExecutablePath);
        Assert.Same(plan, result.Plan);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-42)]
    [InlineData(int.MinValue)]
    public static void NonPositiveProcessIdThrowsArgumentOutOfRangeException(int processId)
    {
        CompiledZapretPlan plan = CreatePlan();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeProcessHostResult(
                processId: processId,
                processName: ProcessName,
                executablePath: ExecutablePath,
                plan: plan));

        Assert.Equal("processId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public static void NullOrWhitespaceProcessNameThrows(string? processName)
    {
        CompiledZapretPlan plan = CreatePlan();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null and ArgumentException for empty/whitespace. Both are
        // acceptable signals for an invalid process name.
        if (processName is null)
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                new RuntimeProcessHostResult(
                    processId: ProcessId,
                    processName: processName!,
                    executablePath: ExecutablePath,
                    plan: plan));

            Assert.Equal("processName", exception.ParamName);
        }
        else
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new RuntimeProcessHostResult(
                    processId: ProcessId,
                    processName: processName,
                    executablePath: ExecutablePath,
                    plan: plan));

            Assert.Equal("processName", exception.ParamName);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public static void NullOrWhitespaceExecutablePathThrows(string? executablePath)
    {
        CompiledZapretPlan plan = CreatePlan();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null and ArgumentException for empty/whitespace. Both are
        // acceptable signals for an invalid executable path.
        if (executablePath is null)
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                new RuntimeProcessHostResult(
                    processId: ProcessId,
                    processName: ProcessName,
                    executablePath: executablePath!,
                    plan: plan));

            Assert.Equal("executablePath", exception.ParamName);
        }
        else
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new RuntimeProcessHostResult(
                    processId: ProcessId,
                    processName: ProcessName,
                    executablePath: executablePath,
                    plan: plan));

            Assert.Equal("executablePath", exception.ParamName);
        }
    }

    [Fact]
    public static void NullPlanThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new RuntimeProcessHostResult(
                processId: ProcessId,
                processName: ProcessName,
                executablePath: ExecutablePath,
                plan: null!));

        Assert.Equal("plan", exception.ParamName);
    }

    private static CompiledZapretPlan CreatePlan()
    {
        return new CompiledZapretPlan(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());
    }
}

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zapret2Pilot.Runtime.Detection;
using Zapret2Pilot.Runtime.Locking;

namespace Zapret2Pilot.Runtime.Tests.Detection;

public sealed class RuntimeOwnershipDetectorTests
{
    private const int TestProcessId = 1234;
    private const string TestProcessName = "runtime-engine";
    private const string TestExecutablePath = "C:\\bin\\runtime-engine.exe";
    private const string TestCommandLine = "--profile default --plan main";
    private const string TestPlanHash = "plan-hash-value";

    [Fact]
    public static void VerifyOwnedRuntimeReturnsOwnedByExpectedRuntime()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        FakeProcessSystemAccessor accessor = new();
        accessor.Register(
            TestProcessId,
            CreateSnapshot(
                TestProcessId,
                TestProcessName,
                TestExecutablePath,
                TestCommandLine,
                startedAt));

        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: startedAt,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, TestPlanHash);

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.OwnedByExpectedRuntime,
            result.Status);
    }

    [Fact]
    public static void VerifyMissingProcessReturnsNoProcess()
    {
        FakeProcessSystemAccessor accessor = new();
        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: DateTimeOffset.UtcNow,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, TestPlanHash);

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.NoProcess,
            result.Status);
    }

    [Fact]
    public static void VerifyProcessNameMismatchReturnsProcessNameMismatch()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        FakeProcessSystemAccessor accessor = new();
        accessor.Register(
            TestProcessId,
            CreateSnapshot(
                TestProcessId,
                "imposter-engine",
                TestExecutablePath,
                TestCommandLine,
                startedAt));

        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: startedAt,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, TestPlanHash);

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.ProcessNameMismatch,
            result.Status);
    }

    [Fact]
    public static void VerifyExecutablePathMismatchReturnsExecutablePathMismatch()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        FakeProcessSystemAccessor accessor = new();
        accessor.Register(
            TestProcessId,
            CreateSnapshot(
                TestProcessId,
                TestProcessName,
                "C:\\bin\\imposter.exe",
                TestCommandLine,
                startedAt));

        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: startedAt,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, TestPlanHash);

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.ExecutablePathMismatch,
            result.Status);
    }

    [Fact]
    public static void VerifyCommandLineHashMismatchReturnsCommandLineHashMismatch()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        FakeProcessSystemAccessor accessor = new();
        accessor.Register(
            TestProcessId,
            CreateSnapshot(
                TestProcessId,
                TestProcessName,
                TestExecutablePath,
                "--different-args",
                startedAt));

        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: startedAt,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, TestPlanHash);

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.CommandLineHashMismatch,
            result.Status);
    }

    [Fact]
    public static void VerifyNullCommandLineReturnsCommandLineUnverifiable()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        FakeProcessSystemAccessor accessor = new();
        accessor.Register(
            TestProcessId,
            new NullCommandLineSnapshot(
                TestProcessId,
                TestProcessName,
                TestExecutablePath,
                startedAt));

        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: startedAt,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, TestPlanHash);

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.CommandLineUnverifiable,
            result.Status);
    }

    [Fact]
    public static void VerifyPlanHashMismatchReturnsPlanHashMismatch()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        FakeProcessSystemAccessor accessor = new();
        accessor.Register(
            TestProcessId,
            CreateSnapshot(
                TestProcessId,
                TestProcessName,
                TestExecutablePath,
                TestCommandLine,
                startedAt));

        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: startedAt,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, "different-plan-hash");

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.PlanHashMismatch,
            result.Status);
    }

    [Fact]
    public static void VerifyProcessStartTimeMismatchReturnsProcessStartTimeMismatch()
    {
        DateTimeOffset snapshotStartedAt = DateTimeOffset.UtcNow;
        DateTimeOffset metadataStartedAt = snapshotStartedAt.AddSeconds(60);

        FakeProcessSystemAccessor accessor = new();
        accessor.Register(
            TestProcessId,
            CreateSnapshot(
                TestProcessId,
                TestProcessName,
                TestExecutablePath,
                TestCommandLine,
                snapshotStartedAt));

        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: metadataStartedAt,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        RuntimeOwnershipVerificationResult result = detector.Verify(metadata, TestPlanHash);

        Assert.Equal(
            RuntimeOwnershipVerificationStatus.ProcessStartTimeMismatch,
            result.Status);
    }

    [Fact]
    public static void VerifyRejectsNullMetadata()
    {
        FakeProcessSystemAccessor accessor = new();
        RuntimeOwnershipDetector detector = new(accessor);

        Assert.Throws<ArgumentNullException>(
            () => detector.Verify(null!, TestPlanHash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public static void VerifyRejectsNullOrEmptyExpectedPlanHash(string? expectedPlanHash)
    {
        FakeProcessSystemAccessor accessor = new();
        RuntimeOwnershipDetector detector = new(accessor);
        RuntimeLockMetadata metadata = CreateMetadata(
            processStartedAtUtc: DateTimeOffset.UtcNow,
            commandLine: TestCommandLine,
            planHash: TestPlanHash);

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null and ArgumentException for empty/whitespace. Both are
        // acceptable signals for invalid input.
        if (expectedPlanHash is null)
        {
            Assert.Throws<ArgumentNullException>(
                () => detector.Verify(metadata, expectedPlanHash!));
        }
        else
        {
            Assert.Throws<ArgumentException>(
                () => detector.Verify(metadata, expectedPlanHash));
        }
    }

    private static RuntimeLockMetadata CreateMetadata(
        DateTimeOffset processStartedAtUtc,
        string commandLine,
        string planHash)
    {
        RuntimeLockProcessMetadata process = new(
            processId: TestProcessId,
            processName: TestProcessName,
            executablePath: TestExecutablePath,
            commandLineHash: ComputeCommandLineHash(commandLine),
            planHash: planHash,
            processStartedAtUtc: processStartedAtUtc);

        return new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: "test-owner",
            process: process,
            acquiredAtUtc: DateTimeOffset.UtcNow);
    }

    private static Snapshot CreateSnapshot(
        int processId,
        string processName,
        string executablePath,
        string commandLine,
        DateTimeOffset startedAtUtc)
    {
        return new Snapshot(
            processId,
            processName,
            executablePath,
            commandLine,
            startedAtUtc);
    }

    private static string ComputeCommandLineHash(string commandLine)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(commandLine);
        byte[] hashBytes = SHA256.HashData(bytes);

        StringBuilder builder = new(hashBytes.Length * 2);
        foreach (byte b in hashBytes)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private sealed record Snapshot(
        int ProcessId,
        string ProcessName,
        string ExecutablePath,
        string CommandLine,
        DateTimeOffset StartedAtUtc) : IProcessSnapshot;

    private sealed record NullCommandLineSnapshot(
        int ProcessId,
        string ProcessName,
        string ExecutablePath,
        DateTimeOffset StartedAtUtc) : IProcessSnapshot
    {
        public string? CommandLine => null;
    }
}

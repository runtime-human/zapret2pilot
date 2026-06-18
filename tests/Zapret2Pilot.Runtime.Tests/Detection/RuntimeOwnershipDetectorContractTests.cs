using System;
using Xunit;
using Zapret2Pilot.Runtime.Detection;
using Zapret2Pilot.Runtime.Locking;

namespace Zapret2Pilot.Runtime.Tests.Detection;

public sealed class RuntimeOwnershipDetectorContractTests
{
    [Fact]
    public static void VerificationResultCanRepresentOwnedRuntime()
    {
        StaticRuntimeOwnershipDetector detector = new(
            RuntimeOwnershipVerificationResult.OwnedByExpectedRuntime());

        RuntimeOwnershipVerificationResult result = detector.Verify(CreateMetadata());

        Assert.Equal(RuntimeOwnershipVerificationStatus.OwnedByExpectedRuntime, result.Status);
    }

    [Fact]
    public static void VerificationResultCanRepresentNoProcess()
    {
        StaticRuntimeOwnershipDetector detector = new(
            RuntimeOwnershipVerificationResult.NoProcess());

        RuntimeOwnershipVerificationResult result = detector.Verify(CreateMetadata());

        Assert.Equal(RuntimeOwnershipVerificationStatus.NoProcess, result.Status);
    }

    [Fact]
    public static void ProcessIdentityCarriesExpectedFields()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        RuntimeProcessIdentity identity = new(
            processId: 1234,
            processName: "runtime-engine",
            executablePath: "runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            processStartedAtUtc: startedAt);

        Assert.Equal(1234, identity.ProcessId);
        Assert.Equal("runtime-engine", identity.ProcessName);
        Assert.Equal("runtime-engine.exe", identity.ExecutablePath);
        Assert.Equal("command-line-hash", identity.CommandLineHash);
        Assert.Equal("plan-hash", identity.PlanHash);
        Assert.Equal(startedAt, identity.ProcessStartedAtUtc);
    }

    [Fact]
    public static void ProcessIdentityRejectsInvalidProcessId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeProcessIdentity(
            processId: 0,
            processName: "runtime-engine",
            executablePath: "runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            processStartedAtUtc: DateTimeOffset.UtcNow));
    }

    [Fact]
    public static void ProcessIdentityRejectsEmptyProcessName()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeProcessIdentity(
            processId: 1234,
            processName: string.Empty,
            executablePath: "runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            processStartedAtUtc: DateTimeOffset.UtcNow));
    }

    private static RuntimeLockMetadata CreateMetadata()
    {
        RuntimeLockProcessMetadata process = new(
            processId: 1234,
            processName: "runtime-engine",
            executablePath: "runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            processStartedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5));

        return new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: "test-owner",
            process: process,
            acquiredAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class StaticRuntimeOwnershipDetector : IRuntimeOwnershipDetector
    {
        private readonly RuntimeOwnershipVerificationResult result;

        public StaticRuntimeOwnershipDetector(RuntimeOwnershipVerificationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            this.result = result;
        }

        public RuntimeOwnershipVerificationResult Verify(RuntimeLockMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);

            return result;
        }
    }
}

using System;
using System.Globalization;
using System.Text.Json;
using Xunit;
using Zapret2Pilot.Runtime.Locking;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Tests;

internal static class RuntimeTestData
{
    public const int ProcessId = 1234;
    public const string ProcessName = "runtime-engine";
    public const string ExecutablePath = "runtime-engine.exe";
    public const string CommandLineHash = "command-line-hash";
    public const string PlanHash = "plan-hash";
    public const string OwnerInstanceId = "test-owner";

    public static string CreateUniqueMutexName()
    {
        return $"Z2P_TEST_{Guid.NewGuid():N}";
    }

    public static RuntimeOwnershipLease RequireLease(RuntimeOwnershipAcquireResult result)
    {
        Assert.True(result.Acquired);
        Assert.NotNull(result.Lease);

        return result.Lease!;
    }

    public static RuntimeOwnershipLease AcquireOwnershipLease()
    {
        RuntimeOwnershipMutex ownershipMutex = new(CreateUniqueMutexName());
        RuntimeOwnershipAcquireResult ownershipResult = ownershipMutex.TryAcquire(TimeSpan.Zero);

        return RequireLease(ownershipResult);
    }

    public static RuntimeLockMetadata CreateLockMetadata(
        string ownerInstanceId = OwnerInstanceId,
        RuntimeLockProcessMetadata? process = null)
    {
        return new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: ownerInstanceId,
            process: process ?? CreateLockProcessMetadata(),
            acquiredAtUtc: DateTimeOffset.UtcNow);
    }

    public static RuntimeLockProcessMetadata CreateLockProcessMetadata(
        int processId = ProcessId,
        string processName = ProcessName)
    {
        return new RuntimeLockProcessMetadata(
            processId: processId,
            processName: processName,
            executablePath: ExecutablePath,
            commandLineHash: CommandLineHash,
            planHash: PlanHash,
            processStartedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    public static string CreateLockMetadataJson(
        string ownerInstanceId,
        int processId)
    {
        DateTimeOffset processStartedAtUtc = DateTimeOffset.Parse(
            "2026-06-18T09:59:55.0000000+00:00",
            CultureInfo.InvariantCulture);
        DateTimeOffset acquiredAtUtc = DateTimeOffset.Parse(
            "2026-06-18T10:00:00.0000000+00:00",
            CultureInfo.InvariantCulture);

        var payload = new
        {
            SchemaVersion = 1,
            OwnerInstanceId = ownerInstanceId,
            Process = new
            {
                ProcessId = processId,
                ProcessName,
                ExecutablePath,
                CommandLineHash,
                PlanHash,
                ProcessStartedAtUtc = processStartedAtUtc
            },
            AcquiredAtUtc = acquiredAtUtc
        };

        return JsonSerializer.Serialize(payload);
    }
}

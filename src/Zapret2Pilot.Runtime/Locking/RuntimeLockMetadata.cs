using System;
using System.Text.Json.Serialization;

namespace Zapret2Pilot.Runtime.Locking;

/// <summary>
/// Recovery metadata only. This metadata is never proof of runtime ownership.
/// </summary>
public sealed record class RuntimeLockMetadata
{
    [JsonConstructor]
    public RuntimeLockMetadata(
        int schemaVersion,
        string ownerInstanceId,
        int processId,
        string processName,
        string executablePath,
        string commandLineHash,
        string planHash,
        DateTimeOffset acquiredAtUtc,
        DateTimeOffset processStartedAtUtc)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ownerInstanceId);

        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLineHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHash);

        if (acquiredAtUtc == default)
        {
            throw new ArgumentException("Acquired timestamp must be specified.", nameof(acquiredAtUtc));
        }

        if (processStartedAtUtc == default)
        {
            throw new ArgumentException("Process started timestamp must be specified.", nameof(processStartedAtUtc));
        }

        SchemaVersion = schemaVersion;
        OwnerInstanceId = ownerInstanceId;
        ProcessId = processId;
        ProcessName = processName;
        ExecutablePath = executablePath;
        CommandLineHash = commandLineHash;
        PlanHash = planHash;
        AcquiredAtUtc = acquiredAtUtc;
        ProcessStartedAtUtc = processStartedAtUtc;
    }

    public int SchemaVersion { get; }

    public string OwnerInstanceId { get; }

    public int ProcessId { get; }

    public string ProcessName { get; }

    public string ExecutablePath { get; }

    public string CommandLineHash { get; }

    public string PlanHash { get; }

    public DateTimeOffset AcquiredAtUtc { get; }

    public DateTimeOffset ProcessStartedAtUtc { get; }
}

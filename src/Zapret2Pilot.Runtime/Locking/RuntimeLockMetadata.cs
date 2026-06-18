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
        RuntimeLockProcessMetadata process,
        DateTimeOffset acquiredAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerInstanceId);
        ArgumentNullException.ThrowIfNull(process);

        if (acquiredAtUtc == default)
        {
            throw new ArgumentException("Acquired timestamp must be specified.", nameof(acquiredAtUtc));
        }

        SchemaVersion = schemaVersion;
        OwnerInstanceId = ownerInstanceId;
        Process = process;
        AcquiredAtUtc = acquiredAtUtc;
    }

    public int SchemaVersion { get; }

    public string OwnerInstanceId { get; }

    public RuntimeLockProcessMetadata Process { get; }

    public DateTimeOffset AcquiredAtUtc { get; }
}

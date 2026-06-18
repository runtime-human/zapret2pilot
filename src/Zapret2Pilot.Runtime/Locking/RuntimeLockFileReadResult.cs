using System;

namespace Zapret2Pilot.Runtime.Locking;

public sealed record class RuntimeLockFileReadResult
{
    private RuntimeLockFileReadResult(
        RuntimeLockFileReadStatus status,
        RuntimeLockMetadata? metadata)
    {
        if (status == RuntimeLockFileReadStatus.Valid && metadata is null)
        {
            throw new ArgumentException("Valid lock file read result must include metadata.", nameof(metadata));
        }

        if (status != RuntimeLockFileReadStatus.Valid && metadata is not null)
        {
            throw new ArgumentException("Only valid lock file read results can include metadata.", nameof(metadata));
        }

        Status = status;
        Metadata = metadata;
    }

    public RuntimeLockFileReadStatus Status { get; }

    public RuntimeLockMetadata? Metadata { get; }

    public static RuntimeLockFileReadResult Missing()
    {
        return new RuntimeLockFileReadResult(
            RuntimeLockFileReadStatus.Missing,
            metadata: null);
    }

    public static RuntimeLockFileReadResult Invalid()
    {
        return new RuntimeLockFileReadResult(
            RuntimeLockFileReadStatus.Invalid,
            metadata: null);
    }

    public static RuntimeLockFileReadResult Valid(RuntimeLockMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new RuntimeLockFileReadResult(
            RuntimeLockFileReadStatus.Valid,
            metadata);
    }
}

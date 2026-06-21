using System;

namespace Zapret2Pilot.Runtime.Detection;

/// <summary>
/// Immutable snapshot of a process observed by <see cref="IProcessSystemAccessor"/>.
/// </summary>
public interface IProcessSnapshot
{
    int ProcessId { get; }

    string ProcessName { get; }

    string ExecutablePath { get; }

    /// <summary>
    /// Command line used to start the process, or <c>null</c> when the accessor
    /// cannot read another process's command line through the managed API
    /// (see <c>DEC-0028</c>). Callers must treat <c>null</c> as
    /// <see cref="RuntimeOwnershipVerificationStatus.CommandLineUnverifiable"/>.
    /// </summary>
    string? CommandLine { get; }

    DateTimeOffset StartedAtUtc { get; }
}

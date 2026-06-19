using System;
using Zapret2Pilot.Runtime.Detection;

namespace Zapret2Pilot.Runtime.Locking;

public sealed record class RuntimeLockProcessMetadata
{
    public RuntimeLockProcessMetadata(
        int processId,
        string processName,
        string executablePath,
        string commandLineHash,
        string planHash,
        DateTimeOffset processStartedAtUtc)
    {
        Identity = new RuntimeProcessIdentity(
            processId,
            processName,
            executablePath,
            commandLineHash,
            planHash,
            processStartedAtUtc);
    }

    public RuntimeProcessIdentity Identity { get; }

    public int ProcessId => Identity.ProcessId;

    public string ProcessName => Identity.ProcessName;

    public string ExecutablePath => Identity.ExecutablePath;

    public string CommandLineHash => Identity.CommandLineHash;

    public string PlanHash => Identity.PlanHash;

    public DateTimeOffset ProcessStartedAtUtc => Identity.ProcessStartedAtUtc;
}

using System;

namespace Zapret2Pilot.Runtime.Detection;

public sealed record class RuntimeProcessIdentity
{
    public RuntimeProcessIdentity(
        int processId,
        string processName,
        string executablePath,
        string commandLineHash,
        string planHash,
        DateTimeOffset processStartedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLineHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHash);

        if (processStartedAtUtc == default)
        {
            throw new ArgumentException("Process started timestamp must be specified.", nameof(processStartedAtUtc));
        }

        ProcessId = processId;
        ProcessName = processName;
        ExecutablePath = executablePath;
        CommandLineHash = commandLineHash;
        PlanHash = planHash;
        ProcessStartedAtUtc = processStartedAtUtc;
    }

    public int ProcessId { get; }

    public string ProcessName { get; }

    public string ExecutablePath { get; }

    public string CommandLineHash { get; }

    public string PlanHash { get; }

    public DateTimeOffset ProcessStartedAtUtc { get; }
}

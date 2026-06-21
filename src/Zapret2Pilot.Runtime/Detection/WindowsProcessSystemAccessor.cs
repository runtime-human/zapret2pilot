using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Zapret2Pilot.Runtime.Detection;

/// <summary>
/// Windows production implementation of <see cref="IProcessSystemAccessor"/>.
/// Reads PID, process name, executable path and start time through the
/// managed <see cref="System.Diagnostics.Process"/> API.
/// </summary>
/// <remarks>
/// Intentionally does not retrieve the command line of another process.
/// See <c>DEC-0028</c> and the <c>0.0.17</c> roadmap entry for the future
/// <c>NtQueryInformationProcess</c> / WMI plan. Until that is approved,
/// <see cref="WindowsProcessSnapshot.CommandLine"/> is always <c>null</c>
/// and the detector returns
/// <see cref="RuntimeOwnershipVerificationStatus.CommandLineUnverifiable"/>.
/// </remarks>
public sealed class WindowsProcessSystemAccessor : IProcessSystemAccessor
{
    public bool TryGetProcessById(int processId, out IProcessSnapshot? snapshot)
    {
        snapshot = null;

        Process? process = TryOpenProcess(processId);
        if (process is null)
        {
            return false;
        }

        try
        {
            string processName = process.ProcessName;
            DateTime startedAtUtc = process.StartTime.ToUniversalTime();

            // MainModule access can fail on protected / system processes.
            // We treat that as a failed read so the detector can map it to
            // its typed result status.
            string? executablePath = TryReadExecutablePath(process);
            if (executablePath is null)
            {
                return false;
            }

            snapshot = new WindowsProcessSnapshot(
                ProcessId: processId,
                ProcessName: processName,
                ExecutablePath: executablePath,
                StartedAtUtc: startedAtUtc);
            return true;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static Process? TryOpenProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            // Process no longer exists.
            return null;
        }
    }

    private static string? TryReadExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record WindowsProcessSnapshot(
        int ProcessId,
        string ProcessName,
        string ExecutablePath,
        DateTimeOffset StartedAtUtc) : IProcessSnapshot
    {
        // TODO(0.0.17): Populate via NtQueryInformationProcess or WMI — requires oracle approval.
        public string? CommandLine => null;
    }
}

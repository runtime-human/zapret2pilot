using System;
using Zapret2Pilot.Core.Runtime;

namespace Zapret2Pilot.Runtime.Hosting;

/// <summary>
/// Success payload produced by <c>RuntimeProcessHost.StartAsync</c> and
/// <c>RuntimeProcessHost.StopAsync</c>. Returned to callers wrapped in
/// the project's standard <see cref="Zapret2Pilot.Core.Results.Result{T}"/>
/// abstraction; failures surface as <c>Result&lt;RuntimeProcessHostResult&gt;.Failure</c>
/// carrying an <see cref="Zapret2Pilot.Core.Results.ErrorInfo"/>.
/// </summary>
/// <remarks>
/// Introduced as part of milestone 0.0.17 ahead of
/// <c>RuntimeProcessHost</c> so that the host's public contract is
/// stable before it is implemented. The host is expected to populate
/// this payload from a live <c>System.Diagnostics.Process</c> instance
/// after a successful launch.
/// </remarks>
public sealed class RuntimeProcessHostResult
{
    public RuntimeProcessHostResult(
        int processId,
        string processName,
        string executablePath,
        CompiledZapretPlan plan)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "Process id must be a positive integer.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(processName, nameof(processName));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath, nameof(executablePath));
        ArgumentNullException.ThrowIfNull(plan, nameof(plan));

        ProcessId = processId;
        ProcessName = processName;
        ExecutablePath = executablePath;
        Plan = plan;
    }

    /// <summary>
    /// Native process identifier (PID) of the launched runtime.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// Human-readable process name as reported by the operating
    /// system (typically the executable's file name without the
    /// extension).
    /// </summary>
    public string ProcessName { get; }

    /// <summary>
    /// Absolute path of the runtime executable that was launched.
    /// </summary>
    public string ExecutablePath { get; }

    /// <summary>
    /// Compiled Zapret plan that the host is running. Mirrors the plan
    /// that was provided in the start context so that callers can
    /// correlate the running process with the inputs that produced it.
    /// </summary>
    public CompiledZapretPlan Plan { get; }
}

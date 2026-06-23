using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Minimal post-start readiness probe used by
/// <c>RuntimeProcessHost</c> immediately after launching the runtime
/// executable.
/// </summary>
/// <remarks>
/// This checker is intentionally minimal for milestone 0.0.17: it
/// waits a short, caller-supplied readiness window and then asks the
/// <see cref="System.Diagnostics.Process"/> whether it has already
/// exited. It performs no I/O on the process (no log parsing, no
/// standard-output sniffing). Any richer readiness signal
/// (heartbeats, log scanning, native liveness) is out of scope and
/// will be layered on top in a later milestone.
/// </remarks>
public static class RuntimeReadinessChecker
{
    /// <summary>
    /// Default readiness window: 250 ms, in the middle of the
    /// 100–500 ms range documented for 0.0.17.
    /// </summary>
    public static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Wait <paramref name="readinessTimeout"/> for the process and
    /// then report whether it is still running. The wait is
    /// cancellable via <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="process">
    /// The just-started runtime process to probe. Must not be
    /// <c>null</c>. The caller is responsible for ensuring the
    /// process is in a started state.
    /// </param>
    /// <param name="readinessTimeout">
    /// How long to wait before checking the process state. Must be
    /// strictly positive. Use <see cref="DefaultReadinessTimeout"/>
    /// for the project-wide default.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the readiness wait. Cancellation
    /// propagates as <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> with <see cref="Unit.Instance"/>
    /// if the process is still running after the readiness window,
    /// or a <see cref="Result{T}.Failure"/> carrying a
    /// <see cref="ErrorCategory.Runtime"/> <see cref="ErrorInfo"/>
    /// when the process has already exited.
    /// </returns>
    public static async Task<Result<Unit>> CheckAsync(
        Process process,
        TimeSpan readinessTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process, nameof(process));

        if (readinessTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(readinessTimeout),
                readinessTimeout,
                "Readiness timeout must be a positive duration.");
        }

        await Task.Delay(readinessTimeout, cancellationToken).ConfigureAwait(false);

        if (process.HasExited)
        {
            return Result.Failure<Unit>(new ErrorInfo(
                code: "RUNTIME_NOT_READY",
                message: $"Runtime process exited with code {process.ExitCode} before the readiness timeout elapsed.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        return Result.Success(Unit.Instance);
    }
}

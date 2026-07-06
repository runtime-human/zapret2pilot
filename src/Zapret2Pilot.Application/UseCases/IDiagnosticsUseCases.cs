using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Typed facade for the diagnostics feature area. Introduced in
/// 0.0.25 Packet 2 (Scope E) as the long-term compile-time
/// contract that replaces reflection-based <c>CommandBus</c>
/// dispatch for diagnostics export.
/// </summary>
public interface IDiagnosticsUseCases
{
    /// <summary>
    /// Exports a diagnostics bundle to the requested directory.
    /// </summary>
    /// <param name="request">The export request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains a
    /// <see cref="DiagnosticsBundleReference"/> pointing at the
    /// produced bundle. In 0.0.25 Packet 2 the call returns a
    /// failure with code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<DiagnosticsBundleReference>> ExportAsync(
        ExportDiagnosticsRequest request,
        CancellationToken cancellationToken = default);
}

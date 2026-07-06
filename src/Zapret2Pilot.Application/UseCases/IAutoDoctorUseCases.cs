using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Typed facade for the AutoDoctor feature area. Introduced in
/// 0.0.25 Packet 2 (Scope E) as the long-term compile-time
/// contract that replaces reflection-based <c>CommandBus</c>
/// dispatch for diagnostic health checks.
/// </summary>
public interface IAutoDoctorUseCases
{
    /// <summary>
    /// Runs a quick AutoDoctor check.
    /// </summary>
    /// <param name="request">The quick-check request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains an
    /// <see cref="AutoDoctorSummaryReadModel"/>. In 0.0.25 Packet 2
    /// the call returns a failure with code
    /// <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<AutoDoctorSummaryReadModel>> RunQuickCheckAsync(
        RunQuickCheckRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a full AutoDoctor check.
    /// </summary>
    /// <param name="request">The full-check request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains an
    /// <see cref="AutoDoctorSummaryReadModel"/>. In 0.0.25 Packet 2
    /// the call returns a failure with code
    /// <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<AutoDoctorSummaryReadModel>> RunFullCheckAsync(
        RunFullCheckRequest request,
        CancellationToken cancellationToken = default);
}

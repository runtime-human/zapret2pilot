using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Typed facade for the runtime update feature area. Introduced in
/// 0.0.25 Packet 2 (Scope E) as the long-term compile-time
/// contract that replaces reflection-based <c>CommandBus</c>
/// dispatch for TUF-mediated runtime update flows.
/// </summary>
public interface IRuntimeUpdateUseCases
{
    /// <summary>
    /// Checks for a runtime update.
    /// </summary>
    /// <param name="request">The check request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains a
    /// <see cref="RuntimeBundleReference"/> for the update bundle.
    /// In 0.0.25 Packet 2 the call returns a failure with code
    /// <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<RuntimeBundleReference>> CheckForUpdateAsync(
        CheckForUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a previously downloaded update candidate.
    /// </summary>
    /// <param name="request">The activation request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> wrapping <see cref="Unit"/> on
    /// success. In 0.0.25 Packet 2 the call returns a failure with
    /// code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<Unit>> ActivateCandidateAsync(
        ActivateCandidateRequest request,
        CancellationToken cancellationToken = default);
}

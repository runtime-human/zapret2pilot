using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Typed facade for the runtime feature area. Introduced in 0.0.25
/// Packet 2 (Scope E) as the long-term compile-time contract that
/// replaces reflection-based <c>CommandBus</c> dispatch for
/// security-critical and frequently exercised runtime operations.
/// The legacy <c>ICommandBus</c> remains registered for non-critical
/// features and is not removed by this packet.
/// </summary>
public interface IRuntimeUseCases
{
    /// <summary>
    /// Starts a runtime session.
    /// </summary>
    /// <param name="request">The start request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains a
    /// <see cref="RuntimeSessionReadModel"/> describing the new
    /// session. In 0.0.25 Packet 2 the call returns a failure with
    /// code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<RuntimeSessionReadModel>> StartAsync(
        StartRuntimeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a running runtime session.
    /// </summary>
    /// <param name="request">The stop request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> wrapping <see cref="Unit"/> on
    /// success. In 0.0.25 Packet 2 the call returns a failure with
    /// code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<Unit>> StopAsync(
        StopRuntimeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current runtime status snapshot.
    /// </summary>
    /// <param name="request">The status request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains a
    /// <see cref="RuntimeStatusReadModel"/>. In 0.0.25 Packet 2 the
    /// call returns a failure with code
    /// <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<RuntimeStatusReadModel>> GetStatusAsync(
        GetRuntimeStatusRequest request,
        CancellationToken cancellationToken = default);
}

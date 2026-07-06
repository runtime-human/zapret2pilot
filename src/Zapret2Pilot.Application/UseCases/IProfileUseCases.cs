using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Typed facade for the profile feature area. Introduced in 0.0.25
/// Packet 2 (Scope E) as the long-term compile-time contract that
/// replaces reflection-based <c>CommandBus</c> dispatch for
/// frequently exercised profile operations.
/// </summary>
public interface IProfileUseCases
{
    /// <summary>
    /// Lists all known profiles.
    /// </summary>
    /// <param name="request">The list request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains a
    /// <see cref="ProfileReadModel"/>. In 0.0.25 Packet 2 the call
    /// returns a failure with code
    /// <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<ProfileReadModel>> ListAsync(
        ListProfilesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a profile from disk.
    /// </summary>
    /// <param name="request">The import request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that, on success, contains a
    /// <see cref="ProfileReadModel"/> describing the profile store
    /// after import. In 0.0.25 Packet 2 the call returns a failure
    /// with code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<ProfileReadModel>> ImportAsync(
        ImportProfileRequest request,
        CancellationToken cancellationToken = default);
}

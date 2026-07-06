using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Typed facade for the data management feature area. Introduced in
/// 0.0.25 Packet 2 (Scope E) as the long-term compile-time
/// contract that replaces reflection-based <c>CommandBus</c>
/// dispatch for storage cleanup operations.
/// </summary>
public interface IDataManagementUseCases
{
    /// <summary>
    /// Cleans up old data from local stores.
    /// </summary>
    /// <param name="request">The cleanup request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> wrapping <see cref="Unit"/> on
    /// success. In 0.0.25 Packet 2 the call returns a failure with
    /// code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    Task<Result<Unit>> CleanupOldDataAsync(
        CleanupOldDataRequest request,
        CancellationToken cancellationToken = default);
}

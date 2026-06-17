using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.Commands;

/// <summary>
/// Dispatches application commands to registered command handlers.
/// </summary>
public interface ICommandBus
{
    Task<Result<TResponse>> SendAsync<TResponse>(
        IAppCommand<TResponse> command,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;
}

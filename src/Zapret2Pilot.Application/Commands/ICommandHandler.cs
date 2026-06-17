using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.Commands;

/// <summary>
/// Handles an application command.
/// </summary>
/// <typeparam name="TCommand">Command type.</typeparam>
/// <typeparam name="TResponse">Command response type.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : IAppCommand<TResponse>
    where TResponse : notnull
{
    Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken = default);
}

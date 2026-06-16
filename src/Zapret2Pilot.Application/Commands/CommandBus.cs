using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.Commands;

/// <summary>
/// Default command bus implementation backed by an <see cref="IServiceProvider" />.
/// </summary>
public sealed class CommandBus : ICommandBus
{
    private static readonly MethodInfo DispatchToHandlerAsyncMethod =
        typeof(CommandBus).GetMethod(
            nameof(DispatchToHandlerAsync),
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Command dispatch helper method was not found.");

    private readonly IServiceProvider serviceProvider;

    public CommandBus(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        this.serviceProvider = serviceProvider;
    }

    public Task<Result<TResponse>> SendAsync<TResponse>(
        IAppCommand<TResponse> command,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        ArgumentNullException.ThrowIfNull(command);

        Type commandType = command.GetType();
        Type responseType = typeof(TResponse);
        Type handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, responseType);

        object? handler = serviceProvider.GetService(handlerType);

        if (handler is null)
        {
            return Task.FromResult(Result.Failure<TResponse>(
                CreateMissingHandlerError(commandType, responseType)));
        }

        if (!handlerType.IsInstanceOfType(handler))
        {
            return Task.FromResult(Result.Failure<TResponse>(
                CreateInvalidHandlerResultError(commandType, responseType)));
        }

        MethodInfo dispatchMethod = DispatchToHandlerAsyncMethod.MakeGenericMethod(
            commandType,
            responseType);

        object? dispatchResult = dispatchMethod.Invoke(
            obj: null,
            parameters: [handler, command, cancellationToken]);

        if (dispatchResult is Task<Result<TResponse>> typedResult)
        {
            return typedResult;
        }

        return Task.FromResult(Result.Failure<TResponse>(
            CreateInvalidHandlerResultError(commandType, responseType)));
    }

    private static Task<Result<TResponse>> DispatchToHandlerAsync<TCommand, TResponse>(
        object handler,
        IAppCommand<TResponse> command,
        CancellationToken cancellationToken)
        where TCommand : IAppCommand<TResponse>
        where TResponse : notnull
    {
        return ((ICommandHandler<TCommand, TResponse>)handler).HandleAsync(
            (TCommand)command,
            cancellationToken);
    }

    private static ErrorInfo CreateMissingHandlerError(Type commandType, Type responseType)
    {
        return new ErrorInfo(
            "Z2P.APPLICATION.COMMAND_HANDLER_NOT_FOUND",
            $"No command handler was registered for command '{commandType.FullName}' and response '{responseType.FullName}'.",
            ErrorSeverity.Error,
            ErrorCategory.Application);
    }

    private static ErrorInfo CreateInvalidHandlerResultError(Type commandType, Type responseType)
    {
        return new ErrorInfo(
            "Z2P.APPLICATION.COMMAND_HANDLER_INVALID_RESULT",
            $"Command handler for command '{commandType.FullName}' and response '{responseType.FullName}' returned an invalid result.",
            ErrorSeverity.Error,
            ErrorCategory.Application);
    }
}

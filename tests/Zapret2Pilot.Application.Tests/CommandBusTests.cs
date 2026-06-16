using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Application.Commands;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.Tests;

public sealed class CommandBusTests
{
    [Fact]
    public static async Task SendAsyncDispatchesCommandToRegisteredHandler()
    {
        TestServiceProvider serviceProvider = new();
        serviceProvider.Register<ICommandHandler<PingCommand, PingResponse>>(
            new PingCommandHandler());

        ICommandBus commandBus = new CommandBus(serviceProvider);

        Result<PingResponse> result = await commandBus.SendAsync(
            new PingCommand("test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("test", result.Value.Message);
    }

    [Fact]
    public static async Task SendAsyncReturnsFailureWhenHandlerIsMissing()
    {
        ICommandBus commandBus = new CommandBus(new TestServiceProvider());

        Result<PingResponse> result = await commandBus.SendAsync(
            new PingCommand("test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.APPLICATION.COMMAND_HANDLER_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
        Assert.Equal(ErrorCategory.Application, result.Error.Category);
    }

    [Fact]
    public static async Task SendAsyncRejectsNullCommand()
    {
        ICommandBus commandBus = new CommandBus(new TestServiceProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            commandBus.SendAsync<PingResponse>(
                null!,
                CancellationToken.None));
    }

    [Fact]
    public static async Task SendAsyncReturnsHandlerFailureResult()
    {
        ErrorInfo error = new(
            "Z2P.TEST.FAILURE",
            "Handler failed.",
            ErrorSeverity.Error,
            ErrorCategory.Application);

        TestServiceProvider serviceProvider = new();
        serviceProvider.Register<ICommandHandler<PingCommand, PingResponse>>(
            new FailingPingCommandHandler(error));

        ICommandBus commandBus = new CommandBus(serviceProvider);

        Result<PingResponse> result = await commandBus.SendAsync(
            new PingCommand("test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    private sealed record class PingCommand(string Message) : IAppCommand<PingResponse>;

    private sealed record class PingResponse(string Message);

    private sealed class PingCommandHandler : ICommandHandler<PingCommand, PingResponse>
    {
        public Task<Result<PingResponse>> HandleAsync(
            PingCommand command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result.Success(new PingResponse(command.Message)));
        }
    }

    private sealed class FailingPingCommandHandler : ICommandHandler<PingCommand, PingResponse>
    {
        private readonly ErrorInfo error;

        public FailingPingCommandHandler(ErrorInfo error)
        {
            ArgumentNullException.ThrowIfNull(error);

            this.error = error;
        }

        public Task<Result<PingResponse>> HandleAsync(
            PingCommand command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result.Failure<PingResponse>(error));
        }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> services = [];

        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            services.TryGetValue(serviceType, out object? service);

            return service;
        }

        public void Register<TService>(TService service)
            where TService : notnull
        {
            services[typeof(TService)] = service;
        }
    }
}

using System;
using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Broker.Transport;

if (!BrokerBootstrapLaunchOptions.TryParse(
        args,
        out BrokerBootstrapLaunchOptions? launchOptions,
        out string? parseError)
    || launchOptions is null)
{
    await Console.Error.WriteLineAsync(
        $"Broker bootstrap arguments rejected: {parseError}");
    return 2;
}

try
{
    WindowsBrokerPeerIdentityResolver identityResolver = new();
    BrokerBootstrapClient bootstrapClient = new(identityResolver);

    using BrokerStartupContext startupContext =
        await bootstrapClient.ReceiveAsync(
            launchOptions,
            CancellationToken.None).ConfigureAwait(false);

    using IHost host = BrokerHostBuilder.Build(
        Array.Empty<string>(),
        startupContext);

    await host.RunAsync().ConfigureAwait(false);
    return 0;
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync(
        "Broker bootstrap was cancelled.");
    return 3;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync(
        $"Broker bootstrap failed: {ex.GetType().Name}.");
    return 4;
}

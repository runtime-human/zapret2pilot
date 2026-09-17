using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Broker.Hosting;

using IHost host = BrokerHostBuilder.Build(args);
await host.RunAsync().ConfigureAwait(false);

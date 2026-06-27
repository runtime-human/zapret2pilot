using System;
using System.Threading;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReactiveUI.Avalonia;
using Zapret2Pilot.App.Shell;

namespace Zapret2Pilot.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using IHost host = AppHost.Build(args);

        AppHost.SetCurrent(host);

        host.StartAsync().GetAwaiter().GetResult();

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(5));

            host.StopAsync(shutdownTimeout.Token)
                .GetAwaiter()
                .GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI(static _ => { })
            .LogToTrace();
    }
}

internal static class AppHost
{
    private static IHost? current;

    public static IServiceProvider Services =>
        current?.Services
        ?? throw new InvalidOperationException("Application host has not been initialized.");

    public static IHost Build(string[] args)
    {
        return Host
            .CreateDefaultBuilder(args)
            .ConfigureServices(static services =>
            {
                string databasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Zapret2Pilot",
                    "z2p.db");
                services.AddRuntimeKernelStateStore(databasePath);
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    public static void SetCurrent(IHost host)
    {
        current = host ?? throw new ArgumentNullException(nameof(host));
    }
}

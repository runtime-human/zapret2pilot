using System;
using System.Reflection;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReactiveUI;
using ReactiveUI.Avalonia;
using Zapret2Pilot.App.Diagnostics;
using Zapret2Pilot.App.Hosting;
using Zapret2Pilot.App.Input;
using Zapret2Pilot.App.Lifecycle;
using Zapret2Pilot.App.Shell;

namespace Zapret2Pilot.App;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // 0.0.25 Scope H: validate CLI before touching the host
        // bootstrap. Anything not in the allowlist is rejected here
        // so it cannot reach Generic Host, configuration sources, or
        // environment-variable bindings.
        CliParseResult cliResult = CliAllowlist.Parse(args);
        if (!cliResult.IsAllowed)
        {
            await Console.Error.WriteLineAsync(
                $"Error {cliResult.ErrorCode}: {cliResult.ErrorMessage}");
            return 1;
        }

        if (cliResult.Mode == CliLaunchMode.Help)
        {
            Console.WriteLine("Zapret2Pilot (z2p.exe)");
            Console.WriteLine("Usage: z2p [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --help, -h    Show this help message");
            Console.WriteLine("  --version     Show version information");
            return 0;
        }

        if (cliResult.Mode == CliLaunchMode.Version)
        {
            string version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "0.0.0";
            Console.WriteLine($"Zapret2Pilot v{version}");
            return 0;
        }

        using IHost host = Z2PHostBuilder.Build(args);

        await host.StartAsync();

        IExceptionPolicy exceptionPolicy = host.Services.GetRequiredService<IExceptionPolicy>();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                exceptionPolicy.Handle(ex, ExceptionContext.AppDomain);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ExceptionSeverity severity = exceptionPolicy.Handle(e.Exception, ExceptionContext.TaskScheduler);
            if (severity == ExceptionSeverity.Recoverable)
            {
                e.SetObserved();
            }
        };

        // ReactiveUI v23 moved the global exception observer from
        // RxApp.DefaultExceptionHandler (removed) to the RxAppBuilder
        // pipeline (WithExceptionHandler). The plan's intent — wire the
        // global observer to IExceptionPolicy.Handle — is preserved.
        IObserver<Exception> reactiveUiObserver = Observer.Create<Exception>(ex =>
            exceptionPolicy.Handle(ex, ExceptionContext.ReactiveUI));

        try
        {
            // 0.0.28 Packet 3 (P0-9): resolve the lifecycle
            // coordinator after the host has started, then wire its
            // SignalShellVisible() call to the shell's
            // MainWindow.Opened event. The coordinator waits in
            // WaitingForShell until the shell actually becomes
            // visible, so runtime commands cannot be enabled before
            // a real MainWindow exists.
            IZ2PApplicationLifecycleCoordinator lifecycle =
                host.Services.GetRequiredService<IZ2PApplicationLifecycleCoordinator>();

            return BuildAvaloniaApp(reactiveUiObserver)
                .AfterSetup(_ =>
                {
                    Dispatcher.UIThread.UnhandledException += (_, e) =>
                    {
                        exceptionPolicy.Handle(e.Exception, ExceptionContext.Dispatcher);
                        e.Handled = exceptionPolicy.ShouldContinue(e.Exception);
                    };
                })
                .StartWithClassicDesktopLifetime(args, desktop =>
                {
                    MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
                    mainWindow.Opened += (_, _) => lifecycle.SignalShellVisible();
                    desktop.MainWindow = mainWindow;
                });
        }
        finally
        {
            using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(5));

            await host.StopAsync(shutdownTimeout.Token);
        }
    }

    public static AppBuilder BuildAvaloniaApp(IObserver<Exception> reactiveUiObserver)
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI(rxui => rxui.WithExceptionHandler(reactiveUiObserver))
            .LogToTrace();
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.App.Diagnostics;
using Zapret2Pilot.App.Input;
using Zapret2Pilot.App.Lifecycle;
using Zapret2Pilot.App.Lifecycle.Steps;
using Zapret2Pilot.App.Navigation;
using Zapret2Pilot.App.Runtime;
using Zapret2Pilot.App.Shell;
using Zapret2Pilot.App.Threading;
using Zapret2Pilot.Application.UseCases;
using Zapret2Pilot.Contracts.Client;

namespace Zapret2Pilot.App.DependencyInjection;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddZ2PAppServices(this IServiceCollection services)
    {
        // Typed use-case facades (0.0.25 Packet 2 / Scope E).
        services.AddZ2PApplicationUseCases();

        services.AddSingleton<INavigationPageFactory, NavigationPageFactory>();
        services.AddSingleton<NavigationRouter>(static sp => new NavigationRouter(
            sp.GetRequiredService<INavigationPageFactory>(),
            RouteId.Dashboard));
        services.AddSingleton<IUiScheduler, AvaloniaUiScheduler>();
        services.AddSingleton<IExceptionPolicy, ExceptionPolicy>();

        // 0.0.28 Packet 3 (P0-9): IStartupStep pipeline. Steps are
        // registered as singletons in the canonical order so the
        // coordinator's pipeline runs StorageRecovery →
        // DeploymentVerification → OwnershipRecovery →
        // CompatibilityPreflight.
        services.AddSingleton<IStartupStep, StorageRecoveryStartupStep>();
        services.AddSingleton<IStartupStep, DeploymentVerificationStartupStep>();
        services.AddSingleton<IStartupStep, OwnershipRecoveryStartupStep>();
        services.AddSingleton<IStartupStep, CompatibilityPreflightStartupStep>();

        services.AddSingleton<Z2PApplicationLifecycleCoordinator>(static sp =>
            new Z2PApplicationLifecycleCoordinator(
                steps: sp.GetServices<IStartupStep>(),
                logger: sp.GetRequiredService<ILogger<Z2PApplicationLifecycleCoordinator>>(),
                services: sp,
                loggerFactory: sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IZ2PApplicationLifecycleCoordinator>(sp => sp.GetRequiredService<Z2PApplicationLifecycleCoordinator>());
        services.AddHostedService(sp => sp.GetRequiredService<Z2PApplicationLifecycleCoordinator>());

        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IExternalLinkLauncher, ExternalLinkLauncher>();

        // #17: App owns only a late-bound RuntimeClient projection.
        // Runtime Broker elevation/bootstrap is a separate hosted lifetime
        // service and the existing OwnershipRecovery startup phase invokes it
        // after the shell is visible. Until attach succeeds the client remains
        // disconnected and fail-closed.
        services.AddSingleton<BrokerRuntimeClient>();
        services.AddSingleton<IRuntimeClient>(
            static sp => sp.GetRequiredService<BrokerRuntimeClient>());

        services.AddSingleton<IAppProcessBindingProvider, WindowsAppProcessBindingProvider>();
        services.AddSingleton<IBrokerExecutableLocator, SiblingBrokerExecutableLocator>();
        services.AddSingleton<IElevatedBrokerLauncher, WindowsElevatedBrokerLauncher>();
        services.AddSingleton<IBrokerBootstrapServerFactory, WindowsBrokerBootstrapServerFactory>();
        services.AddSingleton<IRuntimeBrokerSessionFactory, NamedPipeRuntimeBrokerSessionFactory>();

        services.AddSingleton<RuntimeBrokerBootstrapper>();
        services.AddSingleton<IRuntimeBrokerBootstrapper>(
            static sp => sp.GetRequiredService<RuntimeBrokerBootstrapper>());
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<RuntimeBrokerBootstrapper>());

        services.AddSingleton(static sp => new MainWindowViewModel(
            navigationRouter: sp.GetRequiredService<NavigationRouter>(),
            uiScheduler: sp.GetRequiredService<IUiScheduler>(),
            runtimeClient: sp.GetRequiredService<IRuntimeClient>(),
            logger: sp.GetRequiredService<ILogger<MainWindowViewModel>>(),
            coordinator: sp.GetRequiredService<IZ2PApplicationLifecycleCoordinator>(),
            exceptionPolicy: sp.GetRequiredService<IExceptionPolicy>()));
        services.AddSingleton<MainWindow>();

        return services;
    }
}

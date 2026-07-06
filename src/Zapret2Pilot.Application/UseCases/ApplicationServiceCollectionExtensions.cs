using Microsoft.Extensions.DependencyInjection;
using Zapret2Pilot.Application.Commands;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Composition-root helpers for the typed use-case facades
/// introduced in 0.0.25 Packet 2 (Scope E).
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the long-term typed feature facades as singletons
    /// in the supplied <see cref="IServiceCollection"/>. Each
    /// facade is implemented by a parameterless stub that returns
    /// a <see cref="Zapret2Pilot.Core.Results.Result{T}"/> failure
    /// with code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>; the real
    /// production logic is wired in later packets.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddZ2PApplicationUseCases(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRuntimeUseCases, RuntimeUseCases>();
        services.AddSingleton<IProfileUseCases, ProfileUseCases>();
        services.AddSingleton<IRulesUseCases, RulesUseCases>();
        services.AddSingleton<IAutoDoctorUseCases, AutoDoctorUseCases>();
        services.AddSingleton<IDiagnosticsUseCases, DiagnosticsUseCases>();
        services.AddSingleton<IRuntimeUpdateUseCases, RuntimeUpdateUseCases>();
        services.AddSingleton<IDataManagementUseCases, DataManagementUseCases>();

        return services;
    }

    /// <summary>
    /// Registers the reflection-based <see cref="ICommandBus"/>
    /// used by non-critical legacy features. The bus remains the
    /// sole dispatcher for the commands that were already routed
    /// through it before 0.0.25 Packet 2; the typed use-case
    /// facades do not call it.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddZ2PApplicationCommandBus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<CommandBus>();
        services.AddSingleton<ICommandBus>(static sp => sp.GetRequiredService<CommandBus>());

        return services;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapret2Pilot.Application.Commands;
using Zapret2Pilot.Application.UseCases;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.Tests.UseCases;

/// <summary>
/// Verifies the typed feature facades introduced in 0.0.25 Packet 2
/// (Scope E):
/// <list type="bullet">
///   <item>Each facade resolves from a real DI container built by
///   <see cref="ApplicationServiceCollectionExtensions.AddZ2PApplicationUseCases"/>.</item>
///   <item>Each method returns a failed <see cref="Result{T}"/>
///   with code <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c> (the contract
///   for the stub implementations).</item>
///   <item>No <see cref="IAppCommand{TResponse}"/> or
///   <see cref="CommandBus"/> reference leaks into the facade
///   interfaces themselves.</item>
///   <item>The legacy <see cref="ICommandBus"/> remains
///   registrable for non-critical features.</item>
/// </list>
/// </summary>
public sealed class FeatureFacadeTests
{
    [Fact]
    public void UseCasesFolderContainsExpectedFacadesAndStubs()
    {
        Assembly assembly = typeof(IRuntimeUseCases).Assembly;

        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.IRuntimeUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.IProfileUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.IRulesUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.IAutoDoctorUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.IDiagnosticsUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.IRuntimeUpdateUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.IDataManagementUseCases"));

        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.RuntimeUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.ProfileUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.RulesUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.AutoDoctorUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.DiagnosticsUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.RuntimeUpdateUseCases"));
        Assert.NotNull(assembly.GetType("Zapret2Pilot.Application.UseCases.DataManagementUseCases"));
    }

    [Fact]
    public void FacadeInterfacesDoNotReferenceCommandBus()
    {
        Type[] facadeInterfaces =
        [
            typeof(IRuntimeUseCases),
            typeof(IProfileUseCases),
            typeof(IRulesUseCases),
            typeof(IAutoDoctorUseCases),
            typeof(IDiagnosticsUseCases),
            typeof(IRuntimeUpdateUseCases),
            typeof(IDataManagementUseCases),
        ];

        foreach (Type facade in facadeInterfaces)
        {
            WalkType(facade, type =>
            {
                if (type.Namespace == typeof(IAppCommand<>).Namespace
                    || type.Namespace == typeof(ICommandBus).Namespace
                    || type.Namespace == typeof(CommandBus).Namespace
                    || type.Namespace == typeof(ICommandHandler<,>).Namespace)
                {
                    Assert.Fail(
                        $"Facade '{facade.FullName}' must not reference '{type.FullName}' from the Commands namespace.");
                }
            });
        }
    }

    [Fact]
    public void FacadeInterfacesDeclareOnlyCoreResults()
    {
        // The facades must be self-contained: every method on the
        // facade contract must mention only types from the
        // Application assembly, the Core results assembly, and
        // System.*. In particular, they must not depend on
        // IAppCommand / CommandBus / ICommandHandler.
        Type[] facadeInterfaces =
        [
            typeof(IRuntimeUseCases),
            typeof(IProfileUseCases),
            typeof(IRulesUseCases),
            typeof(IAutoDoctorUseCases),
            typeof(IDiagnosticsUseCases),
            typeof(IRuntimeUpdateUseCases),
            typeof(IDataManagementUseCases),
        ];

        Assembly applicationAssembly = typeof(IRuntimeUseCases).Assembly;
        Assembly coreResultsAssembly = typeof(Result<>).Assembly;

        foreach (Type facade in facadeInterfaces)
        {
            WalkType(facade, type =>
            {
                if (type.Assembly == typeof(object).Assembly)
                {
                    return;
                }

                if (type.Assembly == applicationAssembly)
                {
                    return;
                }

                if (type.Assembly == coreResultsAssembly)
                {
                    return;
                }

                // System.* and Microsoft.* hosting infrastructure are
                // permitted (e.g. CancellationToken from
                // System.Threading.Tasks). The check below narrows
                // the rejection to the Commands namespace, which is
                // the leak we want to prevent.
                if (type.Namespace == typeof(IAppCommand<>).Namespace
                    || type.Namespace == typeof(ICommandBus).Namespace
                    || type.Namespace == typeof(CommandBus).Namespace)
                {
                    Assert.Fail(
                        $"Facade '{facade.FullName}' must not depend on '{type.FullName}' from the Commands namespace.");
                }
            });
        }
    }

    [Fact]
    public void AddZ2PApplicationUseCasesRegistersAllFacadesAsSingletons()
    {
        ServiceCollection services = new();
        services.AddZ2PApplicationUseCases();

        ServiceDescriptor[] expected =
        [
            new(typeof(IRuntimeUseCases), typeof(RuntimeUseCases), ServiceLifetime.Singleton),
            new(typeof(IProfileUseCases), typeof(ProfileUseCases), ServiceLifetime.Singleton),
            new(typeof(IRulesUseCases), typeof(RulesUseCases), ServiceLifetime.Singleton),
            new(typeof(IAutoDoctorUseCases), typeof(AutoDoctorUseCases), ServiceLifetime.Singleton),
            new(typeof(IDiagnosticsUseCases), typeof(DiagnosticsUseCases), ServiceLifetime.Singleton),
            new(typeof(IRuntimeUpdateUseCases), typeof(RuntimeUpdateUseCases), ServiceLifetime.Singleton),
            new(typeof(IDataManagementUseCases), typeof(DataManagementUseCases), ServiceLifetime.Singleton),
        ];

        foreach (ServiceDescriptor descriptor in expected)
        {
            ServiceDescriptor? match = services.SingleOrDefault(
                sd => sd.ServiceType == descriptor.ServiceType);

            Assert.NotNull(match);
            Assert.Equal(descriptor.Lifetime, match!.Lifetime);
            Assert.Equal(descriptor.ImplementationType, match.ImplementationType);
        }
    }

    [Fact]
    public void AllFacadesResolveFromBuiltServiceProvider()
    {
        ServiceCollection services = new();
        services.AddZ2PApplicationUseCases();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRuntimeUseCases>());
        Assert.NotNull(provider.GetRequiredService<IProfileUseCases>());
        Assert.NotNull(provider.GetRequiredService<IRulesUseCases>());
        Assert.NotNull(provider.GetRequiredService<IAutoDoctorUseCases>());
        Assert.NotNull(provider.GetRequiredService<IDiagnosticsUseCases>());
        Assert.NotNull(provider.GetRequiredService<IRuntimeUpdateUseCases>());
        Assert.NotNull(provider.GetRequiredService<IDataManagementUseCases>());
    }

    [Fact]
    public void FacadeSingletonsReturnSameInstanceOnRepeatedResolution()
    {
        ServiceCollection services = new();
        services.AddZ2PApplicationUseCases();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IRuntimeUseCases>(),
            provider.GetRequiredService<IRuntimeUseCases>());
    }

    [Fact]
    public void AddZ2PApplicationCommandBusRegistersLegacyBus()
    {
        ServiceCollection services = new();
        services.AddZ2PApplicationCommandBus();

        using ServiceProvider provider = services.BuildServiceProvider();

        ICommandBus bus = provider.GetRequiredService<ICommandBus>();
        Assert.NotNull(bus);
        Assert.IsType<CommandBus>(bus);
    }

    [Fact]
    public void UseCasesAndCommandBusCoexistInTheSameServiceCollection()
    {
        ServiceCollection services = new();
        services.AddZ2PApplicationUseCases();
        services.AddZ2PApplicationCommandBus();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRuntimeUseCases>());
        Assert.NotNull(provider.GetRequiredService<ICommandBus>());
    }

    // ------------------------------------------------------------------------
    // Per-facade behavioural checks: every method returns a
    // failed Result<T> with code "Z2P.APPLICATION.NOT_IMPLEMENTED".
    // ------------------------------------------------------------------------

    [Fact]
    public async Task RuntimeUseCasesReturnsNotImplementedForEveryMethod()
    {
        await using ServiceProvider provider = BuildProvider();
        IRuntimeUseCases useCases = provider.GetRequiredService<IRuntimeUseCases>();

        OperationId id = NewOperationId();

        await AssertNotImplementedAsync(
            () => useCases.StartAsync(new StartRuntimeRequest(id)));
        await AssertNotImplementedAsync(
            () => useCases.StopAsync(new StopRuntimeRequest(id)));
        await AssertNotImplementedAsync(
            () => useCases.GetStatusAsync(new GetRuntimeStatusRequest(id)));
    }

    [Fact]
    public async Task ProfileUseCasesReturnsNotImplementedForEveryMethod()
    {
        await using ServiceProvider provider = BuildProvider();
        IProfileUseCases useCases = provider.GetRequiredService<IProfileUseCases>();

        OperationId id = NewOperationId();

        await AssertNotImplementedAsync(
            () => useCases.ListAsync(new ListProfilesRequest(id)));
        await AssertNotImplementedAsync(
            () => useCases.ImportAsync(new ImportProfileRequest(id, "C:/profiles/example.json")));
    }

    [Fact]
    public async Task RulesUseCasesReturnsNotImplementedForListAsync()
    {
        await using ServiceProvider provider = BuildProvider();
        IRulesUseCases useCases = provider.GetRequiredService<IRulesUseCases>();

        await AssertNotImplementedAsync(
            () => useCases.ListAsync(new ListRulesRequest(NewOperationId())));
    }

    [Fact]
    public async Task AutoDoctorUseCasesReturnsNotImplementedForEveryMethod()
    {
        await using ServiceProvider provider = BuildProvider();
        IAutoDoctorUseCases useCases = provider.GetRequiredService<IAutoDoctorUseCases>();

        OperationId id = NewOperationId();

        await AssertNotImplementedAsync(
            () => useCases.RunQuickCheckAsync(new RunQuickCheckRequest(id)));
        await AssertNotImplementedAsync(
            () => useCases.RunFullCheckAsync(new RunFullCheckRequest(id)));
    }

    [Fact]
    public async Task DiagnosticsUseCasesReturnsNotImplementedForExportAsync()
    {
        await using ServiceProvider provider = BuildProvider();
        IDiagnosticsUseCases useCases = provider.GetRequiredService<IDiagnosticsUseCases>();

        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "z2p-usecase-tests-" + Guid.NewGuid().ToString("N"));

        await AssertNotImplementedAsync(
            () => useCases.ExportAsync(new ExportDiagnosticsRequest(NewOperationId(), tempDir)));
    }

    [Fact]
    public async Task RuntimeUpdateUseCasesReturnsNotImplementedForEveryMethod()
    {
        await using ServiceProvider provider = BuildProvider();
        IRuntimeUpdateUseCases useCases = provider.GetRequiredService<IRuntimeUpdateUseCases>();

        OperationId id = NewOperationId();

        await AssertNotImplementedAsync(
            () => useCases.CheckForUpdateAsync(new CheckForUpdateRequest(id)));
        await AssertNotImplementedAsync(
            () => useCases.ActivateCandidateAsync(new ActivateCandidateRequest(id, "C:/runtime/bundle.zip")));
    }

    [Fact]
    public async Task DataManagementUseCasesReturnsNotImplementedForCleanupOldDataAsync()
    {
        await using ServiceProvider provider = BuildProvider();
        IDataManagementUseCases useCases = provider.GetRequiredService<IDataManagementUseCases>();

        await AssertNotImplementedAsync(
            () => useCases.CleanupOldDataAsync(new CleanupOldDataRequest(NewOperationId())));
    }

    // ------------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------------

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddZ2PApplicationUseCases();
        return services.BuildServiceProvider();
    }

    private static OperationId NewOperationId() => new(Guid.NewGuid());

    private static async Task AssertNotImplementedAsync<T>(Func<Task<Result<T>>> action)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);

        Result<T> result = await action().ConfigureAwait(false);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.APPLICATION.NOT_IMPLEMENTED", result.Error.Code);
        Assert.Equal(ErrorSeverity.Warning, result.Error.Severity);
        Assert.Equal(ErrorCategory.Application, result.Error.Category);
    }

    private static void WalkType(Type type, Action<Type> visitor)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(visitor);

        HashSet<Type> visited = [];
        Stack<Type> stack = new();
        stack.Push(type);

        while (stack.Count > 0)
        {
            Type current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            visitor(current);

            if (current.IsGenericType)
            {
                foreach (Type argument in current.GetGenericArguments())
                {
                    stack.Push(argument);
                }
            }

            if (current.IsArray)
            {
                stack.Push(current.GetElementType()!);
                continue;
            }

            if (current.BaseType is not null && current.BaseType != typeof(object))
            {
                stack.Push(current.BaseType);
            }

            foreach (Type interfaceType in current.GetInterfaces())
            {
                stack.Push(interfaceType);
            }
        }
    }
}

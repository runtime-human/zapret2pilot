# Z2P 0.0.25 — Bootstrap, Platform Boundaries & UI Composition: План реализации

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Implement task-by-task; steps use checkbox syntax.

**Goal:** Создать production composition root, trusted startup и формальный UI contract без real runtime.

**Architecture:** Отказ от статического `AppHost.Services` и `Host.CreateDefaultBuilder`; переход на явный `Host.CreateApplicationBuilder` с source-generated валидацией, DI-регистрацией всех shell-сервисов, lifecycle-координатором, ReactiveUI lifecycle-примитивами и визуальным контрактом на базе theme tokens / Fluent icons.

**Tech Stack:** C# / .NET 10 / Avalonia 12 / ReactiveUI / System.Reactive / Generic Host / SQLite

## Global Constraints

- В этой версии **не запускается** реальный `winws2`.
- Запрещены Windows Service, IPC, VPN, proxy, MITM.
- Запрещены production parameterless конструкторы ViewModel.
- Запрещён static service locator (`AppHost.Services`, `ServiceLocator`, `static IServiceProvider`).
- `App` сохраняет parameterless конструктор для Avalonia previewer, но не хранит `IServiceProvider` (DEC-0044).
- Security-critical параметры (`TrustedTufRoot`, `DeploymentFlavor`, `DeveloperMode`, executable/runtime roots) нельзя изменить через переменные среды, обычный `appsettings.json` или произвольные CLI-аргументы.
- `Core/Application/Engine/Storage` → `net10.0`; `App/Runtime` → `net10.0-windows10.0.26100.0`.
- Все runtime-действия отключены до фазы `Ready` lifecycle-координатора.
- `ExceptionClassifier` — чистая функция, без DI/IO/allocations, безопасна для `AppDomain.UnhandledException`.
- Evidence Pack должен быть завершён.

## Мастер-чеклист 0.0.25

- [ ] Scope A — Generic Host: `Host.CreateApplicationBuilder`, явные trusted конфигурационные источники, source-generated валидация, валидация service graph.
- [ ] Scope B — Startup Lifecycle: `Z2PApplicationLifecycleCoordinator` с фазами `ProcessBootstrap → ShellVisible → StorageRecovery → DeploymentVerification → OwnershipRecovery → CompatibilityPreflight → Ready → Stopping`.
- [ ] Scope C — Platform TFMs: split TFMs, CA1416 анализ, architecture tests.
- [ ] Scope D — DI composition: явная регистрация deployment context, path providers, navigation, UI scheduler, localization, version, shell, lifecycle coordinator, feature facades, exception policy; удалён `AppHost.Services` и parameterless ViewModel constructors.
- [ ] Scope E — Application API migration start: typed feature facades (`IRuntimeUseCases` и др.); `CommandBus` только за adapter'ом для non-critical.
- [ ] Scope F — ReactiveUI lifecycle: `WhenActivated`, `CompositeDisposable`, centralized UI scheduler, `ReactiveCommand.ThrownExceptions`, `RxApp.DefaultExceptionHandler`.
- [ ] Scope G — Visual contract: theme tokens, Fluent icons, hero state matrix, expanded/medium/compact layouts, freshness states, Design Lab, dynamic version; нет emoji/mock version.
- [ ] Scope H — Privileged input baseline: границы file import, CLI allowlist, safe external-link, no WebView/plugins/custom XAML, no ShellExecute на untrusted input.
- [ ] Scope I — Global exception foundation: подписки Avalonia dispatcher, AppDomain, TaskScheduler, ReactiveUI default handler, hosted/lifecycle fatal paths; классификация; state-compromising exception не продолжает работу.
- [ ] Tests: DI graph resolution, no static locator, trusted configuration source tests, Windows TFM architecture tests, activation/disposal, compiled bindings, XAML resource load, Design Lab states, headless/visual baseline, exception classification.
- [ ] Acceptance: shell стартует через production DI; runtime actions disabled until Ready; environment/CLI не могут заменить runtime/TUF roots; no mock dashboard values; no real `winws2`; Evidence Pack complete.

---

## Packet 1 — Scope A + Scope D: Generic Host migration и DI composition root

**Objective:** Заменить `Host.CreateDefaultBuilder` на `Host.CreateApplicationBuilder`, удалить статический локатор `AppHost.Services`, убрать parameterless конструктор `MainWindowViewModel`, зарегистрировать UI-слои явно.

**Key design decision (DEC-0044):** `App` остаётся parameterless для Avalonia previewer, но не получает `IServiceProvider`. `MainWindow` разрешается в `Program.Main` и передаётся в `IClassicDesktopStyleApplicationLifetime` через callback overload `StartWithClassicDesktopLifetime(args, lifetimeBuilder)`.

**Files:**
- Modify: `src/Zapret2Pilot.App/Program.cs`
- Modify: `src/Zapret2Pilot.App/App.axaml.cs`
- Modify: `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`
- Modify: `src/Zapret2Pilot.App/Shell/MainWindow.axaml.cs`
- Create: `src/Zapret2Pilot.App/Properties/AssemblyInfo.cs`
- Create: `src/Zapret2Pilot.App/appsettings.json`
- Modify: `src/Zapret2Pilot.App/Zapret2Pilot.App.csproj`
- Create: `src/Zapret2Pilot.App/Hosting/Z2PHostBuilder.cs`
- Create: `src/Zapret2Pilot.App/Hosting/Z2PApplicationOptions.cs`
- Create: `src/Zapret2Pilot.App/Hosting/Z2PApplicationOptionsValidator.cs`
- Create: `src/Zapret2Pilot.App/Hosting/Z2PConfigurationDefaults.cs`
- Create: `src/Zapret2Pilot.App/DependencyInjection/AppServiceCollectionExtensions.cs`
- Create: `src/Zapret2Pilot.App/Threading/AvaloniaUiScheduler.cs`
- Create: `tests/Zapret2Pilot.App.ViewModelTests/Hosting/DiGraphResolutionTests.cs`
- Create: `tests/Zapret2Pilot.App.ViewModelTests/Hosting/TrustedConfigurationTests.cs`
- Modify: `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: existing runtime extension methods (`AddRuntimeKernelStateStore`, `AddRuntimeProcessHost`, `AddRuntimeHealthMonitor`, `AddCrashLoopGuard`, `AddRuntimeKernelLoop`, `AddRuntimeSupervisor`).
- Produces: `Z2PHostBuilder.Build(string[] args)`; `Z2PHostBuilder.CreateBuilder(string[] args)`; `Z2PApplicationOptions`; `AppServiceCollectionExtensions.AddZ2PAppServices`; DI-resolvable `MainWindowViewModel`.

**Allowed edits:**
- Composition root, DI registration, App/ViewModel constructors, tests for DI and ViewModels.
- No new NuGet packages in this packet.

**Forbidden edits:**
- No ViewModel command logic changes beyond constructor cleanup.
- No theme/visual changes.
- No exception handler changes.
- No CommandBus/runtime logic changes.
- No TFM changes.
- No lifecycle coordinator.

### Step 1.1 — Create options, validator, and defaults

Create `src/Zapret2Pilot.App/Hosting/Z2PApplicationOptions.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Zapret2Pilot.App.Hosting;

public enum DeploymentFlavor
{
    Installed,
    Portable,
    Development
}

public sealed class Z2PApplicationOptions
{
    [Required]
    public string StorageDatabasePath { get; set; } = default!;

    public DeploymentFlavor DeploymentFlavor { get; set; } = DeploymentFlavor.Development;

    public bool DeveloperMode { get; set; }

    [Required]
    public string TrustedTufRoot { get; set; } = default!;
}
```

Create `src/Zapret2Pilot.App/Hosting/Z2PApplicationOptionsValidator.cs`:
```csharp
using Microsoft.Extensions.Options;

namespace Zapret2Pilot.App.Hosting;

public sealed class Z2PApplicationOptionsValidator : IValidateOptions<Z2PApplicationOptions>
{
    public ValidateOptionsResult Validate(string? name, Z2PApplicationOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.StorageDatabasePath))
            failures.Add("StorageDatabasePath is required.");

        if (string.IsNullOrWhiteSpace(options.TrustedTufRoot))
            failures.Add("TrustedTufRoot is required.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
```

Create `src/Zapret2Pilot.App/Hosting/Z2PConfigurationDefaults.cs`:
```csharp
namespace Zapret2Pilot.App.Hosting;

internal static class Z2PConfigurationDefaults
{
    public const string TrustedTufRoot = "embedded-trusted-root-v1";

    public static string DefaultStorageDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Zapret2Pilot",
            "z2p.db");
    }
}
```

### Step 1.2 — Create host builder

Create `src/Zapret2Pilot.App/Hosting/Z2PHostBuilder.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zapret2Pilot.App.DependencyInjection;
using Zapret2Pilot.Runtime;

namespace Zapret2Pilot.App.Hosting;

internal static class Z2PHostBuilder
{
    public static IHost Build(string[] args)
    {
        return CreateBuilder(args).Build();
    }

    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Explicit, approved configuration sources only.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        // Security-critical values are set in code and cannot be overridden
        // by environment variables, ordinary appsettings, or arbitrary CLI.
        builder.Services.Configure<Z2PApplicationOptions>(options =>
        {
            options.StorageDatabasePath = Z2PConfigurationDefaults.DefaultStorageDatabasePath();
            options.TrustedTufRoot = Z2PConfigurationDefaults.TrustedTufRoot;
            options.DeploymentFlavor = DeploymentFlavor.Development;
            options.DeveloperMode = false;
        });

        builder.Services.AddSingleton<IValidateOptions<Z2PApplicationOptions>, Z2PApplicationOptionsValidator>();
        builder.Services.AddOptions<Z2PApplicationOptions>().ValidateOnStart();

        Z2PApplicationOptions options = new()
        {
            StorageDatabasePath = Z2PConfigurationDefaults.DefaultStorageDatabasePath()
        };

        builder.Services.AddRuntimeKernelStateStore(options.StorageDatabasePath);
        builder.Services.AddRuntimeProcessHost();
        builder.Services.AddRuntimeHealthMonitor();
        builder.Services.AddCrashLoopGuard();
        builder.Services.AddRuntimeKernelLoop();
        builder.Services.AddRuntimeSupervisor();

        builder.Services.AddZ2PAppServices();

        return builder;
    }
}
```

### Step 1.3 — Create UI DI extensions and Avalonia scheduler

Create `src/Zapret2Pilot.App/DependencyInjection/AppServiceCollectionExtensions.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Zapret2Pilot.App.Navigation;
using Zapret2Pilot.App.Shell;
using Zapret2Pilot.App.Threading;

namespace Zapret2Pilot.App.DependencyInjection;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddZ2PAppServices(this IServiceCollection services)
    {
        services.AddSingleton<INavigationPageFactory, NavigationPageFactory>();
        services.AddSingleton<NavigationRouter>();
        services.AddSingleton<IUiScheduler, AvaloniaUiScheduler>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
```

Create `src/Zapret2Pilot.App/Threading/AvaloniaUiScheduler.cs`:
```csharp
using System;
using Avalonia.Threading;

namespace Zapret2Pilot.App.Threading;

public sealed class AvaloniaUiScheduler : IUiScheduler
{
    public void Schedule(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Dispatcher.UIThread.Post(action);
    }
}
```

### Step 1.4 — Rewrite Program.cs

Replace `src/Zapret2Pilot.App/Program.cs` with:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReactiveUI.Avalonia;
using Zapret2Pilot.App.Hosting;
using Zapret2Pilot.App.Shell;

namespace Zapret2Pilot.App;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        using IHost host = Z2PHostBuilder.Build(args);

        await host.StartAsync();

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, desktop =>
                {
                    desktop.MainWindow = host.Services.GetRequiredService<MainWindow>();
                });
        }
        finally
        {
            using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(5));

            await host.StopAsync(shutdownTimeout.Token);
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
```

### Step 1.5 — Clean App.axaml.cs

Replace `src/Zapret2Pilot.App/App.axaml.cs` with:
```csharp
using Avalonia.Markup.Xaml;

namespace Zapret2Pilot.App;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // MainWindow is provided by the desktop lifetime callback in Program.Main.
        base.OnFrameworkInitializationCompleted();
    }
}
```

### Step 1.6 — Remove parameterless ViewModel and window constructors

Edit `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`:
- Remove `public MainWindowViewModel()` and `public MainWindowViewModel(NavigationRouter navigationRouter)`.
- Keep only the full constructor with `NavigationRouter`, `IUiScheduler`, `IRuntimeSupervisor?`, `ILogger<MainWindowViewModel>?`.
- Remove `CreateDefaultNavigationRouter()` helper if it becomes unused, or keep it private static.

Edit `src/Zapret2Pilot.App/Shell/MainWindow.axaml.cs`:
- Remove the parameterless constructor.
- Keep only `public MainWindow(MainWindowViewModel viewModel)`.

### Step 1.7 — Add appsettings.json and project metadata

Create `src/Zapret2Pilot.App/appsettings.json`:
```json
{
  "Z2P": {}
}
```

Add to `src/Zapret2Pilot.App/Zapret2Pilot.App.csproj`:
```xml
  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

Create `src/Zapret2Pilot.App/Properties/AssemblyInfo.cs`:
```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Zapret2Pilot.App.ViewModelTests")]
```

### Step 1.8 — Add DI graph and trusted configuration tests

Create `tests/Zapret2Pilot.App.ViewModelTests/Hosting/DiGraphResolutionTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.App.Hosting;

namespace Zapret2Pilot.App.ViewModelTests.Hosting;

public sealed class DiGraphResolutionTests
{
    [Fact]
    public static void AllSingletonServicesResolve()
    {
        HostApplicationBuilder builder = Z2PHostBuilder.CreateBuilder([]);
        using IHost host = builder.Build();

        IEnumerable<ServiceDescriptor> singletons = builder.Services
            .Where(sd => sd.Lifetime == ServiceLifetime.Singleton &&
                         sd.ImplementationType is not null &&
                         !IsOpenGeneric(sd.ServiceType));

        Assert.All(singletons, descriptor =>
        {
            _ = host.Services.GetRequiredService(descriptor.ServiceType);
        });
    }

    private static bool IsOpenGeneric(Type type) => type.IsGenericTypeDefinition;
}
```

Create `tests/Zapret2Pilot.App.ViewModelTests/Hosting/TrustedConfigurationTests.cs`:
```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using Zapret2Pilot.App.Hosting;

namespace Zapret2Pilot.App.ViewModelTests.Hosting;

public sealed class TrustedConfigurationTests
{
    [Fact]
    public static void EnvironmentVariableCannotOverrideTrustedTufRoot()
    {
        const string envKey = "Z2P__TrustedTufRoot";
        const string envValue = "attacker-controlled-root";
        Environment.SetEnvironmentVariable(envKey, envValue);

        try
        {
            using IHost host = Z2PHostBuilder.Build([]);
            Z2PApplicationOptions options = host.Services
                .GetRequiredService<IOptions<Z2PApplicationOptions>>().Value;

            Assert.Equal(Z2PConfigurationDefaults.TrustedTufRoot, options.TrustedTufRoot);
            Assert.NotEqual(envValue, options.TrustedTufRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }
}
```

### Step 1.9 — Update existing ViewModel tests

Modify `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`:
- Add a private static helper:
```csharp
private static MainWindowViewModel CreateViewModel(
    NavigationRouter? router = null,
    IUiScheduler? scheduler = null,
    IRuntimeSupervisor? supervisor = null) =>
    new(
        router ?? new NavigationRouter(new NavigationPageFactory(), RouteId.Dashboard),
        scheduler ?? new ImmediateUiScheduler(),
        supervisor,
        logger: null);
```
- Replace every `new MainWindowViewModel()` with `CreateViewModel()`.
- Keep `StartBlocked_UpdatesLastAction` using the explicit full constructor as it does now (it already passes router/scheduler/supervisor).

**Verification ladder:**
```powershell
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
dotnet test tests/Zapret2Pilot.Application.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

**Acceptance:**
- `Host.CreateDefaultBuilder` отсутствует в `Program.cs`.
- `AppHost` отсутствует.
- `MainWindowViewModel` имеет ровно один production constructor.
- `MainWindow` имеет ровно один constructor (с `MainWindowViewModel`).
- `App.axaml.cs` не ссылается на `IServiceProvider` / `AppHost`.
- DI graph resolution test passes.
- Trusted configuration test passes.
- Все существующие тесты проходят.

---

## Packet 2 — Scope I: Global Exception Foundation

**Objective:** Подписаться на глобальные исключения, классифицировать их, не позволять state-compromising исключениям приводить к слепому продолжению.

**Files:**
- Create: `src/Zapret2Pilot.App/Diagnostics/ExceptionSeverity.cs`
- Create: `src/Zapret2Pilot.App/Diagnostics/ExceptionClassifier.cs`
- Create: `src/Zapret2Pilot.App/Diagnostics/IExceptionPolicy.cs`
- Create: `src/Zapret2Pilot.App/Diagnostics/ExceptionPolicy.cs`
- Modify: `src/Zapret2Pilot.App/Program.cs`
- Modify: `src/Zapret2Pilot.App/App.axaml.cs`
- Create: `tests/Zapret2Pilot.App.ViewModelTests/Diagnostics/ExceptionClassifierTests.cs`

**Interfaces:**
- Consumes: `ILogger<ExceptionPolicy>`, `Z2PApplicationLifecycleCoordinator` (optional for now).
- Produces: `ExceptionClassifier.Classify(Exception)` → `ExceptionSeverity`; `ExceptionPolicy.Handle(Exception, string context)`.

**Steps:**
1. Define `enum ExceptionSeverity { Fatal, StateCompromising, Recoverable }`.
2. Implement `ExceptionClassifier` as static pure function: `OutOfMemoryException`, `AccessViolationException`, `SEHException` → Fatal; `InvalidOperationException` with data corruption markers → StateCompromising; остальные → Recoverable.
3. Implement `ExceptionPolicy` logging and deciding whether to terminate.
4. В `Program.Main` подписаться на `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, настроить `RxApp.DefaultExceptionHandler`.
5. В `App.axaml.cs` или `Program.cs` подписаться на `Dispatcher.UIThread.UnhandledException`.
6. Добавить тесты классификации.

**Verification:**
```powershell
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
```

---

## Packet 3 — Scope G: Visual Contract Foundation

**Objective:** Заменить emoji на Fluent icons, добавить theme tokens, сделать версию динамической.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Zapret2Pilot.App/Zapret2Pilot.App.csproj`
- Modify: `src/Zapret2Pilot.App/App.axaml`
- Modify: `src/Zapret2Pilot.App/Shared/Theme/LightTheme.axaml`
- Modify: `src/Zapret2Pilot.App/Shell/MainWindow.axaml`
- Modify: `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`
- Modify: `src/Zapret2Pilot.App/Navigation/NavigationItemViewModel.cs`
- Modify: `VERSION`
- Modify: `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `AssemblyInformationalVersionAttribute`.
- Produces: dynamic `AppVersion`; icon bindings.

**Allowed edits:**
- NuGet package `FluentIcons.Avalonia` pinned exact version, совместимую с Avalonia 12.x.
- Theme, XAML, ViewModel version property.

**Forbidden edits:**
- No hero state matrix (requires runtime connection).
- No Design Lab (separate packet).
- No DI changes.
- No exception handling changes.

**Steps:**
1. Добавить `FluentIcons.Avalonia` в `Directory.Packages.props` и App csproj (exact version, проверить совместимость с Avalonia 12.0.5).
2. Добавить theme tokens (`Z2P.Space.*`, `Z2P.Radius.*`, `Z2P.FontSize.*`) в `LightTheme.axaml`.
3. Заменить `NavigationItemViewModel.Marker` emoji на Fluent icon source.
4. Заменить dashboard `✓` и settings `⚙` emoji на icon controls.
5. Изменить `MainWindowViewModel.AppVersion` на чтение `AssemblyInformationalVersionAttribute` с префиксом `v`.
6. Поднять `VERSION` до `0.0.25`; обновить тестовую проверку `AppVersion`.

**Verification:**
```powershell
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
```

---

## Packet 4 — Scope C: Platform TFMs

**Objective:** Разделить TFMs и включить анализ совместимости платформы.

**Files:**
- Modify: `src/Zapret2Pilot.App/Zapret2Pilot.App.csproj`
- Modify: `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj`
- Create: `tests/Zapret2Pilot.App.ViewModelTests/Architecture/TfmArchitectureTests.cs`

**Steps:**
1. Установить App/Runtime TFM в `net10.0-windows10.0.26100.0`.
2. Добавить `<SupportedOSPlatformVersion>10.0.26100.0</SupportedOSPlatformVersion>` при необходимости.
3. Добавить architecture tests, проверяющие TFM выходных сборок.

**Verification:**
```powershell
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
```

---

## Packet 5 — Scope B: Lifecycle Coordinator

**Objective:** Внедрить `Z2PApplicationLifecycleCoordinator` с фазами и отключить runtime-команды до `Ready`.

**Files:**
- Create: `src/Zapret2Pilot.App/Lifecycle/ApplicationLifecyclePhase.cs`
- Create: `src/Zapret2Pilot.App/Lifecycle/IZ2PApplicationLifecycleCoordinator.cs`
- Create: `src/Zapret2Pilot.App/Lifecycle/Z2PApplicationLifecycleCoordinator.cs`
- Modify: `src/Zapret2Pilot.App/DependencyInjection/AppServiceCollectionExtensions.cs`
- Modify: `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`

**Steps:**
1. Определить enum фаз.
2. Реализовать coordinator как `IHostedService`, expose `IObservable<ApplicationLifecyclePhase>` и `bool IsReady`.
3. Зарегистрировать как singleton + hosted service.
4. В `MainWindowViewModel` команды runtime получают `canExecute` от coordinator `IsReady`.

**Verification:**
```powershell
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
```

---

## Packet 6 — Scope F: ReactiveUI Lifecycle (complete)

**Objective:** Полноценно использовать `WhenActivated`, `CompositeDisposable`, централизованный scheduler, обработку исключений команд.

**Files:**
- Modify: `src/Zapret2Pilot.App/Program.cs`
- Modify: `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`
- Modify: `src/Zapret2Pilot.App/Shell/MainWindow.axaml.cs`

**Steps:**
1. Настроить `RxApp.DefaultExceptionHandler` в `BuildAvaloniaApp`.
2. Реализовать `IActivatableViewModel` в `MainWindowViewModel`.
3. Перенести supervisor subscription и подписки на `ReactiveCommand.ThrownExceptions` внутрь `WhenActivated`, управляемые `CompositeDisposable`.

**Verification:**
```powershell
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
```

---

## Packet 7 — Scope E: Application API Migration Start

**Objective:** Ввести typed feature facades; оставить `CommandBus` только за adapter'ом для non-critical.

**Files:**
- Create: `src/Zapret2Pilot.Application/UseCases/IRuntimeUseCases.cs`
- Create: `src/Zapret2Pilot.Application/UseCases/IProfileUseCases.cs`
- Create: `src/Zapret2Pilot.Application/UseCases/IRulesUseCases.cs`
- Create: `src/Zapret2Pilot.Application/UseCases/IAutoDoctorUseCases.cs`
- Create: `src/Zapret2Pilot.Application/UseCases/IDiagnosticsUseCases.cs`
- Create: `src/Zapret2Pilot.Application/UseCases/IRuntimeUpdateUseCases.cs`
- Create: `src/Zapret2Pilot.Application/UseCases/IDataManagementUseCases.cs`
- Create: реализации, возвращающие `Result<T>` NotImplemented или делегирующие существующим сервисам.
- Modify: `AppServiceCollectionExtensions` для регистрации facades.

**Steps:**
1. Определить facade-интерфейсы согласно roadmap §28.
2. Реализовать minimal runtime facade.
3. Зарегистрировать facades в DI.
4. Оставить `CommandBus` для non-critical features.

**Verification:**
```powershell
dotnet test tests/Zapret2Pilot.Application.Tests -c Release
```

---

## Packet 8 — Scope H: Privileged Input Baseline

**Objective:** Установить границы привилегированного ввода.

**Files:**
- Create: `src/Zapret2Pilot.App/Input/CliAllowlist.cs`
- Create: `src/Zapret2Pilot.App/Input/FileImportBoundary.cs`
- Create: `src/Zapret2Pilot.App/Input/ExternalLinkLauncher.cs`
- Modify: `Program.cs` для парсинга только разрешённых CLI-аргументов.

**Steps:**
1. Определить разрешённые CLI verbs/options; отклонять неизвестные.
2. File import boundary валидирует extension, size, magic.
3. External-link launcher whitelist schemes/hosts; no ShellExecute на untrusted input.

**Verification:**
```powershell
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
```

---

## Evidence Pack Checklist

- [ ] `docs/Z2P-IMPLEMENTATION-STATUS.md` обновлён: 0.0.25 Packet 1 завершён, Packets 2–8 в backlog.
- [ ] `docs/Z2P-DECISION-LOG.md` дополнен, если отступления от canon.
- [ ] DI graph resolution test passes.
- [ ] Trusted configuration source tests pass.
- [ ] No static locator references in production code (`grep -R "AppHost\|ServiceLocator\|static IServiceProvider" src/`).
- [ ] No `IServiceProvider` stored in `App` or ViewModels.
- [ ] No real `winws2` references introduced.
- [ ] `VERSION` file bumped to `0.0.25` (Packet 3).

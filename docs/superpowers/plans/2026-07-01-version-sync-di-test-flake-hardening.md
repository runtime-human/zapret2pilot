# 0.0.20 Cleanup / Packet 4 — Version Sync, DI Integration Test, Test Hardening

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three outstanding follow-ups from 0.0.20 packet 3: make `MainWindowViewModel.AppVersion` reflect the current `VERSION` file, add an integration test that proves `RuntimeProcessHost` resolves from the real DI container, and harden the UI non-blocking test against slow CI runners.

**Architecture:** Keep changes minimal and local. `AppVersion` stays a read-only ViewModel property but is updated to the current milestone. DI registration stays untouched; only a new test is added to exercise it. The worker non-blocking test keeps the same contract but widens the timing budget enough to stay green on loaded CI without losing regression sensitivity.

**Tech Stack:** C# / .NET 10 / Avalonia UI / ReactiveUI / xUnit v3 / Microsoft.Extensions.Hosting.

## Global Constraints

- No real `winws2` launch.
- No Windows Service, IPC layer, VPN, proxy, MITM, per-URL router, `.bat`/`.cmd` wrappers, or arbitrary command execution from UI.
- No new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change.
- `AddRuntimeProcessHost` must stay pure: registration must NOT call `AppDataLayout.EnsureCreated()`.
- ViewModels remain ReactiveUI-based; no CommunityToolkit.Mvvm ViewModels.
- Do not commit or push unless explicitly requested by the user.
- Do not overwrite unrelated files.

---

### Task 1: Synchronize `MainWindowViewModel.AppVersion` with `VERSION` (0.0.20)

**Files:**
- Modify: `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs:91`
- Modify: `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs:17`

**Interfaces:**
- Consumes: existing `MainWindowViewModel` constructor, existing `InitialShellIdentityMatchesProjectCanon` test.
- Produces: `AppVersion` returns `"v0.0.20"`; test asserts `"v0.0.20"`.

- [ ] **Step 1: Update ViewModel property**

Change line 91 of `MainWindowViewModel.cs` from:

```csharp
public string AppVersion { get; } = "v0.0.4";
```

to:

```csharp
public string AppVersion { get; } = "v0.0.20";
```

- [ ] **Step 2: Update the corresponding test assertion**

Change line 17 of `MainWindowViewModelTests.cs` from:

```csharp
Assert.Equal("v0.0.4", viewModel.AppVersion);
```

to:

```csharp
Assert.Equal("v0.0.20", viewModel.AppVersion);
```

- [ ] **Step 3: Run focused ViewModel tests**

Run:

```powershell
dotnet test tests\Zapret2Pilot.App.ViewModelTests -c Release
```

Expected: all tests pass, including `InitialShellIdentityMatchesProjectCanon`.

---

### Task 2: Add DI integration test for `RuntimeProcessHost`

**Files:**
- Modify: `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `AddRuntimeKernelWorker()` and `AddRuntimeProcessHost()` extension methods; `RuntimeProcessHost`, `RuntimeKernelWorker`, `IHostedService` types.
- Produces: new test `AddRuntimeProcessHostRegistersResolvableHost` proving the host and worker resolve as singletons from a built service provider.

- [ ] **Step 1: Add the integration test**

Replace the `AddRuntimeProcessHostRegistersResolvableHost` test in `RuntimeServiceCollectionExtensionsTests.cs` inside the existing class. The test must build a real `IHost`, start it (which starts `RuntimeKernelWorker` as an `IHostedService`), perform the resolutions and assertions, then stop the host. The earlier non-async implementation built a bare `ServiceProvider` and never started the worker, which caused `RuntimeProcessHost.Dispose()` to hang on the worker channel that no thread was reading from. The test must be `async Task` and forward `TestContext.Current.CancellationToken` to `StartAsync` / `StopAsync` (xUnit v3 rule):

```csharp
[Fact]
public static async Task AddRuntimeProcessHostRegistersResolvableHost()
{
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    using IHost host = Host.CreateDefaultBuilder()
        .ConfigureServices(static services =>
        {
            services.AddRuntimeKernelWorker();
            services.AddRuntimeProcessHost();
        })
        .Build();

    await host.StartAsync(cancellationToken);

    try
    {
        RuntimeProcessHost host1 = host.Services.GetRequiredService<RuntimeProcessHost>();
        RuntimeProcessHost host2 = host.Services.GetRequiredService<RuntimeProcessHost>();

        Assert.NotNull(host1);
        Assert.Same(host1, host2);

        RuntimeKernelWorker worker = host.Services.GetRequiredService<RuntimeKernelWorker>();
        IHostedService hostedService = host.Services.GetRequiredService<IHostedService>();

        Assert.Same(worker, hostedService);
    }
    finally
    {
        await host.StopAsync(cancellationToken);
    }
}
```

- [ ] **Step 2: Run focused DI tests**

Run:

```powershell
dotnet test tests\Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"
```

Expected: both `AddRuntimeKernelStateStoreRegistersResolvableStore` and `AddRuntimeProcessHostRegistersResolvableHost` pass.

---

### Task 3: Harden `Enqueue_ReturnsImmediately_WithoutBlockingCaller` against CI flake

**Files:**
- Modify: `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeKernelWorkerUiNonBlockingTests.cs:57` and the XML/doc comment at lines 50–55

**Interfaces:**
- Consumes: existing `EnqueueReturnBudgetMs` constant used by both test methods.
- Produces: `EnqueueReturnBudgetMs` set to `10`; doc comment updated to match.

- [ ] **Step 1: Widen the timing budget constant**

Change line 57 from:

```csharp
private const int EnqueueReturnBudgetMs = 5;
```

to:

```csharp
private const int EnqueueReturnBudgetMs = 10;
```

- [ ] **Step 2: Update the XML/doc comment**

Update the comment block at lines 50–55 so the numbers match the new constant. Replace the text with:

```csharp
/// <summary>
/// Upper bound (in milliseconds) for the <c>Enqueue</c> call to
/// return. The work item awaits 100 ms; the enqueue call must
/// return at least 10× faster. A regression that lets the
/// worker block the caller would push this past the bound on
/// any reasonable machine.
/// </summary>
```

> Note: after updating the XML/doc block, scan the rest of `RuntimeKernelWorkerUiNonBlockingTests.cs` for any other stale "5 ms" references (e.g. inside inline comments or the class-level `<remarks>` block) and update them to "10 ms" so the comments stay consistent with `EnqueueReturnBudgetMs`. The constant value on line 57 is the source of truth; the docs/comments must match it.

- [ ] **Step 3: Run focused non-blocking tests**

Run:

```powershell
dotnet test tests\Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeKernelWorkerUiNonBlockingTests"
```

Expected: both `Enqueue_ReturnsImmediately_WithoutBlockingCaller` and `Enqueue_DoesNotBlockCaller_WhenWorkItemAwaitsForever` pass.

---

## Out of Scope

- Launching real `winws2`.
- Promoting `RuntimeProcessHost` to an `IHostedService`.
- Changing `AppVersion` to be driven by assembly metadata or the `VERSION` file automatically.
- Modifying `AddRuntimeProcessHost` registration logic or `AppDataLayout` creation behavior.
- Adding documentation updates beyond the plan file itself.

## Verification Ladder

1. `dotnet restore Zapret2Pilot.slnx`
2. `dotnet build Zapret2Pilot.slnx -c Release`
3. `dotnet test tests\Zapret2Pilot.App.ViewModelTests -c Release`
4. `dotnet test tests\Zapret2Pilot.Runtime.Tests -c Release`
5. `dotnet test Zapret2Pilot.slnx -c Release`

## Stop Conditions

- STOP if any build or test fails and the failure is not caused by the changed files.
- STOP if `AddRuntimeProcessHost` registration is accidentally changed to call `AppDataLayout.EnsureCreated()`.
- STOP if `AppVersion` is not updated exactly to `"v0.0.20"`.
- STOP if the non-blocking test budget is changed to a value >= `WorkItemDelayMs` (100 ms).

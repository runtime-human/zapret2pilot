# Zapret2Pilot / Z2P

Zapret2Pilot is a Windows desktop manager and control plane for zapret2/winws2.

Current version: `0.0.23`.

## Project status

This repository is in early bootstrap stage.

The master roadmap is **`docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md`**
(Version 6, dated 2026-07-04). `docs/Z2P-ROADMAP.md` is the
navigable roadmap entry point that points to the v6 file as the
authoritative body and indexes the per-milestone implementation
record in `docs/Z2P-IMPLEMENTATION-STATUS.md`.

Current milestone:

```text
0.0.23 — Runtime Kernel Correctness Hardening (implemented)
```

Next planned milestone per the v6 master plan:

```text
0.0.24 — Runtime Kernel Lifecycle Closure (planned; v6 §27)
```

## Architecture baseline

Zapret2Pilot is planned as:

- C# / .NET 10 application;
- Windows desktop application;
- Avalonia UI application;
- ReactiveUI + System.Reactive presentation baseline;
- Microsoft.Extensions.Hosting inside the Avalonia app;
- elevated single-process app;
- Core domain primitives for Result/Error and typed IDs;
- Application command dispatch foundation;
- UI navigation foundation with dashboard/profile/settings placeholders;
- SQLite storage foundation with WAL initialization;
- infrastructure foundation for safe paths, atomic writes and diagnostics redaction;
- runtime ownership foundation with Global Mutex and recovery metadata lock file;
- Windows Job Object foundation with kill-on-close containment primitive;
- job assignment seam for Windows Job Objects;
- runtime asset manifest and verification for Zapret2/winws2 assets;
- runtime workspace materialization that writes hostlists, generated config and args file into a workspace directory using the safe path and atomic write infrastructure;
- profile document and strategy pack document schema with a structural validator in `Engine.Zapret2/Profiles`;
- profile definition foundation in `Core.Profiles` plus a pure `ProfileDocumentMapper` that resolves validated `ProfileDocument` + supplied `StrategyPackDocument`s into a `ProfileDefinition` (the domain model accepted by the future profile compiler, per `DEC-0010` and Critical Review #18);
- pure Zapret plan compiler in `Engine.Zapret2/Compiler` that turns a validated `ProfileDefinition` into a `CompiledZapretPlan` with a content-addressed `RuntimePlanCacheKey`;
- explicit runtime transactions (start/stop/apply with rollback) managed by `RuntimeTransactionManager`;
- persistent runtime/session state store (`RuntimeKernelStateStore`) backed by SQLite;
- runtime health monitor hosted service with PeriodicTimer/TimeProvider, probe coalescing and off-worker publication;
- runtime crash-loop guard primitive with success-gated reset;
- conditional runtime_state clearing so ending a historical session does not corrupt the current session;
- no Windows Service in MVP;
- no IPC service layer in MVP;
- no VPN/proxy/MITM/traffic-router functionality;
- no real winws2 launch before Runtime Kernel safety primitives exist.

## Repository layout

```text
src/
  Zapret2Pilot.Core/
  Zapret2Pilot.Application/
  Zapret2Pilot.Storage/
  Zapret2Pilot.Infrastructure/
  Zapret2Pilot.Engine.Zapret2/
  Zapret2Pilot.Runtime/
  Zapret2Pilot.App/

tests/
  Zapret2Pilot.Core.Tests/
  Zapret2Pilot.Application.Tests/
  Zapret2Pilot.Storage.Tests/
  Zapret2Pilot.Infrastructure.Tests/
  Zapret2Pilot.Engine.Zapret2.Tests/
  Zapret2Pilot.Runtime.Tests/
  Zapret2Pilot.Testing.FakeRuntime/
  Zapret2Pilot.App.ViewModelTests/
```

## Build

Requirements:

- .NET 10 SDK.

Commands:

```powershell
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

## Run app shell

```powershell
dotnet run --project src/Zapret2Pilot.App
```

The current app shell uses mock/design-time dashboard data and placeholder navigation only.

It does not launch `winws2`, does not manage runtime processes and does not install a Windows Service.

## Documentation source of truth

Project documentation lives in `docs/`.

Important documents:

- `docs/Z2P-CANON.md`
- `docs/Z2P-ARCHITECTURE.md`
- `docs/Z2P-CRITICAL-REVIEW.md`
- `docs/Z2P-ROADMAP.md` — navigable roadmap entry point.
- `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` — v6 master plan (authoritative body).
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — per-milestone implementation record.
- `docs/Z2P-UI-DESIGN.md`
- `docs/Z2P-CHATGPT-HANDOFF.md`
- `docs/Z2P-DECISION-LOG.md`

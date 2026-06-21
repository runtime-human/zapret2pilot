# Zapret2Pilot / Z2P

Zapret2Pilot is a Windows desktop manager and control plane for zapret2/winws2.

Current version: `0.0.9`.

## Project status

This repository is in early bootstrap stage.

Current milestone:

```text
0.0.9 — Job Assignment Foundation
```

Current patch:

```text
0.0.9 — Job Assignment Foundation
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
  Zapret2Pilot.Runtime/
  Zapret2Pilot.App/

tests/
  Zapret2Pilot.Core.Tests/
  Zapret2Pilot.Application.Tests/
  Zapret2Pilot.Storage.Tests/
  Zapret2Pilot.Infrastructure.Tests/
  Zapret2Pilot.Runtime.Tests/
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
- `docs/Z2P-ROADMAP.md`
- `docs/Z2P-UI-DESIGN.md`
- `docs/Z2P-CHATGPT-HANDOFF.md`
- `docs/Z2P-DECISION-LOG.md`

# Zapret2Pilot / Z2P

Zapret2Pilot is a Windows desktop manager and control plane for zapret2/winws2.

Current version: `0.0.4`.

## Project status

This repository is in early bootstrap stage.

Current milestone:

```text
0.0.4 — UI navigation foundation
```

Current patch:

```text
0.0.4 — UI navigation foundation
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
- no Windows Service in MVP;
- no IPC service layer in MVP;
- no VPN/proxy/MITM/traffic-router functionality;
- no real winws2 launch before Runtime Kernel safety primitives exist.

## Repository layout

```text
src/
  Zapret2Pilot.Core/
  Zapret2Pilot.Application/
  Zapret2Pilot.App/

tests/
  Zapret2Pilot.Core.Tests/
  Zapret2Pilot.Application.Tests/
  Zapret2Pilot.App.ViewModelTests/
```

## Build

Requirements:

- .NET 10 SDK.

Commands:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
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

# Zapret2Pilot / Z2P

Zapret2Pilot is a Windows desktop manager and control plane for zapret2/winws2.

Current version: `0.0.1`.

## Project status

This repository is in early bootstrap stage.

Current milestone:

```text
0.0.1 — Repo bootstrap and initial app skeleton
```

Current patch:

```text
0.0.1-a — Build foundation
```

## Architecture baseline

Zapret2Pilot is planned as:

- C# / .NET 10 application;
- Windows desktop application;
- Avalonia UI application in a later patch;
- elevated single-process app;
- no Windows Service in MVP;
- no IPC service layer in MVP;
- no VPN/proxy/MITM/traffic-router functionality;
- no real winws2 launch in the first implementation milestone.

## Repository layout

```text
src/
  Zapret2Pilot.Core/
  Zapret2Pilot.Application/

tests/
  Zapret2Pilot.Core.Tests/
  Zapret2Pilot.Application.Tests/
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

## Documentation source of truth

Project documentation lives in `docs/`.

Important documents:

- `docs/Z2P-CANON.md`
- `docs/Z2P-ARCHITECTURE.md`
- `docs/Z2P-CRITICAL-REVIEW.md`
- `docs/Z2P-ROADMAP.md`
- `docs/Z2P-CHATGPT-HANDOFF.md`
- `docs/Z2P-DECISION-LOG.md`

# Zapret2Pilot / Z2P — Implementation Status

## 0.0.1-a — Build foundation

Status: **implemented**.

Implemented files:

- `Zapret2Pilot.slnx`
- `.gitignore`
- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`
- `VERSION`
- root `README.md`
- `src/Zapret2Pilot.Core/Zapret2Pilot.Core.csproj`
- `src/Zapret2Pilot.Core/CoreAssemblyMarker.cs`
- `src/Zapret2Pilot.Application/Zapret2Pilot.Application.csproj`
- `src/Zapret2Pilot.Application/ApplicationAssemblyMarker.cs`
- `tests/Zapret2Pilot.Core.Tests/Zapret2Pilot.Core.Tests.csproj`
- `tests/Zapret2Pilot.Core.Tests/CoreAssemblyMarkerTests.cs`
- `tests/Zapret2Pilot.Application.Tests/Zapret2Pilot.Application.Tests.csproj`
- `tests/Zapret2Pilot.Application.Tests/ApplicationAssemblyMarkerTests.cs`

Validation to run locally:

```powershell
dotnet --info
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Notes:

- The first patch intentionally contains no UI project.
- The next implementation patch is `0.0.1-b — Avalonia + ReactiveUI shell`.

## 0.0.7 — Runtime ownership foundation

Status: **implemented**.

Merged in:

- PR #8 — `feat(runtime): add runtime ownership foundation`
- Merge commit: `1f59888e868688af0ec430f972002300a782c0f2`

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/`
- `tests/Zapret2Pilot.Runtime.Tests/`
- runtime ownership mutex primitive;
- lease-based ownership acquisition and release;
- runtime lock metadata model;
- runtime lock file store using existing safe path and atomic write infrastructure;
- stale metadata recovery after ownership is acquired;
- ownership detector contracts and process identity verification result models;
- runtime tests for ownership, lock file lifecycle, stale recovery and detector contracts;
- `VERSION = 0.0.7`;
- root README status update.

Validation status:

- GitHub CI passed before merge.
- SonarCloud quality gate passed before merge.

Notes:

- The lock file is recovery and diagnostics metadata only. It is not proof of ownership.
- Atomic ownership is the named mutex primitive.
- Real runtime process hosting remains intentionally out of scope until the next Windows process-safety primitive is implemented.
- `Directory.Build.props` currently contains a narrow temporary NuGet audit suppression for advisory `GHSA-2m69-gcr7-jv3q`, caused by the transitive SQLite native package chain. Remove that suppression once the dependency chain moves to a fixed renamed package.

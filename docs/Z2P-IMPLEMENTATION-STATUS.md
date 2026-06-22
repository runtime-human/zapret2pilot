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

## 0.0.9 — Job Assignment Foundation

Status: **implemented**.

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/Properties/AssemblyInfo.cs` (`InternalsVisibleTo("Zapret2Pilot.Runtime.Tests")`);
- `src/Zapret2Pilot.Runtime/Windows/RuntimeJobObjectAssignmentStatus.cs` — typed result enum;
- `src/Zapret2Pilot.Runtime/Windows/RuntimeProcessHandle.cs` — public readonly struct wrapper around `IntPtr` (no `SafeProcessHandle` exposure);
- `src/Zapret2Pilot.Runtime/Windows/RuntimeJobObjectAssignmentResult.cs` — record class with factory methods and validation;
- `src/Zapret2Pilot.Runtime/Windows/IJobObjectNativeApi.cs` — internal native seam for tests;
- `src/Zapret2Pilot.Runtime/Windows/JobObjectNativeApi.cs` — production `IJobObjectNativeApi` wrapping `Marshal.GetLastPInvokeError`;
- `src/Zapret2Pilot.Runtime/Windows/IRuntimeJobObjectProcessAssigner.cs` — public process-assignment contract;
- `src/Zapret2Pilot.Runtime/Windows/RuntimeJobObjectProcessAssigner.cs` — production implementation with platform guard, disposed check, invalid-handle check, cast check, and native error mapping (`ERROR_ACCESS_DENIED` → `AccessDenied`, `ERROR_INVALID_HANDLE` → `InvalidHandle`);
- `src/Zapret2Pilot.Runtime/Windows/WindowsJobObjectNativeMethods.cs` — added `AssignProcessToJobObject` P/Invoke via `LibraryImport`;
- `src/Zapret2Pilot.Runtime/Windows/RuntimeJobObject.cs` — added `internal SafeJobObjectHandle SafeHandle` to enable the assigner to reach the native handle without exposing it publicly;
- `tests/Zapret2Pilot.Runtime.Tests/Windows/RuntimeJobObjectProcessAssignerTests.cs` — unit tests for null rejection, disposed rejection, invalid handle rejection, unsupported-platform behavior, and `Assigned`/`AccessDenied`/`InvalidHandle` mapping via fake `IJobObjectNativeApi` (no real process is launched);
- `VERSION = 0.0.9`;
- root `README.md` version/milestone/architecture baseline update;
- `docs/Z2P-ROADMAP.md` — added `0.0.9 — Job Assignment Foundation` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test Zapret2Pilot.Runtime.Tests -c Release
```

Notes:

- The `RuntimeProcessHandle` struct is intentionally a thin `IntPtr` wrapper and does not expose `SafeProcessHandle`.
- `SafeJobObjectHandle` remains `internal`; the assigner reaches it through the `internal` accessor on `RuntimeJobObject`.
- No real process is started, killed, or assigned in the unit tests; mapping is exercised through a private fake `IJobObjectNativeApi`.
- Error code 5 is `ERROR_ACCESS_DENIED` and error code 6 is `ERROR_INVALID_HANDLE` on Windows.
- The `0.0.9` milestone is a process-safety seam only. Real process hosting (e.g. `RuntimeProcessHost`) and `winws2` launch remain explicitly out of scope until later roadmap steps.

## 0.0.10 — Runtime Detector Implementation

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/Detection/IProcessSnapshot.cs` — immutable process introspection seam with nullable `CommandLine` (see `DEC-0028`);
- `src/Zapret2Pilot.Runtime/Detection/IProcessSystemAccessor.cs` — public testable accessor contract;
- `src/Zapret2Pilot.Runtime/Detection/WindowsProcessSystemAccessor.cs` — Windows production accessor built on `System.Diagnostics.Process`; `CommandLine` is intentionally always `null` and carries a `TODO(0.0.17)` marker referencing the future `NtQueryInformationProcess`/WMI work;
- `src/Zapret2Pilot.Runtime/Detection/RuntimeOwnershipDetector.cs` — real verification implementation with the documented order: PID → process name → executable path → command-line hash (or `CommandLineUnverifiable` when unavailable) → plan hash → process start time (±5 s) → `OwnedByExpectedRuntime`;
- `src/Zapret2Pilot.Runtime/Detection/IRuntimeOwnershipDetector.cs` — signature updated to `Verify(RuntimeLockMetadata, string expectedPlanHash)`;
- `src/Zapret2Pilot.Runtime/Detection/RuntimeOwnershipVerificationStatus.cs` — added `CommandLineUnverifiable = 8`;
- `src/Zapret2Pilot.Runtime/Detection/RuntimeOwnershipVerificationResult.cs` — added factory methods: `ProcessNameMismatch()`, `ExecutablePathMismatch()`, `CommandLineHashMismatch()`, `CommandLineUnverifiable()`, `PlanHashMismatch()`, `ProcessStartTimeMismatch()`;
- `tests/Zapret2Pilot.Runtime.Tests/Detection/FakeProcessSystemAccessor.cs` — test-only accessor that returns a registered snapshot by PID, or `false`/`null` when not registered;
- `tests/Zapret2Pilot.Runtime.Tests/Detection/RuntimeOwnershipDetectorTests.cs` — focused unit tests covering the owned path, every mismatch path, null command line, null metadata and null/empty plan hash;
- `tests/Zapret2Pilot.Runtime.Tests/Detection/RuntimeOwnershipDetectorContractTests.cs` — updated to the new two-argument `Verify` signature and to the matching `StaticRuntimeOwnershipDetector` stub;
- `docs/Z2P-DECISION-LOG.md` — added `DEC-0028 — Nullable CommandLine in IProcessSnapshot`;
- `docs/Z2P-ROADMAP.md` — added the `0.0.10 — Runtime Detector Implementation` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section;
- `VERSION = 0.0.10`.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- No real process is started, killed, or assigned in any unit test. `WindowsProcessSystemAccessor` is exercised only indirectly by the production host wiring, not by tests.
- `WindowsProcessSystemAccessor` does NOT retrieve another process's real command line. The managed `System.Diagnostics.Process` API does not expose this; future command-line verification is deferred to roadmap step `0.0.17` and requires oracle approval (see `DEC-0028`).
- The detector uses ordinal string comparison for `CommandLineHash` and `PlanHash`, and ordinal case-insensitive comparison for `ProcessName` and `ExecutablePath` (Windows file system is case-insensitive by default).
- Process start time is tolerated within ±5 seconds of the lock metadata timestamp to absorb clock skew and to reduce false-positive `ProcessStartTimeMismatch` results when a child process starts very close to the recorded timestamp.
- The `0.0.10` milestone is the real verification implementation. It is still host-agnostic in the sense that production wiring (DI registration, configuration of the accessor) is intentionally out of scope and will land in a later kernel-host milestone.

## 0.0.11 — Runtime Asset Manifest

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Core/FileSystem/ISafePathResolver.cs` — minimal path-resolver contract added to Core so that engine adapters can resolve user paths without depending on Infrastructure;
- `src/Zapret2Pilot.Infrastructure/Zapret2Pilot.Infrastructure.csproj` — added `ProjectReference` to `Zapret2Pilot.Core` so that `SafePathResolver` can implement `ISafePathResolver`;
- `src/Zapret2Pilot.Infrastructure/FileSystem/SafePathResolver.cs` — now implements `ISafePathResolver` from Core (no behavior change);
- `src/Zapret2Pilot.Engine.Zapret2/Zapret2Pilot.Engine.Zapret2.csproj` — new project; references `Zapret2Pilot.Core` only (no Infrastructure reference by design);
- `src/Zapret2Pilot.Engine.Zapret2/EngineZapret2AssemblyMarker.cs` — `Engine.Zapret2` layer marker;
- `src/Zapret2Pilot.Engine.Zapret2/Assets/AssetKind.cs` — `Executable`, `Hostlist`, `StrategyPack`, `Config` enum;
- `src/Zapret2Pilot.Engine.Zapret2/Assets/ZapretRuntimeAsset.cs` — single-asset record (`RelativePath`, `ExpectedHash`, `Kind`);
- `src/Zapret2Pilot.Engine.Zapret2/Assets/ZapretAssetManifest.cs` — manifest record (runtime executable + hostlists + strategy packs);
- `src/Zapret2Pilot.Engine.Zapret2/Assets/ZapretAssetVerificationSummary.cs` — success summary with verified relative paths;
- `src/Zapret2Pilot.Engine.Zapret2/Assets/ZapretAssetVerifier.cs` — verifies presence, path safety (via injected `ISafePathResolver`) and SHA-256 hash; returns `Result<ZapretAssetVerificationSummary>` with typed failure codes (`AssetPathUnsafe`, `AssetMissing`, `AssetHashMismatch`);
- `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj` — added `ProjectReference` to `Zapret2Pilot.Engine.Zapret2`;
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Zapret2Pilot.Engine.Zapret2.Tests.csproj` — new test project; references `Zapret2Pilot.Engine.Zapret2` (and Core transitively);
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/EngineZapret2AssemblyMarkerTests.cs` — verifies `Engine.Zapret2` layer marker;
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Assets/ZapretAssetVerifierTests.cs` — `ValidManifestSucceeds`, `MissingExecutableReturnsFailure`, `HashMismatchReturnsFailure`, `PathEscapeAttemptReturnsFailure` (uses a private `ThrowingPathResolver` so the unsafe-path branch is exercised without touching disk);
- `Zapret2Pilot.slnx` — added `Zapret2Pilot.Engine.Zapret2` (under `src/`) and `Zapret2Pilot.Engine.Zapret2.Tests` (under `tests/`);
- `docs/Z2P-DECISION-LOG.md` — added `DEC-0029 — ISafePathResolver abstraction in Core`;
- `docs/Z2P-ROADMAP.md` — added the `0.0.11 — Runtime Asset Manifest` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section;
- `README.md` — version, current milestone, architecture baseline and repository layout updated;
- `VERSION = 0.0.11`.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Engine.Zapret2.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The verifier uses an injected `ISafePathResolver`, not the concrete `SafePathResolver` from Infrastructure. `Engine.Zapret2` does not reference `Infrastructure`.
- The `ThrowingPathResolver` test fake keeps the unsafe-path branch hermetic and does not touch the real file system.
- No real `winws2` process is started, killed, or assigned. The verifier is read-only and is the only `Engine.Zapret2` runtime primitive at this point.
- SHA-256 comparison is case-insensitive on the hex string so that both lowercase and uppercase manifests verify consistently.
- The `0.0.11` milestone is the first `Engine.Zapret2` slice: manifest + asset verification. Profile compilation and `winws2` argument building remain intentionally out of scope until later `Engine.Zapret2` milestones.

# Zapret2Pilot / Z2P — Implementation Status

> **Master plan: Version 6, dated 2026-07-04.**
>
> The master plan that drives every implementation packet from
> `0.0.24` onwards is `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md`. The
> navigable roadmap entry point is `docs/Z2P-ROADMAP.md`. This file
> continues the per-milestone implementation record started under the
> earlier roadmap; per-milestone scope, tests, acceptance and
> Evidence Pack requirements for `0.0.24`–`0.1.0` are defined in the
> v6 master plan and are not duplicated here.
>
> The detailed record below is preserved verbatim for the
> already-implemented milestones (`0.0.1`–`0.0.23`); the
> `0.0.24`–`0.1.0` sections at the bottom are placeholders that
> must be filled in as each milestone reaches the
> `Implemented` status defined in v6 §21.3.

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

## 0.0.12 — Runtime Workspace Materialization

Status: **implemented**.

Implemented files and areas:

- `src/Zapret2Pilot.Core/Runtime/CompiledZapretPlan.cs` — minimal placeholder for the future profile compiler output (generated config content, args content, hostlist contents keyed by relative path);
- `src/Zapret2Pilot.Runtime/Workspace/IRuntimeWorkspaceMaterializer.cs` — async materializer contract returning `Result<RuntimeWorkspaceMaterializeResult>`;
- `src/Zapret2Pilot.Runtime/Workspace/RuntimeWorkspaceMaterializer.cs` — production implementation: validates arguments, ensures workspace directory, verifies assets via `ZapretAssetVerifier` with a workspace-rooted `SafePathResolver`, then writes `generated.cfg`, `args.txt`, and hostlist files via `AtomicFileWriter`;
- `src/Zapret2Pilot.Runtime/Workspace/RuntimeWorkspaceMaterializeResult.cs` — success result exposing workspace directory, args file path, generated config path, and written hostlist paths;
- `tests/Zapret2Pilot.Runtime.Tests/Workspace/RuntimeWorkspaceMaterializerTests.cs` — focused tests for happy path, path traversal rejection, atomic replace of existing files, and missing asset failure;
- `VERSION = 0.0.12`;
- `docs/Z2P-ROADMAP.md` — added the `0.0.12 — Runtime Workspace Materialization` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The materializer uses the same `SafePathResolver` instance for both asset verification and file writes, so unsafe paths are rejected uniformly and the workspace root acts as a containment boundary.
- All file writes go through `AtomicFileWriter`; partial writes never leave the workspace inconsistent.
- `CompiledZapretPlan` is intentionally a minimal placeholder. The real profile compiler and `winws2` argument builder will arrive in later milestones.
- No real process is started, killed, or assigned. The materializer only writes files.

## 0.0.13 — Profile Document Foundation

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Engine.Zapret2/Profiles/ProfileDocument.cs` — profile document DTO (`ProfileId`, display name, description, strategy references, hostlist references);
- `src/Zapret2Pilot.Engine.Zapret2/Profiles/StrategyPackDocument.cs` — strategy pack document skeleton (`StrategyPackId`, `StrategyDocument` list);
- `src/Zapret2Pilot.Engine.Zapret2/Profiles/ProfileDocumentValidator.cs` — pure lexical/structural validator returning `Result` with error codes `ProfileDocumentMissing`, `ProfileIdMissing`, `ProfileDisplayNameMissing`, `StrategyReferencesMissing`, `HostlistReferencesMissing`, `StrategyReferenceInvalid`, `DuplicateStrategyReference`, `HostlistReferenceInvalid`, `HostlistPathUnsafe`;
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Profiles/ProfileDocumentValidatorTests.cs` — focused unit tests for valid document, empty ID/name, duplicate strategy references, invalid hostlist paths, null collections and invalid references;
- `VERSION = 0.0.13`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Engine.Zapret2.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The validator is intentionally DTO-level and does not access the filesystem or resolve strategy/hostlist contents. Mapping to domain `ProfileDefinition` and compiler integration are explicitly out of scope for 0.0.13.
- No real process is started, killed, or assigned.

## 0.0.14 — Profile Definition Foundation

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Core/Profiles/StrategyDefinition.cs` — `sealed record` `StrategyDefinition` with name + ordered `IReadOnlyList<string>` parameters and explicit constructor validation (`ArgumentNullException.ThrowIfNull` / `ArgumentException` for null/blank name);
- `src/Zapret2Pilot.Core/Profiles/StrategyPackDefinition.cs` — `sealed record` `StrategyPackDefinition` with `StrategyPackId Id` and `IReadOnlyList<StrategyDefinition> Strategies`;
- `src/Zapret2Pilot.Core/Profiles/ProfileDefinition.cs` — `sealed record` `ProfileDefinition`, `StrategyAssignment` and `HostlistAssignment`; `Description` is the only nullable member, the rest are non-null/non-blank; all five Core.Profiles types are dependency-free of Avalonia, Windows APIs, SQLite, the file system and `Process` (they only reference `Zapret2Pilot.Core.Primitives` and `Zapret2Pilot.Core.Results`);
- `src/Zapret2Pilot.Engine.Zapret2/Profiles/IProfileMapper.cs` — public mapper contract: `Result<ProfileDefinition> Map(ProfileDocument profile, IReadOnlyList<StrategyPackDocument> strategyPacks)`;
- `src/Zapret2Pilot.Engine.Zapret2/Profiles/ProfileDocumentMapper.cs` — production implementation that returns `ProfileDocumentMissing` (ErrorCategory.Profile) for a null profile, `StrategyPackMissing` (ErrorCategory.StrategyPack) when a `StrategyReference` cannot be matched to any supplied `StrategyPackDocument` by `StrategyPackId`, `StrategyMissing` (ErrorCategory.StrategyPack) when the matched pack does not contain the named `StrategyDocument`, and `HostlistReferenceInvalid` (ErrorCategory.Hostlist) for a `HostlistReference` with a null `HostlistId` or null/whitespace `RelativePath`; the mapper is a pure resolver and does NOT duplicate the path-traversal checks already performed by `ProfileDocumentValidator`;
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Profiles/ProfileDocumentMapperTests.cs` — focused xUnit tests: `ValidProfileMapsToDefinition`, `MissingStrategyPackReturnsStrategyPackMissing`, `MissingStrategyInExistingPackReturnsStrategyMissing`, `NullHostlistRelativePathReturnsHostlistReferenceInvalid`, `BlankHostlistRelativePathReturnsHostlistReferenceInvalid`, `NullHostlistIdReturnsHostlistReferenceInvalid`, `EmptyStrategyAndHostlistListsMapSuccessfully`, `NullProfileReturnsProfileDocumentMissing`;
- `VERSION = 0.0.14`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated to reflect the new milestone.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Engine.Zapret2.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The mapper is intentionally a pure resolver. It does not access the filesystem, does not load hostlist contents, does not compile to a `CompiledZapretPlan`, and does not build `winws2` arguments. Profile compilation and `winws2` argument building remain explicitly out of scope for this milestone.
- The mapper uses `Equals` on `StrategyPackId` for pack matching and ordinal string comparison for strategy-name matching, consistent with the validator's `StrategyReferenceComparer`.
- The `Core.Profiles` types follow the same null/blank-guard pattern as `Zapret2Pilot.Core.Runtime.CompiledZapretPlan.HostlistContent` (`ArgumentNullException.ThrowIfNull` + `ArgumentException` for whitespace), which keeps the new domain types consistent with the existing Core style and analyzers (`CA1510`).
- Per `DEC-0010` and Critical Review #18, the boundary between `ProfileDocument` (DTO) and `ProfileDefinition` (domain) is now enforced at the type level: the future profile compiler (0.0.15+) will accept `ProfileDefinition` only.
- No real process is started, killed, or assigned. The mapper is a read-only resolver.
- No new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change, no `Zapret2Pilot.slnx` change (the new `Core.Profiles` types live inside the existing `Zapret2Pilot.Core` project and the new mapper lives inside the existing `Zapret2Pilot.Engine.Zapret2` project).

## 0.0.15 — Zapret Plan Compiler

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Core/Runtime/RuntimePlanCacheKey.cs` — new `sealed record` `RuntimePlanCacheKey(string Value)` in `Core.Runtime` (NOT in `Engine.Zapret2.Compiler`, to avoid a Core → Engine.Zapret2 dependency); constructor rejects null/blank values; lowercase hex digest by convention;
- `src/Zapret2Pilot.Core/Runtime/CompiledZapretPlan.cs` — extended **additively** for 0.0.15: the original 3-parameter constructor `(generatedConfigContent, argsContent, hostlists)` is preserved (it delegates to the new 8-parameter constructor with `null` for the compiler-produced fields); new 8-parameter constructor and new nullable properties `Id`, `ProfileId`, `CommandLine`, `Arguments`, `CacheKey`;
- `src/Zapret2Pilot.Engine.Zapret2/Compiler/ZapretCompilerOptions.cs` — `sealed record` with non-blank `CompilerVersion` and `IReadOnlyDictionary<string, string> ExtraOptions`; exposes `ToCanonicalString()` that sorts the keys lexicographically and includes the compiler version, used as the canonical form for the cache key;
- `src/Zapret2Pilot.Engine.Zapret2/Compiler/CompilationInputs.cs` — `sealed record` carrying the full DEC-0011 input set: `ProfileDefinition`, `ProfileDocumentHash` (non-blank), `StrategyPackHashes` keyed by `StrategyPackId`, `HostlistFingerprints` keyed by `HostlistId`, `RuntimeManifestHash` (non-blank), `ZapretCompilerOptions`; constructor rejects nulls and blank hashes;
- `src/Zapret2Pilot.Engine.Zapret2/Compiler/WinwsArgumentBuilder.cs` — pure, dependency-free static helper producing structured `argv` tokens and a newline-joined `args.txt` body. Strategy tokens are passed through verbatim; hostlist tokens are emitted as `--hostlists=hostlists/<relativePath>`. Tokens containing space, double-quote, tab, newline, carriage return or another shell metacharacter (`|`, `&`, `<`, `>`, `;`, `(`, `)`, `*`, `?`, `[`, `]`, `^`, `!`, `$`, `` ` ``, `\\`) are wrapped in `"..."` with embedded `"` escaped as `\"`. No filesystem I/O;
- `src/Zapret2Pilot.Engine.Zapret2/Compiler/ZapretPlanCompiler.cs` — `public sealed class` with `public Result<CompiledZapretPlan> Compile(CompilationInputs inputs)`. Null inputs are returned as a typed `Result.Failure` with `ErrorCategory.Runtime`. The cache key is `SHA-256(UTF-8(canonical inputs))` in lowercase hex, covering profile id, profile document hash, sorted strategy pack hashes, sorted hostlist fingerprints, runtime manifest hash and the compiler options' canonical string (which includes the compiler version). `RuntimePlanId` is derived deterministically from the cache key. `CommandLine` is the constant placeholder `"bin/winws2.exe"` (absolute-path resolution is a runtime-kernel concern). `GeneratedConfigContent` is the minimal `"# Generated by Zapret2Pilot\n"` header (real config generation is future work). `Hostlists` carries one `HostlistContent` per `HostlistAssignment` with the relative path and an empty `Content` (hostlist file contents are loaded by the orchestrator, not the compiler);
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Compiler/ZapretPlanCompilerTests.cs` — focused xUnit tests: `ValidInputsProducePopulatedPlan`, `NullInputsAreRejected`, `PlanIdIsDerivedFromCacheKey`, `TokensContainingShellMetacharactersAreQuoted`, `EmptyProfileProducesEmptyArguments`;
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Compiler/RuntimePlanCacheKeyTests.cs` — focused xUnit tests: `SameInputsProduceEqualKeys`, `ChangingProfileDocumentHashChangesTheKey`, `ChangingStrategyPackHashChangesTheKey`, `ChangingHostlistFingerprintChangesTheKey`, `ChangingRuntimeManifestHashChangesTheKey`, `ChangingCompilerOptionsChangesTheKey`, `ChangingCompilerVersionChangesTheKey`, `RecordEqualityIsStructuralAndHashCodeMatches`, `ConstructorRejectsNullOrWhitespace`;
- `VERSION = 0.0.15`;
- `docs/Z2P-ROADMAP.md` — added the `0.0.15 — Zapret Plan Compiler` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build src/Zapret2Pilot.Engine.Zapret2/Zapret2Pilot.Engine.Zapret2.csproj -c Release
dotnet test tests/Zapret2Pilot.Engine.Zapret2.Tests -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
dotnet build Zapret2Pilot.slnx -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- `RuntimePlanCacheKey` lives in `Zapret2Pilot.Core.Runtime` so that `CompiledZapretPlan` (also in `Core.Runtime`) can reference it without introducing a `Core → Engine.Zapret2` dependency. Placing it in `Engine.Zapret2.Compiler` as the original work plan suggested would have created a circular dependency.
- The existing 3-parameter `CompiledZapretPlan` constructor is preserved exactly as it was at 0.0.12. The new 8-parameter constructor is an additive overload that compiler-produced plans use; legacy plans (including the existing `RuntimeWorkspaceMaterializer` tests) keep using the 3-parameter form.
- `RuntimeWorkspaceMaterializer` and `RuntimeWorkspaceMaterializerTests` are **not** modified; the additive extension to `CompiledZapretPlan` does not change the materializer's consumer contract.
- The compiler is intentionally a pure, hermetic component: it does not access the filesystem, does not load hostlist contents, does not perform any process work and does not launch `winws2`. Hostlist file contents are loaded by a future orchestrator step and merged into the plan's `Hostlists` before the materializer is invoked; the compiler only records the relative path.
- `CommandLine` is a placeholder constant (`"bin/winws2.exe"`). Absolute-path resolution and the actual process launch belong to the runtime kernel, not to the compiler.
- The cache key covers every DEC-0011 input: profile id, profile document hash, every strategy pack hash (sorted lexicographically by pack id), every hostlist fingerprint (sorted lexicographically by hostlist id), runtime manifest hash, compiler options (compiler version + extra options, sorted by key), and compiler version (via the options). SHA-256 is applied to the UTF-8 bytes of the canonical string and emitted as lowercase hex.
- The argument builder applies safe command-line quoting: space is the minimum trigger; tokens containing space, double-quote, tab, newline, carriage return or another shell metacharacter are wrapped in `"..."` and embedded `"` is escaped as `\"`. Backslashes in the source token are preserved (no double-escape) so that file paths are not mangled.
- The compiler exposes `Compile` as an instance method (not static) to match the planned public API. CA1822 is suppressed locally with a documented pragma, mirroring the convention already used by `ProfileDocumentMapper.Map` in this codebase.
- No real process is started, killed, or assigned in any unit test. The compiler, the builder and their tests are pure and hermetic.
- No new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change, no `Zapret2Pilot.slnx` change (the new `Core.Runtime.RuntimePlanCacheKey` lives inside the existing `Zapret2Pilot.Core` project and the new compiler types live inside the existing `Zapret2Pilot.Engine.Zapret2` project).

## 0.0.16 — Runtime Transaction Model

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionState.cs` — public `enum` describing the documented transaction state machine (`Pending`, `Active`, `Committing`, `Committed`, `RollingBack`, `RolledBack`, `Failed`);
- `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionResult.cs` — public `sealed record class` with a private constructor that enforces the invariant `Error != null iff State == Failed`; static factories `Committed()`, `RolledBack()` and `Failed(ErrorInfo)`; `Failed(null)` throws `ArgumentNullException`;
- `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransaction.cs` — public `sealed class` with `internal` constructor; exposes stable `Id`, observable `State` and carried `Plan?`; `Commit` / `Rollback` are valid only from `Active`; `internal Activate()` and `internal MarkFailed(ErrorInfo)` are the manager-only seams;
- `src/Zapret2Pilot.Runtime/Transactions/IRuntimeTransactionManager.cs` — public contract with `BeginStart(plan)`, `BeginStop()`, `BeginApply(newPlan)`, `Commit(transaction)` and `Rollback(transaction)`;
- `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionManager.cs` — in-memory production implementation. Tracks `isRunning` and `currentPlan` plus a `Dictionary<RuntimeTransaction, Stack<Action>>` of rollback actions. Each `Begin*` flow records a `RestoreStoppedState` / `RestoreRunningState(previousPlan)` action and activates the transaction before returning it. Two new `internal` read-only properties (`IsRunning`, `CurrentPlan`) are exposed for the test project only via the existing `InternalsVisibleTo("Zapret2Pilot.Runtime.Tests")`;
- `tests/Zapret2Pilot.Runtime.Tests/Transactions/RuntimeTransactionManagerTests.cs` — 18 focused xUnit tests covering null-plan rejection on every `Begin*`, the "already running" / "not running" guards, the success path for every transition, the rollback behavior for every state-changing method, unknown-transaction rejection, `RuntimeTransactionResult` validation rules, and the `Active`-only commit/rollback state machine. No real process is started, killed, or assigned;
- `VERSION = 0.0.16`;
- `README.md` — version, current milestone, current patch, and architecture baseline updated (new bullet: "explicit runtime transactions (start/stop/apply with rollback) managed by `RuntimeTransactionManager`");
- `docs/Z2P-ROADMAP.md` — added the `0.0.16 — Runtime Transaction Model` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The transaction constructor is `internal`; only the manager can create transactions. The test project reaches it through the existing `InternalsVisibleTo` declaration, so no public API surface is added beyond the new types in the `Zapret2Pilot.Runtime.Transactions` namespace.
- The `IsRunning` and `CurrentPlan` properties on `RuntimeTransactionManager` are `internal` for the same reason: they exist to keep the new unit tests hermetic and must not be promoted to the public surface in this milestone.
- No real process is started, killed, or assigned. The manager mutates only its own in-memory state (`isRunning`, `currentPlan`, the `activeTransactions` dictionary). The transaction model is the foundation for the future kernel host wiring; the host itself is intentionally out of scope for 0.0.16.
- Thread-safety is intentionally deferred. `RuntimeTransactionManager` is single-threaded by convention; explicit synchronization will be added in a later milestone without breaking the public surface.
- The `Begin*` methods apply the proposed state change and then call `transaction.Activate()`, so callers never observe a transaction in the `Pending` state. The rollback action is pushed onto the `Stack<Action>` BEFORE the state mutation runs, so the manager cannot be left in an inconsistent state on the rollback path even if a future milestone adds side effects to the state mutation.
- `Commit` and `Rollback` both return `Result<Unit>`; success is asserted via `IsSuccess` rather than through the value payload.
- `RuntimeTransactionResult.Failed(null)` throws `ArgumentNullException`; non-failed results never carry an `ErrorInfo`. The private constructor enforces the inverse invariants as well, so it is impossible to construct a successful result with an error or a failed result without one.
- No new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change, no `Zapret2Pilot.slnx` change (the new types live inside the existing `Zapret2Pilot.Runtime` project and the new test file lives inside the existing `Zapret2Pilot.Runtime.Tests` project).

## 0.0.17 — Runtime Process Host

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessStartContext.cs` — public input DTO for `RuntimeProcessHost.StartAsync` carrying the `CompiledZapretPlan`, the resolved `ZapretAssetManifest`, the absolute workspace directory and the absolute path of the runtime executable to launch; constructor rejects null/blank arguments via `ArgumentNullException.ThrowIfNull` and `ArgumentException.ThrowIfNullOrWhiteSpace`;
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHostResult.cs` — public success payload returned from a successful start/stop, exposing the live `ProcessId`, `ProcessName`, `ExecutablePath` and the `CompiledZapretPlan` that was launched; constructor rejects non-positive PIDs, null/whitespace names/paths and null plans;
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` — public `sealed class` that runs the start pipeline on the calling thread to preserve ownership-mutex thread affinity: acquire the global ownership mutex, perform stale lock recovery, materialize the workspace, begin a `Start` transaction, create a Windows Job Object with kill-on-close, launch the runtime process, assign it to the job object, run a readiness check, write the recovery lock file, and finally commit the transaction. Any failure between `BeginStart` and `Commit` rolls the transaction back and tears down the partially-constructed resources. `StopAsync` reverses that pipeline (begin a `Stop` transaction, kill the process if any, wait for exit bounded by a constructor-supplied `stopTimeout`, dispose the process and the job object, delete the lock file, dispose the ownership lease, and commit);
- `src/Zapret2Pilot.Runtime/Health/RuntimeReadinessChecker.cs` — public `static` post-start readiness probe: waits a short, caller-supplied readiness window (default 250 ms, in the documented 100–500 ms range) and then reports whether the process is still running; returns `Result<Unit>` with `RUNTIME_NOT_READY` (`ErrorCategory.Runtime`) when the process has already exited; performs no log parsing or standard-output sniffing (those are explicitly future work);
- `tests/Zapret2Pilot.Testing.FakeRuntime/Zapret2Pilot.Testing.FakeRuntime.csproj` — new console project; `<Description>` documents it as the test-only fake Zapret runtime used by `RuntimeProcessHost` tests in milestone 0.0.17;
- `tests/Zapret2Pilot.Testing.FakeRuntime/Program.cs` — fake lifecycle: print a single `READY` line on stdout, flush, then block until SIGINT/Ctrl+C and exit with code 0. It is a test-only stand-in for `winws2.exe` and must not reference any production code;
- `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj` — added `<ProjectReference Include="..\Zapret2Pilot.Testing.FakeRuntime\..." PrivateAssets="all" />` so the fake `.exe` and `.dll` are copied next to the test assembly at build time;
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessStartContextTests.cs` — focused xUnit tests for the input DTO: valid construction, null plan, null manifest, null/whitespace workspace, null/whitespace runtime path;
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostResultTests.cs` — focused xUnit tests for the success payload: valid construction, non-positive PID, null/whitespace process name, null/whitespace executable path, null plan;
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs` — integration-style xUnit tests for the host: null-context rejection, null-manifest rejection, double-start rejection (`AlreadyRunning`), and a full `StartAsync` → `StopAsync` round-trip with the bundled `FakeRuntime` (the host launches the fake executable, assigns it to the job object, runs the readiness check, writes the recovery lock file and commits the transaction);
- `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeReadinessCheckerTests.cs` — focused xUnit tests for the readiness probe: success path, fast-exit detection (`RUNTIME_NOT_READY`), null process rejection, non-positive timeout rejection and `CancellationToken` propagation;
- `Zapret2Pilot.slnx` — added the new `tests/Zapret2Pilot.Testing.FakeRuntime/Zapret2Pilot.Testing.FakeRuntime.csproj` so the fake is built as part of the solution;
- `VERSION = 0.0.17`;
- `README.md` — version, current milestone, current patch, and architecture baseline updated;
- `docs/Z2P-ROADMAP.md` — added the `0.0.17 — Runtime Process Host` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- **The implementation uses a fake runtime binary only (`Zapret2Pilot.Testing.FakeRuntime`); real `winws2` launch remains explicitly out of scope for this milestone and remains gated by oracle approval** (per `docs/Z2P-CANON.md`, `docs/Z2P-CRITICAL-REVIEW.md` and `DEC-0028`). `RuntimeProcessHost` launches exactly the executable path supplied in `RuntimeProcessStartContext.RuntimeExecutablePath`; in the milestone's tests that path is the `Zapret2Pilot.Testing.FakeRuntime.exe` copied next to the test assembly. No production code path resolves to the real `winws2.exe` in 0.0.17.
- The `TODO(0.0.17)` marker on `WindowsProcessSystemAccessor.CommandLine` is intentionally **not** retired in this milestone. Cross-process command-line retrieval (`NtQueryInformationProcess` / WMI) still requires oracle approval and remains a separate roadmap item.
- The host honors the existing safety primitives end-to-end: ownership mutex is acquired before launch, the transaction is committed only after the recovery lock file is written, and rollback tears down the process, the job object, the lock file and the ownership lease in the right order. The host is not designed for concurrent `StartAsync` calls — its public contract is sequential by design.
- The host tests are the only ones that launch a real OS process; the fake process is part of the test binary and is killed deterministically by `StopAsync` (via the job object's kill-on-close behavior and the explicit kill step) before each test completes.
- `RuntimeReadinessChecker` is intentionally minimal: it performs no I/O on the process (no log parsing, no standard-output sniffing), it only inspects `Process.HasExited` after a bounded wait. Heartbeats, log scanning and native liveness probes are layered on top in a later milestone.
- `RuntimeProcessStartContext` and `RuntimeProcessHostResult` were introduced as standalone DTOs ahead of the host so that the host's public contract is stable before the host is implemented; the constructor validation matches the project's analyzer-friendly pattern (`ArgumentNullException.ThrowIfNull` + `ArgumentException.ThrowIfNullOrWhiteSpace`).
- No new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change. The only `Zapret2Pilot.slnx` change is the addition of the new `Zapret2Pilot.Testing.FakeRuntime` test project.

## 0.0.19 — Runtime State Store

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Storage/Sqlite/SqliteDbInitializer.cs` — added migration `0002_runtime_state_store` with `runtime_sessions` and `runtime_state` tables; refactored migration loop so future migrations can be appended;
- `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs` — public contract for persistent runtime/session state;
- `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs` — default SQLite-backed implementation using `SqliteConnectionFactory`;
- `src/Zapret2Pilot.Runtime/State/RuntimeSessionRecord.cs` — immutable session record (`RuntimeSessionId`, `StartedAtUtc`, `EndedAtUtc?`, `ProfileId?`, `RuntimePlanId?`, `RuntimePlanCacheKey?`, `RuntimeSessionState`);
- `src/Zapret2Pilot.Runtime/State/RuntimeSessionState.cs` — `Active`, `Stopped`, `Failed` enum;
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` — `AddRuntimeKernelStateStore(this IServiceCollection, string databasePath)` extension registering `SqliteStorageOptions`, `SqliteConnectionFactory`, `SqliteDbInitializer` and `IRuntimeKernelStateStore`;
- `src/Zapret2Pilot.App/Program.cs` — registers the state store against `%PROGRAMDATA%\Zapret2Pilot\z2p.db`;
- `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj` — added `ProjectReference` to `Zapret2Pilot.Storage` and `PackageReference` to `Microsoft.Extensions.Hosting`;
- `src/Zapret2Pilot.App/Zapret2Pilot.App.csproj` — added `ProjectReference` to `Zapret2Pilot.Runtime`;
- `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs` — 11 unit tests for start/end/current/recent session behavior and validation;
- `tests/Zapret2Pilot.Runtime.Tests/State/TemporarySqliteDatabase.cs` — hermetic test fixture for file-backed SQLite;
- `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs` — DI integration test resolving `IRuntimeKernelStateStore` from a generic host;
- `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj` — added `Microsoft.Extensions.Hosting` reference for the integration test;
- `VERSION = 0.0.19`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The store is synchronous to match the existing `Zapret2Pilot.Storage` repository style; it is intended to be called from the Runtime Kernel's single-writer thread in future milestones.
- `StartSession` automatically closes any previously active session, so the singleton `runtime_state` row never points at more than one active session.
- Timestamps are stored as ISO-8601 (`O`) strings and parsed with `DateTimeStyles.RoundtripKind`.
- No real `winws2` process is started, killed or assigned. The milestone only adds SQLite persistence and DI wiring.
- No new NuGet packages were added to `Directory.Packages.props`; the only new package reference is `Microsoft.Extensions.Hosting`, which is already centrally managed.

## 0.0.21 — Runtime Health Monitor Hosted Service

Status: **implemented**.

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj` — add
  `System.Reactive` `PackageReference` (version 6.1.0 is already
  managed centrally in `Directory.Packages.props`);
- `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs` —
  new `EndSession(RuntimeSessionId, RuntimeSessionState)` overload;
- `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs` —
  implement the new overload, refactor the private `CloseSession`
  helper to accept the terminal state;
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` —
  `internal Process? RunningProcess { get; }` accessor (thread-safe
  under the existing `stateLock`);
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs` — new
  `public enum` with `Unknown`, `Healthy`, `Exited`;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs` —
  new immutable `sealed record class`;
- `src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs` —
  new public contract (`SnapshotChanged`, `LatestSnapshot`);
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs` —
  new `IHostedService`, `IRuntimeHealthMonitor`, `IDisposable`
  implementation with a `System.Threading.Timer` that only
  enqueues probes onto `RuntimeKernelWorker`;
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` —
  new `AddRuntimeHealthMonitor(this IServiceCollection)` extension
  registering the monitor as a singleton under three keys;
- `src/Zapret2Pilot.App/Program.cs` — call
  `services.AddRuntimeHealthMonitor();` after
  `services.AddRuntimeProcessHost();` in `AppHost.Build`;
- `VERSION = 0.0.21`;
- `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` — `AppVersion`
  bumped from `v0.0.20` to `v0.0.21`;
- `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`
  — the matching `v0.0.21` test assertion;
- `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj` —
  add `System.Reactive` `PackageReference`;
- new xUnit tests:
  - `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs` —
    `EndSessionWithFailedState_MarksSessionAsFailed`,
    `EndSessionWithFailedStateAndUnknownId_Throws`,
    `EndSessionWithFailedStateAndNullId_Throws`,
    `EndSessionWithActiveState_Throws`;
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs` —
    five contract tests (`Constructor_RejectsUnknownEnumValue`,
    `Constructor_RejectsNonPositiveProcessId`,
    `Constructor_AcceptsNullProcessId`, `RecordEquality_IsStructural`,
    `RecordEquality_DiffersWhenAnyFieldDiffers`);
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs` —
    seven integration tests against `FakeRuntime`
    (`Constructor_InitialSnapshotIsUnknown`,
    `StartAsync_BeforeAnyProcess_SnapshotRemainsUnknown`,
    `Probe_AfterProcessStart_PublishesHealthySnapshot`,
    `Probe_AfterProcessExit_PublishesExitedSnapshot`,
    `Probe_TransitionToExited_MarksActiveSessionAsFailed`,
    `StopAsync_DisposesTimerAndStopsPublishingSnapshots`,
    `SnapshotChanged_SubscribersReceiveInitialAndTransitionSnapshots`);
  - `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs` —
    `AddRuntimeHealthMonitorRegistersMonitorAsHostedService`;
  - `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs` —
    promoted to `partial` so the integration tests can reuse the
    existing `HostFixture` via the new
    `RuntimeProcessHostTests.TestHelpers.cs` partial file
    (`HostFixture` is now `internal sealed class` so the sibling
    test file can reach it);
- `docs/Z2P-ROADMAP.md` — new `0.0.21` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally once the implementation lands:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
dotnet build src/Zapret2Pilot.App/Zapret2Pilot.App.csproj -c Release
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeKernelStateStoreTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthSnapshotTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthMonitorTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The milestone does not launch a real `winws2`; the
  `Probe_AfterProcessStart_PublishesHealthySnapshot` and
  `Probe_AfterProcessExit_PublishesExitedSnapshot` tests are
  Windows-gated because `FakeRuntime` is a Windows process and the
  host's job-object / mutex plumbing is platform-specific. The
  cross-platform contract tests
  (`Constructor_InitialSnapshotIsUnknown`,
  `StartAsync_BeforeAnyProcess_SnapshotRemainsUnknown`,
  `StopAsync_DisposesTimerAndStopsPublishingSnapshots`,
  `Constructor_RejectsUnknownEnumValue`,
  `Constructor_RejectsNonPositiveProcessId`,
  `Constructor_AcceptsNullProcessId`,
  `RecordEquality_IsStructural`,
  `RecordEquality_DiffersWhenAnyFieldDiffers`,
  and the four new state-store tests) run on every host.
- The monitor's `Timer` is intentionally created and disposed
  inside `IHostedService.StartAsync` / `StopAsync`; the Generic
  Host owns the lifetime, so `AppHost.Build` does not need to
  call `monitor.StartAsync` / `monitor.StopAsync` manually.
- `IRuntimeHealthMonitor` is intentionally a separate abstraction
  from the underlying `RuntimeProcessHost` so the future
  presentation layer can subscribe to
  `monitor.SnapshotChanged` without depending on the host itself.
- The plan preserves the pre-existing pre-committed changes to
  `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`,
  `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`,
  `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`
  and
  `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeKernelWorkerUiNonBlockingTests.cs`;
  only the `v0.0.20` strings are bumped to `v0.0.21`.
- No new NuGet package versions are added; the only new
  `PackageReference` lines are `System.Reactive` on
  `Zapret2Pilot.Runtime` and on the test project, both pulling
  from the central `System.Reactive` 6.1.0 entry in
  `Directory.Packages.props`.
- No `global.json` change, no lock file change, no
  `Zapret2Pilot.slnx` change.

## 0.0.22 — Runtime Crash Loop Guard

Status: **implemented (current)**.

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/Guard/CrashLoopGuardOptions.cs` — public `sealed record` carrying `BaseBackoff` (default 2 s), `MaxBackoff` (default 5 min), `StabilityWindow` (default 60 s) and `MaxConsecutiveFailures` (default 10); constructor rejects null options, zero/negative backoff values, `MaxBackoff < BaseBackoff`, zero/negative stability window and non-positive `MaxConsecutiveFailures`;
- `src/Zapret2Pilot.Runtime/Guard/CrashLoopGuardResult.cs` — public immutable `sealed record class` with `IsAllowed`, `BackoffRemaining` and `ConsecutiveFailures`;
- `src/Zapret2Pilot.Runtime/Guard/ICrashLoopGuard.cs` — public contract with `Check`, `RecordFailure`, `RecordSuccess` and `Reset`;
- `src/Zapret2Pilot.Runtime/Guard/CrashLoopGuard.cs` — public `sealed class` implementing the contract: thread-safe via a private monitor lock; accepts the configuration record and an optional `Func<DateTimeOffset>?` clock (defaults to `DateTimeOffset.UtcNow`); backoff formula `min(BaseBackoff * 2^(n - 1), MaxBackoff)`; permanent lockout when `consecutiveFailures > MaxConsecutiveFailures`; stability window measures "time since the most recent event (success or failure)" so a fresh failure restarts the window; permanent lockout is sticky and only `Reset()` escapes it;
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` — new `AddCrashLoopGuard(this IServiceCollection)` extension registering `CrashLoopGuardOptions` (with documented defaults) and `ICrashLoopGuard` as singletons; the guard is intentionally NOT registered as an `IHostedService` (it owns no background timer and no disposable resources);
- `src/Zapret2Pilot.App/Program.cs` — call `services.AddCrashLoopGuard();` immediately after `services.AddRuntimeHealthMonitor();` in `AppHost.Build`;
- `VERSION = 0.0.22`;
- `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` — `AppVersion` bumped from `v0.0.21` to `v0.0.22`;
- `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs` — the existing assertion bumped from `v0.0.21` to `v0.0.22`;
- new xUnit tests in `tests/Zapret2Pilot.Runtime.Tests/Guard/`:
  - `CrashLoopGuardOptionsTests.cs` — 14 validation tests (defaults, every rejection path, the equal-base/max edge case, plus null-options rejection through the `CrashLoopGuard` constructors);
  - `CrashLoopGuardTests.cs` — 11 behavior tests covering `Check`, `RecordFailure`, `RecordSuccess`, `Reset`, the cap, the permanent lockout, the concurrent stress test, and the `Result` validation;
  - all tests use a deterministic `FakeClock` so they stay fast, do not depend on real time and never sleep.
- `docs/Z2P-ROADMAP.md` — new `0.0.22` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~CrashLoopGuard"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- `CrashLoopGuard` is a pure in-memory primitive: no I/O, no process work, no P/Invoke.
- Every public method (`Check`, `RecordFailure`, `RecordSuccess`, `Reset`) takes the same private monitor lock; the `ConcurrentRecordFailureAndCheck_StateRemainsConsistent` test is the load-bearing proof of thread-safety.
- The backoff formula doubles and saturates at `MaxBackoff`; the permanent-lockout rule uses a strict `>` comparison (the 11th consecutive failure is the first to be rejected as a permanent lockout) and is sticky.
- The stability window is measured from the most recent event (success or failure), so a fresh failure always restarts the window.
- The guard is intentionally NOT wired into `RuntimeProcessHost`, `RuntimeHealthMonitor` or `RuntimeKernelWorker` in 0.0.22. That integration is a future milestone gated on its own oracle review.
- No new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change, no `Zapret2Pilot.slnx` change.

## 0.0.23 — Runtime Kernel Correctness Hardening

Status: **implemented**.

Implemented slices:

- Health monitor hardening — `RuntimeHealthMonitor` now uses a `PeriodicTimer` driven by an injected `TimeProvider` (replacing the previous callback-based `System.Threading.Timer`), coalesces health probes so only one probe is in flight at a time, publishes snapshots from the monitor loop instead of the `RuntimeKernelWorker` thread, and observes + logs `worker.Enqueue` errors instead of letting them fault the worker;
- State store conditional clear — `RuntimeKernelStateStore.ClearRuntimeState` is now conditional on `runtime_state.session_id` matching the ended session, so a historical `EndSession` no longer clears the singleton row of a newer active session; the contract is documented in `IRuntimeKernelStateStore`;
- Crash guard reset semantics + saturation — `CrashLoopGuard` now resets the consecutive-failure counter only when `RecordSuccess()` has observed a stable, failure-free window, and saturates the counter at `MaxConsecutiveFailures + 1`; the new reset semantics are documented in `ICrashLoopGuard`;
- DI registration — `RuntimeServiceCollectionExtensions` registers `TimeProvider.System` so the monitor can be constructed by the Generic Host container;
- New tests:
  - `RuntimeHealthMonitorTests` — `StopAsync_DoesNotPublishAfterReturn`, `ThrowingSubscriber_DoesNotFaultKernelWorker`, `RepeatedTicks_CoalesceIntoOneProbe`;
  - `RuntimeKernelStateStoreTests` — `EndHistoricalSession_DoesNotClearCurrentSession`, `EndHistoricalSessionWithFailedState_DoesNotClearCurrentSession`;
  - `CrashLoopGuardTests` — `RecordFailure_NoSuccess_AfterStabilityWindow_DoesNotReset`, `RecordSuccess_BeforeFailure_AfterStabilityWindow_DoesNotReset`, `RecordFailure_SaturatesAtMaxConsecutiveFailuresPlusOne`;
- `VERSION` bumped to `0.0.23`;
- `MainWindowViewModel.AppVersion` bumped to `v0.0.23`;
- `MainWindowViewModelTests` assertion bumped to `v0.0.23`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-CRITICAL-REVIEW.md` updated.
- `IRuntimeSupervisor` / `RuntimeSupervisor` (`src/Zapret2Pilot.Runtime/Supervisor/`) — single owner of the runtime start / stop state machine; implements `IHostedService`; gates every `StartAsync` through `ICrashLoopGuard.Check` and surfaces a `RuntimeSupervisorStatus.StartBlocked` snapshot with the `CrashLoopGuardResult` carried in `RuntimeSupervisorState.GuardResult`; records failures against the guard on failed starts and unexpected `Exited` health snapshots; records successes against the guard on `Healthy` transitions; subscribes to `IRuntimeHealthMonitor.SnapshotChanged` and triggers an automatic stop on `Exited`; publishes immutable `RuntimeSupervisorState` snapshots through `CurrentState` and a hot `StateChanged` observable backed by `BehaviorSubject<RuntimeSupervisorState>`; serialises start / stop on a private semaphore so re-entrant `StartAsync` returns a typed `RuntimeSupervisorAlreadyRunning` failure.
- `AddRuntimeSupervisor` DI extension in `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` — registers the same singleton instance under `RuntimeSupervisor`, `IRuntimeSupervisor` and `IHostedService`. `Program.cs` (`AppHost.Build`) calls `AddRuntimeSupervisor` immediately after `AddCrashLoopGuard`.
- `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` — subscribes to `IRuntimeSupervisor.StateChanged` on the UI scheduler and maps `RuntimeSupervisorStatus.StartBlocked` snapshots to `LastAction` so the Avalonia UI can show a "too many crashes, retry in N seconds" / "permanent lockout" message.
- New tests:
  - `tests/Zapret2Pilot.Runtime.Tests/Testing/FakeClock.cs` — deterministic `TimeProvider` shared by supervisor and guard tests so scenarios stay fast and never sleep.
  - `tests/Zapret2Pilot.Runtime.Tests/Supervisor/RuntimeSupervisorTests.cs` — focused unit tests with hand-rolled fake `IRuntimeProcessHost` and `IRuntimeHealthMonitor`, the shared `FakeClock`, and a real `CrashLoopGuard` configured with small, fast options; covers `Check`-gated start, blocked start, failed start → `RecordFailure`, `Healthy` transition → `RecordSuccess`, `Exited` snapshot → automatic stop → `RecordFailure`, idempotent `StopAsync`, re-entrant `StartAsync` → typed `RuntimeSupervisorAlreadyRunning`, semaphore serialisation and observable `StateChanged` semantics.
  - `tests/Zapret2Pilot.App.ViewModelTests/FakeRuntimeSupervisor.cs` — in-memory `IRuntimeSupervisor` fake for view-model tests, backed by a `BehaviorSubject<RuntimeSupervisorState>` so tests can drive transitions synchronously.
  - `StartBlocked_UpdatesLastAction` in `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs` — asserts the view-model maps a `StartBlocked` snapshot to `LastAction`.
  - `AddRuntimeSupervisorRegistersSupervisorAsHostedService` in `RuntimeServiceCollectionExtensionsTests` — asserts the same singleton instance is resolvable as `RuntimeSupervisor`, `IRuntimeSupervisor` and `IHostedService`.

Deferred to future packets (gated on their own oracle reviews):

- `RuntimeKernelWorker` deeper lifecycle fixes — `Dispose` deadlock risk, async-continuation thread affinity, explicit queue-full semantics;
- Real `winws2` launch, automatic restart, UI Start/Stop, Auto Doctor, runtime logs, tray control.

Validation commands to run locally:

```powershell
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeHealthMonitor"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeKernelStateStore"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~CrashLoopGuard"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeSupervisorTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

---

# v6 master plan — 0.0.24+ milestone placeholders

The sections below are placeholders for the v6 master plan milestones
`0.0.24`–`0.1.0`. Each placeholder records the v6 reference, the
milestone status as of the v6 baseline, the v6 §26 acceptance summary
and a "What to add when this milestone reaches Implemented" reminder.
Status is set to **Not Started / Planned** for every milestone; a
milestone may be marked `Implemented` only when the v6 §21.3 rule
("code merged; docs synced; acceptance green; evidence pack complete;
unresolved findings explicitly downgraded/deferred with rationale")
is satisfied.

## 0.0.24 — Runtime Kernel Lifecycle Closure

Status: **implemented**.

Implemented files and areas:

- `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelLoop.cs` — single
  lifecycle authority: dedicated named thread, bounded
  `Channel<RuntimeKernelCommand>`, single reader, reducer state
  commit, effect intent dispatch, idempotent operation identity,
  explicit `OperationId` / `Generation` correlation, cancellation
  reason and irreversible-boundary semantics, idempotent
  `StopCore` / `DisposeCore` with bounded effect drain and
  terminal invariant report;
- `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelState.cs` and
  `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelStatus.cs` — the
  immutable state record and the lifecycle / automation-owner
  status enum (replaces the previous `RuntimeSupervisorStatus`
  dual-axis);
- `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelCommand.cs` and
  `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelReducer.cs` — the
  typed command surface and the pure deterministic reducer that
  returns the next state, the `RuntimeEffectIntent`s, the public
  events and the command outcome;
- `src/Zapret2Pilot.Runtime/Kernel/RuntimeEffectIntent.cs`,
  `RuntimeEffectKind.cs`, `RuntimeOperationId.cs`,
  `RuntimeGeneration.cs`, `RuntimeTransition.cs`,
  `RuntimeReducerResult.cs`, `RuntimeCancellationReason.cs`,
  `AutomationOwner.cs` — the typed plumbing the reducer / loop
  work with;
- `src/Zapret2Pilot.Runtime/Kernel/RuntimeStatePublisher.cs` —
  publishes the latest immutable snapshot outside the kernel
  thread, coalesces high-frequency observations, isolates
  throwing / slow subscribers, and replays the latest value to
  late subscribers;
- `src/Zapret2Pilot.Runtime/Kernel/IRuntimeEffectRunner.cs` and
  `src/Zapret2Pilot.Runtime/Kernel/RuntimeProcessEffectRunner.cs`
  — the new effect boundary. The runner executes process-host
  effects under the `OperationId` / `Generation` identity of the
  dispatching command, reports completions back to the loop,
  and discards stale completions without mutating state;
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeKernelWorker.cs` —
  **deleted** (its lifecycle state machine is folded into
  `RuntimeKernelLoop` per v6 §0.3 critical correction C3 and
  `DEC-0039`);
- `src/Zapret2Pilot.Runtime/Hosting/IRuntimeProcessHost.cs` and
  `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` —
  adapted to the new effect-runner boundary; the host remains
  the lock-secured, mutex-affine adapter and the sole owner of
  the ownership-mutex lease, the Job Object handle and the
  process handle. Its public surface stays source-compatible
  with the v5-era `StartAsync` / `StopAsync` and
  `RunningProcess` seam so that supervisor and view-model
  collaborators continue to compile;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs` —
  adapted to probe `IRuntimeProcessHost` directly under its own
  timer, publish snapshots through `RuntimeStatePublisher`
  outside the kernel thread, and stop being a third lifecycle
  authority;
- `src/Zapret2Pilot.Runtime/Supervisor/IRuntimeSupervisor.cs`,
  `RuntimeSupervisor.cs`, `RuntimeSupervisorStatus.cs` —
  `RuntimeSupervisor` is now a thin façade over
  `RuntimeKernelLoop`; the `SemaphoreSlim` lifecycle state
  machine is removed and the supervisor exposes the same
  `IRuntimeSupervisor` contract to the Application layer
  (source-compatible);
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs`
  — DI cleanup: registers `RuntimeKernelLoop` as a singleton
  hosted service, drops the now-obsolete
  `AddRuntimeKernelWorker` / `AddRuntimeProcessHost` worker
  plumbing, and updates `AddRuntimeSupervisor` to resolve the
  loop singleton;
- `src/Zapret2Pilot.App/Program.cs` — registers the new
  `RuntimeKernelLoop` and no longer touches the worker;
- new xUnit tests under `tests/Zapret2Pilot.Runtime.Tests/Kernel/`:
  - `RuntimeKernelLoopTests.cs` — concurrent / faulted /
    cancelled loop tests, admission-after-shutdown rejection,
    duplicate `OperationId` idempotency, automation-owner
    conflict, `StaleCompletionIsIgnored`,
    `SubscriberFailureDoesNotBreakKernel`,
    `DisposeReleasesAllResources`, `LateStartCannotOverwriteStopped`,
    `StopDuringStartSupersedesStart`,
    `ExitedDuringStartCannotPublishRunning`,
    `RepeatedExitTriggersOneCleanup`, `ShutdownDuringEveryState`,
    `OverlappingAutomationOwnersRejected`,
    `SecondProductionRuntimeRejected`,
    `CancellationAfterIrreversibleBoundaryRequiresRecovery`;
  - `RuntimeKernelReducerTests.cs` — exhaustive state × command
    table coverage and persisted-seed generated sequences;
  - `RuntimeProcessEffectRunnerTests.cs` — effect runner
    identity propagation, stale-completion discard and
    faulted-runner recovery;
  - `RuntimeStatePublisherTests.cs` — coalescing,
    slow-subscriber isolation, late-subscriber replay and
    throwing-subscriber containment;
- updated xUnit tests:
  - `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`
    — adapted to the effect-runner boundary (no worker
    dependency);
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs`
    — probes the `IRuntimeProcessHost` adapter directly and
    asserts off-kernel publication;
  - `tests/Zapret2Pilot.Runtime.Tests/Supervisor/RuntimeSupervisorTests.cs`
    — drives the supervisor façade through the loop;
  - `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`
    — asserts the loop is registered as the same instance
    under `RuntimeKernelLoop`, `IRuntimeKernelLoop` and
    `IHostedService`;
  - `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeKernelWorkerTests.cs`
    and `RuntimeKernelWorkerUiNonBlockingTests.cs` — **deleted**
    together with the worker.
- `VERSION = 0.0.24`;
- `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` —
  `AppVersion` bumped from `v0.0.23` to `v0.0.24`;
- `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`
  — the matching `v0.0.24` test assertion;
- `docs/Z2P-ROADMAP.md` — current `VERSION` set to `0.0.24`,
  next milestone set to `0.0.25`, `0.0.24` moved from the
  upcoming-milestone table to the historical / implemented
  index;
- `docs/Z2P-DECISION-LOG.md` — new `DEC-0050` records the
  reducer-driven `RuntimeKernelLoop` as the single lifecycle
  authority;
- `docs/Z2P-CRITICAL-REVIEW.md` — `0.0.24` bullet added,
  dual-authority / no-stale-completion / orphan-FakeRuntime
  findings closed.

Validation commands to run locally:

```powershell
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeKernelLoop"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeKernelReducer"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeProcessEffectRunner"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeStatePublisher"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeSupervisor"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeHealthMonitor"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeProcessHost"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensions"
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- This is the first milestone that ships `RuntimeKernelLoop` and
  the first milestone after which a real `winws2` is allowed
  (gated by `0.0.24`–`0.0.28` per v6 §26.1 and §32). Real
  `winws2` launch remains forbidden in this milestone; the
  new boundary is exercised end-to-end against
  `Zapret2Pilot.Testing.FakeRuntime`.
- The pre-`0.0.24` `RuntimeKernelWorker` and the supervisor
  `SemaphoreSlim` design are *superseded*. The earlier
  "awaited delegates preserve dedicated-thread affinity" claim
  is removed from the architecture document; the kernel thread
  is now an explicit, named entity owned by `RuntimeKernelLoop`.
- `IRuntimeSupervisor` keeps its public surface; it is a façade
  over the loop. Tests against the supervisor therefore remain
  green without a sweeping rewrite, and the view-model
  subscription contract (`StateChanged`) is unchanged.
- No new NuGet packages, no `global.json` change, no
  `Directory.Packages.props` change, no lock file change, no
  `Zapret2Pilot.slnx` change. The new types live inside the
  existing `Zapret2Pilot.Runtime` project and the new tests
  live inside the existing `Zapret2Pilot.Runtime.Tests`
  project.

## 0.0.25 — Bootstrap, Platform Boundaries & UI Composition

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §28.
- Required outcome (v6 §26): trusted configuration, Windows TFMs,
  no service locator, testable visual shell.
- v6 §0.3 critical corrections applied here: C6 (Generic Host
  defaults restricted), C7 (Windows-specific TFMs),
  C8 (typed feature facades over reflection CommandBus).
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.26 — Privileged Boundary & Secure Process Launch

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §29.
- Required outcome (v6 §26): STARTUPINFOEX/Job containment before
  execution; exact argv/handles.
- v6 §0.3 critical corrections applied here: C4 (secure process
  creation is the only production launcher; no
  `Process.Start → AssignProcessToJobObject` before the first real
  winws2). Also includes the v6 §29 portable bootstrap spike.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.27 — Safe SQLite, Durable State & Recovery

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §30.
- Required outcome (v6 §26): fixed controlled native SQLite, single
  writer, online backup, journals.
- v6 §0.3 critical corrections applied here: C1 (SQLite native
  runtime is a P0 release blocker; deprecated/vulnerable
  `SQLitePCLRaw.lib.e_sqlite3` is forbidden;
  `Microsoft.Data.Sqlite.Core` + controlled native sqlite3 with
  runtime version gate).
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry. The
  `NuGetAuditSuppress` for the current native SQLite package must be
  removed before this milestone can be marked `Implemented`.

## 0.0.28 — Deployment Trust, Bundle Compatibility & Preflight

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §31.
- Required outcome (v6 §26): installed/portable trust; whole-app
  bootstrap spike; verified bundles.
- v6 §0.3 critical corrections applied here: C2 (portable staging
  covers the whole elevated application, not only winws2). Also
  introduces `RuntimeCapabilityManifest` and `UpstreamPromotionState`
  per v6 §0.4 / §11.9 / §11.10.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry. Public portable
  ZIP remains forbidden before `0.0.42` (see v6 §52).

## 0.0.29 — Real winws2 Developer Smoke

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §32.
- Required outcome (v6 §26): first isolated controlled real-runtime
  launch.
- v6 hard prerequisite (v6 §32): Evidence Packs for `0.0.24`–`0.0.28`
  complete (single Runtime authority, secure contained process
  creation, safe SQLite native runtime, durable recovery, trusted
  configuration, installed/portable app trust foundation, verified
  compatible bundle, bounded stdout/stderr, global exception
  policy).
- Gate: Developer channel, explicit internal build capability,
  isolated disposable Windows VM, verified exact Runtime Bundle,
  approved fixed Strategy Pack / profile, no Stable/public UI Start
  path, no ordinary shared CI runner.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.30 — Application Runtime UX & Dashboard SSoT

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §33.
- Required outcome (v6 §26): compile-time typed use cases and honest
  dashboard.
- v6 §0.3 critical corrections applied here: C8 (typed feature
  facades) and v6 §6 (four-dimensional health model with
  `DashboardHeroPolicy`).
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.31 — Profiles, Rules & Deterministic Compiler

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §34.
- Required outcome (v6 §26): real hostlists/config, versioned
  canonical hash.
- v6 §0.3 critical corrections applied here: C5 (compiler cache
  contract: separate `CompilerCompatibilityVersion`,
  `CompilerOptionsVersion`, `CanonicalizationVersion`; typed
  length-prefixed canonical hash writer). Also introduces the
  Traffic Impact Analyzer (v6 §34 Scope I) and the curated Strategy
  Catalog (v6 §34 Scope J).
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.32 — Apply Profile, PlanDiff & Rollback

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §35.
- Required outcome (v6 §26): durable crash-recoverable switching.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.33 — Observability & Network Hooks

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §36.
- Required outcome (v6 §26): bounded output, LoggerMessage,
  Activity/Meter, local logs.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.34 — Key Services Probe Engine

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §37.
- Required outcome (v6 §26): versioned bounded efficacy checks.
- v6 §0.4 corrections applied here: probe contexts
  (`BaselineWithoutBypass`, `ProductionHealth`, `CandidateEvaluation`,
  `ControlNetwork`) and the bounded service capability model
  (`WebAccess`, `MediaDelivery`, `RealtimeUdp`, `NativeClient`).
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.35 — Auto Doctor Quick/Full

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §38.
- Required outcome (v6 §26): isolated candidates and explainable
  scoring.
- v6 §0.4 corrections applied here: hard-gate + Pareto selection
  (no auto-selecting the first perfect candidate; equally effective
  scoped / safer / more stable / simpler strategies win).
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.36 — Autopilot & Network Binding

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §39.
- Required outcome (v6 §26): stable automatic policy without
  flapping.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.37 — Full MVP UI, Onboarding & Accessibility

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §40.
- Required outcome (v6 §26): complete product UI and usability
  preparation.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.38 — Tray & Desktop Lifecycle

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §41.
- Required outcome (v6 §26): correct close/exit/shutdown/sleep
  behavior.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.39 — Crash Recovery, Support Bundle & Data Management

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §42.
- Required outcome (v6 §26): recovery UX, typed redaction, cleanup.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.40 — TUF Runtime Repository Trust & Catalog

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §43.
- Required outcome (v6 §26): reviewed POUF, conformance-tested
  metadata client.
- v6 §0.3 critical corrections applied here: C12 (TUF client is a
  separate security-critical subsystem; needs POUF, conformance
  vectors, root rotation / rollback / freeze tests, independent
  review).
- Gate: no target download / activation in this milestone; only
  metadata catalog and resolver. Stable cannot select Development
  root.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.41 — Runtime Download, Activation & Rollback

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §44.
- Required outcome (v6 §26): resilient download, safe extraction,
  leases, candidate activation.
- v6 §0.3 critical corrections applied here: C11 (runtime update
  uses explicit leases; Current / Candidate / Previous bundles
  cannot be deleted while leased).
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.42 — MSI + Secure Portable Packaging, Signing & Upgrade

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §45.
- Required outcome (v6 §26): WiX MSI and signed NativeAOT
  whole-app portable bootstrapper.
- v6 §0.3 critical corrections applied here: C2 (whole-app
  portable staging). The portable bootstrapper becomes the public
  production entry point and the v6 §10.4 NativeAOT design becomes
  the production architecture (was a spike in `0.0.28`).
- Gate (v6 §52): public portable ZIP is forbidden before this
  milestone.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.0.43 — Release Candidate Hardening

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §46.
- Required outcome (v6 §26): security/soak/usability/provenance
  freeze. Feature freeze; only P0/P1 correctness/security fixes,
  performance regressions, documentation/evidence and release
  tooling fixes.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.

## 0.1.0 — Full MVP

Status: **Not Started / Planned.**

- v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §47.
- Required outcome (v6 §26): stable installer + portable release.
- Final release gate (v6 §47): all P0 blockers closed, no
  vulnerable / suppressed SQLite native dependency, portable
  whole-app staging approved, TUF conformance / security review
  approved, real-runtime soak passed, installer/portable
  signatures verified, canonical docs synced, exact source tag
  and provenance published.
- When this milestone is implemented, replace this block with the
  standard status entry and the matching DEC entry.


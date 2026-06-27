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

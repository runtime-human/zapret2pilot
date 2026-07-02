# Zapret2Pilot / Z2P — Roadmap

Versioning starts from `0.0.1` and increments by patch versions during early development.

Implementation is performed through ChatGPT in small patches. Codex is not the implementation executor for this project.

## 0.0.1 — Repo bootstrap and initial app skeleton

Goal:

- create the first code skeleton without any real runtime launch;
- establish .NET 10 solution structure;
- establish Avalonia + ReactiveUI + Generic Host baseline;
- keep runtime/process work out of the first patch.

Substeps:

### 0.0.1-a — Build foundation

Status: Implemented.

Implemented in:

- solution file;
- `src/` and `tests/` layout;
- `Directory.Build.props`;
- `Directory.Packages.props`;
- `global.json`;
- `VERSION = 0.0.1`;
- `.gitignore`;
- minimal root `README.md`;
- `Zapret2Pilot.Core` skeleton;
- `Zapret2Pilot.Application` skeleton;
- `Zapret2Pilot.Core.Tests`;
- `Zapret2Pilot.Application.Tests`.

Scope:

- solution file;
- `src/` and `tests/` layout;
- `Directory.Build.props`;
- `Directory.Packages.props`;
- `global.json`;
- `VERSION = 0.0.1`;
- `.gitignore`;
- minimal root `README.md`;
- Core project skeleton;
- Application project skeleton;
- basic test projects.

Non-goals:

- no Avalonia UI yet;
- no real winws2 launch;
- no RuntimeSupervisor;
- no Windows Job Objects;
- no SQLite;
- no Auto Doctor.

### 0.0.1-b — Avalonia + ReactiveUI shell

Status: Implemented.

Implemented in:

- `src/Zapret2Pilot.App`;
- Avalonia desktop app project;
- ReactiveUI baseline;
- System.Reactive baseline;
- `Microsoft.Extensions.Hosting` inside app;
- light theme placeholder;
- `MainWindow`;
- sidebar skeleton;
- dashboard skeleton with mock/design-time data;
- `MainWindowViewModel` based on ReactiveUI;
- ViewModel tests;
- solution update;
- README status update.

Scope:

- Avalonia desktop app project;
- ReactiveUI baseline;
- `Microsoft.Extensions.Hosting` inside app;
- light theme placeholder;
- MainWindow;
- sidebar skeleton;
- dashboard skeleton with mock/design-time data;
- ViewModel tests.

Non-goals:

- no real winws2 launch;
- no runtime process logic in ViewModel;
- no Windows Service;
- no IPC service layer;
- no tray yet.

Acceptance for 0.0.1:

- `dotnet restore` passes;
- `dotnet build -c Release` passes;
- `dotnet test -c Release` passes;
- app starts and shows mock dashboard shell;
- no Windows Service;
- no IPC service layer;
- no `Process.Start` / `Process.Kill` runtime logic;
- no `.bat` / `.cmd` wrapper architecture;
- no real `winws2` launch.

## 0.0.2 — Core primitives

Status: Implemented.

Implemented in:

- `Result`;
- `Result<T>`;
- `Unit`;
- `ErrorInfo`;
- `ErrorSeverity`;
- `ErrorCategory`;
- typed IDs:
  - `ProfileId`;
  - `StrategyPackId`;
  - `RuntimePlanId`;
  - `RuntimeSessionId`;
  - `RuntimeTransactionId`;
  - `HostlistId`;
  - `ProbeSessionId`;
  - `EventId`;
- unit tests for result primitives and typed IDs;
- `VERSION = 0.0.2`;
- README status update.

Scope:

- `Result<T>`;
- `Result`;
- `Unit`;
- `ErrorInfo`;
- `ErrorSeverity`;
- `ErrorCategory`;
- typed IDs:
  - `ProfileId`;
  - `StrategyPackId`;
  - `RuntimePlanId`;
  - `RuntimeSessionId`;
  - `RuntimeTransactionId`;
  - `HostlistId`;
  - `ProbeSessionId`;
  - `EventId`.

Acceptance:

- unit tests for primitives;
- no dependency from Core to UI/Windows/SQLite.

## 0.0.3 — Application command foundation

Status: Implemented.

Implemented in:

- `IAppCommand<TResponse>`;
- `ICommandBus` with single generic response inference;
- `ICommandHandler<TCommand,TResponse>`;
- `CommandBus` backed by `IServiceProvider.GetService(Type)`;
- exact runtime command type handler resolution;
- missing handler failure result;
- command bus tests for successful dispatch, missing handler, null command and handler failure propagation;
- `VERSION = 0.0.3`;
- README status update.

Scope:

- `IAppCommand<TResponse>`;
- `ICommandBus` with single generic response inference;
- `ICommandHandler<TCommand,TResponse>`;
- no runtime commands yet;
- basic handler resolution tests.

Acceptance:

- CommandBus call site does not require two explicit generic parameters;
- no runtime process logic.

## 0.0.4 — UI navigation foundation

Status: Implemented.

Implemented in:

- `RouteId` strongly typed shell route identifier;
- `INavigationRouter`;
- `NavigationRouter`;
- `INavigationPageFactory`;
- `NavigationPageFactory`;
- `NavigationPageViewModel` placeholder page representation;
- `NavigationItemViewModel` ReactiveUI sidebar item model;
- dashboard/profile/settings placeholder routes;
- `IUiScheduler` abstraction;
- `ImmediateUiScheduler` synchronous initial scheduler;
- shell XAML bindings for route switching;
- ViewModel tests for route selection, unknown route behavior and scheduler behavior;
- `VERSION = 0.0.4`;
- README status update.

Scope:

- `NavigationRouter`;
- `RouteId`;
- page factory abstraction;
- dashboard/profile/settings route placeholders;
- UI scheduler abstraction.

Acceptance:

- ViewModels remain ReactiveUI-based;
- no CommunityToolkit.Mvvm ViewModels;
- no direct background-thread UI mutation.

## 0.0.5 — Storage foundation

Status: Implemented.

Implemented in:

- `Zapret2Pilot.Storage` project;
- `Zapret2Pilot.Storage.Tests` project;
- `Microsoft.Data.Sqlite` storage provider;
- SQLite connection factory;
- DB initializer;
- migrations foundation;
- `app_settings` table;
- `event_journal` table;
- settings repository skeleton;
- event journal skeleton;
- file-backed SQLite tests;
- required SQLite PRAGMA:
  - `journal_mode=WAL`;
  - `busy_timeout=5000`;
  - `synchronous=NORMAL`;
  - `foreign_keys=ON`;
- `VERSION = 0.0.5`;
- README status update.

Scope:

- SQLite connection factory;
- DbInitializer;
- migrations foundation;
- settings repository;
- event journal skeleton.

Required PRAGMA:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

Acceptance:

- SQLite initializer tests;
- no UI direct database access.

## 0.0.6 — File safety foundation

Status: Implemented.

Implemented in:

- `Zapret2Pilot.Infrastructure` project;
- `Zapret2Pilot.Infrastructure.Tests` project;
- `AppDataLayout`;
- `AppDataPathProvider`;
- `SafePathResolver`;
- `AtomicFileWriter`;
- `DiagnosticsRedactor`;
- tests for app data layout creation;
- tests for path traversal prevention;
- tests for atomic text writes;
- tests for diagnostics redaction;
- `VERSION = 0.0.6`;
- README status update.

Scope:

- ProgramData layout;
- AtomicFileWriter;
- SafePathResolver;
- diagnostics redaction first version.

Acceptance:

- atomic write tests;
- path traversal tests;
- redaction tests.

## 0.0.7 — Runtime ownership foundation

Scope:

- Global Mutex ownership;
- runtime lock metadata;
- stale lock recovery;
- process ownership detector interfaces.

Acceptance:

- lock file is metadata only;
- mutex is atomic ownership primitive;
- stale lock scenarios tested.

## 0.0.8 — Job Objects foundation

Status: Implemented.

Scope:

- Windows Job Object primitive;
- close-time child process cleanup configuration;
- safe native handle lifetime;
- unsupported-platform guard;
- focused tests for supported and unsupported behavior.

Acceptance:

- primitive can be created and disposed safely on Windows;
- native handle ownership is deterministic;
- unsupported platforms return controlled behavior;
- production runtime code still has no real process-launch integration.

## 0.0.9 — Job Assignment Foundation

Status: Implemented.

Scope:

- `AssignProcessToJobObject` P/Invoke seam;
- `IRuntimeJobObjectProcessAssigner` abstraction;
- `RuntimeJobObjectProcessAssigner` production implementation;
- `RuntimeProcessHandle` process-handle wrapper;
- fake-native `IJobObjectNativeApi` seam for unit tests;
- platform guards for non-Windows hosts;
- result model that maps native error codes to typed statuses;
- focused unit tests covering all rejection paths and error mapping.

Acceptance:

- `dotnet build` and `dotnet test` pass;
- no real process is launched in any unit test;
- production runtime code carries no `Process.Start`, `Process.Kill`, `winws2`, `WinDivert`, or `WindowsService` references;
- handle lifetime remains owned by `RuntimeJobObject`; `SafeProcessHandle` is never exposed publicly.

## 0.0.10 — Runtime Detector Implementation

Status: Implemented.

Scope:

- `IProcessSnapshot` immutable process introspection seam (nullable `CommandLine`);
- `IProcessSystemAccessor` testable seam with a Windows production accessor (`WindowsProcessSystemAccessor`);
- `RuntimeOwnershipDetector` real verification implementation: PID, process name, executable path, command-line hash, plan hash and process start time (±5 s);
- `RuntimeOwnershipVerificationResult` factory methods for all mismatch statuses and `CommandLineUnverifiable`;
- `RuntimeOwnershipVerificationStatus.CommandLineUnverifiable = 8`;
- `IRuntimeOwnershipDetector.Verify(RuntimeLockMetadata, string expectedPlanHash)` signature;
- `FakeProcessSystemAccessor` and focused `RuntimeOwnershipDetector` unit tests;
- contract tests updated to the new two-argument signature;
- `DEC-0028 — Nullable CommandLine in IProcessSnapshot`.

Acceptance:

- `dotnet build` and `dotnet test` pass on the whole solution;
- no real process is launched in any unit test;
- `WindowsProcessSystemAccessor` does NOT retrieve another process's real command line (returns `null` with a `TODO(0.0.17)` marker);
- no new NuGet packages and no unsafe P/Invoke for process introspection;
- verification order matches the specification exactly;
- `IProcessSnapshot.CommandLine` is `string?` and is never coerced to non-null.

## 0.0.11 — Runtime Asset Manifest

Status: Implemented.

Scope:

- `Zapret2Pilot.Engine.Zapret2` project: a Zapret2 engine adapter that owns runtime asset verification, profile compilation and `winws2` argument building (no real process launch yet);
- runtime asset manifest model: `ZapretAssetManifest`, `ZapretRuntimeAsset`, `AssetKind` and `ZapretAssetVerificationSummary`;
- `ZapretAssetVerifier` that verifies presence, path safety and SHA-256 hash of every asset declared in the manifest and returns a `Result<ZapretAssetVerificationSummary>`;
- `ISafePathResolver` minimal abstraction in `Zapret2Pilot.Core.FileSystem`, implemented by `Zapret2Pilot.Infrastructure.FileSystem.SafePathResolver`, so that `Engine.Zapret2` stays free of `Infrastructure` and remains unit-testable with a fake resolver;
- `Zapret2Pilot.Engine.Zapret2.Tests` with focused tests for: valid manifest, missing executable, hash mismatch, and path escape attempt;
- `DEC-0029 — ISafePathResolver abstraction in Core`.

Acceptance:

- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole solution;
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole solution;
- no real `winws2` process is launched by `Engine.Zapret2` or by its tests;
- the asset verifier uses `ISafePathResolver` from Core, not a concrete Infrastructure type;
- no new NuGet packages.

## 0.0.12 — Runtime Workspace Materialization

Status: Implemented.

Scope:

- `CompiledZapretPlan` placeholder in `Zapret2Pilot.Core.Runtime`: a minimal record carrying the generated config content, the args file content and a list of hostlist payloads (relative path + content); the real profile compiler (0.0.13+) will produce a richer record, but the materializer API is designed to remain stable across that evolution;
- `IRuntimeWorkspaceMaterializer` abstraction and `RuntimeWorkspaceMaterializer` production implementation in `Zapret2Pilot.Runtime.Workspace`: validates arguments, ensures the workspace directory exists, runs `ZapretAssetVerifier` against the same workspace root before any file is written, then writes the generated config (`generated.cfg`), the args file (`args.txt`) and every hostlist payload into `<workspace>/hostlists/<name>`;
- `RuntimeWorkspaceMaterializeResult` value object exposing the workspace directory, the args file path, the generated config path and the list of written hostlist paths;
- all workspace writes go through `AtomicFileWriter.WriteAllText` and all paths are resolved through a workspace-rooted `SafePathResolver`, so a partial write never leaves the workspace in an inconsistent state and no caller-supplied relative path can ever escape the workspace root;
- the materializer delegates asset integrity to `ZapretAssetVerifier` (0.0.11) and never duplicates that logic; verifier failures are propagated as `WorkspaceAssetVerificationFailed` with the original error category preserved;
- `Zapret2Pilot.Runtime.Tests/Workspace/RuntimeWorkspaceMaterializerTests.cs` with focused unit tests for: happy path, hostlist path-traversal rejection, atomic replacement of pre-existing files, and missing-asset failure propagation (no real process is launched, the resolver is the production `SafePathResolver` from Infrastructure, and `TemporaryDirectory` is used for hermetic test fixtures);
- `VERSION = 0.0.12`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated to reflect the new milestone.

Acceptance:

- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole solution;
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole solution;
- no real `winws2` process is launched by `Runtime.Workspace` or by its tests;
- the materializer uses the same `SafePathResolver` (rooted at the workspace directory) for both asset verification and file writing, so unsafe paths are rejected uniformly;
- file writes are atomic and do not leave orphan `.tmp` files in the workspace;
- no new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change.

## 0.0.13 — Profile Document Foundation

Status: Implemented.

Scope:

- `ProfileDocument` DTO in `Zapret2Pilot.Engine.Zapret2/Profiles`: stable `ProfileId`, display name, optional description, strategy references and hostlist references;
- `StrategyPackDocument` skeleton with `StrategyPackId` and a list of `StrategyDocument` items;
- `StrategyReference` and `HostlistReference` value records;
- `ProfileDocumentValidator` that performs pure lexical/structural validation and returns `Result`: validates IDs, display names, duplicate strategy references, and unsafe hostlist relative paths (no filesystem access);
- `Zapret2Pilot.Engine.Zapret2.Tests/Profiles/ProfileDocumentValidatorTests.cs` with focused unit tests for valid document, empty ID/name, duplicate strategy references, invalid hostlist paths, null collections and invalid references;
- `VERSION = 0.0.13`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated to reflect the new milestone.

Acceptance:

- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole solution;
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole solution;
- validator rejects empty IDs, empty display names, duplicate strategy references and unsafe hostlist paths;
- no real `winws2` process is launched;
- no filesystem access from the validator.

## 0.0.14 — Profile Definition Foundation

Status: Implemented.

Follows 0.0.13. Builds on top of the validated `ProfileDocument`/`StrategyPackDocument` DTOs and the `ProfileDocumentValidator` introduced there.

Scope:

- `Zapret2Pilot.Core.Profiles` namespace with the validated domain model required by `DEC-0010` and Critical Review #18:
  - `ProfileDefinition` (validated domain model: `ProfileId`, `DisplayName`, optional `Description`, `IReadOnlyList<StrategyAssignment>`, `IReadOnlyList<HostlistAssignment>`);
  - `StrategyAssignment` (resolved strategy + origin `StrategyPackId`);
  - `HostlistAssignment` (resolved `HostlistId` + relative path; path-traversal safety remains the validator's responsibility);
  - `StrategyDefinition` (name + ordered parameter tokens);
  - `StrategyPackDefinition` (pack id + resolved strategies);
  - all five are `sealed record` types with explicit constructor validation, no filesystem/UI/SQLite/Process dependencies (only `Zapret2Pilot.Core.Primitives` and `Zapret2Pilot.Core.Results` are used);
- `Zapret2Pilot.Engine.Zapret2.Profiles.IProfileMapper` interface and `ProfileDocumentMapper` production implementation:
  - signature: `Result<ProfileDefinition> Map(ProfileDocument profile, IReadOnlyList<StrategyPackDocument> strategyPacks)`;
  - returns `ProfileDocumentMissing` (ErrorCategory.Profile) for a null profile document;
  - returns `StrategyPackMissing` (ErrorCategory.StrategyPack) when a `StrategyReference` cannot be matched to any supplied `StrategyPackDocument` by `StrategyPackId`;
  - returns `StrategyMissing` (ErrorCategory.StrategyPack) when the matched pack does not contain the named `StrategyDocument`;
  - returns `HostlistReferenceInvalid` (ErrorCategory.Hostlist) for a `HostlistReference` with a null `HostlistId` or null/whitespace `RelativePath`;
  - does NOT duplicate the path-traversal checks already performed by `ProfileDocumentValidator`; the mapper only guarantees the hostlist reference is structurally complete;
  - is a pure resolver: no filesystem access, no `CompiledZapretPlan`, no `winws2` argument building, no process work;
- `Zapret2Pilot.Engine.Zapret2.Tests/Profiles/ProfileDocumentMapperTests.cs` with focused xUnit tests for: valid mapping, missing strategy pack, missing strategy in an existing pack, null/blank hostlist relative path, null hostlist id, empty strategy/hostlist lists, and null profile;
- `VERSION = 0.0.14`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated to reflect the new milestone.

Acceptance:

- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole solution;
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole solution;
- the mapper resolves every `StrategyReference` against the supplied strategy packs by `StrategyPackId` and produces a `StrategyAssignment` carrying the matched `StrategyDefinition`;
- the mapper returns the documented typed failures for null profile, missing pack, missing strategy and structurally invalid hostlist references;
- no real `winws2` process is launched;
- the mapper does not access the filesystem and does not perform path-traversal checks (those remain the validator's job);
- no new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change;
- the `Core.Profiles` types are dependency-free of Avalonia, Windows APIs, SQLite, the file system and `Process`.

## 0.0.15 — Zapret Plan Compiler

Status: Implemented.

Follows 0.0.14. Implements the pure, deterministic Zapret plan compiler that turns a validated `ProfileDefinition` and a fully-resolved set of DEC-0011 inputs into a fully populated `CompiledZapretPlan`, and a content-addressed `RuntimePlanCacheKey` that drives the future runtime plan cache.

Scope:

- `Zapret2Pilot.Core.Runtime.RuntimePlanCacheKey` — new `sealed record` in **Core.Runtime** (not in `Engine.Zapret2.Compiler`, which would create a Core → Engine.Zapret2 dependency) holding the lowercase-hex SHA-256 digest of the canonicalized compilation inputs; constructor rejects null/blank values; participates in `CompiledZapretPlan` additively;
- `Zapret2Pilot.Core.Runtime.CompiledZapretPlan` — extended **additively** for 0.0.15:
  - the original 3-parameter constructor `(generatedConfigContent, argsContent, hostlists)` is preserved with its semantics (calls the new 8-parameter constructor with `null` for the compiler-produced fields);
  - a new 8-parameter constructor is added: `(generatedConfigContent, argsContent, hostlists, id, profileId, commandLine, arguments, cacheKey)`;
  - new nullable properties: `RuntimePlanId? Id`, `ProfileId? ProfileId`, `string? CommandLine`, `IReadOnlyList<string>? Arguments`, `RuntimePlanCacheKey? CacheKey`;
  - the existing `RuntimeWorkspaceMaterializer` and its tests are NOT modified and still consume the legacy 3-parameter constructor;
- `Zapret2Pilot.Engine.Zapret2.Compiler.ZapretCompilerOptions` — `sealed record` with `CompilerVersion` (non-blank) and `IReadOnlyDictionary<string, string> ExtraOptions`; exposes `ToCanonicalString()` for deterministic hashing (keys sorted lexicographically);
- `Zapret2Pilot.Engine.Zapret2.Compiler.CompilationInputs` — `sealed record` carrying the full DEC-0011 input set: `ProfileDefinition Definition`, `string ProfileDocumentHash`, `IReadOnlyDictionary<StrategyPackId, string> StrategyPackHashes`, `IReadOnlyDictionary<HostlistId, string> HostlistFingerprints`, `string RuntimeManifestHash`, `ZapretCompilerOptions Options`; constructor rejects null arguments and blank hashes;
- `Zapret2Pilot.Engine.Zapret2.Compiler.WinwsArgumentBuilder` — pure, dependency-free static helper that produces structured `argv` tokens and a newline-joined `args.txt` body:
  - for each `StrategyAssignment` in order, every `StrategyDefinition.Parameters` token is appended verbatim;
  - for each `HostlistAssignment`, a single token `--hostlists=hostlists/<relativePath>` is appended;
  - tokens that contain a space, a double-quote or another shell metacharacter (`\t`, `\n`, `\r`, `|`, `&`, `<`, `>`, `;`, `(`, `)`, `*`, `?`, `[`, `]`, `^`, `!`, `$`, `` ` ``, `\\`) are wrapped in `"..."` with embedded `"` escaped as `\"`;
- `Zapret2Pilot.Engine.Zapret2.Compiler.ZapretPlanCompiler` — `public sealed class` with `public Result<CompiledZapretPlan> Compile(CompilationInputs inputs)`:
  - null inputs are returned as a typed `Result.Failure` with `ErrorCategory.Runtime` (no exception is thrown);
  - the content-addressed cache key is computed as `SHA-256(UTF-8(canonical inputs))` in lowercase hex, covering profile id, profile document hash, sorted strategy pack hashes, sorted hostlist fingerprints, runtime manifest hash, and the compiler options' canonical string (which includes the compiler version);
  - `RuntimePlanId` is derived deterministically from the cache key value;
  - `CommandLine` is the constant placeholder `"bin/winws2.exe"` (absolute-path resolution is a runtime-kernel concern, not a compiler concern);
  - `GeneratedConfigContent` is the minimal `"# Generated by Zapret2Pilot\n"` header; real config generation is future work;
  - `Hostlists` carries one `HostlistContent` per `HostlistAssignment`, with the relative path and an empty `Content` (hostlist file contents are loaded by the orchestrator, not the compiler); the materializer still has the relative paths it needs;
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Compiler/ZapretPlanCompilerTests.cs` — focused xUnit tests: valid inputs produce a populated plan, null inputs are rejected with a typed failure, the `Id` is derived from the `CacheKey`, tokens containing shell metacharacters are quoted, and an empty profile produces empty `Arguments` / `ArgsContent`;
- `tests/Zapret2Pilot.Engine.Zapret2.Tests/Compiler/RuntimePlanCacheKeyTests.cs` — focused xUnit tests: same inputs produce equal keys, changing any of the DEC-0011 inputs (profile document hash, strategy pack hash, hostlist fingerprint, runtime manifest hash, compiler options, compiler version) changes the key, the record's structural equality and `GetHashCode` behave correctly, and the constructor rejects null/blank values;
- `VERSION = 0.0.15`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated to reflect the new milestone.

Out of scope for 0.0.15:

- launching any process, touching `winws2` or `RuntimeProcessHost`;
- computing actual document hashes / hostlist fingerprints from the filesystem (inputs are pre-computed strings);
- materializing the generated config file content (real `generated.cfg` generation is future work);
- absolute-path resolution of `winws2.exe` (the runtime kernel owns this);
- loading hostlist file contents (the orchestrator owns this; the compiler only records the relative path);
- wiring the compiler into the runtime kernel or the storage layer.

Verification ladder:

- `dotnet build src/Zapret2Pilot.Engine.Zapret2/Zapret2Pilot.Engine.Zapret2.csproj -c Release` — passes;
- `dotnet test tests/Zapret2Pilot.Engine.Zapret2.Tests -c Release` — passes;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` — passes (the existing materializer tests still consume the legacy 3-parameter `CompiledZapretPlan` constructor);
- `dotnet build Zapret2Pilot.slnx -c Release` — passes on the whole solution;
- `dotnet test Zapret2Pilot.slnx -c Release` — passes on the whole solution;
- no real `winws2` process is launched by the compiler, the builder or any of their tests;
- the compiler does not access the filesystem, does not load hostlist contents and does not perform any process work.

Reviewer focus:

- `RuntimePlanCacheKey` lives in `Core.Runtime`, not in `Engine.Zapret2.Compiler`;
- the existing 3-parameter `CompiledZapretPlan` constructor is preserved and the materializer is not modified;
- `CompilationInputs` carries all six DEC-0011 inputs; the cache key is content-addressed, deterministic and SHA-256 based;
- `WinwsArgumentBuilder` is a pure static helper with no filesystem I/O and quotes tokens that contain shell metacharacters;
- new tests cover compiler happy path, null rejection, cache key stability / sensitivity and argument escaping.

## 0.0.16 — Runtime Transaction Model

Status: Implemented.

Follows 0.0.15. Introduces the in-memory Runtime Transaction Model that all future kernel hosts must use to mutate runtime state: every state change is proposed via a `RuntimeTransaction` returned by `RuntimeTransactionManager` and becomes permanent only after a successful `Commit`. Every transaction carries a rollback path that the manager re-executes on `Rollback` or on a failed commit transition, so the runtime is always restored to the state it had before the in-flight transaction was activated.

Scope:

- `Zapret2Pilot.Runtime.Transactions.RuntimeTransactionState` — public `enum` describing the documented state machine: `Pending` → `Active` → (`Committing` → `Committed` | `RollingBack` → `RolledBack`); any state may transition to `Failed` when a terminal error occurs (e.g. a rollback action threw);
- `Zapret2Pilot.Runtime.Transactions.RuntimeTransactionResult` — public `sealed record class` validation/factory type: a private constructor enforces the invariant that `Error` is non-null iff `State == Failed`; the public static factories `Committed()`, `RolledBack()` and `Failed(ErrorInfo)` are the only way to obtain a value; `Failed(null)` throws `ArgumentNullException`;
- `Zapret2Pilot.Runtime.Transactions.RuntimeTransaction` — public `sealed class` with an `internal` constructor (manager-only creation); exposes stable `Id`, observable `State` and the carried `Plan?`; `Commit` and `Rollback` are valid only from `Active` (anything else returns `RuntimeTransactionResult.Failed("RuntimeTransactionInvalidState")`); `Activate()` and `MarkFailed(ErrorInfo)` are `internal` seams used by the manager;
- `Zapret2Pilot.Runtime.Transactions.IRuntimeTransactionManager` — public contract with `BeginStart(plan)`, `BeginStop()`, `BeginApply(newPlan)`, `Commit(transaction)` and `Rollback(transaction)`. Each `Begin*` method creates the transaction, applies the proposed state change, records a rollback action, activates the transaction and returns it. `Commit` and `Rollback` reject unknown transactions with `RuntimeTransactionUnknown`;
- `Zapret2Pilot.Runtime.Transactions.RuntimeTransactionManager` — in-memory production implementation. Tracks `isRunning` and `currentPlan` plus a `Dictionary<RuntimeTransaction, Stack<Action>>` of recorded rollback actions. Each `Begin*` flow pushes a `RestoreStoppedState` / `RestoreRunningState(previousPlan)` action so the rollback can run the recorded actions in LIFO order. Two `internal` read-only properties (`IsRunning`, `CurrentPlan`) are exposed for unit tests via the existing `InternalsVisibleTo("Zapret2Pilot.Runtime.Tests")`;
- `tests/Zapret2Pilot.Runtime.Tests/Transactions/RuntimeTransactionManagerTests.cs` — focused xUnit tests: `BeginStart_NullPlan_ReturnsFailure`, `BeginStart_WhenRunning_ReturnsFailure`, `BeginStart_Success_TransactionIsActiveAndRuntimeRunning`, `StartThenCommit_Succeeds`, `StartThenRollback_SucceedsAndStopsRuntime`, `BeginStop_WhenNotRunning_ReturnsFailure`, `StopThenRollback_RestoresRunningStateWithPreviousPlan`, `BeginApply_NullPlan_ReturnsFailure`, `BeginApply_WhenNotRunning_ReturnsFailure`, `ApplyThenRollback_RestoresPreviousPlan`, `Commit_UnknownTransaction_ReturnsFailure`, `Rollback_UnknownTransaction_ReturnsFailure`, `Transaction_Commit_FromNonActiveState_ReturnsFailedResult`, `Transaction_Rollback_FromNonActiveState_ReturnsFailedResult`, `TransactionResult_Failed_NullError_Throws`, `TransactionResult_Committed_HasNoError`, `TransactionResult_RolledBack_HasNoError`, `TransactionResult_Failed_CarriesError`. No real process is launched; the manager is exercised entirely in memory;
- `VERSION = 0.0.16`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated to reflect the new milestone.

Acceptance:

- state transitions follow the documented `Pending → Active → (Committing → Committed | RollingBack → RolledBack)` flow; committing or rolling back from any other state returns `RuntimeTransactionInvalidState` and does not mutate the manager's `isRunning` / `currentPlan`;
- `BeginStart` while already running returns `RuntimeAlreadyRunning` and leaves the existing plan intact;
- `BeginStop` / `BeginApply` while not running return `RuntimeNotRunning`;
- `BeginStart` and `BeginApply` reject null plans with `RuntimeTransactionStartPlanNull` / `RuntimeTransactionApplyPlanNull` respectively;
- rolling back a `BeginStart` transaction restores the manager to the stopped state with no current plan; rolling back a `BeginStop` or `BeginApply` transaction restores the previous running state and the previous plan; rollback actions run in LIFO order and a single failure in any action terminates rollback with `RuntimeTransactionRollbackFailed`;
- `Commit` / `Rollback` of a transaction that was not produced by this manager returns `RuntimeTransactionUnknown`;
- `RuntimeTransactionResult.Failed(null)` throws `ArgumentNullException`; non-failed results never carry an `ErrorInfo`;
- the manager exposes its observable state only via the two `internal` properties — no public API change beyond the new `Transactions` namespace and types.

Verification ladder:

- `dotnet restore Zapret2Pilot.slnx`;
- `dotnet build Zapret2Pilot.slnx -c Release`;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` (all 18 transaction tests pass);
- `dotnet test Zapret2Pilot.slnx -c Release` (whole solution stays green).

Reviewer focus:

- the transaction constructor is `internal`; only the manager can create transactions (the test project reaches the constructor via `InternalsVisibleTo`, which is the only consumer outside the assembly);
- `Begin*` returns the transaction in `Active` state — there is no observable `Pending` window for callers;
- every `Begin*` flow pushes a rollback action BEFORE applying the proposed state change, so the manager can never be left in an inconsistent state on the rollback path;
- `Result<Unit>` is the success type for `Commit` / `Rollback`; the unit tests assert `IsSuccess` rather than the value payload;
- the `IsRunning` and `CurrentPlan` properties are `internal` only — they must not be promoted to the public surface in this milestone.

## 0.0.17 — Runtime Process Host

Status: Implemented.

Follows 0.0.16. Wires the existing safety primitives (global ownership mutex, lock file store, `RuntimeTransactionManager`, Windows Job Object, `RuntimeWorkspaceMaterializer`, `ZapretAssetVerifier`) together into a single `RuntimeProcessHost` that can launch, contain and shut down a Zapret2 runtime process end-to-end. **The implementation uses a fake runtime binary only (`Zapret2Pilot.Testing.FakeRuntime`); real `winws2` launch remains explicitly out of scope and remains gated by oracle approval.**

Scope:

- `Zapret2Pilot.Runtime.Hosting.RuntimeProcessStartContext` — public input DTO for `RuntimeProcessHost.StartAsync` carrying the `CompiledZapretPlan`, the resolved `ZapretAssetManifest`, the absolute workspace directory and the absolute path of the runtime executable to launch; constructor rejects null/blank arguments;
- `Zapret2Pilot.Runtime.Hosting.RuntimeProcessHostResult` — public success payload returned from a successful start/stop, exposing the live `ProcessId`, `ProcessName`, `ExecutablePath` and the `CompiledZapretPlan` that was launched;
- `Zapret2Pilot.Runtime.Hosting.RuntimeProcessHost` — public `sealed class` that runs the start pipeline on the calling thread to preserve ownership-mutex thread affinity: acquire the global ownership mutex, perform stale lock recovery, materialize the workspace, begin a `Start` transaction, create a Windows Job Object with kill-on-close, launch the runtime process, assign it to the job object, run a readiness check, write the recovery lock file, and finally commit the transaction. Any failure between `BeginStart` and `Commit` rolls the transaction back and tears down the partially-constructed resources. `StopAsync` reverses that pipeline (begin a `Stop` transaction, kill the process if any, wait for exit bounded by a constructor-supplied `stopTimeout`, dispose the process and the job object, delete the lock file, dispose the ownership lease, and commit);
- `Zapret2Pilot.Runtime.Health.RuntimeReadinessChecker` — public `static` post-start readiness probe that waits a short, caller-supplied readiness window (default 250 ms, in the documented 100–500 ms range) and then reports whether the process is still running; returns `Result<Unit>` with `RUNTIME_NOT_READY` (`ErrorCategory.Runtime`) when the process has already exited; performs no log parsing or standard-output sniffing (those are future work);
- `tests/Zapret2Pilot.Testing.FakeRuntime` — new console project that prints a single `READY` line on stdout, flushes it and blocks until SIGINT/Ctrl+C, then exits with code 0. It is a test-only stand-in for `winws2.exe` and is referenced by `Zapret2Pilot.Runtime.Tests` via `<ProjectReference PrivateAssets="all" />` so that the fake `.exe` and `.dll` are copied next to the test assembly;
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessStartContextTests.cs` — focused xUnit tests for the input DTO: valid construction, null plan, null manifest, null/whitespace workspace, null/whitespace runtime path;
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostResultTests.cs` — focused xUnit tests for the success payload: valid construction, non-positive PID, null/whitespace process name or executable path, null plan;
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs` — integration-style xUnit tests for the host: null-context rejection, null-manifest rejection, double-start rejection (`AlreadyRunning`), and a full `StartAsync` → `StopAsync` round-trip with the bundled `FakeRuntime` (the host launches the fake executable, assigns it to the job object, runs the readiness check, writes the recovery lock file and commits the transaction);
- `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeReadinessCheckerTests.cs` — focused xUnit tests for the readiness probe: success path, fast-exit detection (`RUNTIME_NOT_READY`), null process rejection, non-positive timeout rejection and `CancellationToken` propagation;
- `Zapret2Pilot.slnx` — added the new `tests/Zapret2Pilot.Testing.FakeRuntime/Zapret2Pilot.Testing.FakeRuntime.csproj` so the fake is built as part of the solution;
- `VERSION = 0.0.17`;
- `README.md`, `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` updated to reflect the new milestone.

Out of scope for 0.0.17:

- launching the real `winws2.exe` binary; the production wiring uses `Zapret2Pilot.Testing.FakeRuntime` as a placeholder until oracle approves the real launch (per `docs/Z2P-CANON.md`, `docs/Z2P-CRITICAL-REVIEW.md` and `DEC-0028`);
- cross-process command-line retrieval (`NtQueryInformationProcess` / WMI) — still requires oracle approval, the `TODO(0.0.17)` marker on `WindowsProcessSystemAccessor.CommandLine` is intentionally not retired in this milestone;
- log scraping, heartbeat / native liveness probes or any richer readiness signal than "process did not exit during the readiness window";
- surfacing the runtime host through the Avalonia UI or wiring it into `RuntimeTransactionManager` call sites from the application layer.

Acceptance:

- the host launches a process only when the start context, the ownership mutex, the transaction manager, the workspace materializer, the asset verifier and the job object are all available and consistent;
- the host rolls back the transaction and tears down partially-constructed resources (process, job object, lock file) on any failure between `BeginStart` and `Commit`;
- the host never launches `winws2`; it launches exactly the executable path supplied in `RuntimeProcessStartContext.RuntimeExecutablePath`, which in the milestone's tests is the `Zapret2Pilot.Testing.FakeRuntime.exe` copied next to the test assembly;
- `RuntimeReadinessChecker` is a pure probe that performs no I/O on the process; it only inspects `Process.HasExited` after a bounded wait;
- no new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change.

Verification ladder:

- `dotnet build Zapret2Pilot.slnx -c Release` — passes on the whole solution (including the new `Zapret2Pilot.Testing.FakeRuntime` project);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` — passes (all host, DTO and readiness tests run against the bundled `FakeRuntime`);
- `dotnet test Zapret2Pilot.slnx -c Release` — passes on the whole solution;
- the host tests are the only ones that launch a real OS process; the fake process is part of the test binary and is killed deterministically by `StopAsync` before each test completes.

Reviewer focus:

- confirm that the production wiring uses the fake runtime and that no production code path can resolve to the real `winws2.exe` without an explicit, oracle-approved change;
- confirm that the `TODO(0.0.17)` marker on `WindowsProcessSystemAccessor.CommandLine` is still present (cross-process command-line retrieval remains explicitly deferred);
- confirm that `RuntimeProcessHost` honors the existing safety primitives end-to-end: ownership mutex is acquired before launch, the transaction is committed only after the lock file is written, and rollback tears down the process, the job object, the lock file and the ownership lease in the right order.

## 0.0.18 — Runtime Process Host Hardening

Status: Implemented.

Scope:

- `VerifiedRuntimeExecutablePath` value object binding the launch path to `ZapretAssetVerificationSummary`;
- `RuntimeProcessHost` cleanup hardening: logged best-effort catch blocks, disabled stdout/stderr redirection, correct stop transaction semantics after an irreversible kill;
- analyzer baseline (`Meziantou.Analyzer`, `Roslynator.Analyzers`) and Avalonia 12.0.5 bump;
- documentation refresh for `DEC-0030`, `DEC-0031`, `DEC-0032` and Critical Review findings #26–#29.

Acceptance:

- `dotnet build Zapret2Pilot.slnx -c Release` passes;
- `dotnet test Zapret2Pilot.slnx -c Release` passes;
- no real `winws2` launch;
- no new forbidden dependencies.

## 0.0.19 — Runtime State Store

Status: Implemented.

Scope:

- `RuntimeKernelStateStore` persistent runtime/session state store in `Zapret2Pilot.Runtime.State`;
- `IRuntimeKernelStateStore` contract with `StartSession`, `EndSession`, `GetCurrentSession` and `GetRecentSessions`;
- SQLite migration `0002_runtime_state_store` creating `runtime_sessions` and `runtime_state` tables;
- DI extension `AddRuntimeKernelStateStore` in `Zapret2Pilot.Runtime.DependencyInjection`;
- App host registration in `Zapret2Pilot.App/Program.cs`;
- unit tests (`RuntimeKernelStateStoreTests`) and DI integration test (`RuntimeServiceCollectionExtensionsTests`).

Acceptance:

- `dotnet build Zapret2Pilot.slnx -c Release` passes;
- `dotnet test Zapret2Pilot.slnx -c Release` passes;
- no real `winws2` launch;
- no new NuGet packages beyond the already-centrally-managed `Microsoft.Extensions.Hosting` reference.

## 0.0.20 — Health, Readiness & RuntimeKernelWorker Integration

Status: Implemented.

Follows 0.0.19. Closes the host-integration half of `DEC-0033` (P0-7) by wiring the dedicated `RuntimeKernelWorker` and the `RuntimeProcessHost` into the Generic Host used by `z2p.exe`, and by converting the entry point to `async Task<int>` so the UI thread never blocks on a kernel future. Builds on the `RuntimeKernelWorker` added in 0.0.20 packet 1 and the worker-marshalled `RuntimeProcessHost` added in 0.0.20 packet 2.

Scope:

- `src/Zapret2Pilot.App/Program.cs` — `Program.Main` is now `public static async Task<int> Main(string[] args)` and still carries `[STAThread]`. The host's `StartAsync` and `StopAsync` are awaited; the old `GetAwaiter().GetResult()` calls are gone. The call order `AppHost.Build` → `AppHost.SetCurrent(host)` → `await host.StartAsync()` → Avalonia classic-desktop lifetime → `await host.StopAsync(...)` is preserved;
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` — new `AddRuntimeProcessHost()` extension that registers `RuntimeProcessHost` and every constructor dependency it needs as singletons: `RuntimeOwnershipMutex` (built with `RuntimeOwnershipNames.GlobalMutexName`), `RuntimeLockFileStore` (built with the runtime directory from `AppDataPathProvider.GetDefaultLayout()`), `RuntimeStaleLockRecovery`, `IRuntimeWorkspaceMaterializer` (built via `RuntimeWorkspaceMaterializer.CreateForRoot`), `IRuntimeTransactionManager`, `IRuntimeJobObjectProcessAssigner` and the host itself. The runtime directory is registered once as a singleton `AppDataLayout` so every factory resolves it from the service provider — the lambdas stay free of captured locals, and registration never touches the filesystem (no `AppDataLayout.EnsureCreated()` call inside the extension);
- `Zapret2Pilot.App/Program.cs` host composition — `AddRuntimeKernelStateStore`, `AddRuntimeKernelWorker` and `AddRuntimeProcessHost` are registered in that order in `AppHost.Build`. The worker is registered both as its concrete type and as an `IHostedService`, so the Generic Host owns its lifetime; the process host resolves the same singleton instance via `GetRequiredService<RuntimeKernelWorker>()`;
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeKernelWorkerUiNonBlockingTests.cs` — focused xUnit v3 tests that prove the UI non-blocking contract: a work item that awaits 100 ms (or `Timeout.Infinite`) on the worker thread does not cause the `Enqueue` call on the caller thread to block. The enqueue-call return latency is asserted to stay below 5 ms, which is the contract P0-7 introduced. Tests use `StopAsync` + `Dispose` in `finally` and never block the test thread waiting for the work item inside the enqueue call;
- `VERSION = 0.0.20`;
- `docs/Z2P-ROADMAP.md`, `docs/Z2P-DECISION-LOG.md` and `docs/Z2P-CRITICAL-REVIEW.md` updated for `0.0.20`.

Out of scope for 0.0.20 packet 3:

- launching a real `winws2` process;
- promoting `RuntimeProcessHost` to a hosted service (it is a singleton, not an `IHostedService` — the worker is the only hosted service the kernel registers);
- changing `MainWindowViewModel.AppVersion` (it is a hard-coded display value, not driven by `VERSION`);
- any non-kernel UI, application or storage refactor.

Acceptance:

- `dotnet restore Zapret2Pilot.slnx` passes;
- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole solution;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` passes (the two new UI non-blocking tests are included in the suite);
- `dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release` passes (no view-model changes were required for this packet);
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole solution;
- `Program.Main` is `async Task<int>`, the host is started and stopped with `await`, and the Avalonia lifetime runs after `host.StartAsync()` returns;
- the new `AddRuntimeProcessHost` extension resolves end-to-end from the service provider: the host constructor receives the registered `RuntimeOwnershipMutex`, `RuntimeStaleLockRecovery`, `IRuntimeWorkspaceMaterializer`, `IRuntimeTransactionManager`, `IRuntimeJobObjectProcessAssigner`, `RuntimeLockFileStore`, `ILogger<RuntimeProcessHost>` and the same `RuntimeKernelWorker` singleton that the Generic Host starts as an `IHostedService`;
- the UI non-blocking contract is enforced by a regression test: a 100 ms work item inside the worker does not delay the `Enqueue` call by more than 5 ms on the calling thread;
- no new NuGet packages, no `global.json` change, no `Directory.Packages.props` change, no lock file change.

Verification ladder:

- `dotnet restore Zapret2Pilot.slnx`;
- `dotnet build Zapret2Pilot.slnx -c Release`;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` (all runtime tests, including the two new UI non-blocking tests, pass);
- `dotnet test tests/Zapret2Pilot.App.ViewModelTests -c Release` (view-model tests stay green; the `AppVersion` assertion is unaffected by the `VERSION` bump);
- `dotnet test Zapret2Pilot.slnx -c Release` (whole solution stays green).

Reviewer focus:

- confirm `Program.Main` carries `[STAThread]`, is `async Task<int>`, awaits `host.StartAsync()` and `host.StopAsync(...)`, and the call order is `Build` → `SetCurrent` → `StartAsync` → Avalonia lifetime → `StopAsync`;
- confirm `AddRuntimeProcessHost` does NOT call `AppDataLayout.EnsureCreated()` and that the factory lambdas resolve `AppDataLayout` from the service provider rather than capturing a local string;
- confirm `AddRuntimeKernelWorker` is registered BEFORE `AddRuntimeProcessHost` in `AppHost.Build`, so the host's constructor resolves the same `RuntimeKernelWorker` instance that the Generic Host starts as an `IHostedService`;
- confirm the new UI non-blocking test measures the `Enqueue` call only, not the work item, and that the test does not block the test thread waiting for the work item inside the enqueue call;
- confirm `Z2P-CRITICAL-REVIEW.md` finding #29 (P0-7) is marked **Resolved** and that the live doc matches the implemented behaviour.

## 0.0.21 — Runtime Health Monitor Hosted Service

Status: planned.

Follows 0.0.20. Adds the `RuntimeHealthMonitor` Generic-Host
`IHostedService` that periodically probes the live runtime process,
publishes immutable `RuntimeHealthSnapshot` values through an
`IObservable<RuntimeHealthSnapshot>` and marks the active session as
`Failed` on an unexpected process exit. Closes the observable gap
between the kernel's `RuntimeProcessHost` and any future UI / event
projection that needs to react to runtime state transitions, and
removes the requirement for a future health milestone to add the
"transitions into Exited → Failed" wiring.

### Scope

- `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj` — add a
  `PackageReference` to `System.Reactive` (version 6.1.0 is already
  centrally managed in `Directory.Packages.props`);
- `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs` and
  `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs` — new
  `EndSession(RuntimeSessionId, RuntimeSessionState)` overload that
  closes a session as `Stopped` or `Failed`; refactor the private
  `CloseSession` helper to accept the final state;
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` — new
  `internal Process? RunningProcess { get; }` accessor (thread-safe
  under the existing `stateLock`) so the monitor can read the live
  process from the kernel worker thread without leaking the host's
  private state;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs` — new
  `public enum RuntimeHealthState { Unknown, Healthy, Exited }`;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs` — new
  immutable `sealed record class` with constructor validation;
- `src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs` — new
  public contract exposing `IObservable<RuntimeHealthSnapshot>
  SnapshotChanged` and `RuntimeHealthSnapshot LatestSnapshot`;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs` — new
  `IHostedService`, `IRuntimeHealthMonitor`, `IDisposable`
  implementation:
  - owns a `System.Threading.Timer` whose callback runs on a
    `ThreadPool` thread but only enqueues a probe;
  - the probe runs on the dedicated `RuntimeKernelWorker` thread;
  - reads `RuntimeProcessHost.RunningProcess` on the worker thread;
  - builds a snapshot (`Unknown` when no process, `Healthy` when
    alive, `Exited` when `HasExited`);
  - on a transition from `Healthy` / `Unknown` to `Exited` while
    the state store has an active session, calls
    `stateStore.EndSession(current.Id, RuntimeSessionState.Failed)`;
  - publishes the snapshot through a
    `BehaviorSubject<RuntimeHealthSnapshot>` exposed via
    `AsObservable()`;
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` —
  new `AddRuntimeHealthMonitor(this IServiceCollection)` extension
  that registers the monitor as a singleton, as
  `IRuntimeHealthMonitor`, and as `IHostedService`, all resolving
  to the same instance;
- `src/Zapret2Pilot.App/Program.cs` — call
  `services.AddRuntimeHealthMonitor();` immediately after
  `services.AddRuntimeProcessHost();` in `AppHost.Build`;
- `VERSION` bumped to `0.0.21`;
- `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` — `AppVersion`
  bumped from `v0.0.20` to `v0.0.21`;
- `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`
  — the existing assertion bumped from `v0.0.20` to `v0.0.21`;
- `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj`
  — add a `System.Reactive` `PackageReference` so the health-monitor
  tests can use `BehaviorSubject` / `IObservable` operators;
- new xUnit tests:
  - `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs`
    — four new tests for the `EndSession(id, state)` overload
    (`Failed` happy path, unknown id, null id, invalid `Active`
    state);
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs`
    — five new contract tests for the snapshot record;
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs`
    — seven new integration tests using `FakeRuntime` (initial
    `Unknown`, `Healthy` after start, `Exited` after kill, `Failed`
    session recorded, `StopAsync` stops publishing, observable
    receives initial + transitions, and the `BehaviorSubject`
    subscription test);
  - `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`
    — one new DI test confirming `IRuntimeHealthMonitor` and
    `IHostedService` resolve to the same singleton.

### Acceptance

- `dotnet restore Zapret2Pilot.slnx` passes;
- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole
  solution;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release`
  passes (all 25+ runtime tests, including the new state-store
  overload tests, the snapshot contract tests, the health-monitor
  integration tests and the new DI test);
- `dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release`
  passes (the bumped `v0.0.21` assertion holds);
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole
  solution;
- `RuntimeHealthMonitor` is started and stopped by the Generic Host
  (no manual lifecycle code in `AppHost.Build`);
- the monitor's timer callback only enqueues work; every read of
  the runtime process happens on the `RuntimeKernelWorker` thread;
- a `Healthy → Exited` transition marks the active session as
  `Failed` in `IRuntimeKernelStateStore`; a `Exited → Exited` repeat
  probe does not re-record the session;
- the new `EndSession(id, state)` overload rejects
  `RuntimeSessionState.Active` with `ArgumentException` and does
  not mutate the persisted state;
- no real `winws2` process is launched; the integration tests use
  `Zapret2Pilot.Testing.FakeRuntime` only;
- no new NuGet package versions; the only `PackageReference`
  additions are the already-centrally-managed `System.Reactive`
  6.1.0 on `Zapret2Pilot.Runtime` and on the test project;
- no Windows Service, IPC, VPN, proxy, MITM, per-URL router, or
  `.bat` / `.cmd` wrapper is added.

### Verification ladder

- `dotnet restore Zapret2Pilot.slnx`;
- `dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release`;
- `dotnet build src/Zapret2Pilot.App/Zapret2Pilot.App.csproj -c Release`;
- `dotnet build Zapret2Pilot.slnx -c Release`;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeKernelStateStoreTests"` (state-store overload tests);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthSnapshotTests"` (snapshot contract tests);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthMonitorTests"` (integration tests against `FakeRuntime`);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"` (DI integration test);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release` (whole runtime test suite);
- `dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release` (App view-model tests);
- `dotnet test Zapret2Pilot.slnx -c Release` (whole solution).

### Out of scope for 0.0.21

- launching a real `winws2` process; the monitor only observes the
  process owned by `RuntimeProcessHost` and currently the host is
  wired against `FakeRuntime`;
- surfacing the `IObservable<RuntimeHealthSnapshot>` through the
  Avalonia UI; this milestone only registers and tests the
  observable, no `MainWindowViewModel` is wired to it;
- changing the `RuntimeHealthSnapshot` payload; the snapshot carries
  `State`, `ProcessId` and `ObservedAtUtc` only — richer fields
  (uptime, last heartbeat, last command-line hash) are future
  work;
- promoting `IRuntimeHealthMonitor` to a multi-process health
  aggregator; the monitor owns a single `RuntimeProcessHost`
  reference and is intentionally not a multi-instance coordinator;
- changing the `RuntimeHealthState` enum; only `Unknown`, `Healthy`
  and `Exited` are defined, additional states (e.g. `Recovering`,
  `Degraded`) are future work.

### Reviewer focus

- confirm that `RuntimeHealthMonitor.StartAsync` only creates a
  `System.Threading.Timer` and `StopAsync` disposes it; the timer
  callback MUST NOT touch `RuntimeProcessHost`, the state store or
  the `BehaviorSubject` directly;
- confirm that every read of `RuntimeProcessHost.RunningProcess`
  happens inside a `worker.Enqueue` body so the read runs on the
  dedicated `RuntimeKernelWorker` thread, not on a `ThreadPool`
  thread;
- confirm that the `BehaviorSubject` is constructed with an
  initial `Unknown` snapshot and that `LatestSnapshot` returns that
  value before `StartAsync` is called;
- confirm that the `EndSession(id, state)` overload rejects
  `RuntimeSessionState.Active` and that the existing
  `EndSession(id)` overload is preserved unchanged;
- confirm that the new `internal Process? RunningProcess { get; }`
  accessor takes the host's `stateLock` and that the returned
  `Process` reference MUST NOT be disposed by the caller;
- confirm that `AddRuntimeHealthMonitor` registers the monitor as
  the same singleton instance under three keys
  (`RuntimeHealthMonitor`, `IRuntimeHealthMonitor`, `IHostedService`)
  and that `Program.cs` calls it after `AddRuntimeProcessHost`;
- confirm that the test fixture for `RuntimeHealthMonitorTests`
  reuses the existing `HostFixture` from `RuntimeProcessHostTests`
  via the new partial-class helper file, instead of duplicating
  the wiring logic;
- confirm that the integration test
  `Probe_TransitionToExited_MarksActiveSessionAsFailed` actually
  opens a session in the state store, observes the `Healthy`
  snapshot, kills the FakeRuntime from outside the host, waits for
  the `Exited` snapshot, and asserts the session is recorded as
  `Failed` — the test is the load-bearing proof of the
  health-monitor contract.

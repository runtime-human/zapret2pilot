# Zapret2Pilot / Z2P — Roadmap

Versioning starts from `0.0.1` and increments patch versions during early development.

## 0.0.1 — Documentation and project canon

Scope:

- create docs directory;
- add canon, architecture, roadmap, critical review register;
- add official sources list;
- add Codex handoff;
- add decision log.

No code yet.

## 0.0.2 — Repository skeleton

Scope:

- solution file;
- `src/` and `tests/` layout;
- Directory.Build.props;
- Directory.Packages.props;
- global.json;
- VERSION;
- .gitignore;
- initial README.

## 0.0.3 — Avalonia + ReactiveUI shell

Scope:

- Avalonia app;
- ReactiveUI setup;
- Generic Host inside app;
- basic DI;
- light theme;
- main window;
- sidebar;
- mock dashboard.

Non-goals:

- no real runtime;
- no winws2;
- no storage.

## 0.0.4 — Core primitives

Scope:

- Result<T>;
- ErrorInfo;
- typed IDs;
- base enums;
- unit tests.

## 0.0.5 — Application shell

Scope:

- CommandBus with inferred response type;
- OperationGate;
- EventRouter interfaces;
- DashboardSnapshot model;
- ViewModel tests.

## 0.0.6 — Storage foundation

Scope:

- SQLite connection factory;
- WAL/busy_timeout initializer;
- base migrations;
- settings repository;
- event journal skeleton.

## 0.0.7 — File and security foundation

Scope:

- ProgramData layout;
- AtomicFileWriter;
- SafePathResolver;
- DiagnosticsRedactor initial algorithm;
- tests.

## 0.0.8 — Runtime ownership foundation

Scope:

- Global Mutex ownership;
- runtime lock metadata;
- stale lock recovery;
- process ownership detector interfaces.

## 0.0.9 — Windows Job Objects foundation

Scope:

- WindowsJobObjectFactory;
- kill-on-close job options;
- RuntimeProcessHost fake process integration;
- Windows-only integration tests where possible.

## 0.0.10 — Profiles and compiler skeleton

Scope:

- ProfileDocument;
- ProfileDefinition;
- validation pipeline;
- StrategyPackDefinition;
- HostlistDefinition;
- CompiledZapretPlan;
- RuntimePlanCacheKey.

## 0.0.11 — Fake runtime supervisor

Scope:

- fake runtime tool in test-only project;
- RuntimeSupervisor start/stop/crash flows;
- CrashLoopGuard;
- RuntimeKernelStateStore.

## 0.0.12 — Real runtime asset verification

Scope:

- RuntimeAssetManifest;
- manifest hash verification;
- runtime workspace;
- generated file materialization.

## 0.0.13 — Real winws2 start/stop

Scope:

- real process launch;
- Job Object assignment;
- lock metadata;
- dashboard real runtime state.

## 0.0.14 — Runtime transaction and rollback

Scope:

- RuntimeApplyTransaction;
- snapshot;
- apply profile;
- rollback on failure;
- RuntimePlanDiff model.

## 0.0.15 — Diagnostics

Scope:

- structured logs;
- event journal;
- redacted diagnostics bundle;
- crash report writer.

## 0.0.16 — Probing P0

Scope:

- DNS probe;
- TCP probe;
- TLS/SNI probe;
- HTTP probe;
- Key Services Check;
- ProbeFailureClass.

## 0.0.17 — Auto Doctor Quick

Scope:

- preflight;
- baseline probes;
- candidate selector;
- ProfileScoringWeights;
- recommendation UI.

## 0.0.18 — Auto Doctor Full and candidate testing

Scope:

- temporary runtime sessions;
- candidate matrix;
- full mode timeout budget;
- apply with rollback.

## 0.0.19 — Tray and runtime UX

Scope:

- close to tray;
- tray states;
- runtime actions from tray;
- degraded/crashed state UI.

## 0.0.20 — Internal MVP

Scope:

- elevated launch;
- dashboard;
- profiles;
- rules;
- runtime start/stop;
- diagnostics;
- Auto Doctor basic/full;
- tray.

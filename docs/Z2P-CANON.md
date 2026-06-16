# Zapret2Pilot / Z2P — Project Canon

## 1. Product identity

Product name: **Zapret2Pilot**.  
Short name: **Z2P**.  
Main executable: **z2p.exe**.  
Target platform: **Windows desktop**.  
Main stack: **C# / .NET 10 / Avalonia UI**.

Zapret2Pilot is a modern Windows desktop manager and control plane for zapret2/winws2.

It is not a VPN, proxy, MITM tool, packet engine, traffic router or per-URL routing layer.

## 2. Source of truth and implementation model

GitHub repository: `MrFr3di/zapret2pilot`.

Docs source of truth: `docs/` in GitHub.

Project Sources are not the primary source of truth.

Code is designed, written and reviewed through ChatGPT in small steps. Codex is not used as the implementation executor for this project.

Every architecture-changing implementation step must keep these files consistent:

- `docs/Z2P-CANON.md`;
- `docs/Z2P-ARCHITECTURE.md`;
- `docs/Z2P-CRITICAL-REVIEW.md`;
- `docs/Z2P-ROADMAP.md`;
- `docs/Z2P-DECISION-LOG.md`.

## 3. Core architecture decision

Zapret2Pilot is an **elevated single-process desktop application**.

Initial architecture explicitly does **not** use Windows Service.

Runtime model:

```text
User starts z2p.exe
  -> Windows shows UAC once
  -> z2p.exe runs elevated
  -> Z2P can start/stop/restart winws2 during the same app session
  -> no repeated UAC while z2p.exe remains open or minimized to tray
```

When the application is fully closed and started again, Windows will show UAC again.

## 4. Explicit non-goals

Do not build:

- Windows Service architecture in the initial version;
- IPC service layer;
- VPN;
- local proxy-router;
- MITM;
- packet engine;
- per-URL router;
- full DPI research checker;
- background DPI monitor;
- `.bat` / `.cmd` wrapper architecture;
- raw winws2 args editor as primary UX;
- cloud telemetry.

No arbitrary command execution from UI.

## 5. Runtime Kernel

Because there is no service, Zapret2Pilot must have a strong internal Runtime Kernel inside `z2p.exe`.

Runtime Kernel owns:

- RuntimeSupervisor;
- RuntimeProcessHost;
- RuntimeTransactionManager;
- RuntimeHealthMonitor;
- RuntimeKernelStateStore;
- RuntimeOwnershipMutex;
- RuntimeLockFileStore;
- RuntimeOwnershipDetector;
- RuntimeCrashRecovery;
- RuntimeExitCoordinator;
- CrashLoopGuard.

The UI must never call `Process.Start`, `Process.Kill`, WinDivert or zapret2 directly.

## 6. Windows process safety

RuntimeProcessHost must use **Windows Job Objects**.

Required behavior:

- create Job Object for winws2;
- set kill-on-close behavior;
- assign winws2 process to the Job Object;
- keep job handle alive while runtime session is active;
- if z2p.exe crashes, Windows must terminate winws2 through the Job Object.

Runtime lock metadata is recovery. Job Objects are prevention.

## 7. Runtime ownership

Runtime ownership uses two mechanisms:

```text
Global Mutex = atomic runtime ownership
Lock file    = metadata for recovery and diagnostics
```

Global mutex name:

```text
Global\Z2P_RUNTIME_OWNER_v1
```

Lock file path:

```text
C:\ProgramData\Zapret2Pilot\runtime\z2p-runtime.lock
```

The lock file is not the source of truth. PID from lock file is only a hint.

Process ownership must verify:

- PID exists;
- process name is expected;
- executable path matches verified runtime path;
- command line hash matches compiled plan;
- plan hash matches;
- process creation time is compatible with lock metadata.

## 8. Internal hosting model

Zapret2Pilot uses `Microsoft.Extensions.Hosting` inside the Avalonia app.

This does not mean Windows Service.

Generic Host is used for:

- dependency injection;
- configuration;
- logging;
- hosted background services;
- lifecycle;
- graceful startup/shutdown.

## 9. UI framework decision

Presentation Layer uses:

- Avalonia UI;
- ReactiveUI;
- System.Reactive.

Do not mix ReactiveUI ViewModels with CommunityToolkit.Mvvm ViewModels.

All UI updates must be marshalled to Avalonia UI thread through a central scheduler/dispatcher abstraction.

## 10. UI design canon

Final dashboard direction:

- light theme;
- left sidebar;
- no status widget in lower-left sidebar;
- lower-left sidebar contains only Documentation / About;
- top-right contains only RU / settings / window controls;
- no duplicate status chips in header;
- main runtime status appears only in the large dashboard card;
- main status title: `Обход активен`;
- large green check icon in status card;
- main actions: `Остановить`, `Проверить сейчас`, `Диагностика`;
- cards: Режим работы, Текущий профиль обхода, Проверка ключевых сервисов, Диагностика обхода, Последние события.

Tray icon must show runtime state separately.

## 11. Application routing

Allowed routing:

- NavigationRouter;
- CommandBus;
- EventRouter;
- ProfileSelector;
- AutoDoctorStateMachine.

Forbidden routing:

- TrafficRouter;
- UrlRouter;
- PacketRouter;
- ProxyRouter;
- VPN-like route engine.

## 12. Profiles and runtime plans

Raw winws2 arguments are not the source of truth.

Source of truth:

```text
ProfileDocument
  -> validate/map
ProfileDefinition
  -> compile
CompiledZapretPlan
  -> execute
winws2 arguments
```

Generated args are artifacts only.

## 13. RuntimePlanCache

RuntimePlanCache is allowed only with deterministic content-based keys.

Cache key must include hashes of:

- ProfileDocument;
- StrategyPackDocument;
- hostlist fingerprints;
- runtime asset manifest;
- compiler version;
- compiler options.

Do not cache by ProfileId only.

## 14. File safety

All generated runtime files must be written through AtomicFileWriter.

Pattern:

```text
write temp file in same directory
flush
atomic move temp -> target
verify final file readable
```

All user/import paths must pass through SafePathResolver.

## 15. SQLite

SQLite must be initialized with:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

UI must not access SQLite directly. Event and diagnostic tables require retention.

## 16. Auto Doctor

Auto Doctor is bounded.

It is not a full DPI checker.

It must not:

- run forever;
- monitor every URL;
- switch profiles on every request;
- do MITM;
- store browsing history;
- silently apply aggressive profiles without clear confidence.

ProbeClassifier must distinguish DPI/network artifacts from HTTP/application errors.

## 17. Diagnostics and privacy

Privacy-first defaults:

- no cloud telemetry;
- no readable URL history;
- no query params in logs;
- no cookies;
- no raw packet dumps by default;
- diagnostics export redacted by default.

## 18. First implementation boundary

First implementation version is `0.0.1`.

`0.0.1` may create repo/bootstrap skeleton, Avalonia shell, ReactiveUI baseline, Generic Host baseline, Core/Application skeleton and tests.

`0.0.1` must not:

- start `winws2`;
- implement RuntimeProcessHost;
- implement Windows Job Objects;
- implement SQLite storage;
- implement Auto Doctor;
- implement profile compiler;
- add Windows Service;
- add IPC service layer;
- add `.bat` / `.cmd` wrapper architecture.

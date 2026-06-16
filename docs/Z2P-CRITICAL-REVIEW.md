# Zapret2Pilot / Z2P — Critical Review Register

This document tracks critical review findings accepted into the architecture.

## P0 — must be fixed before Runtime Kernel implementation

### 1. Job Objects are mandatory

Problem: lock files and exit coordinators do not prevent `winws2.exe` from surviving a crash of `z2p.exe`.

Decision: RuntimeProcessHost must create a Windows Job Object, set kill-on-close behavior and assign winws2 to it.

Required components:

- WindowsJobObjectFactory;
- RuntimeJobHandle;
- RuntimeProcessHost integration;
- Windows-only integration test.

### 2. Lock file is not ownership

Problem: PID check + lock file has TOCTOU risk and stale metadata risk.

Decision:

```text
Global Mutex = ownership
Lock file    = metadata
```

Mutex name:

```text
Global\Z2P_RUNTIME_OWNER_v1
```

Process ownership detection must verify PID, executable path, command line hash, plan hash and creation time.

### 3. Generic Host inside Avalonia app

Problem: manual bootstrapper chain makes startup/shutdown order fragile.

Decision: use Microsoft.Extensions.Hosting inside `z2p.exe`.

This is not Windows Service.

### 4. UI dispatch strategy

Problem: runtime/background events cannot mutate ViewModel state directly.

Decision: UI uses ReactiveUI + System.Reactive and central UI scheduler abstraction.

No direct `Dispatcher.UIThread.Post` outside UI infrastructure.

### 5. SQLite WAL and busy timeout

Problem: background writes and UI reads can cause SQLITE_BUSY and UI freezes.

Decision:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

### 6. RuntimePlanCache must be deterministic

Problem: caching by ProfileId can apply stale plans when hostlists or strategy packs change.

Decision: cache key is hash of all inputs.

### 7. Atomic writes for generated files

Problem: generated hostlists/configs may be read partially or locked by AV/scanners.

Decision: all generated runtime files use AtomicFileWriter.

### 8. Safe path resolution

Problem: elevated GUI importing profiles creates path traversal risk.

Decision: all external/user paths go through SafePathResolver and must remain inside allowed roots.

### 9. Network change hooks

Problem: profile may become invalid after Wi-Fi/Ethernet/VPN changes.

Decision: add NetworkChangeMonitor hooks in P0; advanced network-aware binding is P1.

### 10. Crash loop backoff

Problem: immediate restart attempts create crash loops.

Decision: RuntimeSupervisor uses CrashLoopGuard with exponential backoff and resets only after stability window.

## P1 — before internal MVP

- DiagnosticsRedactor algorithm and tests.
- Storage retention service.
- Auto Doctor Quick/Full modes.
- ProbeFailureClass taxonomy.
- ProfileScoringWeights as named config + table-driven tests.
- Tray status icon states.
- HVCI/WinDivert compatibility diagnostics.
- Scheduled Task autostart provider if autostart is added.
- RuntimePlanDiff UI.

## P2 — after MVP

- Signed strategy packs.
- Runtime updater with rollback.
- QUIC probe.
- Advanced TCP/L4 probes.
- Community profiles.
- Advanced Lua validation.

## Explicitly rejected shortcuts

- UI directly starts/kills runtime.
- `.bat` / `.cmd` as architecture.
- raw winws2 args as profile source of truth.
- traffic/per-URL router inside Z2P.
- unbounded Auto Doctor.
- fake runtime inside production Runtime project.

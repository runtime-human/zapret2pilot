# Zapret2Pilot / Z2P — Decision Log

This is a lightweight decision log. Larger decisions can later become ADR files.

## 2026-06 — Product identity

Decision:

- Product name: Zapret2Pilot.
- Short name: Z2P.
- Main executable: z2p.exe.

Reason:

- Full product name is readable in UI/docs.
- Short executable name is convenient for files, CLI and internal tooling.

## 2026-06 — No Windows Service in initial architecture

Decision:

- Initial architecture uses elevated single-process desktop app.
- No Windows Service.

Reason:

- Simpler installation and development.
- Lower IPC/service complexity.

Risk:

- UAC appears on full app start.
- Strong in-process runtime containment is mandatory.

Mitigation:

- Job Objects;
- Global Mutex ownership;
- lock metadata recovery;
- tray behavior;
- crash handling.

## 2026-06 — Generic Host inside app

Decision:

- Use Microsoft.Extensions.Hosting inside Avalonia app.

Reason:

- structured lifetime;
- DI;
- configuration;
- logging;
- hosted services;
- graceful startup/shutdown.

Not a Windows Service.

## 2026-06 — ReactiveUI for Presentation Layer

Decision:

- Use ReactiveUI + System.Reactive for ViewModels.
- Do not mix CommunityToolkit.Mvvm ViewModels.

Reason:

- Z2P is event-heavy;
- runtime and UI state are naturally observable;
- scheduler-aware UI updates are critical.

## 2026-06 — RuntimeProcessHost uses Job Objects

Decision:

- winws2 process must be assigned to Windows Job Object with kill-on-close.

Reason:

- prevents orphan winws2 when z2p.exe crashes.

## 2026-06 — Runtime ownership model

Decision:

- Global Mutex for ownership.
- Lock file for metadata only.

Reason:

- avoids TOCTOU lock-file ownership bug;
- keeps recovery data available.

## 2026-06 — No network router inside Z2P

Decision:

- Z2P does not implement traffic router, URL router, packet router or VPN-like route engine.

Reason:

- zapret2/winws2 owns network packet/runtime behavior;
- Z2P manages profiles, runtime lifecycle and diagnostics.

## 2026-06 — Typed profiles, not raw args

Decision:

- ProfileDocument -> ProfileDefinition -> CompiledZapretPlan -> winws2 args.

Reason:

- validation;
- testability;
- rollback;
- UI editor;
- safer elevated app behavior.

## 2026-06 — SQLite WAL

Decision:

- SQLite uses WAL, busy_timeout, synchronous=NORMAL, foreign_keys=ON.

Reason:

- avoid SQLITE_BUSY and UI freezes under background writes.

## 2026-06 — Auto Doctor bounded

Decision:

- Auto Doctor has Quick and Full modes.
- It is not a full DPI checker.

Reason:

- avoid unbounded diagnostics;
- reduce false positives;
- avoid privacy risks.

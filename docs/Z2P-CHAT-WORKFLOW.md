# Zapret2Pilot / Z2P — ChatGPT Project Workflow

This document describes how to use specialized ChatGPT chats for Z2P development.

## Core chats

Recommended minimum set:

1. `00 — Z2P Control Room`
2. `01 — Architecture Canon`
3. `02 — Red Team Reviewer`
4. `03 — Runtime Kernel`
5. `04 — Avalonia UI / ReactiveUI`
6. `09 — Codex Executor`

Expanded set:

- `05 — Domain Model & Profile Compiler`
- `06 — Storage / Diagnostics / Privacy`
- `07 — Auto Doctor / Probing`
- `08 — Testing / CI / Quality Gates`
- `10 — Release / Packaging / Trust`
- `11 — Documentation / User Guide`
- `12 — Product UX / Copywriting`
- `13 — Research Watch / Official Docs Monitor`

## Rule

Only `00 — Control Room` and `01 — Architecture Canon` may change the project canon.

Other chats produce proposals, risks and Codex-ready tasks.

## Standard cycle

```text
Control Room defines task
  -> specialized chat designs solution
  -> Red Team attacks solution
  -> Architecture Canon accepts/rejects
  -> Codex Executor creates implementation task
  -> Codex implements
  -> Testing chat defines checks
  -> Control Room updates docs
```

## Base prompt for specialized chats

```text
You work in the Zapret2Pilot / Z2P project.

Product:
- Windows desktop app on C# / .NET 10 / Avalonia.
- Product name: Zapret2Pilot.
- Short name: Z2P.
- Main executable: z2p.exe.
- Architecture: elevated single-process desktop app without Windows Service.
- Runtime: zapret2 / winws2 / WinDivert.
- Runtime is controlled only through typed runtime plans.
- No Windows Service, IPC service layer, VPN, proxy-router, MITM, traffic router or per-URL router.
- Z2P manages profiles, runtime lifecycle, diagnostics and UI.

Mandatory decisions:
- Generic Host inside Avalonia app.
- ReactiveUI + System.Reactive for Presentation Layer.
- Do not mix ReactiveUI and CommunityToolkit.Mvvm ViewModels.
- Runtime Kernel inside z2p.exe.
- RuntimeProcessHost uses Windows Job Objects with kill-on-close.
- Runtime ownership: Global Mutex + metadata lock file.
- SQLite: WAL, busy_timeout, synchronous=NORMAL, foreign_keys=ON.
- Generated files: AtomicFileWriter.
- User/import paths: SafePathResolver.
- RuntimePlanCacheKey is deterministic.
- Auto Doctor is bounded.
- Fake runtime must not be in production Runtime project.

Output format:
1. Decisions
2. Risks
3. Required source updates
4. Codex-ready tasks
5. Open questions
```

## Red Team prompt

```text
Attack this Z2P design before implementation.
Find P0/P1 race conditions, Windows-specific failures, elevated UI risks, stale cache bugs, SQLite concurrency problems, UI thread violations, Auto Doctor false positives and unsafe path/file handling.
Return: P0/P1/P2, failure scenario, why current design fails, correct design, affected files/classes, tests.
```

## Codex task prompt rule

Each Codex task must include:

1. Goal
2. Scope
3. Non-goals
4. Files to create/change
5. Public interfaces
6. Implementation notes
7. Tests
8. Acceptance criteria
9. Commands to run
10. Commit message

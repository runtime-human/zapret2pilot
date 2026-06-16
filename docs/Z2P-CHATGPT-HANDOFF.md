# Zapret2Pilot / Z2P — ChatGPT Implementation Handoff

This document is the baseline contract for implementation work done through ChatGPT.

## 1. Project baseline

You are working on **Zapret2Pilot / Z2P**.

- Repository: `MrFr3di/zapret2pilot`.
- Docs source of truth: `docs/` in GitHub.
- Product name: Zapret2Pilot.
- Short name: Z2P.
- Main executable: z2p.exe.
- Stack: C# / .NET 10 / Avalonia UI.
- UI: Russian-first.
- Architecture: elevated single-process desktop app without Windows Service.
- Runtime: zapret2 / winws2 / WinDivert.

## 2. Working model

Code is designed, written and reviewed through ChatGPT.

Do not use Codex as the implementation executor.

The expected loop:

```text
Control Room
  ↓
Implementation chat
  ↓
GitHub changes
  ↓
Review / Red Team
  ↓
Docs update
  ↓
Next implementation step
```

## 3. Mandatory read-before-work docs

Before making any architectural or implementation decision, read the relevant GitHub docs:

- `docs/Z2P-CANON.md`
- `docs/Z2P-ARCHITECTURE.md`
- `docs/Z2P-CRITICAL-REVIEW.md`
- `docs/Z2P-ROADMAP.md`
- `docs/Z2P-DECISION-LOG.md`
- `docs/Z2P-SOURCES-OFFICIAL.md` when platform facts are involved

Project Sources are not the source of truth. GitHub `docs/` is the source of truth.

## 4. Non-negotiable architecture rules

Do not add:

- Windows Service;
- IPC service layer;
- VPN/proxy/MITM;
- traffic router;
- per-URL router;
- raw `.bat` / `.cmd` wrapper architecture;
- arbitrary command execution from UI;
- real winws2 launch before the roadmap step that explicitly asks for it.

Do use:

- Microsoft.Extensions.Hosting inside Avalonia app;
- ReactiveUI for Presentation Layer;
- Runtime Kernel inside `z2p.exe`;
- typed commands and use cases;
- typed profiles and compiled runtime plans;
- Global Mutex + lock metadata;
- Windows Job Objects for runtime process containment;
- SQLite WAL;
- AtomicFileWriter;
- SafePathResolver;
- deterministic RuntimePlanCacheKey.

## 5. Implementation task format

Every implementation step must be small and explicit.

Required format:

1. Goal.
2. Scope.
3. Non-goals.
4. Files to create/change.
5. Public interfaces.
6. Implementation notes.
7. Tests.
8. Acceptance criteria.
9. Commands to run.
10. GitHub commit message.
11. Docs to update.

## 6. Task sizing rules

- One task = one architectural step.
- Prefer 5–15 files per task.
- Do not combine UI, runtime and storage in the same task unless explicitly integration work.
- If public contracts change, update docs.
- If runtime process handling changes, add tests or a Windows-only test plan.
- If official platform behavior is involved, check official docs first.

## 7. First implementation task template

```text
Goal:
Create the initial Zapret2Pilot solution skeleton.

Scope:
- .NET 10 solution.
- Avalonia app project.
- ReactiveUI baseline.
- Generic Host inside the app.
- Core primitives project.
- Test projects.
- VERSION = 0.0.1.

Non-goals:
- Do not launch winws2.
- Do not add Windows Service.
- Do not add IPC.
- Do not implement Auto Doctor.
- Do not add updater.

Files/projects:
- Zapret2Pilot.slnx or Zapret2Pilot.sln
- Directory.Build.props
- Directory.Packages.props
- global.json
- VERSION
- src/Zapret2Pilot.App
- src/Zapret2Pilot.Application
- src/Zapret2Pilot.Core
- tests/Zapret2Pilot.Core.Tests
- tests/Zapret2Pilot.App.ViewModelTests

Implementation notes:
- App must use Avalonia.
- Presentation must use ReactiveUI.
- App must build a Generic Host during startup.
- Dashboard may use mock data.
- No runtime process launch.

Acceptance criteria:
- dotnet restore passes.
- dotnet build -c Release passes.
- dotnet test -c Release passes.
- App launches and shows shell/dashboard skeleton.
- No Windows Service is created.
```

## 8. Commit message style

Use clear messages:

```text
docs: add Z2P architecture canon
chore: initialize .NET solution
feat(app): add Avalonia shell skeleton
feat(core): add result and typed ID primitives
feat(runtime): add runtime ownership abstractions
```

Russian commit messages are acceptable if they are clear and specific.

## 9. Required checks before marking task done

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

If a command cannot be run, say so explicitly and explain why.

## 10. Red flags

Reject implementation if it:

- starts winws2 from ViewModel;
- adds `.bat`/`.cmd` as primary flow;
- stores raw args as profile source of truth;
- adds Windows Service in MVP;
- writes generated files without atomic write;
- reads/writes arbitrary user paths from imported profiles;
- updates ViewModel from background thread;
- uses SQLite without WAL;
- keeps fake runtime inside production project;
- ignores cancellation tokens in long operations.
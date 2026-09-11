# Zapret2Pilot / Z2P

<!-- Z2P:REPOSITORY_OVERVIEW -->

Zapret2Pilot is a Windows desktop control plane for zapret2/winws2.

## Repository state

`docs/Z2P-CURRENT-STATE.json` is the **only dynamic repository-state authority**. Resolve the live project version, architecture version, active track and current work item from that file; do not copy that tuple into README or canonical Markdown.

The implementation at the v7 rebase boundary is the existing pre-broker Runtime Kernel baseline. The architecture migration described below does not claim that the broker already exists, and user-facing real `winws2` remains gated by the controlled evidence work in #20.

## v7 target process model

```text
unelevated z2p.exe
  Control Plane: UI / Application / profiles / probes / SQLite / TUF client
        |
        | bounded authenticated typed local IPC
        v
session-scoped elevated z2p-broker.exe
  sole runtime mutation authority / RuntimeKernelLoop / secure launch / Job lifetime
        |
        v
winws2 + relevant Lua/strategy payload + WinDivert
```

The broker is **not** a persistent Windows Service. Runtime lifetime remains bounded by the application/tray session. Rust is not an accepted implementation decision; any C#/NativeAOT/Rust comparison is deferred to #22 after the contract and correctness corpus are frozen.

## Preserved implementation

v7 is a migration, not a rewrite. It preserves the reducer-driven `RuntimeKernelLoop`, generation/stale-completion rejection, cancellation/supersede semantics, `RuntimeAffinityOwner`, ownership mutex, Job Object work, transactions/recovery, verified assets, deterministic compiler, SQLite/storage, diagnostics/privacy and the v6 Current/Candidate/PreviousKnownGood + leases + TUF/update/release invariants.

The location of the authority changes: after the broker migration, the existing Runtime Kernel is recomposed under the broker rather than duplicated in `z2p.exe`.

## Build and repository-truth validation

Requires the .NET 10 SDK selected by `global.json`.

```powershell
pwsh ./build/Test-RepositoryTruthContract.ps1
pwsh ./build/Validate-RepositoryTruth.ps1
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release --no-restore
dotnet test Zapret2Pilot.slnx -c Release --no-build
```

These scripts provide repository/CI validation; the scripts themselves are not the enforcement mechanism. GitHub enforcement is now supplied by the active `main-required-ci` repository ruleset (id `22957088`), verified under #26 from GitHub API state. It targets `main`, requires a pull request and the strict GitHub Actions check `Build and test` (`integration_id=15368`), blocks deletion and non-fast-forward updates, and has no configured bypass actors. While that ruleset remains active, normal updates to `main` are gated by the required CI check.

## Documentation authority

Start with:

1. `docs/Z2P-CURRENT-STATE.json` — sole dynamic state authority and canonical-path registry.
2. `docs/Z2P-CANON.md` — current non-negotiable project contract.
3. `docs/Z2P-ARCHITECTURE.md` — current master architecture.
4. `docs/Z2P-ROADMAP.md` — current master roadmap/execution graph.
5. `docs/Z2P-DECISION-LOG.md` — active decisions and supersession index.
6. `docs/Z2P-IMPLEMENTATION-STATUS.md` — implementation baseline/evidence pointers.

The v6 master plan and proposed v7 RFC are preserved under `docs/history/` as design/history sources; they are not current implementation instructions.

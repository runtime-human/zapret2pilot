# Zapret2Pilot / Z2P

<!-- Z2P-CURRENT-STATE: architecture=v7; version=0.0.25; track=#13; work=#13 -->

Zapret2Pilot is a Windows desktop control plane for zapret2/winws2.

## Current repository state

- Project version: `0.0.25`.
- Architecture contract: **v7**.
- Active architecture track: **#13 — Z2P v7 architecture rebase**.
- Current orchestration item: **#13** until the orchestrator explicitly selects the next child issue after v7-A review/merge.
- #14 is the documentation/truth migration represented by PR #25 and closes on merge; it must not remain the post-merge current work item.
- Current code is still the pre-broker `0.0.25+` implementation. This PR changes documentation and validation only; it does not claim that the broker already exists.
- User-facing real `winws2` execution remains forbidden until the correctness/evidence gate in **#20** is satisfied.

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

v7 is a migration from the implemented `0.0.25+` repository, not a rewrite. It preserves the reducer-driven `RuntimeKernelLoop`, generation/stale-completion rejection, cancellation/supersede semantics, `RuntimeAffinityOwner`, ownership mutex, Job Object work, transactions/recovery, verified assets, deterministic compiler, SQLite/storage, diagnostics/privacy and the v6 Current/Candidate/PreviousKnownGood + leases + TUF/update/release invariants.

The location of the authority changes: after the broker migration, the existing Runtime Kernel is recomposed under the broker rather than duplicated in `z2p.exe`.

## Build

Requires the .NET 10 SDK selected by `global.json`.

```powershell
pwsh ./build/Validate-RepositoryTruth.ps1
dotnet restore Zapret2Pilot.slnx
dotnet build Zapret2Pilot.slnx -c Release --no-restore
dotnet test Zapret2Pilot.slnx -c Release --no-build
```

## Documentation authority

Start with:

1. `docs/Z2P-CURRENT-STATE.json` — machine-readable current state.
2. `docs/Z2P-CANON.md` — current non-negotiable project contract.
3. `docs/Z2P-ARCHITECTURE.md` — current master architecture.
4. `docs/Z2P-ROADMAP.md` — current master roadmap/execution graph.
5. `docs/Z2P-DECISION-LOG.md` — active decisions and supersession index.
6. `docs/Z2P-IMPLEMENTATION-STATUS.md` — implemented state and evidence pointers.

The v6 master plan and the proposed v7 RFC are preserved under `docs/history/` as design/history sources; they are not current implementation instructions.

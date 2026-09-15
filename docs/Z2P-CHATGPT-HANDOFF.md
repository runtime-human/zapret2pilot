# Z2P — Implementation-agent handoff

Dynamic source: `docs/Z2P-CURRENT-STATE.json`.

Before work:

1. verify exact `main` SHA;
2. read current state, canon, architecture, roadmap, decision log and implementation status;
3. read the issue named by `currentWorkItem`;
4. treat `docs/history/`, archived RFCs and `Z2P-NEXT.md` as non-authoritative context only.

Do not copy the current-state dynamic tuple into Markdown. The orchestrator changes `currentWorkItem` in the JSON contract; canonical documents change only when their stable architecture/roadmap/status content changes.

After #14, #15 and #16 may run in parallel; #17 must not start until both are complete.

v7 invariant: the existing `RuntimeKernelLoop` is the sole runtime authority and is migrated under a session-scoped elevated broker; do not create a second App authority. No persistent Windows Service. No real `winws2` before #20. No Rust decision before #22.

Every architecture-changing PR must update canonical docs when the architecture actually changes; ordinary orchestration selection updates only the current-state contract. Run the repository-truth contract probe, validator, restore/build/tests on exact head.

Repository-truth scripts provide CI validation; GitHub enforcement is supplied separately by the active `main-required-ci` repository ruleset (id `22957088`), verified under #26 from GitHub API state. Normal updates to `main` require a pull request and the strict GitHub Actions check `Build and test` (`integration_id=15368`), with no configured bypass actors and deletion/non-fast-forward updates blocked. This enforced merge gate may be relied on while that ruleset remains active.

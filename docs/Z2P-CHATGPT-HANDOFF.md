# Z2P — Implementation-agent handoff

Current source: `docs/Z2P-CURRENT-STATE.json`.

Before work:

1. verify exact `main` SHA;
2. read current state, canon, architecture, roadmap, decision log and implementation status;
3. read the issue named by `currentWorkItem`;
4. treat `docs/history/`, archived RFCs and `Z2P-NEXT.md` as non-authoritative context only.

v7 invariant: the existing `RuntimeKernelLoop` is the sole runtime authority and is migrated under a session-scoped elevated broker; do not create a second App authority. No persistent Windows Service. No real `winws2` before #20. No Rust decision before #22.

Every architecture-changing PR must update the current-state contract/canonical docs or leave an explicit reason why those values do not change. Repository-truth CI is blocking.

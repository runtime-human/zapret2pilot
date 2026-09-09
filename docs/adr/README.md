# Z2P ADR index

Current decisions are indexed in `docs/Z2P-DECISION-LOG.md`. Historical pre-v7 decision text is preserved in `docs/history/Z2P-DECISION-LOG-v6.md`.

v7 ADRs:

- `DEC-0052-v7-privilege-boundary.md` — supersedes whole-process elevation while retaining no-Service-for-MVP.
- `DEC-0053-v7-runtime-authority-relocation.md` — moves/recomposes the sole RuntimeKernelLoop authority under Broker.
- `DEC-0054-v7-truth-model.md` — separates desired/observed/liveness/readiness/capability/recommendation truth.
- `DEC-0055-v7-runtime-identity-secure-launch.md` — full behavior identity, immutable staging/revalidation, fail-closed containment.
- `DEC-0056-repository-truth-contract.md` — machine-readable current state + CI gate.

Existing `DEC-0028-runtime-ownership-foundation.md` remains historical/active supporting detail where not superseded.

# Z2P — Roadmap tail: 0.0.7

## 0.0.7 — Runtime ownership foundation

Status: **implemented**.

Merged in:

- PR #8 — `feat(runtime): add runtime ownership foundation`
- Merge commit: `1f59888e868688af0ec430f972002300a782c0f2`

Implemented scope:

- `Zapret2Pilot.Runtime` project;
- `Zapret2Pilot.Runtime.Tests` project;
- named mutex ownership primitive;
- lease-based ownership acquisition and release;
- runtime lock metadata model;
- runtime lock file store using existing safe path and atomic write infrastructure;
- stale metadata recovery after ownership is acquired;
- process identity and ownership detector contracts;
- tests for ownership, lock file lifecycle, stale recovery and detector contracts.

Acceptance status:

- Lock file is metadata only.
- Mutex is the atomic ownership primitive.
- Stale metadata scenarios are tested.
- CI and SonarCloud passed before merge.

Notes:

- Real runtime process hosting remains intentionally out of scope until the next Windows process-safety primitive is implemented.
- `Directory.Build.props` currently contains a narrow temporary NuGet audit suppression for advisory `GHSA-2m69-gcr7-jv3q`, caused by the transitive SQLite native package chain. Remove it once the dependency chain moves to a fixed renamed package.

## Next implementation step

`0.0.8 — Job Objects foundation`

The next patch should add the Windows process-safety primitive required before any real runtime process hosting is allowed.

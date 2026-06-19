# DEC-0028 — Runtime ownership foundation

Date: 2026-06

## Decision

Add `Zapret2Pilot.Runtime` as the owner of initial runtime ownership primitives.

The implemented ownership foundation uses:

- a named mutex as the atomic ownership primitive;
- runtime lock metadata as recovery and diagnostics data only;
- a lock file store backed by existing safe path and atomic write infrastructure;
- stale metadata recovery only after ownership has already been acquired;
- process identity and ownership detector contracts without a real process host.

## Rationale

The desktop app has no Windows Service in the initial architecture. That means the in-process Runtime Kernel must first establish clear ownership before any future runtime process hosting is allowed.

The lock file is intentionally not a source of truth. It exists to help recovery and diagnostics. The mutex is the only atomic ownership primitive in this milestone.

## Consequences

- Future runtime start flow must acquire ownership before writing runtime metadata.
- Future stale metadata cleanup must happen only after ownership acquisition.
- Future process hosting must add the next Windows process-safety primitive before any real runtime launch.
- The process detector contracts are present, but real process lookup remains out of scope for this milestone.

## Validation

- PR #8 was merged into `main`.
- GitHub CI passed before merge.
- SonarCloud quality gate passed before merge.

## Temporary follow-up

`Directory.Build.props` contains a narrow temporary NuGet audit suppression for advisory `GHSA-2m69-gcr7-jv3q`, caused by the transitive SQLite native package chain. Remove it once the dependency chain moves to a fixed renamed package.

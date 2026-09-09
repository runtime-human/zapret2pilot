# DEC-0056 — Repository truth contract

Status: **ACCEPTED**  
Date: 2026-09-09

## Decision

Use one small machine-readable file, `docs/Z2P-CURRENT-STATE.json`, for project version, architecture version, active track/current work and canonical document paths.

Use explicit unique markers for the current master architecture and current master roadmap. CI validates the marker owners, `VERSION`, README/current-doc synchronization, historical v6/RFC pointers and that `Z2P-NEXT.md` is non-canonical.

## RED evidence

Before canonical docs were changed, CI run #147 on the v7-A branch failed at `Validate repository truth`; restore/build/test were skipped. This proves the gate detects the pre-existing drift class rather than being a post-hoc green-only check.

## Scope

The validator is a small PowerShell script in `build/`; no custom documentation framework or runtime behavior is introduced.

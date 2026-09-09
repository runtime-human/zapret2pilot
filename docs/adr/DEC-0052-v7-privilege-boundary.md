# DEC-0052 — v7 privilege boundary

Status: **ACCEPTED ARCHITECTURE / NOT IMPLEMENTED BY #14**  
Date: 2026-09-09  
Supersedes: DEC-0003 and the elevated-single-process clause of DEC-0002.  

## Decision

Use an unelevated `z2p.exe` Control Plane and a minimal session-scoped elevated `z2p-broker.exe` Runtime Plane connected by a later bounded/authenticated/typed local IPC contract.

Retain the active DEC-0002 decision that MVP has no persistent Windows Service. Broker/runtime lifetime remains bounded by the application/tray session. The broker exposes no arbitrary process/file/shell execution and performs no arbitrary remote download.

## Why

Whole-process elevation unnecessarily places UI, parsing, SQLite, probes/update input and broad application logic inside the privileged TCB. The runtime mutation authority is the part that requires privilege.

## Consequences

- #16 defines the security/wire contract before IPC implementation.
- #17 implements the first broker only after that RED contract.
- current 0.0.25+ code remains unchanged by this ADR PR.
- Rust is not selected.

# DEC-0054 — v7 truth model

Status: **ACCEPTED**  
Date: 2026-09-09

## Decision

Keep separate typed contracts for:

- `DesiredState`;
- `ObservedRuntimeState`;
- `RuntimeLiveness`;
- `RuntimeReadiness`;
- `ServiceCapabilityHealth`;
- `Recommendation`.

Process liveness is never sufficient evidence for a user-visible claim that bypass/service capability works. Capability evidence must be scoped to runtime generation, network epoch and policy/endpoint version so stale evidence is rejected.

Auto Doctor produces evidence/recommendation. Autopilot is a separate bounded policy layer. Neither is the runtime authority.

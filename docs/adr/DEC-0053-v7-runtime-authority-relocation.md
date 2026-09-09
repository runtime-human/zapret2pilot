# DEC-0053 — Runtime authority relocation

Status: **ACCEPTED ARCHITECTURE / MIGRATION DECISION**  
Date: 2026-09-09  
Supersedes: DEC-0006 location only.  
Retains/rebases: DEC-0050 one-authority rule.  

## Decision

`RuntimeKernelLoop` remains the single production runtime lifecycle/mutation authority. During v7 broker migration the existing reducer/generation/cancellation/recovery implementation is moved/recomposed under `z2p-broker.exe`; it is not duplicated or rewritten in App.

The Control Plane owns desired state, runtime projections, application policy and evidence. `RuntimeSupervisor`/future `RuntimeClient` are façades and cannot become another state machine.

## Preserved evidence

Preserve generation/stale-completion rejection, Stop-supersedes-Start cancellation, irreversible-boundary recovery semantics, guaranteed lifecycle/coalesced observation transport, `RuntimeAffinityOwner`, ownership/Job/transactions/recovery and associated tests.

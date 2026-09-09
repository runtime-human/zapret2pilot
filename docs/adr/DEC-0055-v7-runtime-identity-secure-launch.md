# DEC-0055 — Runtime identity, immutable staging and secure launch

Status: **ACCEPTED ARCHITECTURE / IMPLEMENTATION DEFERRED TO #18/#19**  
Date: 2026-09-09

## Decision

Execution identity is the accepted behavior-relevant immutable payload, including the applicable `winws2`/runtime/WinDivert and Lua/strategy content, not a raw path or display version.

Preserve Candidate/Current/PreviousKnownGood, leases, TUF, updater-failure and whole-app portable-staging invariants. Before privileged execution, the broker must revalidate/promote an immutable protected staged identity. A previous hash plus `File.Exists` is not TOCTOU closure.

Production launch is fail-closed: mandatory Job containment is established before child instruction execution. `Process.Start -> AssignProcessToJobObject` is not the final approved production path.

## Deferred proof

#18 owns supported secure process creation/containment. #19 owns protected staging/reparse/replace-after-check/broker revalidation and previous-good retention.

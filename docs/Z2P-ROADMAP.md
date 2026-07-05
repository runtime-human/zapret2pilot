# Zapret2Pilot / Z2P — Roadmap

> **Master plan: Version 6, dated 2026-07-04.**
>
> The canonical body of this roadmap is the v6 master plan
> `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md`. This document is the
> navigable roadmap entry point: it states the v6 baseline, summarizes
> the v6 milestone plan, lists the v6 architectural invariants, and
> indexes the pre-`0.0.24` historical milestones.
>
> The detailed per-milestone implementation record (files changed,
> tests added, validation commands) for `0.0.1`–`0.0.23` is preserved
> verbatim in `docs/Z2P-IMPLEMENTATION-STATUS.md`. The detailed v6
> milestone body (goal / scope / tests / acceptance for every
> `0.0.24`–`0.1.0`) is the v6 file itself.
>
> If this document and the v6 file ever drift, the v6 file is
> authoritative.

## Status

```text
Current VERSION:        0.0.24
Master plan:            v6 (2026-07-04)
Next milestone:         0.0.25 — Bootstrap, Platform Boundaries & UI Composition
Real winws2 launch:     forbidden before 0.0.24–0.0.28 are complete
Public portable ZIP:    forbidden before 0.0.42
Stable runtime updater: forbidden before TUF conformance/security review
```

The roadmap is treated as a **migration plan**, not a greenfield rewrite
(see `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §2 "Фактический baseline
`0.0.23`" for the exact `main` baseline).

---

# 1. v6 master plan — milestone summary

Source: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §26.

| Version | Milestone | Required outcome |
|---|---|---|
| `0.0.25` | Bootstrap, Platform Boundaries & UI Composition | Trusted configuration, Windows TFMs, no service locator, testable visual shell |
| `0.0.26` | Privileged Boundary & Secure Process Launch | STARTUPINFOEX/Job containment before execution; exact argv/handles |
| `0.0.27` | Safe SQLite, Durable State & Recovery | Fixed controlled native SQLite, single writer, online backup, journals |
| `0.0.28` | Deployment Trust, Bundle Compatibility & Preflight | Installed/portable trust; whole-app bootstrap spike; verified bundles |
| `0.0.29` | Real winws2 Developer Smoke | First isolated controlled real-runtime launch |
| `0.0.30` | Application Runtime UX & Dashboard SSoT | Compile-time typed use cases and honest dashboard |
| `0.0.31` | Profiles, Rules & Deterministic Compiler | Real hostlists/config, versioned canonical hash |
| `0.0.32` | Apply Profile, PlanDiff & Rollback | Durable crash-recoverable switching |
| `0.0.33` | Observability & Network Hooks | Bounded output, LoggerMessage, Activity/Meter, local logs |
| `0.0.34` | Key Services Probe Engine | Versioned bounded efficacy checks |
| `0.0.35` | Auto Doctor Quick/Full | Isolated candidates and explainable scoring |
| `0.0.36` | Autopilot & Network Binding | Stable automatic policy without flapping |
| `0.0.37` | Full MVP UI, Onboarding & Accessibility | Complete product UI and usability preparation |
| `0.0.38` | Tray & Desktop Lifecycle | Correct close/exit/shutdown/sleep behavior |
| `0.0.39` | Crash Recovery, Support Bundle & Data Management | Recovery UX, typed redaction, cleanup |
| `0.0.40` | TUF Runtime Repository Trust & Catalog | Reviewed POUF, conformance-tested metadata client |
| `0.0.41` | Runtime Download, Activation & Rollback | Resilient download, safe extraction, leases, candidate activation |
| `0.0.42` | MSI + Secure Portable Packaging | WiX MSI and signed NativeAOT whole-app portable bootstrapper |
| `0.0.43` | Release Candidate Hardening | Security/soak/usability/provenance freeze |
| `0.1.0` | Full MVP | Stable installer + portable release |

## 1.1. Mandatory gates (v6 §26.1)

```text
No real winws2 before 0.0.24–0.0.28 are complete.

No public portable before whole-app staging is proven.

No Stable runtime updater before TUF conformance/security review.

No 0.1.0 with vulnerable/suppressed SQLite native dependency.
```

## 1.2. Release channels (v6 §22)

```text
Developer       0.0.29
Internal Alpha  0.0.30–0.0.36
Closed Beta     0.0.37–0.0.42
Release Cand.   0.0.43
Stable          0.1.0
```

---

# 2. v6 architectural invariants (summary)

Source: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §4. Full text and
implementation implications are in the v6 file; the items below are
the load-bearing rules referenced by canon, architecture, critical
review and the decision log.

```text
4.1  RuntimeKernelLoop is the single lifecycle authority.
4.2  UI → Application commands/queries → Runtime facade only.
4.3  Only a VerifiedRuntimeBundle may be launched.
4.4  No child instruction before Job Object containment.
4.5  UI cannot claim "Обход активен" only because process is alive.
4.6  After app crash: Job close kills tree; mutex releases;
     durable state remains reconcilable; next start enters recovery;
     no automatic unsafe takeover.
4.7  Privacy: no telemetry, no browsing history, no cookies,
     no query parameters, no packet dumps, no readable target history,
     diagnostics redacted.
4.8  Upstream release != approved Z2P Runtime Bundle.
4.9  Any update verification / network / download / extraction /
     compatibility / candidate-test failure must not change Current,
     must not delete PreviousKnownGood, must not break a running
     runtime, and must not move UI to a false Running/Updated.
4.10 Portable package root is untrusted; main elevated z2p.exe runs
     only from the protected staged app root.
4.11 SQLite managed provider != trusted native SQLite engine.
4.12 Runtime-critical use cases are exposed only through typed facades.
4.13 Bundle/app/workspace/download artifacts cannot be deleted while
     they are leased by a running process, activation, rollback,
     recovery, diagnostics or current app session.
4.14 At most one Z2P-owned production winws2 instance per machine.
4.15 AutomationOwner = Z2P. Internally-adaptive Strategy Packs are
     excluded from Stable MVP.
4.16 Community artifacts never cross directly into the privileged
     execution boundary; they become typed, signed, policy-validated
     Strategy Packs via the release infrastructure.
4.17 Remote content scope for MVP is RuntimeBundle only; Strategy
     Packs, Probe Policy and built-in hostlists remain versioned
     bundled assets.
4.18 A service cannot be marked fully operational from evidence that
     tests only one capability.
```

## 2.1. v6 critical corrections over earlier revisions (v6 §0.3, §0.4)

```text
C1  SQLite native runtime is a P0 release blocker
    (no deprecated/vulnerable SQLitePCLRaw.lib.e_sqlite3).

C2  Portable staging must cover the whole elevated application,
    not only winws2; verifying a package after the elevated z2p.exe
    has already loaded it is too late.

C3  RuntimeSupervisor and RuntimeKernelWorker merge into one authority;
    no two semaphore/channel state machines, no false async thread
    affinity.

C4  Secure process creation is the only production launcher path;
    Process.Start → AssignProcessToJobObject is forbidden before
    the first real winws2.

C5  Compiler cache contract: separate CompilerCompatibilityVersion,
    CompilerOptionsVersion and CanonicalizationVersion; typed
    length-prefixed canonical hash writer replaces delimiter-based
    string concatenation.

C6  Generic Host defaults are restricted: environment variables,
    ordinary appsettings and arbitrary CLI cannot change executable
    or runtime roots, the trusted TUF root, the deployment flavor
    or developer gates.

C7  Windows-specific projects get an explicit Windows TFM
    (net10.0-windows10.0.26100.0); Core / Application / Engine
    stay on net10.0.

C8  Application CommandBus is no longer the runtime reflection /
    service-locator center; critical use cases are exposed through
    compile-time typed feature facades.

C9  CI is a security boundary: actions pinned to commit SHAs,
    locked restore, audit, architecture tests, evidence artifacts,
    release provenance.

C10 Error, cancellation and deadline contracts are systemic;
    cancellation after an irreversible boundary means
    rollback/recovery, not ordinary Cancelled.

C11 Runtime update uses explicit leases; Current / Candidate /
    Previous bundles cannot be deleted while they are leased.

C12 TUF client is treated as a separate security-critical subsystem
    (POUF, conformance vectors, root rotation / rollback / freeze
    tests, independent review).

C13 Runtime Capability Manifest is a small reviewed contract, not a
    full mirror of the Lua API.

C14 Z2P is the only production-runtime owner and the only automation
    owner; multiple upstream instances are not exposed as a product
    feature; internal adaptive orchestrators do not mix with Autopilot.

C15 Compiled plans carry a Traffic Impact Analyzer output (capture
    scope, compatibility risk, WinDivert filter breadth).

C16 Probe contexts replace the single universal probe result:
    BaselineWithoutBypass, ProductionHealth, CandidateEvaluation,
    ControlNetwork are interpreted separately.

C17 Service capability model is bounded: WebAccess, MediaDelivery,
    RealtimeUdp, NativeClient with Automated / PartiallyAutomated /
    ManualConfirmation / Unsupported probeability.

C18 Auto Doctor uses hard gates plus Pareto selection; the first
    "working" strategy is not auto-selected; equally effective
    scoped/safer/more stable/simpler strategies win.

C19 Runtime conflict detection is generic and non-destructive: Z2P
    detects and explains, but does not kill or delete third-party
    processes / services.

C20 Strategy Catalog is curated, not dozens of BAT presets; UI shows
    family, provenance, risk, capability coverage, evidence.

C21 Built-in hostlists are immutable; user overlays (additions and
    exclusions) survive updates.

C22 Community intake stays in the release infrastructure: client
    never downloads community Lua / BAT / EXE.

C23 Remote content scope is RuntimeBundle only for MVP; Strategy
    Packs, Probe Policy and built-in hostlists ship with the app.

C24 Upstream master and upstream release are separated
    (ObservedInMaster → PublishedUpstream → CandidateInZ2P →
    ApprovedStable); a change in master is not a Stable release.

C25 Roadmap synchronization is an obligatory repository gate: the
    current master plan must live in docs/ or implementation agents
    receive stale acceptance criteria.
```

---

# 3. Pre-`0.0.24` historical milestones (index)

The detailed per-milestone implementation record for every release
listed below is in `docs/Z2P-IMPLEMENTATION-STATUS.md`; the
corresponding decisions are in `docs/Z2P-DECISION-LOG.md`. This index
is the navigable summary; do not duplicate the detailed record here.

| Version | Title | Status | Detailed record |
|---|---|---|---|
| `0.0.1` | Repo bootstrap and initial app skeleton (Avalonia + ReactiveUI shell) | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.2` | Core primitives | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.3` | Application command foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.4` | UI navigation foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.5` | Storage foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.6` | File safety foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.7` | Runtime ownership foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.8` | Job Objects foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.9` | Job Assignment Foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.10` | Runtime Detector Implementation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.11` | Runtime Asset Manifest | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.12` | Runtime Workspace Materialization | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.13` | Profile Document Foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.14` | Profile Definition Foundation | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.15` | Zapret Plan Compiler | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.16` | Runtime Transaction Model | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.17` | Runtime Process Host | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.18` | Runtime Process Host Hardening | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.19` | Runtime State Store | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.20` | Health, Readiness & RuntimeKernelWorker Integration | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.21` | Runtime Health Monitor Hosted Service | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.22` | Runtime Crash Loop Guard | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.23` | Runtime Kernel Correctness Hardening | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |
| `0.0.24` | Runtime Kernel Lifecycle Closure | Implemented | `Z2P-IMPLEMENTATION-STATUS.md` |

## 3.1. Notable architecture corrections between 0.0.20 and 0.0.23

These items are recorded as DEC entries (see `docs/Z2P-DECISION-LOG.md`)
and are *superseded* by the v6 critical corrections listed in §2.1.
The v6 file and the decision log remain the source of truth for the
final architecture; this list is a pointer for traceability only.

```text
0.0.20 — RuntimeKernelWorker single-thread worker contract.
0.0.21 — Health monitor hosted service; off-worker publication.
0.0.22 — CrashLoopGuard (typed, in-memory, time-driven backoff,
        stability-window reset, permanent lockout).
0.0.23 — RuntimeSupervisor as the single owner of start/stop,
        gate by CrashLoopGuard, observe health monitor,
        publish RuntimeSupervisorState; RuntimeHealthMonitor
        uses PeriodicTimer + TimeProvider with probe coalescing;
        CrashLoopGuard reset semantics tightened (success-gated
        reset, saturation cap); RuntimeKernelStateStore.ClearRuntimeState
        made conditional on session_id match.
```

---

# 4. What is forbidden until the gates in §1.1 are met

Source: v6 §52 "Immediate execution order" and v6 §50 "Decisions to
record", condensed for the navigable roadmap.

```text
Forbidden before 0.0.29:
  user-facing real runtime Start;
  Auto Doctor candidate launch;
  profile apply to real runtime;
  tray real-runtime control;
  public installer/portable claiming runtime readiness;
  runtime updater activation.

Forbidden before 0.0.42:
  public portable ZIP;
  direct elevated launch from portable package root;
  claims that portable has installer-equivalent trust without
  whole-app staging.

Always forbidden (MVP scope):
  Windows Service, IPC, runtime after app exit;
  Scheduled Task autostart;
  auto-update of z2p.exe / installer / portable;
  auto-download of arbitrary upstream "latest";
  silent runtime activation without compatibility, test, rollback;
  remote catalog, cloud telemetry, community marketplace;
  arbitrary Lua, arbitrary CLI / runtime args editor;
  arbitrary executable path, arbitrary WinDivert filters;
  deep QUIC lab, runtime control CLI;
  takeover of another process, ARM64 product build, Native AOT
  product build, plugin system.
```

---

# 5. Where the v6 content lives

This document is the navigable entry point for the v6 master plan.
The full v6 content — sources of truth, baseline `0.0.23`, MVP user
journey, runtime kernel model, four-dimensional health, Application
API and error catalog, durable transactions, foreign runtime policy,
deployment / portable trust boundary, runtime bundle model, runtime
update architecture (TUF, key hierarchy, transport, activation,
rollback, retention, UI, errors, threat matrix), Windows native
interop policy, secure process creation, file identity security,
configuration and platform targets, persistence / SQLite native /
recovery, probe policy, observability, testing strategy, Evidence
Pack, release channels, dependency policy, CI/CD and supply-chain
security, packaging technologies, milestone bodies `0.0.24`–`0.1.0`,
incident response, performance program, documentation changes,
official documentation baseline and the final verdict — is the v6
file itself.

Files that must stay synchronized with v6:

- `docs/Z2P-CANON.md` — project canon.
- `docs/Z2P-ARCHITECTURE.md` — architecture document.
- `docs/Z2P-CRITICAL-REVIEW.md` — critical review register.
- `docs/Z2P-ROADMAP.md` — this file (navigable entry point).
- `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` — v6 master plan (authoritative body).
- `docs/Z2P-DECISION-LOG.md` — decision log.
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — per-milestone implementation record.
- `docs/Z2P-UI-DESIGN.md` — UI design canon.
- `README.md` — project README.

The v6 file also lists the new documents that the v6 work introduces
(`Z2P-RUNTIME-UPDATE-POUF.md`, `Z2P-SQLITE-NATIVE-POLICY.md`,
`Z2P-PORTABLE-BOOTSTRAP-CONTRACT.md`, `Z2P-STRATEGY-CATALOG.md`,
`Z2P-TRAFFIC-IMPACT.md`, `Z2P-SERVICE-CAPABILITY-MODEL.md`, etc.);
those are out of scope for the current docs-sync packet and are
created on demand by the corresponding milestones.

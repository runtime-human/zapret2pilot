# Zapret2Pilot / Z2P — Documentation Index

This directory is the working source of truth for Zapret2Pilot architecture and planning.

## Documents

- `Z2P-CANON.md` — short project canon and non-negotiable architectural rules.
- `Z2P-ARCHITECTURE.md` — current architecture overview.
- `Z2P-CRITICAL-REVIEW.md` — accepted critical review findings and fixes.
- `Z2P-ROADMAP.md` — versioned implementation plan.
- `Z2P-UI-DESIGN.md` — UI and UX canon.
- `Z2P-SOURCES-OFFICIAL.md` — official documentation sources for architecture decisions.
- `Z2P-CHATGPT-HANDOFF.md` — implementation handoff for coding through ChatGPT.
- `Z2P-DECISION-LOG.md` — lightweight decision log.

## Current direction

Zapret2Pilot is a Windows-first desktop control plane for zapret2/winws2:

- C# / .NET 10 / Avalonia UI.
- Elevated single-process desktop app.
- No Windows Service in the initial architecture.
- Runtime Kernel inside `z2p.exe`.
- Typed profiles and compiled runtime plans.
- Auto Doctor is bounded and diagnostic, not a full DPI research engine.
- No VPN/proxy/MITM/per-URL traffic router.
- Privacy-first diagnostics and logs.

All implementation tasks must keep these documents consistent.

## Working model

Code is implemented through ChatGPT in small steps.

Do not use Codex as the implementation executor for this project.
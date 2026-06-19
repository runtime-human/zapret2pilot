# Z2P — Next implementation step

## Next version

`0.0.8 — Job Objects foundation`

## Goal

Add the next required Windows process-safety primitive before any real runtime process hosting is introduced.

## Scope

- Add a small Windows Job Object primitive under `Zapret2Pilot.Runtime`.
- Configure close-time child process cleanup at the primitive level.
- Use safe handle ownership for native handle lifetime.
- Add explicit unsupported-platform handling.
- Add focused unit tests.

## Non-goals

- No real runtime executable launch.
- No full runtime process host orchestration.
- No UI changes.
- No Application-layer commands.
- No profile compiler.
- No network or adapter integration.

## Acceptance

- The primitive can be created and disposed safely on Windows.
- Native handle ownership is deterministic.
- Unsupported platforms are handled explicitly.
- Production runtime code still contains no real process-launch integration.

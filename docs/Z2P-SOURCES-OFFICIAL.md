# Zapret2Pilot / Z2P — Official Sources

This file lists official or primary sources that should be checked before changing architecture or implementation strategy.

## .NET / C#

### .NET Generic Host

Use for internal app lifecycle, dependency injection, configuration, logging and hosted services.

- https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- https://learn.microsoft.com/en-us/dotnet/core/extensions/workers

Architecture impact:

- Generic Host is used inside Avalonia app.
- This does not imply Windows Service.

### Native AOT

Use only for future CLI/tooling evaluation, not the main Avalonia UI in MVP.

- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/

Architecture impact:

- UI is not Native AOT target initially.
- Core libraries should stay AOT-friendly where practical.

### System.Text.Json source generation

- https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation

Architecture impact:

- Profiles, settings, runtime manifests and diagnostics should use source-generated JSON where practical.

## Avalonia / ReactiveUI

### Avalonia threading

- https://docs.avaloniaui.net/docs/app-development/threading

Architecture impact:

- Runtime/background events must not mutate UI state directly.
- UI updates go through scheduler/dispatcher abstraction.

### Avalonia performance

- https://docs.avaloniaui.net/docs/app-development/performance

Architecture impact:

- compiled bindings;
- virtualization;
- lazy loading;
- no blocking UI thread.

### ReactiveUI with Avalonia

- https://www.reactiveui.net/docs/handbook/view-models/boilerplate-code.html
- https://www.reactiveui.net/docs/handbook/commands/
- https://www.reactiveui.net/docs/handbook/scheduling/
- https://www.reactiveui.net/docs/handbook/collections/

Architecture impact:

- Presentation Layer uses ReactiveUI + System.Reactive.
- Do not mix ReactiveUI and CommunityToolkit.Mvvm ViewModels.

## Windows process/runtime

### Windows Job Objects

- https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects
- https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject
- https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-setinformationjobobject
- https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information

Architecture impact:

- RuntimeProcessHost must use Job Objects.
- `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` is mandatory for winws2 child process containment.

### Task Scheduler RunLevel

- https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-runlevel-principaltype-element

Architecture impact:

- Autostart without repeated UAC requires Scheduled Task.
- Not part of MVP unless explicitly added.

### HVCI / Memory Integrity

- https://learn.microsoft.com/en-us/windows-hardware/drivers/bringup/device-guard-and-credential-guard

Architecture impact:

- Diagnostics should identify HVCI/Memory Integrity conflicts with driver-based runtime.

## SQLite

### WAL

- https://www.sqlite.org/wal.html

Architecture impact:

- Use WAL to reduce read/write blocking.

### PRAGMA reference

- https://www.sqlite.org/pragma.html

Architecture impact:

Required initialization:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

## Windows app release/trust

### SmartScreen reputation

- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation

Architecture impact:

- Public release requires signing/trust strategy.
- Runtime assets require manifest verification.

## Project-specific external dependencies

### zapret / zapret2 / winws2

Add official repository/docs links here after runtime source is finalized.

### WinDivert

Add exact WinDivert version and docs link here when runtime bundle strategy is finalized.

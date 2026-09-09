# Zapret2Pilot / Z2P — Roadmap до полного MVP, редакция 6

**Статус:** рекомендуемый master-plan до `0.1.0`, скорректированный по результатам ревью текущего кода, ecosystem-репозиториев zapret/zapret2, portable trust boundary, SQLite supply-chain и runtime lifecycle  
**Дата актуализации:** 4 июля 2026  
**Текущий baseline репозитория:** `main`, `VERSION = 0.0.24`  
**Целевая версия:** `0.1.0 — Full MVP`  
**Публичные формы поставки:** **installer + portable**  
**Платформа MVP:** Windows 11 x64  
**Стек:** C# / .NET 10 / Avalonia 12 / ReactiveUI / System.Reactive / SQLite  
**Runtime:** zapret2 / winws2 / WinDivert  
**Архитектура:** elevated single-process desktop application без Windows Service и IPC  
**Основной executable:** `z2p.exe`

---

# 0. Итоговое архитектурное решение

Zapret2Pilot остаётся:

```text
elevated Avalonia desktop application
+ Generic Host
+ typed Application use cases
+ serialized Runtime Kernel
+ verified immutable runtime bundles
+ TUF-secured runtime repository and updater
+ profile compiler
+ bounded probing / Auto Doctor
+ SQLite state
+ diagnostics / recovery
+ installer and portable distributions
```

Zapret2Pilot не становится:

```text
Windows Service
IPC broker
VPN
proxy
MITM
packet engine
per-URL router
GUI над bat/cmd
cloud telemetry product
```

## 0.1. Публичные формы поставки

Публичный выпуск содержит две поддерживаемые формы:

```text
Installer:
  основной рекомендуемый вариант;
  per-machine MSI;
  приложение исполняется из Program Files;
  runtime/state размещаются по утверждённому ProgramData layout;
  ACL создаются installer-ом.

Portable:
  официальный ZIP-артефакт;
  без MSI, registry registration и shortcuts;
  запускается через отдельный подписанный bootstrapper;
  bootstrapper проверяет и staging-ует весь elevated application payload;
  основной z2p.exe никогда не исполняется elevated прямо из Downloads/Desktop/USB;
  runtime bundle отдельно staging-уется и проверяется;
  machine data можно удалить из UI.
```

Portable остаётся полноценной рабочей поставкой. Отличие от installer — способ доставки и обновления приложения, а не урезанный runtime.

## 0.2. Сохранённые решения редакции 4

1. Installer + portable.
2. TUF-secured repository для одобренных Runtime Bundle.
3. Immutable side-by-side bundles.
4. Candidate → probation → Current/PreviousKnownGood.
5. Pure reducer и один Runtime lifecycle authority.
6. Четырёхмерная health model.
7. Durable transaction journal.
8. Typed profiles, Strategy Packs и Rules.
9. Bounded probing и Auto Doctor.
10. Evidence Pack на каждый milestone.
11. Supply-chain provenance, signing и SBOM.
12. Отсутствие Windows Service, IPC и runtime-after-exit.

## 0.3. Критические коррекции редакции 5

1. **SQLite native runtime становится P0 release blocker.**
   Запрещено выпускать Z2P с deprecated/vulnerable `SQLitePCLRaw.lib.e_sqlite3`.
   Storage переводится на `Microsoft.Data.Sqlite.Core` и контролируемый native SQLite с минимальной безопасной версией, проверяемой в runtime.

2. **Portable staging распространяется на всё elevated приложение.**
   Проверять package внутри уже запущенного elevated `z2p.exe` слишком поздно: native loader и managed host уже могли загрузить подменённые файлы.

3. **RuntimeSupervisor и RuntimeKernelWorker объединяются в один authority.**
   Не остаётся двух semaphore/channel state machines и ложного обещания async thread affinity.

4. **Secure process creation становится единственным production launcher.**
   `Process.Start → AssignProcessToJobObject` запрещается до первого real `winws2`.

5. **Compiler cache contract исправляется.**
   Вводятся отдельные `CompilerCompatibilityVersion`, `CompilerOptionsVersion` и `CanonicalizationVersion`; строковая неоднозначная canonicalization заменяется typed length-prefixed writer.

6. **Generic Host defaults ограничиваются.**
   Environment variables, ordinary appsettings и arbitrary CLI не могут менять executable/runtime roots, trusted TUF root, deployment flavor и developer gates.

7. **Windows-specific проекты получают Windows TFM.**
   Core/Application/Engine сохраняют `net10.0`; App/Runtime/Platform.Windows используют явный Windows target.

8. **Application CommandBus перестаёт быть runtime reflection/service-locator центром.**
   Critical use cases предоставляются через compile-time typed feature facades.

9. **CI превращается в security boundary.**
   Actions pin-ятся на commit SHA, добавляются locked restore, audit, architecture tests, evidence artifacts и release provenance.

10. **Error, cancellation и deadline contracts становятся системными.**
    После irreversible boundary cancellation означает rollback/recovery, а не обычный `Cancelled`.

11. **Runtime update получает явные leases.**
    Current/Candidate/Previous bundle нельзя удалить во время runtime, rollback, diagnostics или activation.

12. **TUF client рассматривается как отдельная security-critical subsystem.**
    Нужны POUF, conformance vectors, root rotation/rollback/freeze tests и отдельный review.

## 0.4. Оптимизированные ecosystem-коррекции редакции 6

Редакция 6 принимает полезные идеи из `bol-van/zapret2`, `bol-van/zapret-win-bundle`, Flowseal-производных сборок, zapret2 GUI, DPI-checkers и blockcheck-проектов, но не переносит их process/service/update архитектуру напрямую.

Принятые улучшения:

1. **Runtime Capability Manifest в сокращённом виде.**
   Compatibility определяется не только версией `winws2`, но и проверенными capability IDs. Manifest не дублирует весь Lua API.

2. **Один production runtime и один automation owner.**
   Поддержка нескольких upstream instances не используется как продуктовая функция. `Z2P` остаётся единственным владельцем переключений; внутренние adaptive orchestrators не смешиваются с Autopilot.

3. **Traffic Impact Analyzer.**
   Каждый compiled plan получает объяснимую оценку области перехвата, риска совместимости и ширины WinDivert-фильтра.

4. **Probe contexts вместо одного универсального результата.**
   Baseline без обхода, production health, candidate evaluation и control network evidence интерпретируются раздельно.

5. **Ограниченная Service Capability Model.**
   MVP различает Web access, Media delivery, Realtime UDP и Native client, а также автоматическую, частичную и ручную проверяемость.

6. **Auto Doctor использует hard gates + Pareto selection.**
   Первая «рабочая» стратегия не выбирается автоматически; при равной эффективности побеждает более scoped, безопасная, стабильная и простая.

7. **Generic conflict detection.**
   Z2P детектирует технический конфликт и показывает evidence, но не убивает и не удаляет чужие процессы/службы автоматически.

8. **Curated Strategy Catalog вместо десятков BAT-пресетов.**
   UI показывает семейства, provenance, risk, capability coverage и network-local evidence.

9. **Built-in hostlists + user overlays.**
   Встроенные данные immutable; пользовательские добавления и исключения не перезаписываются обновлением.

10. **Community intake остаётся release-инфраструктурой.**
    Клиент не скачивает community Lua/BAT/EXE. Идеи переводятся в typed Strategy Pack после provenance, policy validation и VM tests.

11. **Remote content scope MVP ограничен Runtime Bundle.**
    Strategy Packs, Probe Policy и built-in hostlists версионируются независимо, но поставляются с приложением до отдельного post-MVP security milestone.

12. **Upstream master и release разделены.**
    Наличие изменений в `master` не означает доступность для Stable. Используются состояния `ObservedInMaster → PublishedUpstream → CandidateInZ2P → ApprovedStable`.

13. **Roadmap synchronization становится обязательным repository gate.**
    Актуальный master-plan должен находиться в `docs/`, иначе implementation agents получают устаревшие acceptance criteria.

Оптимизация scope:

```text
MVP:
  runtime capability manifest
  traffic impact
  probe contexts
  bounded service capabilities
  Pareto Auto Doctor
  conflict detection
  curated built-in strategies
  provenance/overlays

Post-MVP seams only:
  remote Strategy Pack updates
  remote Probe/Data Pack updates
  offline bundle import
  configurable mirrors
  BAT/CMD migration
  second runtime engine
  runtime-internal adaptive profiles
```

## 0.5. Release blockers, сохранённые редакцией 6

До первого real `winws2` должны быть закрыты:

```text
single Runtime authority
secure CreateProcessW containment
safe SQLite native runtime
trusted configuration sources
durable runtime state/recovery
bounded runtime output
global exception policy
exact compiler/cache versions
OS-specific platform boundary
```

До публичного portable:

```text
signed minimal bootstrapper
whole-app protected staging
portable package manifest
unexpected DLL rejection
app/runtime staging leases
portable cleanup and migration
```

До `0.1.0`:

```text
TUF conformance/security review
installer + portable full matrix
signed/provenanced artifacts
no suppressed vulnerable dependency
usability study
24-hour soak and fault injection
```

---

# 1. Источники истины и порядок доверия


## 1.1. Project source of truth

Внутри проекта приоритет:

1. `docs/Z2P-CANON.md`;
2. принятые DEC/ADR;
3. `docs/Z2P-ARCHITECTURE.md`;
4. `docs/Z2P-CRITICAL-REVIEW.md`;
5. этот roadmap;
6. `docs/Z2P-IMPLEMENTATION-STATUS.md`;
7. код и tests;
8. README;
9. старые чаты и концептуальные документы.

При расхождении код не считается автоматически правильным: расхождение оформляется как finding.

## 1.2. External source of truth

Для платформенных решений использовать только:

- Microsoft Learn / официальные .NET docs;
- официальные Avalonia docs и packages;
- официальные ReactiveUI docs/packages;
- SQLite official docs;
- WiX/FireGiant official docs;
- GitHub official docs;
- официальный upstream `bol-van/zapret2`;
- официальную WinDivert documentation;
- официальные NuGet package pages.

## 1.3. Ecosystem evidence policy

Community repositories are **non-normative evidence sources**.

They may provide:

- real-world failure reports;
- strategy candidates;
- diagnostics taxonomies;
- UX patterns;
- regression inputs;
- host/service knowledge requiring independent verification.

They may not directly provide to a Stable client:

- executables or drivers;
- arbitrary Lua;
- BAT/CMD/PowerShell execution;
- service definitions;
- registry/hosts/DNS mutations;
- unsigned runtime catalogs;
- unreviewed Strategy Packs.

Trust order for zapret behavior:

```text
official released source/tag and official docs
→ exact upstream commit inspection
→ Z2P conformance evidence
→ reviewed community evidence
→ anecdotal reports
```

AI-generated/community documentation is discovery material, not source of truth.

## 1.4. Temporal verification rule

Перед началом каждого milestone, зависящего от внешнего компонента:

```text
verify current stable version
verify support status
verify breaking changes
record exact source URL/date/version
update compatibility decision if needed
```

Нельзя считать версию «актуальной» только потому, что она записана в предыдущем roadmap.

---

# 2. Фактический baseline `0.0.23`

Этот roadmap является migration plan, а не greenfield rewrite.

## 2.1. Package baseline

На текущем `main` закреплены:

```text
Avalonia                     12.0.5
Avalonia.Desktop             12.0.5
ReactiveUI                   23.2.28
ReactiveUI.Avalonia          12.0.3
System.Reactive               6.1.0
Microsoft.Extensions.Hosting 10.0.9
Microsoft.Data.Sqlite         10.0.9
xUnit v3                      3.2.2
```

UI package baseline не требует немедленной замены. `ReactiveUI.Avalonia 12.0.3` остаётся осознанным compatibility pin для Avalonia 12/ReactiveUI 23.

## 2.2. Current App composition

Текущий код:

- использует `Host.CreateDefaultBuilder`;
- стартует Host до Avalonia window;
- содержит static `AppHost.Services`;
- использует service-locator access из `App`;
- не завершил production registration NavigationRouter/UI scheduler;
- сохраняет demo/parameterless ViewModel construction path;
- имеет hardcoded database path/shutdown timeout;
- не содержит app-instance preflight, deployment trust и global crash marker.

## 2.3. Current UI

Присутствуют:

- Avalonia shell;
- compiled bindings в `MainWindow`;
- light design direction;
- sidebar/dashboard cards;
- ReactiveUI commands;
- начальное отображение supervisor action.

Остаются mock/demo:

- hero/status values;
- uptime/mode/profile/service latency;
- recent events;
- часть commands/navigation;
- hardcoded version;
- non-activation subscription lifetime.

## 2.4. Current Application layer

Текущий `CommandBus`:

- получает handler через `IServiceProvider`;
- строит generic types/methods reflection-ом;
- вызывает `MethodInfo.Invoke` на каждый dispatch;
- скрывает handler graph до runtime;
- не является желаемым critical-path API.

Он остаётся временным foundation и мигрирует к typed feature facades.

## 2.5. Current RuntimeSupervisor

Текущий supervisor:

- использует отдельный `SemaphoreSlim`;
- освобождает lock до завершения host start;
- допускает stale start completion;
- публикует через raw `BehaviorSubject`;
- может выполнять subscriber callback на publisher thread;
- знает детали default CrashLoopGuard policy;
- имеет fire-and-forget automatic stop;
- имеет dispose ordering defect;
- смешивает hosted-service и runtime operation names.

## 2.6. Current RuntimeKernelWorker

Текущий worker:

- имеет bounded channel;
- объявляет `FullMode.Wait`, но пишет через `TryWrite`;
- принимает async delegates;
- не гарантирует continuation на dedicated thread;
- не защищает double start;
- не различает not-started/running/stopped;
- имеет dispose ordering defect;
- дублирует supervisor serialization.

## 2.7. Current process/security gaps

- process исполняется до Job assignment;
- args строятся через joining instead of correct Windows encoding;
- verified executable proof недостаточно opaque/final-handle-bound;
- path safety преимущественно lexical;
- no final file identity verification;
- stdout/stderr pump отсутствует;
- runtime sessions/recovery не полностью wired;
- UAC manifest/app single-instance/global exception policy не завершены;
- real `winws2` не должен запускаться.

## 2.8. Current storage blocker

Storage foundation правильно включает WAL/busy timeout/foreign keys, но dependency graph содержит suppressed deprecated/vulnerable native SQLite package.

Следствия:

- suppression нельзя считать permanent acceptance;
- native SQLite version/provenance не контролируются достаточно строго;
- WAL safety зависит от actual native engine;
- live backup strategy ещё не закреплена;
- single-writer coordination отсутствует.

Это P0 до real runtime.

## 2.9. Current compiler gaps

Compiler pure/deterministic по форме, но:

- documented compiler version отсутствует в actual cache input;
- canonical input — delimiter-based string;
- generated config placeholder;
- hostlist contents placeholder/empty;
- raw argument tokens впоследствии join-ятся небезопасно.

## 2.10. Current CI gaps

CI выполняет restore/build/test, но пока:

- actions referenced by mutable major tags;
- no locked restore;
- no format gate;
- no architecture tests;
- no audit/dependency-review gate;
- no evidence artifact upload;
- no release provenance/attestation;
- no isolated privileged test workflow.

---

# 3. Полное определение MVP


## 3.1. Обязательный user journey

Пользователь может:

1. скачать installer или portable;
2. проверить publisher/checksum;
3. запустить приложение;
4. один раз подтвердить UAC;
5. увидеть first-run onboarding;
6. пройти compatibility/preflight;
7. выбрать manual mode или Autopilot;
8. выбрать встроенный профиль;
9. запустить verified `winws2`;
10. увидеть реальное process/health состояние;
11. проверить YouTube, Discord и Telegram;
12. остановить/restart runtime;
13. сменить профиль с PlanDiff, downtime warning и rollback;
14. управлять typed Rules/hostlists;
15. запустить Auto Doctor Quick;
16. запустить Auto Doctor Full;
17. закрепить профиль;
18. свернуть приложение в tray;
19. пережить network reconnect и sleep/resume;
20. получить recovery UX после crash;
21. открыть logs/diagnostics;
22. экспортировать redacted support bundle;
23. очистить локальные данные;
24. проверить наличие одобренной версии zapret2;
25. скачать новый совместимый Runtime Bundle без остановки текущего runtime;
26. проверить, протестировать и активировать bundle с rollback;
27. вручную вернуться на PreviousKnownGood bundle;
28. обновить installer или заменить portable release;
29. завершить приложение без orphan runtime.

## 3.2. Не входит в `0.1.0`

- Windows Service;
- IPC;
- runtime after app exit;
- Scheduled Task autostart;
- автоматическое обновление самого `z2p.exe`/installer/portable;
- прямое автоматическое скачивание произвольного upstream `latest`;
- silent runtime activation без совместимости, теста и rollback;
- remote catalog;
- cloud telemetry;
- community marketplace;
- arbitrary Lua;
- arbitrary CLI/runtime args editor;
- arbitrary executable path;
- arbitrary WinDivert filters;
- deep QUIC lab;
- runtime control CLI;
- takeover чужого process;
- ARM64;
- Native AOT product build;
- plugin system.

---

# 4. Основные архитектурные инварианты

## 4.1. Runtime authority

```text
RuntimeKernelLoop — единственный lifecycle authority.
```

Никто кроме него не может:

- начинать/останавливать runtime;
- менять active operation generation;
- владеть process/job/mutex handles;
- изменять runtime session state;
- коммитить apply transaction;
- принимать crash/exit decision.

## 4.2. UI boundary

```text
UI → Application commands/queries → Runtime facade
```

UI не имеет references к:

- `Process`;
- P/Invoke;
- SafeHandle;
- SQLite provider;
- runtime workspace;
- raw lock metadata;
- WinDivert;
- concrete `RuntimeProcessHost`.

## 4.3. Execution integrity

```text
Only a VerifiedRuntimeBundle may be launched.
```

Verified bundle:

- связан с exact manifest;
- имеет canonical root;
- имеет file identity;
- имеет expected hashes;
- compatible with app/compiler/strategy;
- находится в approved secure execution root.

## 4.4. Containment

```text
No child instruction before Job Object containment.
```

## 4.5. State honesty

UI не может показать:

```text
Обход активен
```

только потому, что process жив.

## 4.6. Crash safety

После app crash:

- Job close kills runtime tree;
- named mutex releases;
- durable state remains reconcilable;
- next start enters recovery;
- no automatic unsafe takeover.

## 4.7. Privacy

По умолчанию:

- no telemetry;
- no browsing history;
- no cookies;
- no query parameters;
- no packet dumps;
- no readable target history;
- diagnostics redacted.

## 4.8. Runtime update trust

```text
Upstream release != approved Z2P Runtime Bundle.
```

Новый upstream zapret2 становится доступным пользователю только после:

- pin exact tag and commit;
- ingest exact upstream assets;
- record hashes and provenance;
- license review;
- compatibility validation;
- isolated Windows smoke;
- manual promotion;
- TUF metadata signing;
- publication as immutable target.

HTTPS, GitHub Release и artifact attestation являются transport/provenance evidence, но не заменяют client-side TUF verification.

## 4.9. Update non-regression

Любая ошибка проверки metadata, сети, загрузки, распаковки, compatibility или candidate test:

```text
не изменяет Current bundle
не удаляет PreviousKnownGood
не нарушает работающий runtime
не переводит UI в ложное Running/Updated
```

## 4.10. Portable app trust

```text
Portable package root is untrusted.
```

Основной elevated `z2p.exe` запускается только из защищённого staged app root после полной проверки package/app payload.

## 4.11. Storage engine trust

```text
SQLite managed provider != trusted native SQLite engine.
```

Production storage разрешён только после проверки native version, provider configuration, provenance and required compile/runtime behavior.

## 4.12. Compile-time Application boundary

Runtime-critical use cases доступны через typed facades. Reflection/service locator не является архитектурным transport layer.

## 4.13. Destructive operation leases

Bundle/app/workspace/download artifact нельзя удалить, пока он используется process, activation, rollback, recovery, diagnostics или current app session.

## 4.14. Single production runtime

```text
At most one Z2P-owned production winws2 instance per machine.
```

Upstream support for multiple instances is not a product permission. Auto Doctor candidates execute sequentially and never overlap production runtime.

## 4.15. Single automation owner

```text
AutomationOwner = Z2P
```

Autopilot, Auto Doctor and profile switching are coordinated by Z2P. Strategy Packs marked `InternallyAdaptive=true` are excluded from Stable MVP; future support must disable conflicting Z2P switching and expose the reduced explainability.

## 4.16. Curated ecosystem intake

Community artifacts never cross directly into the privileged execution boundary.

```text
repository evidence
→ provenance snapshot
→ typed translation
→ static policy validation
→ compile/conformance tests
→ isolated VM tests
→ manual promotion
→ built-in signed Strategy Pack
```

## 4.17. Remote content boundary

For `0.1.0`, remote update authority covers only approved Runtime Bundles.

```text
Remote in MVP:
  RuntimeBundle

Bundled/versioned in MVP:
  StrategyPack
  ProbePolicy
  BuiltInHostlistData
```

Their schemas contain independent versions so remote delivery can be added later without redesigning domain models.

## 4.18. Diagnostic honesty

A service cannot be marked fully operational from evidence that tests only one capability.

```text
YouTube homepage != media delivery
Discord web != voice UDP
Telegram web != native MTProto client
```

Every result carries capability, probeability, evidence source, confidence, freshness and limitations.

---

# 5. Runtime Kernel: итоговая модель

## 5.1. Почему текущие Supervisor + generic Worker нужно объединить

Сейчас существуют две serialization layers:

```text
RuntimeSupervisor.SemaphoreSlim
RuntimeKernelWorker.Channel
```

Они имеют разные lifecycle semantics, а async delegates размывают thread ownership.

Рекомендуемая целевая модель:

```text
RuntimeKernelLoop
  bounded Channel<RuntimeKernelCommand>
  single reader
  dedicated named thread
  pure reducer
  explicit effects
  generation/operation IDs
```

`RuntimeSupervisor` становится Application-facing facade, а не вторым state machine.

## 5.2. Commands

```text
Initialize
RequestStart
RequestStop
RequestRestart
RequestApplyProfile
RequestResetCrashGuard
RequestShutdown
StartCompleted
StopCompleted
ProcessExited
ReadinessObserved
EfficacyObserved
SafetyObserved
NetworkChanged
TimeoutElapsed
RecoveryCompleted
```

Каждая operation command содержит:

```text
OperationId
Generation
RequestedAt
CallerIntent
CancellationRegistration
```

## 5.3. Pure reducer

```csharp
RuntimeTransition Reduce(
    RuntimeKernelState state,
    RuntimeKernelCommand command);
```

Result:

```text
NextState
EffectIntents
Publications
DurableEvents
CommandOutcome
```

Reducer:

- не делает I/O;
- не вызывает process APIs;
- не пишет DB;
- не публикует observer callbacks;
- детерминирован;
- тестируется exhaustive/property tests.

## 5.4. Effect execution

### Kernel-thread effects

Только операции, требующие thread ownership:

- acquire/release named mutex;
- mutate handle ownership table;
- create/close Job handle;
- commit reducer state;
- immediate P/Invoke where bounded and non-blocking.

### External async effects

- bundle verification;
- workspace materialization;
- process readiness wait;
- probes;
- DB non-critical queries;
- diagnostics export.

Completion всегда возвращается как command с original `OperationId`/`Generation`.

## 5.5. Stale result rule

Completion применяется только если:

```text
completion.Generation == current.Generation
AND completion.OperationId == expected.OperationId
AND state accepts completion kind
```

Иначе:

```text
IgnoredStaleCompletion event
no state mutation
effect cleanup if necessary
```

## 5.6. Queue policy

- bounded capacity;
- `SingleReader=true`;
- multiple writers;
- explicit `WaitToWriteAsync`;
- no silent `TryWrite` loss;
- commands classified:
  - Critical;
  - User;
  - Observation;
- observations may coalesce;
- lifecycle commands never silently drop;
- shutdown closes acceptance before drain.

## 5.7. Public state publication

Не использовать raw `BehaviorSubject` на kernel thread.

Добавить:

```text
RuntimeStatePublisher
```

Properties:

- current immutable snapshot;
- publication queue outside kernel thread;
- replay latest;
- per-subscriber exception isolation;
- bounded/coalesced high-frequency observations;
- observable adapter for ReactiveUI;
- no callback on kernel thread.

---

# 6. Четырёхмерная health model

## 6.1. ProcessLiveness

```text
Unknown
NotRunning
Starting
Alive
Exited
Stopping
```

## 6.2. RuntimeReadiness

```text
Unknown
Waiting
Ready
Failed
TimedOut
```

## 6.3. BypassEfficacy

```text
Unknown
Checking
Effective
Partial
Ineffective
Offline
Stale
```

## 6.4. SystemSafety

```text
Unknown
Safe
Warning
Unsafe
Blocked
```

Примеры Safety:

- bundle hash mismatch;
- invalid ACL;
- foreign runtime conflict;
- unsupported driver;
- permanent crash lockout.

## 6.5. Composite hero policy

```text
DashboardHeroPolicy
  ProcessLiveness
  RuntimeReadiness
  BypassEfficacy
  SystemSafety
  Freshness
    → DashboardHeroState
```

Никакой ViewModel не формирует hero text самостоятельно.

## 6.6. Evidence metadata

Every non-static health observation carries:

```text
ObservedAtUtc
ValidUntilUtc
EvidenceSource
Confidence
CorrelationId
```

Confidence:

```text
Unknown
Low
Medium
High
```

Expired evidence becomes `Stale`/`Unknown`; UI cannot preserve green success indefinitely.

## 6.7. Service capability observations

MVP capability kinds:

```text
WebAccess
MediaDelivery
RealtimeUdp
NativeClient
```

Probeability:

```text
Automated
PartiallyAutomated
ManualConfirmation
Unsupported
```

Observation:

```text
ServiceCapabilityObservation
  ServiceId
  CapabilityKind
  Probeability
  Outcome
  ObservedAtUtc
  ValidUntilUtc
  Confidence
  EvidenceSource
  Limitations
```

Rules:

- aggregate service status never exceeds the strongest proven capability;
- manual confirmation is recorded separately from automated evidence;
- unsupported capability remains `Unknown`, not `Failed`;
- expired capability evidence cannot keep the hero green.

## 6.8. Incremental delivery

```text
0.0.24:
  Liveness + Readiness semantics.

0.0.28:
  Safety populated by deployment/compatibility preflight.

0.0.34:
  Efficacy populated by probes.

0.0.30/0.0.37:
  UI composite mapping and evidence freshness.
```

---

# 7. Application API, operation coordination и error semantics

## 7.1. Compile-time typed use cases

Текущий reflection-based `CommandBus` допускается только как временный bootstrap artifact.

Целевая Application API:

```text
IRuntimeUseCases
IProfileUseCases
IRulesUseCases
IAutoDoctorUseCases
IDiagnosticsUseCases
IRuntimeUpdateUseCases
IDataManagementUseCases
```

Пример:

```csharp
Task<Result<RuntimeSessionReadModel>> StartAsync(
    StartRuntimeRequest request,
    CancellationToken cancellationToken);

Task<Result<Unit>> StopAsync(
    StopRuntimeRequest request,
    CancellationToken cancellationToken);
```

Правила:

- handler dependency graph проверяется при Host build;
- no `IServiceProvider.GetService(Type)` на каждом command;
- no `MethodInfo.Invoke` в critical path;
- no MediatR dependency в MVP;
- records-команды можно сохранить как request models;
- cross-cutting policies выполняются явными decorators/pipeline components;
- Runtime Kernel получает только validated typed command.

Допустимые decorators:

```text
Validation
Correlation
Structured logging
Application operation policy
Deadline
Authorization/trust classification
```

## 7.2. ApplicationOperationCoordinator

Coordinator не является вторым Runtime Kernel и не владеет process state.

Он решает UX/use-case conflicts:

```text
ApplyProfile conflicts with AutoDoctor
RuntimeBundleActivation conflicts with Running/Apply/Doctor
DiagnosticsExport allowed while Running
ProfileImport allowed while Running
KeyServicesCheck allowed while Running
DataCleanup blocked during export/runtime/update
```

Результат:

```text
Allowed
Rejected
Queued
RequiresConfirmation
CancelsExisting
```

Coordinator выдаёт bounded operation lease:

```text
ApplicationOperationLease
  OperationId
  OperationKind
  AcquiredAtUtc
  Deadline
```

## 7.3. Central error catalog

Все публичные errors используют единый формат:

```text
Z2P.<AREA>.<SUBSYSTEM>.<CONDITION>
```

Примеры:

```text
Z2P.RUNTIME.START.ALREADY_RUNNING
Z2P.RUNTIME.PROCESS.CREATE_FAILED
Z2P.RUNTIME.BUNDLE.HASH_MISMATCH
Z2P.RUNTIME.STATE.STALE_COMPLETION
Z2P.STORAGE.SQLITE.UNSAFE_VERSION
Z2P.PROFILE.SCHEMA.UNSUPPORTED
Z2P.UPDATE.METADATA.EXPIRED
Z2P.PORTABLE.APP_PAYLOAD.UNTRUSTED
```

`ErrorInfo` расширяется либо дополняется typed descriptor:

```text
Code
Category
Severity
UserMessageResourceKey
TechnicalDetail
CorrelationId
RecoveryAction
Retryability
IsSecurityRelevant
```

Expected failure → `Result<T>`.

Unexpected exception:

- логируется;
- получает correlation ID;
- не показывается пользователю как raw `Exception.Message`;
- переводит subsystem в controlled Faulted/RecoveryRequired, если state integrity неизвестна.

## 7.4. Cancellation и deadlines

Каждая long-running operation имеет:

```text
OperationId
RequestedAtUtc
Deadline
CancellationReason
IrreversibleBoundary
```

Reasons:

```text
UserRequested
HostShutdown
Timeout
Superseded
SafetyAbort
```

До irreversible boundary:

```text
Cancelled
```

После irreversible boundary:

```text
RollbackRequired
RecoveryRequired
```

Нельзя вернуть обычный `Cancelled`, если process уже создан, старый runtime остановлен или durable commit частично выполнен.

Duration измеряется через monotonic `TimeProvider`:

```text
GetTimestamp
GetElapsedTime
```

Wall-clock используется только для persistence и UI timestamps.

## 7.5. Idempotency

Каждая mutating Application operation имеет idempotency/operation identity.

Повторный request с тем же `OperationId`:

- возвращает persisted/in-flight outcome;
- не запускает второй process;
- не создаёт вторую transaction;
- не повторяет activation/rollback без явной state transition.

---

# 8. Durable transaction model


## 8.1. Projection

`runtime_transactions` хранит текущий state.

## 8.2. Append-only journal

`runtime_transaction_events`:

```text
EventSequence
TransactionId
EventKind
PreviousState
NewState
OperationId
PayloadJson
OccurredAtUtc
```

Events:

```text
Created
TargetValidated
TargetCompiled
WorkspacePrepared
PreviousSnapshotPersisted
PreviousStopStarted
PreviousStopped
TargetStartStarted
TargetStarted
TargetReady
TargetHealthy
CommitStarted
Committed
RollbackStarted
PreviousRestarted
RolledBack
Failed
```

## 8.3. Atomicity

Projection update и journal append выполняются в одной SQLite transaction.

## 8.4. Recovery

Recovery uses last durable event plus process/ownership facts.

Каждый transition:

- idempotent;
- version checked;
- correlated;
- safe to retry or explicitly non-retryable.

---

# 9. Foreign runtime policy

MVP:

```text
detect
classify
explain
block own launch
```

MVP не делает:

```text
adopt
take over
automatically kill
```

## 9.1. Required detection

- process name;
- executable path if accessible;
- process creation time;
- file signature/hash if accessible;
- global runtime ownership mutex;
- lock metadata;
- expected own session records.

## 9.2. Command-line retrieval

Cross-process command line:

- optional diagnostics enrichment;
- not required to classify process as foreign;
- not required for own launch safety;
- not a blocker for MVP;
- no mandatory WMI/NtQueryInformationProcess implementation.

Это снимает лишнюю unsafe/native поверхность без ослабления политики «не принимать чужой runtime».

## 9.3. Runtime conflict detection

`RuntimeConflictDetector` reports technical facts, not destructive product guesses.

```text
OwnAppInstance
OwnRuntimeOwner
ForeignWinwsProcess
ForeignPacketFilterProcess
WinDivertDriverState
UnexpectedDriverImage
AmbiguousPacketFilterConflict
```

Optional enrichment may suggest a known product with confidence, but launch policy is based on evidence such as process identity, driver image path, service state and ownership facts.

Allowed behavior:

```text
detect
explain evidence
block unsafe Start
show manual recovery guidance
```

Forbidden behavior:

```text
automatic kill
service delete
registry cleanup
antivirus exclusion
third-party file deletion
```

---

# 10. Deployment model и portable trust boundary

## 10.1. DeploymentFlavor

```csharp
enum DeploymentFlavor
{
    Installed,
    Portable,
    Development
}
```

Flavor:

- встраивается на этапе build;
- входит в informational metadata;
- не выбирается environment variable, CLI или mutable marker;
- учитывается в data layout, update channel и diagnostics;
- не влияет на runtime ownership namespace: installed и portable не могут работать одновременно.

## 10.2. Installed flavor

```text
Application payload:
  %ProgramFiles%\Zapret2Pilot\

Machine state:
  %ProgramData%\Zapret2Pilot\Installed\

User UI state:
  %LocalAppData%\Zapret2Pilot\Installed\

Runtime bundles:
  %ProgramData%\Zapret2Pilot\RuntimeBundles\

Workspaces:
  %ProgramData%\Zapret2Pilot\Runtime\Work\
```

Installer:

- создаёт и проверяет ACL;
- регистрирует uninstall/repair/upgrade;
- подписывает MSI и Z2P-owned PE;
- не создаёт Windows Service;
- не запускает runtime во время установки.

## 10.3. Portable package

Публичный artifact:

```text
Zapret2Pilot-<version>-win-x64-portable.zip
```

В package root находятся:

```text
z2p-portable.exe
payload\
  z2p.exe
  managed/native dependencies
  app-payload.manifest.json
  runtime-package payload
portable-package.manifest.json
THIRD-PARTY-NOTICES.txt
README-PORTABLE.txt
```

Пользователь запускает только:

```text
z2p-portable.exe
```

Основной `z2p.exe` из ZIP root не является supported entry point.

## 10.4. Portable bootstrapper

`Zapret2Pilot.PortableBootstrapper` — отдельный минимальный security boundary.

Рекомендуемая реализация:

```text
C# / .NET 10 NativeAOT
win-x64
single native executable
self-contained
no Avalonia
no ReactiveUI
no SQLite
no third-party runtime packages без отдельного review
```

Bootstrapper responsibilities:

1. определить package root;
2. отклонить запуск из ZIP virtual folder;
3. проверить embedded package identity;
4. проверить manifest size/hash/signature;
5. проверить полный file inventory;
6. отклонить unexpected executable/DLL;
7. запросить elevation только после non-privileged precheck;
8. создать versioned protected staging root;
9. скопировать весь application payload;
10. назначить ACL;
11. повторно проверить staged files по final handles;
12. запустить staged `z2p.exe`;
13. передать только allowlisted bootstrap token/metadata;
14. не загружать DLL/plugin из source package root.

Bootstrapper не:

- запускает `winws2`;
- открывает SQLite;
- импортирует профили;
- выполняет Application use cases;
- загружает remote code;
- интерпретирует arbitrary CLI.

## 10.5. Why whole-app staging is mandatory

Elevated managed application не может надёжно проверить собственный portable directory после запуска: Windows loader и .NET host уже могли загрузить подменённые native/managed files.

Поэтому production portable flow:

```text
untrusted portable folder
→ minimal signed bootstrapper
→ complete app payload verification
→ protected app staging
→ re-verification
→ launch staged elevated z2p.exe
→ runtime bundle verification/staging
```

Только staging `winws2` недостаточен.

## 10.6. Portable protected layout

```text
%ProgramData%\Zapret2Pilot\Portable\
  Apps\
    <app-version>\<package-hash>\
      z2p.exe
      managed/native dependencies
      app-payload.manifest.json
  State\
  Downloads\
  Staging\

%LocalAppData%\Zapret2Pilot\Portable\
  ui-settings.json
  exports\
```

Runtime bundles остаются в общем защищённом store:

```text
%ProgramData%\Zapret2Pilot\RuntimeBundles\
```

## 10.7. Package and app identity

```text
PortablePackageId
AppPayloadId
AppVersion
BuildCommit
DeploymentFlavor
PackageManifestHash
AppPayloadManifestHash
SigningPublisher
CreatedAtUtc
```

`PortablePackageId` и `AppPayloadId` входят в:

- bootstrap handoff;
- app startup verification;
- diagnostics;
- staged app DB record;
- cleanup lease.

## 10.8. Full and restricted mode

`PortableFull` разрешён только когда:

- package extracted;
- bootstrapper signature valid;
- full inventory matches manifest;
- local filesystem supported;
- protected staging created;
- ACL verified;
- staged app reverified;
- runtime store available;
- elevation successful.

`PortableRestricted`:

- допускает bootstrap diagnostics и package inspection;
- не запускает основной elevated UI, если app payload trust не доказан;
- может предложить копирование package в local directory;
- не запускает runtime.

Внутри уже trusted staged app restricted mode может разрешить:

- profile validation;
- diagnostics viewing;
- data export;
- no runtime launch.

## 10.9. Portable application update

Новая portable версия:

```text
new ZIP
→ new bootstrapper/package verification
→ new versioned protected app staging
→ staged app migration/preflight
→ launch new staged app
```

Не перезаписывать running app files.

Старые staged app versions удаляются только когда:

- нет AppPayloadLease;
- версия не нужна для recovery;
- новая версия успешно прошла startup/preflight;
- retention policy разрешает deletion.

## 10.10. Portable cleanup

UI action:

```text
Удалить данные portable-версии
```

Требования:

- runtime остановлен;
- нет in-flight update/export;
- exclusive app-instance ownership;
- profiles/settings сначала можно экспортировать;
- удалить portable State/Downloads/Staging;
- удалить неиспользуемые staged app versions;
- удалить unleased portable runtime artifacts;
- не удалять installer data;
- сформировать deterministic cleanup report.

Source ZIP/package folder пользователь удаляет самостоятельно.

## 10.11. Installed/portable concurrency

Общие named objects:

```text
Global\Z2P_APP_INSTANCE_v1
Global\Z2P_RUNTIME_OWNER_v1
```

Lock metadata:

```text
DeploymentFlavor
AppPayloadId
AppVersion
DatabasePathFingerprint
SessionId
```

Правила:

- только один Z2P process per machine;
- никакого cross-flavor takeover;
- second instance показывает понятную ошибку и завершается;
- activation/cleanup не обходят global ownership.

---

# 11. Runtime bundle model

## 11.1. Immutable side-by-side layout

```text
%ProgramData%\Zapret2Pilot\RuntimeBundles\
  zapret2-1.0.2-z2p.1\
  zapret2-1.0.3-z2p.1\
```

Active bundle files never update in place.

Keep at least:

- Current;
- PreviousKnownGood;
- Candidate/Probation;
- bundled fallback while required.

## 11.2. Bundle manifest

```text
SchemaVersion
BundleId
BundleRevision
UpstreamRepository
UpstreamTag
UpstreamCommit
RuntimeVersion
LuaCompatVersion
Winws2Sha256
WinDivertDllSha256
WinDivertDriverSha256
LuaAssetHashes
BlobAssetHashes
SupportedArchitecture
MinimumWindowsBuild
Z2PMinVersion
Z2PMaxVersion
CompilerCompatibilityVersion
StrategyPackCompatibilityVersion
CanonicalizationVersion
CreatedAtUtc
```

Manifest определяет exact inventory: неизвестный executable/DLL запрещён.

## 11.3. Independent version axes

```text
AppVersion
AppPayloadSchemaVersion
RuntimeBundleVersion
ProfileSchemaVersion
StrategyPackSchemaVersion
CompilerCompatibilityVersion
CompilerOptionsVersion
CanonicalizationVersion
ProbePolicyVersion
ScoringPolicyVersion
DatabaseSchemaVersion
DiagnosticsSchemaVersion
TufClientProfileVersion
```

Нельзя выводить compatibility из одной общей версии приложения.

## 11.4. VerifiedRuntimeBundle

Production launcher принимает только opaque object:

```text
VerifiedRuntimeBundle
  BundleIdentity
  CanonicalRoot
  ManifestHash
  ExactFileInventory
  FileIdentities
  ExpectedHashes
  SignatureResults
  CompatibilityResult
  VerifiedAtUtc
  VerificationGeneration
```

Конструктор internal/private. Создание возможно только через verifier.

Обычный `string path` или публично создаваемый value object не является proof.

## 11.5. Verification freshness and TOCTOU

Перед process creation выполняется final verification:

- final path by handle;
- volume/file identity;
- expected root;
- ACL;
- hash/signature policy;
- no reparse escape;
- bundle compatibility;
- exact executable identity.

`VerificationGeneration` привязывается к launch operation.

Изменившийся file identity между verification и process creation блокирует запуск.

## 11.6. Integrity policy

```text
SHA-256:
  exact content binding.

Authenticode:
  publisher trust for signed PE where available.
```

Typed outcomes:

```text
HashMismatch
Unsigned
SignatureInvalid
UnexpectedPublisher
TimestampInvalid
TrustUnavailable
ManifestMismatch
AclInvalid
FileIdentityChanged
UnexpectedFile
```

## 11.7. Bundle leases

```text
RuntimeBundleLease
  LeaseId
  BundleId
  LeaseKind
  OwnerOperationId
  AcquiredAtUtc
  ExpiresAtUtc / explicit release
```

Lease kinds:

```text
RunningRuntime
CandidateValidation
Activation
Rollback
DiagnosticsExport
Recovery
```

Garbage collection не может удалить bundle с активным lease.

Lease persistence:

- runtime/activation/recovery leases durable;
- short diagnostics lease может быть in-memory, если cleanup безопасен после crash;
- startup recovery удаляет stale leases только после reconciliation.

## 11.8. Lifecycle

```text
BundledDefault
Staged
Verified
InstalledCandidate
Probation
Current
PreviousKnownGood
Quarantined
Retired
Deleting
Deleted
```

Все destructive transitions journaled.

## 11.9. Runtime Capability Manifest

A bundle exposes a **small reviewed capability contract**, not a generated mirror of the full Lua API.

```text
RuntimeCapabilities
  RuntimeVersion
  UpstreamCommit
  Nfqws2CompatVersion
  LuaCompatVersion
  WinDivertVersion
  SupportsGracefulShutdown
  SupportsProfileNames
  SupportsProfileCookies
  SupportsRawWinDivertFilter
  SupportsMultipleInstances
  SupportsCompressedLua
  SupportsLoopbackFilter
  SupportsRuntimeSandbox
```

Rules:

- capability values come from release-pipeline evidence, not filename inference;
- `SupportsMultipleInstances` remains informational in MVP;
- unsupported/unknown capability blocks any Strategy Pack that requires it;
- capability contract is separately versioned and included in bundle manifest hash.

## 11.10. Upstream promotion state

```text
ObservedInMaster
PublishedUpstream
CandidateInZ2P
ApprovedStable
Rejected
Revoked
```

Stable Z2P consumes only `ApprovedStable`. A changelog entry or commit in upstream `master` is never sufficient for client availability.

---

# 12. Runtime update architecture


## 12.1. Product decision

Runtime update входит в `0.1.0`.

Пользователь получает:

```text
bundled Current runtime
+ optional background metadata check
+ manual download by default
+ controlled candidate test
+ transactional activation
+ PreviousKnownGood rollback
```

Само приложение в MVP не обновляет себя. Новая версия `z2p.exe` поставляется новым MSI либо новым portable ZIP; portable bootstrapper размещает новую versioned app payload в защищённом staging root без in-place overwrite.

## 12.2. Неприемлемая модель

Запрещено:

```text
GitHub API → latest zapret2
→ скачать архив
→ распаковать рядом с z2p.exe
→ немедленно запустить
```

Причины:

- upstream release может быть несовместим с текущим compiler/profile schema;
- tag/release storage не является root of trust;
- отдельные файлы bundle могут относиться к разным версиям;
- новая Lua compatibility может ломать Strategy Packs;
- отсутствует rollback/freeze/mix-and-match защита;
- portable directory может быть user-writable;
- release может быть корректным upstream, но ещё не одобренным Z2P.

## 12.3. Выбранный trust protocol

Не проектировать собственную схему «catalog.json + одна подпись».

Выбран:

```text
The Update Framework specification 1.0.x
Z2P Runtime Repository POUF
```

На дату документа актуальная спецификация — TUF `1.0.34`.

TUF нужен для защиты от:

- arbitrary target installation;
- rollback;
- freeze;
- mix-and-match;
- fast-forward;
- endless-data;
- repository/mirror compromise;
- compromise одного online key.

До прохождения interoperability/conformance tests Z2P не заявляет полную TUF compliance публично.

## 12.4. Почему не использовать GitHub Release как доверие

GitHub Releases используются как distribution transport и provenance source.

Клиент не доверяет:

- release title;
- `latest` marker;
- tag name без commit pin;
- asset filename;
- redirect destination;
- GitHub API response сам по себе.

Клиент доверяет только:

- embedded trusted TUF root;
- correctly updated root chain;
- non-expired threshold-signed target metadata;
- target length/hash;
- internal Runtime Bundle manifest;
- final staged file identities/hashes/ACL.

GitHub artifact attestations используются в promotion pipeline как дополнительное доказательство происхождения, но не как единственный runtime client trust anchor.

## 12.5. TUF repository profile

Z2P POUF фиксирует:

```text
Specification: TUF 1.0.x
Metadata format: canonical JSON
Consistent snapshots: enabled
Top-level roles:
  root
  targets
  snapshot
  timestamp

Delegated roles:
  runtime-stable
  runtime-development (internal builds only)

Mirrors role:
  not used in MVP

Public stable client:
  trusts only stable repository root

Development client:
  embeds a separate development root
```

Stable app не может переключиться на development repository через setting, environment variable или CLI.

## 12.6. Key hierarchy

Рекомендуемая production policy:

```text
root:
  ECDSA P-256
  2-of-3 threshold
  offline
  expiry 730 days

targets:
  ECDSA P-256
  2-of-3 threshold
  offline/hardware-backed
  expiry 180 days

snapshot:
  ECDSA P-256
  1-of-1
  online protected release environment
  expiry 30 days

timestamp:
  ECDSA P-256
  1-of-1
  online protected release environment
  expiry 7 days
```

Перед Stable release сроки и operational ability проверяются повторно. Expiry должен быть достаточно коротким для freeze detection и достаточно реалистичным для сопровождения проекта.

Private keys:

- никогда не хранятся в repository;
- root/targets не хранятся как обычный GitHub Actions secret;
- offline copies encrypted and geographically separated;
- key identifiers, owners and rotation dates documented;
- online keys rotated on schedule and after incident.

## 12.7. Client bootstrap

Installer и portable содержат один stable trusted root:

```text
Resources/RuntimeRepository/root.json
```

Trusted root:

- embedded in signed application resources;
- has known version and hash;
- copied to protected metadata store on first use;
- never replaced by unsigned local file;
- updated only through sequential TUF root rotation.

Development build embeds different root and cannot publish itself as Stable.

## 12.8. Repository layout

Conceptual layout:

```text
metadata/
  1.root.json
  2.root.json
  <version>.snapshot.json
  <version>.targets.json
  <version>.runtime-stable.json
  timestamp.json

targets/
  <sha256>.z2p-runtime-zapret2-1.0.2-z2p.1.zip
```

Exact filenames and wire format are documented in `Z2P-RUNTIME-UPDATE-POUF.md`.

Metadata and targets may use different HTTPS base URIs. Both URIs are embedded/allowlisted by DeploymentFlavor and release channel.

## 12.9. Runtime target custom metadata

TUF target custom fields carry product compatibility:

```text
BundleId
BundleRevision
UpstreamRepository
UpstreamTag
UpstreamCommit
UpstreamAssetName
UpstreamAssetSha256
RuntimeVersion
LuaCompatVersion
SupportedArchitecture
MinimumWindowsBuild
MinZ2PVersion
MaxZ2PVersion
CompilerCompatibilityVersion
StrategyPackCompatibilityVersion
ProfileSchemaRange
CanonicalizationVersion
ReleaseChannel
ReleaseNotesPath
SecurityClassification
PublishedAtUtc
```

Target path, length and SHA-256 remain standard signed TUF target fields.

No client decision relies only on custom display text.

## 12.10. Upstream release is only an input

Текущий official upstream baseline на дату документа:

```text
zapret2 v1.0.2
commit 4df9449
```

Это candidate baseline, а не вечный default.

Upstream monitoring workflow:

```text
official releases API
→ detect new tag
→ resolve exact commit
→ record release notes/assets/hashes
→ create issue/PR
→ no automatic user publication
```

## 12.11. Runtime promotion pipeline

A new upstream release passes:

```text
1. Discovery
2. Source/provenance capture
3. Quarantine download
4. License/notices review
5. Archive/content inventory
6. Malware/static scan
7. Z2P bundle construction
8. Manifest generation
9. Compiler/StrategyPack compatibility
10. Golden profile compilation
11. FakeRuntime/package tests
12. Isolated Windows real-runtime smoke
13. Manual review
14. Stable target signing
15. Snapshot/timestamp publication
16. Repository consistency verification
17. Evidence Pack publication
```

Failure at any stage leaves the release unapproved and invisible to Stable clients.

## 12.12. Repository tooling

User machines do not require Python.

Release infrastructure may use the current pinned official `python-tuf` reference tooling for:

- metadata generation;
- signing workflows;
- repository verification;
- test repository generation.

Tool versions are locked and recorded in provenance.

Поскольку официальный `python-tuf` отмечает repository module как нестабильный API, release tooling:

- pin exact python-tuf version;
- uses a small Z2P-owned wrapper;
- avoids unpinned internal APIs;
- verifies generated repository with a second independent read/verify step;
- treats tooling upgrade as a security-sensitive change.

The Z2P client remains managed .NET code.

## 12.13. .NET client implementation decision

At implementation time:

1. evaluate maintained, security-reviewed .NET TUF clients;
2. require TUF 1.0 support, root rotation, consistent snapshots and conformance evidence;
3. reject abandoned or opaque packages;
4. if none meets requirements, implement a narrow Z2P TUF client for the documented POUF.

A custom client is considered a separate security-critical subsystem and must:

- implement the official detailed client workflow exactly for the documented Z2P POUF;
- use `System.Text.Json` source-generated DTOs for bounded parsing;
- preserve signed bytes/objects according to the selected TUF metadata format;
- avoid an ad-hoc “sort JSON properties and verify” implementation;
- reject duplicate properties, excessive depth, excessive size and unknown mandatory semantics;
- avoid floating-point metadata;
- support sequential root rotation;
- persist high-water metadata versions;
- verify threshold signatures, expiry, length and hashes;
- use platform cryptography (`ECDsa`) with fixed accepted algorithms;
- pass repositories generated by pinned official `python-tuf`;
- pass official/community interoperability vectors where applicable;
- pass rollback, freeze, mix-and-match, fast-forward and malformed-metadata corpus;
- receive a focused independent security review before Stable.

The POUF must explicitly define:

```text
metadata format and canonicalization
accepted key types/curves
role thresholds
consistent-snapshot naming
maximum metadata sizes
maximum delegation depth
root rotation procedure
trusted-time/high-water behavior
error handling
```

Do not implement a vaguely “TUF-inspired” protocol and call it TUF.

## 12.14. Client components

Without creating an empty new production project, place responsibilities as follows:

```text
Zapret2Pilot.Core/
  RuntimeUpdates/
    RuntimeRepositoryId
    RuntimeTargetId
    RuntimeUpdateId
    RuntimeUpdateState
    RuntimeUpdateCandidate
    RuntimeUpdateFailure
    RuntimeBundleLifecycleState

Zapret2Pilot.Application/
  RuntimeUpdates/
    CheckRuntimeUpdatesCommand
    DownloadRuntimeUpdateCommand
    CancelRuntimeDownloadCommand
    ActivateRuntimeBundleCommand
    RollbackRuntimeBundleCommand
    RemoveRuntimeBundleCommand
    GetRuntimeUpdateStatusQuery
    RuntimeUpdateCoordinator
    RuntimeUpdateApplicationState

Zapret2Pilot.Infrastructure/
  RuntimeUpdates/Tuf/
    TufRuntimeRepositoryClient
    TufMetadataVerifier
    TufTrustedRootStore
    TufCanonicalJson
    TufRepositoryTransport
  RuntimeUpdates/Download/
    RuntimeTargetDownloader
    RuntimeDownloadResumeStore
  RuntimeUpdates/Archive/
    RuntimeBundleArchiveExtractor

Zapret2Pilot.Runtime/
  Bundles/
    RuntimeBundleStore
    RuntimeBundleActivator
    RuntimeBundleLease
    RuntimeBundleGarbageCollector
    RuntimeBundleProbationPolicy

Zapret2Pilot.Storage/
  RuntimeUpdates/
    RuntimeRepositoryStateStore
    RuntimeDownloadStateStore
    RuntimeBundleInstallationStore
    RuntimeActivationHistoryStore
```

`Platform.Windows` provides signature, ACL and final file identity checks.

## 12.15. Update operation state machine

```text
Idle
CheckingMetadata
MetadataUnavailable
NoUpdate
UpdateAvailable
Downloading
DownloadCancelled
DownloadFailed
VerifyingTarget
Extracting
VerifyingBundle
InstalledCandidate
ActivationPending
TestingCandidate
Activated
RollbackPending
RolledBack
Quarantined
Failed
```

This state machine is separate from Runtime Kernel lifecycle but coordinates through Application Operation Coordinator.

## 12.16. Operation conflict policy

Allowed while runtime is Running:

- metadata check;
- target download;
- archive hash verification;
- extraction into isolated secure staging;
- full offline bundle verification.

Forbidden while runtime is Running:

- changing Current bundle;
- deleting Current/PreviousKnownGood;
- candidate runtime test;
- activation;
- rollback.

Activation conflicts with:

- ApplyProfile;
- Auto Doctor;
- app shutdown;
- another activation;
- bundle garbage collection.

## 12.17. Background check policy

Default:

```text
Enabled: true
CheckInterval: 24 hours
StartupDelay: after app Ready
Jitter: bounded random delay
AutoDownload: false
AutoActivate: false
Channel: Stable, immutable in Stable build
```

Check is metadata-only until the user confirms download.

Manual “Проверить обновления” bypasses interval but not security validation.

Failure to check:

- does not block current runtime;
- does not mark current bundle unsafe;
- produces a bounded diagnostic event;
- uses backoff;
- never loops aggressively.

## 12.18. Privacy of update checks

Requests contain no:

- machine identifier;
- network fingerprint;
- current profile;
- probe results;
- user account;
- telemetry token.

Allowed:

- generic product User-Agent;
- app version/channel if operationally required;
- standard HTTP cache headers.

No cookies are stored.

## 12.19. System clock policy

TUF metadata expiration relies on time.

Client stores:

```text
LastTrustedRepositoryUtc
LastAcceptedRootVersion
LastAcceptedTimestampVersion
LastAcceptedSnapshotVersion
LastAcceptedTargetsVersion
```

If system clock moves materially backwards relative to trusted state:

```text
RuntimeUpdateClockInvalid
```

Behavior:

- existing verified runtime remains usable;
- metadata refresh/download activation is blocked;
- UI explains how to fix system time;
- no bypass setting in Stable build.

## 12.20. HTTP transport

Use a dedicated named `HttpClient` with a long-lived handler or `IHttpClientFactory`.

`Microsoft.Extensions.Http.Resilience` may be used only for idempotent transport:

```text
GET timestamp/snapshot/targets/root metadata
GET runtime target
Range resume
```

Standard resilience pipeline:

```text
total request timeout
per-attempt timeout
bounded retry with jitter
circuit breaker
connection timeout
```

Forbidden automatic retries:

```text
ActivateBundle
StartRuntime
ApplyProfile
CommitTransaction
Rollback
DeleteBundle
```

Requirements:

- HTTPS only;
- allowlisted metadata/target base URIs;
- manual redirect validation;
- maximum redirect count;
- no HTTPS → HTTP downgrade;
- reject redirect to non-allowlisted host unless release policy explicitly permits;
- `HttpCompletionOption.ResponseHeadersRead`;
- connect timeout;
- inactivity/read timeout;
- cancellation;
- no unbounded buffering;
- system proxy support;
- platform certificate validation;
- no “ignore TLS errors” option;
- bounded retries only for transient idempotent failures;
- `PooledConnectionLifetime` configured for DNS refresh;
- no cookies;
- no credentials forwarded across redirects.

Repository authenticity still comes from TUF, not TLS alone.

## 12.21. Download limits


Before download:

- target length comes from signed metadata;
- target length must be below hard security cap;
- free space check includes archive, extraction and rollback margin;
- target filename/path is generated internally;
- destination is secure staging.

During download:

- never write beyond signed length;
- short/long body fails;
- incremental SHA-256;
- progress throttled;
- no UI-thread writes;
- cancellation leaves a resumable partial only when safe.

## 12.22. Resume

Partial state contains:

```text
TargetPath
ExpectedLength
ExpectedSha256
DownloadedLength
ETag
LastModified
SourceUriFingerprint
CreatedAtUtc
```

Resume uses HTTP Range + `If-Range` where available.

Rules:

- partial target identity must match current signed metadata;
- server mismatch/rejected range restarts from zero;
- full target SHA-256 always reverified;
- partial metadata is not trusted as final integrity evidence;
- stale partials are garbage-collected.

## 12.23. Archive safety

After target hash verification, extract into a new secure staging directory.

Reject:

- absolute paths;
- `..`;
- drive/device paths;
- ADS;
- duplicate normalized entries;
- case-colliding entries;
- symlink/reparse entries;
- unsupported entry types;
- excessive entry count;
- excessive uncompressed total;
- suspicious compression ratio;
- files absent from internal manifest;
- unexpected executable files.

Internal manifest defines exact file set, sizes and hashes.

No file is executed from extraction staging.

## 12.24. Bundle installation

```text
verified archive
→ secure staging extraction
→ internal manifest verification
→ compatibility evaluation
→ ACL verification
→ atomic directory move on same volume
→ final file identity/hash verification
→ InstalledCandidate DB record
```

If final BundleId directory already exists:

- verify exact identity;
- reuse only if every file matches;
- otherwise mark store corruption and block.

## 12.25. Bundle lifecycle states

```text
BundledDefault
Installed
Candidate
Current
PreviousKnownGood
Probation
Quarantined
Retired
ReferencedByHistory
```

A bundle may have multiple flags/history but one active role at a time.

## 12.26. Activation model

Downloading is not activation.

Activation request:

```text
1. Reverify candidate.
2. Evaluate compatibility again.
3. Acquire Application operation lease.
4. Ensure no Apply/Doctor/update activation.
5. Preserve previous Current.
6. Preserve whether runtime was Running.
7. Stop current runtime if user confirmed.
8. Start candidate explicitly by BundleId.
9. Wait readiness.
10. Run controlled health/key-service gate.
11. On success, commit Current/PreviousKnownGood atomically.
12. Restore prior running/stopped intent.
```

Global “current” pointer is never changed before candidate test passes.

## 12.27. Candidate test behavior

If runtime was Running:

- current runtime stops;
- candidate starts with current compatible profile;
- success leaves candidate running;
- failure restarts previous bundle/profile.

If runtime was Stopped:

- candidate starts only for bounded smoke;
- after success it stops;
- candidate becomes Current but user state remains Stopped.

## 12.28. Probation and automatic rollback

After activation:

```text
Probation window
  minimum stable duration
  readiness success
  no process crash
  bounded health evidence
```

Automatic rollback is allowed once when:

- candidate process crashes;
- readiness repeatedly fails;
- fatal startup classification occurs.

Efficacy-only degradation does not silently roll back; user receives recommendation.

On automatic rollback:

- candidate becomes Quarantined;
- PreviousKnownGood restored;
- one automatic restart maximum;
- evidence recorded.

## 12.29. Manual rollback

User may select:

```text
Вернуться к предыдущей версии модуля
```

Only PreviousKnownGood is offered in MVP.

Rollback:

- uses the same transaction/journal model;
- re-verifies old bundle;
- preserves current profile if compatible;
- otherwise uses last compatible known-good profile;
- never downloads an old bundle merely because a local file is missing without fresh trusted metadata.

## 12.30. Retention and leases

Keep by default:

```text
Current
PreviousKnownGood
Candidate/Probation
bundled fallback while required
```

Concrete leases:

```text
RuntimeBundleLease
RuntimeWorkspaceLease
DownloadedArtifactLease
AppPayloadLease
```

A bundle may be removed only when:

- not Current/Previous/Candidate;
- no active runtime process references it;
- no activation/rollback/recovery/diagnostics lease;
- no persisted session/transaction requires it;
- retention policy allows deletion.

Deletion protocol:

```text
acquire GC operation lease
→ transition to Deleting
→ recheck references
→ close handles
→ delete immutable directory
→ verify absence
→ transition to Deleted
```

Crash during deletion is reconciled on startup.

No “best effort directory delete” outside lifecycle state machine.

## 12.31. Compatibility resolution


An available target is classified:

```text
Compatible
CompatibleWithWarnings
RequiresNewStrategyPack
RequiresNewZ2P
UnsupportedWindows
UnsupportedArchitecture
Revoked
Unknown
```

MVP does not remotely update app compiler code.

If target requires a Strategy Pack not already trusted/compatible with current app:

```text
Сначала обновите Zapret2Pilot
```

or the release is not offered.

## 12.32. Revocation/advisory policy

No generic remote kill switch.

TUF metadata may carry an offline-signed security classification:

```text
Approved
Deprecated
RevokedCritical
```

Behavior:

- `Deprecated`: warn and recommend update;
- `RevokedCritical`: block new activations and new starts of that bundle after trusted metadata refresh;
- never forcibly terminate an already running process solely due to remote metadata;
- offer PreviousKnownGood or bundled fallback;
- record security event.

Critical revocation requires offline targets/root authority, not only timestamp/snapshot online keys.

## 12.33. Offline behavior

- app always ships with at least one verified default bundle;
- no network is required for first run;
- expired/unavailable update metadata does not invalidate bundled/current runtime;
- user may disable checks;
- download failures leave Current untouched;
- downloaded candidates remain local and verifiable.

## 12.34. Installer and portable integration

Both distributions:

- embed same Stable trusted root;
- ship same default bundle identity;
- use same Runtime Bundle Store contract;
- consume same stable repository;
- show same update status.

Installed:

- may use installer-owned default bundle;
- downloaded bundles live in protected ProgramData side-by-side store.

Portable:

- default payload is staged to protected ProgramData;
- downloaded bundles use the same secure store;
- portable package folder is never a runtime execution root;
- cleanup UI can remove portable-downloaded/staged bundles not shared/in use.

## 12.35. Persistence schema

Add:

```sql
runtime_repository_state
  repository_id TEXT PRIMARY KEY
  trusted_root_version INTEGER NOT NULL
  timestamp_version INTEGER
  snapshot_version INTEGER
  targets_version INTEGER
  last_trusted_utc TEXT
  last_check_utc TEXT
  last_success_utc TEXT
  last_error_code TEXT
  metadata_json TEXT NOT NULL

runtime_bundle_installations
  bundle_id TEXT PRIMARY KEY
  bundle_revision TEXT NOT NULL
  target_path TEXT
  target_sha256 TEXT NOT NULL
  manifest_sha256 TEXT NOT NULL
  install_root TEXT NOT NULL
  lifecycle_state TEXT NOT NULL
  upstream_tag TEXT NOT NULL
  upstream_commit TEXT NOT NULL
  installed_utc TEXT NOT NULL
  verified_utc TEXT NOT NULL
  quarantine_reason TEXT

runtime_downloads
  update_id TEXT PRIMARY KEY
  target_path TEXT NOT NULL
  expected_length INTEGER NOT NULL
  expected_sha256 TEXT NOT NULL
  partial_path TEXT NOT NULL
  downloaded_length INTEGER NOT NULL
  etag TEXT
  state TEXT NOT NULL
  created_utc TEXT NOT NULL
  updated_utc TEXT NOT NULL
  last_error_code TEXT

runtime_bundle_activations
  activation_id TEXT PRIMARY KEY
  from_bundle_id TEXT
  to_bundle_id TEXT NOT NULL
  state TEXT NOT NULL
  runtime_was_running INTEGER NOT NULL
  started_utc TEXT NOT NULL
  completed_utc TEXT
  failure_code TEXT
```

State values have CHECK constraints and affected-row validation.

## 12.36. UI

`Модуль zapret2` displays:

- Current;
- PreviousKnownGood;
- available approved version;
- upstream tag/commit;
- Z2P bundle revision;
- size;
- compatibility;
- release notes summary;
- last check;
- security status.

Actions:

```text
Проверить обновления
Скачать
Отменить загрузку
Установить после остановки
Проверить и активировать
Вернуться к предыдущей версии
Удалить неиспользуемые версии
```

No action is shown as available unless Application read model permits it.

## 12.37. Update events and metrics

Events:

```text
RuntimeUpdateCheckStarted
RuntimeUpdateCheckCompleted
RuntimeUpdateAvailable
RuntimeDownloadStarted
RuntimeDownloadProgressSampled
RuntimeDownloadCompleted
RuntimeTargetVerified
RuntimeBundleInstalled
RuntimeActivationStarted
RuntimeActivationSucceeded
RuntimeActivationFailed
RuntimeRollbackStarted
RuntimeRollbackSucceeded
RuntimeBundleQuarantined
RuntimeMetadataExpired
RuntimeRepositorySecurityError
```

Metrics:

- check duration;
- metadata bytes;
- download bytes/duration;
- resume count;
- verification duration;
- extraction duration;
- activation duration;
- rollback count;
- metadata security failures.

No endpoint/profile data in metrics.

## 12.38. Typed errors

Minimum catalog:

```text
RuntimeRepositoryUnavailable
RuntimeMetadataExpired
RuntimeMetadataRollbackDetected
RuntimeMetadataFreezeSuspected
RuntimeMetadataMixAndMatch
RuntimeMetadataSignatureInvalid
RuntimeRootRotationInvalid
RuntimeUpdateClockInvalid
RuntimeTargetTooLarge
RuntimeTargetLengthMismatch
RuntimeTargetHashMismatch
RuntimeDownloadRedirectRejected
RuntimeDownloadCancelled
RuntimeDownloadDiskSpaceInsufficient
RuntimeArchiveUnsafePath
RuntimeArchiveBombRejected
RuntimeBundleManifestInvalid
RuntimeBundleCompatibilityFailed
RuntimeBundleInstallConflict
RuntimeActivationConflict
RuntimeCandidateReadinessFailed
RuntimeCandidateCrashed
RuntimeRollbackFailed
RuntimeBundleRevoked
```

## 12.39. Threat/test matrix

Required test repositories and fake HTTP server cover:

- valid root rotation;
- invalid root jump;
- insufficient signatures;
- compromised timestamp key;
- compromised snapshot key;
- rollback;
- freeze/expired metadata;
- mix-and-match;
- fast-forward;
- wrong target;
- target length too large;
- endless/chunked body;
- truncated response;
- redirect loop;
- HTTPS downgrade;
- resume ETag mismatch;
- hash mismatch;
- disk full;
- cancellation;
- archive traversal;
- duplicate entries;
- zip bomb;
- reparse race;
- final file replacement;
- activation crash at every durable step;
- candidate process crash;
- rollback crash;
- offline current-runtime operation.

## 12.40. Evidence and security review

Before Stable:

- TUF POUF reviewed;
- key ceremony documented;
- root/targets public keys backed up;
- interoperability tests against python-tuf;
- attack corpus green;
- independent focused review of client workflow/canonicalization;
- update repository operations drill;
- key rotation drill;
- emergency revocation drill;
- Evidence Pack complete.


## 12.41. MVP content scope and future seams

The TUF client in `0.1.0` resolves only:

```text
ContentType = RuntimeBundle
```

Unknown content types are rejected.

Domain models still carry independent versions:

```text
StrategyPackVersion
ProbePolicyVersion
BuiltInHostlistDataVersion
```

Future seams:

```text
IStrategyPackSource
IProbePolicySource
IBuiltInDataPackSource
IRuntimeTargetSource
```

Only local/bundled implementations are registered in Stable MVP. Remote Strategy/Data delivery requires a separate threat model, keys/roles, rollback policy and release milestone after `0.1.0`.

# 13. Windows native interop policy

## 13.1. Platform boundary

Целевой project:

```text
Zapret2Pilot.Platform.Windows
```

До физического выделения existing `Runtime.Windows` namespace рассматривается как migration boundary.

Только этот слой может содержать:

- `LibraryImport`;
- retained `DllImport`;
- CsWin32-generated bindings;
- Win32 structs/constants;
- SafeHandle implementations;
- ACL/Authentiсode/final-path APIs;
- process creation APIs.

## 13.2. Binding strategy

Лучшее решение для текущего кода:

```text
existing explicit native interfaces
+ source-generated LibraryImport
+ SafeHandle
+ fake-native seams
+ selective CsWin32 where generated declaration reduces risk
```

Не выполнять массовую замену всех bindings ради единообразия.

## 13.3. Rules

- owning and borrowed handles explicit;
- every owning handle wrapped by SafeHandle;
- `SetLastError=true` semantics tested;
- Win32 error captured immediately;
- exact `StructLayout`, packing and Unicode;
- no duplicate declaration of one API;
- no `IntPtr` as untyped long-lived ownership;
- unmanaged buffers use bounded lifetime;
- no direct native call from App/Application/Core/Engine;
- platform support annotations enabled.

## 13.4. Architecture enforcement

Architecture tests fail when:

```text
LibraryImport/DllImport appears outside Windows boundary
App references System.Diagnostics.Process
Application references Windows-specific assembly
Core references filesystem/platform packages
```

## 13.5. Interop tests

- fake-native unit tests;
- x64 layout/size assertions;
- invalid-handle/error propagation;
- leak tests;
- failure injection after each acquired resource;
- real Windows integration on isolated runner/VM.

---

# 14. Secure process creation

## 14.1. Production launcher

Единственный production path:

```text
CreateJobObjectW
→ SetInformationJobObject(KILL_ON_JOB_CLOSE)
→ InitializeProcThreadAttributeList
→ PROC_THREAD_ATTRIBUTE_JOB_LIST
→ optional PROC_THREAD_ATTRIBUTE_HANDLE_LIST
→ CreateProcessW(
     absolute lpApplicationName,
     EXTENDED_STARTUPINFO_PRESENT,
     CREATE_SUSPENDED,
     CREATE_UNICODE_ENVIRONMENT
   )
→ validate returned process identity
→ register process-handle wait
→ ResumeThread
```

Fallback:

```text
CREATE_SUSPENDED
→ AssignProcessToJobObject
→ verify assignment
→ ResumeThread
```

Только после explicit compatibility decision и tests.

Запрещено:

```text
Process.Start
→ AssignProcessToJobObject after execution began
```

## 14.2. LaunchRequest

```text
VerifiedRuntimeBundle
AbsoluteApplicationPath
RawArgumentTokens
WorkingDirectory
MinimalEnvironment
StdIoPolicy
CreationFlags
OperationId
Generation
PlanHash
RuntimeSessionId
```

Launcher не принимает arbitrary environment dictionary или unverified path.

## 14.3. Windows command line

`CompiledZapretPlan` хранит raw tokens.

Отдельные components:

```text
WindowsCommandLineEncoder
ArgsFileSerializer
EnvironmentBlockBuilder
```

Golden tests используют официальные Windows parsing semantics и corpus:

- spaces;
- empty args;
- quotes;
- backslashes before quotes;
- trailing backslashes;
- Unicode;
- long command lines;
- args-file fallback where supported.

`string.Join(" ", args)` запрещён.

## 14.4. Handle inheritance

Default:

```text
bInheritHandles = FALSE
```

Если stdout/stderr redirect включён:

- create exact pipe handles;
- use handle list;
- inherit only required child ends;
- parent ends non-inheritable;
- bounded async pumps start before ResumeThread;
- every failure closes all handles.

## 14.5. DLL search policy

- fully qualified application path;
- controlled current directory;
- no executable search via PATH;
- no plugin probing;
- no DLL load from portable source root;
- native runtime inventory fixed by bundle manifest;
- evaluate `SetDefaultDllDirectories`/safe search without breaking WinDivert/Lua;
- no arbitrary `LoadLibrary` path from profiles/settings.

## 14.6. Job policy

MVP:

```text
KILL_ON_JOB_CLOSE
no breakaway
accounting/query support
child process containment
process-tree observation
```

Do not enable without proof:

```text
ACTIVE_PROCESS_LIMIT=1
CPU/memory caps
dynamic-code mitigations
arbitrary process mitigation flags
```

## 14.7. Process monitoring

Primary lifecycle evidence:

```text
wait on process handle
query exit code
```

Job completion port may supplement:

- child process events;
- tree diagnostics;
- accounting.

It is not the sole source of truth because not every Job notification is guaranteed.

## 14.8. Stop policy

Before claiming graceful stop, verify upstream behavior.

Possible classified outcomes:

```text
GracefulProtocolSupported
CtrlBreakSupported
ForcedTerminateProcess
ForcedJobTermination
AlreadyExited
```

Stop deadlines and escalation are explicit and evidence-backed.

## 14.9. Acceptance

- no child instruction before containment;
- zero handle leak at every injected failure;
- process identity matches verified bundle;
- unexpected child remains in Job;
- app crash closes Job and process tree;
- argument golden vectors;
- stdout/stderr cannot deadlock;
- no `Process.Start` production path.

---

# 15. Windows path, payload and file identity security

## 15.1. Lexical checks are insufficient

`Path.GetFullPath + StartsWith` is only the first filter.

Security decisions are based on opened handles/final identities, not only normalized strings.

## 15.2. Required validation

For application payload, runtime bundle, workspace and downloaded artifacts as applicable:

- reject device namespace unless explicitly allowed;
- reject network execution roots;
- reject reparse traversal;
- inspect path segments;
- open final file/directory with controlled flags;
- retrieve final path by handle;
- retrieve volume serial/file identity;
- verify expected root/volume;
- verify ACL/owner;
- verify hash/signature/inventory;
- preserve handle/identity between final verification and security-sensitive operation where practical.

## 15.3. Test corpus

```text
..
absolute path
UNC
\\?\
\\.\
junction
symlink
mount point
hard link
8.3 short name
alternate data stream
trailing dot/space
reserved device name
case collision
Unicode normalization
network share
removable/read-only media
reparse swap
file replacement race
directory replacement race
same-name different file ID
unexpected DLL/EXE
```

## 15.4. Runtime executable requirements

- approved local volume;
- supported filesystem;
- secure immutable bundle root;
- no unexpected reparse;
- matching file ID/hash/signature policy;
- compatible manifest;
- no mutable user-data/source-portable location.

## 15.5. Portable app payload requirements

Before main elevated app starts:

- complete package inventory matches manifest;
- bootstrapper identity/signature valid;
- source payload copied to new protected versioned root;
- staged inventory reverified;
- executable/native dependencies remain inside staged app root;
- no source-root DLL search;
- AppPayloadId/final identities passed through trusted bootstrap handoff.

## 15.6. Workspace requirements

Workspace is mutable data, never an executable root.

It may contain only allowlisted generated artifacts:

```text
args/config
hostlists
session metadata
bounded logs/temp
```

No DLL/EXE/script is loaded/executed from workspace unless a future explicit signed design changes this rule.

---

# 16. Configuration, platform targets и startup trust


## 16.1. Host builder

Целевой API:

```csharp
Host.CreateApplicationBuilder(...)
```

Но standard defaults не считаются автоматически безопасными для elevated application.

Bootstrap создаёт explicit configuration graph.

## 16.2. Security-critical settings

Examples:

```text
DeploymentFlavor
ApplicationStagingRoot
RuntimeBundleRoot
RuntimeExecutablePolicy
TrustedTufRoot
RepositoryBaseUris
SigningPublisherPolicy
AllowedStrategyPacks
DeveloperRuntimeGate
DriverPolicy
```

Allowed sources:

```text
compiled constants
signed embedded resources
installer-created trusted machine configuration
verified staged app manifest
```

Forbidden sources:

```text
arbitrary environment variables
ordinary appsettings override
user settings
imported profiles
untrusted CLI
working-directory files
```

## 16.3. User preferences

Examples:

```text
language
close-to-tray
log retention
default work mode
confirmation preferences
window state
```

Sources:

```text
validated defaults
user settings store
approved machine policy where introduced
```

## 16.4. CLI allowlist

Stable build:

```text
--safe-mode
--collect-diagnostics
--verify-runtime-bundle
```

Internal/developer commands exist only in Developer channel or compile-time feature.

Unknown argument:

```text
typed bootstrap error
no fallback interpretation
```

## 16.5. Options validation

Security and runtime options:

- sealed records/classes;
- source-generated options validator;
- `ValidateOnStart`;
- no security-sensitive default that silently points to current directory;
- invariants tested against all configuration providers.

## 16.6. Target framework split

Generic projects:

```text
Zapret2Pilot.Core             net10.0
Zapret2Pilot.Application      net10.0
Zapret2Pilot.Engine.Zapret2   net10.0
Zapret2Pilot.Storage          net10.0
Zapret2Pilot.Infrastructure   net10.0 where platform-neutral
```

Windows projects:

```text
Zapret2Pilot.Platform.Windows net10.0-windows10.0.26100.0
Zapret2Pilot.Runtime          net10.0-windows10.0.26100.0
Zapret2Pilot.App              net10.0-windows10.0.26100.0
PortableBootstrapper          Windows-specific NativeAOT target
```

If the final supported Windows baseline changes before release, TFM/minimum OS build is updated by DEC and compatibility tests.

Benefits:

- CA1416 enforcement;
- compile-time platform boundary;
- honest Windows 11 baseline;
- no Win32 API leakage into Core/Application.

## 16.7. Startup coordinator

Do not depend on incidental registration order of many hosted services.

Use one top-level:

```text
Z2PApplicationLifecycleCoordinator
```

Phases:

```text
ProcessBootstrap
ShellVisible
CriticalStorageRecovery
DeploymentVerification
RuntimeOwnershipRecovery
CompatibilityPreflight
Ready
Stopping
Stopped
```

The window appears before non-critical probes/update checks.

Runtime actions remain disabled until critical preflight succeeds.

## 16.8. Static service locator removal

Remove:

```text
AppHost.Services
IServiceProvider access from App/ViewModels
```

`App` receives/inherits a constructed root object through bootstrap integration.

Composition graph is validated in tests.

## 16.9. Privileged input boundary

Treat as untrusted:

- clipboard;
- drag-and-drop;
- file picker;
- imports;
- URLs;
- CLI;
- environment;
- current directory;
- portable source directory.

MVP forbids:

- WebView;
- arbitrary plugins;
- custom XAML packages;
- ShellExecute on imported text/path;
- arbitrary external process launch.

External links use an allowlisted unelevated launcher or are copied to clipboard.

---

# 17. Persistence, SQLite native runtime и recovery

## 17.1. P0 SQLite remediation

Production Z2P must not ship with the deprecated/vulnerable native package currently suppressed through `NuGetAuditSuppress`.

Target dependency model:

```text
Microsoft.Data.Sqlite.Core
+ explicit SQLitePCL provider
+ controlled native sqlite3.dll
```

Minimum native SQLite version is selected from the latest officially fixed stable branch at implementation/release time and recorded in:

```text
SqliteNativeMinimumVersion
native/sqlite-manifest.json
SBOM
release provenance
```

For the current review baseline, the minimum accepted native line is:

```text
SQLite 3.51.3+
or an official maintained backport containing the same WAL-reset corruption fix
```

The exact release is re-verified against official SQLite security/release notes at `0.0.27` implementation and again before RC.

Startup verifies:

```sql
SELECT sqlite_version();
PRAGMA compile_options;
PRAGMA journal_mode;
PRAGMA foreign_keys;
```

Unsafe/unknown version:

```text
Z2P.STORAGE.SQLITE.UNSAFE_VERSION
```

and the production DB is not opened for writes.

`NuGetAuditSuppress` for this dependency must be removed before real-runtime approval.

## 17.2. Connection policy

SQLite ADO.NET objects are not shared concurrently.

```text
Writes:
  StorageWriteCoordinator
  bounded Channel<StorageWriteCommand>
  single writer
  short transactions

Reads:
  short-lived connections
  read-only where applicable
  no shared mutable command/reader
```

Do not combine WAL with shared-cache mode.

## 17.3. Required pragmas

On every connection as applicable:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

Initialization verifies actual returned values.

## 17.4. Durable schema

Core tables include:

```text
schema_migrations
application_state
runtime_state
runtime_sessions
runtime_crash_history
runtime_transactions
runtime_transaction_events
recovery_events
event_journal
runtime_bundle_installations
runtime_bundle_leases
runtime_activation_history
trusted_repository_state
runtime_downloads
staged_app_payloads
app_payload_leases
```

Constraints:

- one active runtime session;
- singleton runtime state;
- legal enum/state checks;
- immutable historical IDs;
- affected-row validation;
- optimistic state version where needed;
- foreign keys.

## 17.5. Backup

Never `File.Copy(z2p.db)` as the primary live-WAL backup.

Use SQLite Online Backup through `BackupDatabase`/supported native API.

Before migration:

```text
open source and backup destination
→ online backup
→ integrity/quick check backup
→ persist backup manifest/hash
→ apply migration transaction
→ verify schema/version
```

Keep bounded backups.

## 17.6. Unclean shutdown recovery

After unclean shutdown:

```sql
PRAGMA quick_check;
```

Then reconcile:

- DB runtime state;
- app/runtime mutex;
- lock metadata;
- process identity;
- bundle/app payload leases;
- incomplete transactions;
- staged/deleting artifacts.

Do not silently recreate a corrupt DB.

## 17.7. Durable transaction journal

Projection + append-only event journal are updated in one SQLite transaction.

Events are idempotent and version checked.

Recovery derives next safe action from:

```text
last durable event
+ current process/ownership facts
+ bundle identity
+ app payload identity
```

## 17.8. Crash budgets

Persist separately:

```text
GlobalEmergencyBudget
PerBundleBudget
PerProductionPlanBudget
PerAutoDoctorRunBudget
PerCandidateBudget
```

Candidate/Doctor failures do not directly poison production plan budget.

## 17.9. Downgrade

- normal downgrade blocked;
- newer DB schema detected explicitly;
- no automatic schema rollback;
- backup and export paths documented;
- previous app version may start only through explicit compatible recovery procedure.

## 17.10. Storage acceptance

- no suppressed vulnerable SQLite package;
- runtime SQLite version gate;
- WAL concurrency tests;
- checkpoint/write fault injection;
- online backup restore;
- quick_check recovery;
- single-writer ordering;
- busy timeout behavior;
- low-disk and disk-full;
- migration crash at each boundary;
- installed/portable data isolation;
- lease recovery.

---

# 18. Probe policy governance


## 18.1. Versioned built-in policy

```text
ProbePolicyVersion
ServiceId
DnsTargets
TcpTargets
TlsSni
HttpMethod
HttpPath
ExpectedResponseClasses
Timeout
RateLimit
PrivacyClass
ControlTargets
FallbackTargets
ReviewedAtUtc
```

Policy is shipped as signed/hashed application asset.

No remote policy update in MVP.

## 18.2. Evidence classes

Network/DPI evidence:

- deterministic reset;
- repeated TLS/SNI timeout with controls working;
- DNS poisoning evidence;
- scoped network timeout.

Not DPI evidence by itself:

- HTTP 403;
- HTTP 404;
- HTTP 429;
- generic 5xx;
- service outage;
- one NXDOMAIN;
- local offline;
- captive portal.

## 18.3. Endpoint maintenance

Every endpoint records:

- purpose;
- privacy impact;
- allowed frequency;
- expected result classes;
- fallback;
- last manual validation date.

## 18.4. Probe contexts

```text
BaselineWithoutBypass
ProductionHealth
CandidateEvaluation
ControlNetwork
```

The same network error may have different meaning by context. Active bypass cannot be used as the sole evidence of ISP baseline behavior.

## 18.5. Capability and probeability

Probe Policy maps each check to:

```text
ServiceId
CapabilityKind
Probeability
ExpectedEvidence
KnownLimitations
```

MVP supports only bounded built-in endpoints. Custom arbitrary URLs/headers/cookies are excluded.

## 18.6. Classification policy

Diagnostic labels use likelihood, not unsupported certainty:

```text
LikelyTlsInterference
LikelyTcpReset
LikelyDnsInterference
LikelyReadStall
LikelyIpBlocking
Inconclusive
```

Every classification contains alternative explanations and confidence.

## 18.7. Data-pack boundary

Probe Policy and built-in hostlist data are immutable, versioned application assets in MVP. They are not fetched from community repositories at runtime.

---

# 19. Observability, logs and correlation

## 19.1. Built-in primitives

```text
ILogger
ActivitySource
Meter
```

No cloud exporter in MVP.

## 19.2. Source-generated logging

Use `[LoggerMessage]` for hot/security-critical paths:

- Runtime Kernel transitions;
- process creation;
- bundle verification;
- storage migration;
- update metadata;
- candidate activation;
- recovery.

Benefits:

- stable templates;
- lower allocations;
- explicit EventId;
- compile-time generation.

## 19.3. Event ID registry

Central registry:

```text
RuntimeKernelEventIds
StorageEventIds
UpdateEventIds
ProbeEventIds
UiEventIds
SecurityEventIds
```

EventId is stable across releases unless semantics change.

## 19.4. Activities

```text
RuntimeStart
RuntimeStop
ApplyProfile
Rollback
BundleVerification
BundleDownload
BundleActivation
ProbeSession
AutoDoctorRun
DatabaseMigration
DiagnosticsExport
PortableAppStaging
```

No sensitive host/URL information in tags.

## 19.5. Metrics

```text
runtime.start.duration
runtime.stop.duration
runtime.readiness.duration
runtime.crash.count
runtime.rollback.count
runtime.kernel.queue.depth
runtime.kernel.stale_completion.count
runtime.log.dropped_lines
storage.busy.count
storage.write_queue.depth
probe.duration
update.download.bytes
update.activation.duration
portable.staging.duration
ui.snapshot.rate
```

Metrics remain local/support-bundle only.

## 19.6. Runtime stdout/stderr pump

Before real `winws2`:

```text
stdout reader
stderr reader
bounded channel
maximum line length
maximum aggregate bytes
rate limit
drop-oldest policy
dropped-line counter
shutdown drain deadline
```

Rules:

- readers start before child resume when redirected;
- child pipe handles exact;
- no synchronous UI/SQLite write from pump;
- binary/invalid text classified safely;
- secrets/redaction applied before persistent logs;
- pipe fill cannot block runtime indefinitely.

## 19.7. Structured event journal

Correlation fields:

```text
AppInstanceId
OperationId
RuntimeSessionId
RuntimeTransactionId
ProbeSessionId
AutoDoctorRunId
BundleId
AppPayloadId
PlanId
```

Retention and batch writes are bounded.

## 19.8. Logging provider

Select local file provider by ADR/package security review.

Requirements:

- rolling files;
- hard quota;
- bounded flush;
- local only;
- no raw packets;
- no URLs/query/cookies;
- no readable browsing history;
- crash-safe best effort.

## 19.9. Redaction

Regex foundation is not enough as the final policy.

Add typed redaction pipeline:

```text
structured field allowlist
URL parser/query removal
path masking
stable salted host/IP hashing
secret token patterns
maximum field/string length
binary rejection
```

Every support field declares privacy class.

---

# 20. Testing and verification strategy

## 20.1. Layers

```text
Unit
Reducer transition
Property/model-based
Architecture conformance
Storage integration
FakeRuntime process integration
Windows native integration
Headless Avalonia
Visual regression
Appium
Installer
Portable bootstrap/staging
Runtime updater/TUF
Real winws2 isolated smoke
Soak/fault injection
Targeted fuzzing
```

## 20.2. Runtime model tests

- exhaustive allowed/forbidden transition table;
- generated command sequences;
- persisted random seeds;
- generation monotonicity;
- stale completion rejection;
- cancellation before/after irreversible boundary;
- shutdown at every state;
- duplicate command/idempotency.

Minimum generated sequence count is evidence-backed, not a permanent magic number.

## 20.3. Property-based targets

- canonical hash writer;
- Windows command-line encoder;
- hostlist normalization;
- profile round-trip/migration;
- redaction;
- path normalization;
- journal replay.

Third-party property library only after xUnit v3/support review; deterministic internal generators are acceptable.

## 20.4. Fuzzing targets

Before RC:

```text
ProfileDocument parser
StrategyPack parser
RuntimeBundleManifest parser
ProbePolicy parser
TUF metadata parser/verifier
hostlist parser
SafePathResolver
WindowsCommandLineEncoder
DiagnosticsRedactor
portable package manifest
archive extractor
```

Every finding becomes a committed regression input.

## 20.5. Native/process tests

- structure size/layout;
- SafeHandle ownership;
- CreateProcess failure at each stage;
- Job assignment/kill-on-close;
- child process containment;
- handle inheritance;
- app crash;
- process exit race;
- final file identity replacement;
- junction/symlink/hardlink race.

## 20.6. Storage tests

- exact native SQLite version;
- WAL concurrent readers/writer;
- checkpoint/write race;
- disk full;
- busy timeout;
- online backup;
- migration crash;
- quick_check recovery;
- journal replay;
- lease recovery.

## 20.7. Portable tests

- signed bootstrapper;
- source folder tamper;
- unexpected DLL;
- package race during copy;
- whole-app staging;
- ACL failure;
- final staged hash mismatch;
- move between folders/machines;
- network/read-only/removable locations;
- installed/portable collision;
- staged app retention/cleanup.

## 20.8. TUF/update tests

- official/generated conformance repositories;
- sequential root rotation;
- threshold failures;
- rollback/freeze/mix-and-match;
- expired metadata;
- clock rollback;
- oversized metadata/target;
- malformed JSON/duplicate properties;
- redirect/proxy/range resume;
- partial download corruption;
- archive traversal/zip bomb;
- activation crash and rollback;
- bundle/app leases.

## 20.9. Architecture conformance

Tests fail on:

```text
Core → App/Runtime/Storage/Windows
Application → Avalonia/Windows
App → Process/SQLite/PInvoke
PInvoke outside Windows boundary
Runtime → App
production → Testing.FakeRuntime
IServiceProvider usage outside composition/approved temporary adapter
Process.Start in production runtime launch path
```

## 20.10. Ecosystem regression corpus

Maintain reviewed, license-compatible fixtures derived from real ecosystem failure modes:

- broad TCP/443 and UDP/443 capture;
- game-port regressions;
- scoped vs all-traffic strategies;
- TLS 1.2/TLS 1.3 divergence;
- QUIC-only failures;
- TCP 16–20 KB stalls;
- DNS interception/poison evidence;
- active-bypass-distorts-baseline scenarios;
- upstream `master` newer than published release;
- unsupported runtime capability;
- internally adaptive Strategy Pack rejection;
- stale community strategy provenance;
- foreign WinDivert consumer conflict.

Corpus inputs are normalized test data, not executable community scripts.

## 20.11. Evidence Pack

Every milestone:

```text
docs/evidence/0.0.xx/
  summary.md
  scope.md
  invariants.md
  test-matrix.md
  manual-qa.md
  threat-model-delta.md
  known-limitations.md
  evidence.json
```

Large logs/screenshots remain CI artifacts with hashes.

A milestone is `Implemented` only when code, docs, acceptance and evidence are complete.

---

# 21. Evidence Pack


## 21.1. Repository contents

For every milestone:

```text
docs/evidence/0.0.xx/
  summary.md
  scope.md
  invariants.md
  test-matrix.md
  manual-qa.md
  threat-model-delta.md
  known-limitations.md
  evidence.json
```

## 21.2. Large artifacts

Do not commit huge logs/screenshots.

`evidence.json` records:

```text
CommitSha
Version
BuildConfiguration
CI run URL/id
Artifact hashes
Test counts
Random seeds
Visual baseline revision
Manual machine info
Known failures
Approver
```

Large evidence remains CI artifact with SHA-256.

## 21.3. Status rule

Milestone may be marked `Implemented` only if:

- code merged;
- docs synced;
- acceptance green;
- evidence pack complete;
- unresolved findings explicitly downgraded/deferred with rationale.

---

# 22. Release channels

```text
Developer
Internal Alpha
Closed Beta
Release Candidate
Stable
```

Mapping:

| Version range | Channel |
|---|---|
| `0.0.29` | Developer |
| `0.0.30–0.0.36` | Internal Alpha |
| `0.0.37–0.0.42` | Closed Beta |
| `0.0.43` | Release Candidate |
| `0.1.0` | Stable |

Informational version example:

```text
0.0.40+closed.beta.<short-sha>
```

Stable application trusts only Stable Runtime Repository metadata. Internal builds embed a separate development repository root.

Installer/portable channels are never mixed in the same artifact name.


# 23. Dependency policy

## 23.1. General rule

Do not upgrade packages merely to change version numbers.

Update when:

- security advisory;
- relevant correctness bug;
- required API;
- supported compatibility improvement;
- platform support requirement.

## 23.2. No permanent vulnerable suppression

A `NuGetAuditSuppress` is temporary only and must include:

```text
advisory
affected package/path
owner
reason
expiry date
replacement plan
release-blocker status
```

Security-critical/native suppression cannot survive into Stable unless a documented independent risk acceptance is approved. The current SQLite suppression is explicitly not accepted for `0.1.0`.

## 23.3. Update groups

Low-risk automated PR:

- analyzers;
- test runners;
- build-only tooling.

Manual review:

- Avalonia;
- ReactiveUI;
- Microsoft.Extensions.*;
- Microsoft.Data.Sqlite.Core/provider;
- native SQLite;
- interop generators;
- resilience/TUF packages;
- WiX SDK.

No auto-merge:

- runtime/native packages;
- signing;
- installer/bootstrapper;
- cryptography/update;
- security-sensitive libraries.

## 23.4. Schedule

```text
monthly dependency window
quarterly native/runtime audit
pre-release dependency freeze/review
immediate advisory triage
```

## 23.5. Locking

- central package management;
- lock files for release projects;
- locked restore in CI/release;
- dependency graph/hash recorded in provenance;
- transitive native dependencies inspected explicitly.

---

# 24. CI/CD and supply-chain security


## 24.1. Pull-request workflow

Required jobs:

```text
format
locked restore
Release build
unit tests
reducer/property tests
architecture conformance
storage tests
FakeRuntime tests
Headless UI
NuGet audit
dependency review where available
CodeQL/SARIF where available
evidence artifact upload
```

Commands baseline:

```powershell
dotnet restore Zapret2Pilot.slnx --locked-mode
dotnet format Zapret2Pilot.slnx --verify-no-changes --no-restore
dotnet build Zapret2Pilot.slnx -c Release --no-restore
dotnet test Zapret2Pilot.slnx -c Release --no-build --logger trx
```

## 24.2. Workflow hardening

- pin every GitHub Action to full commit SHA;
- minimal job-specific `permissions`;
- concurrency group cancels stale PR run;
- explicit job timeout;
- no secrets in untrusted PR jobs;
- protected release environment;
- CODEOWNERS for Runtime/Windows/Storage/Updater/Installer/workflows;
- artifact retention explicit;
- downloaded artifacts verified by hash;
- release tags protected/immutable by policy.

Mutable action tags are not accepted in release workflow.

## 24.3. Dependency controls

- central package management;
- NuGet lock files for all release projects;
- locked restore in CI/release;
- `NuGetAuditMode=all` or current equivalent;
- no broad audit suppression;
- temporary suppression requires owner, reason, expiry and release blocker classification;
- dependency diff reviewed in PR;
- monthly update window;
- quarterly native/runtime audit.

No auto-merge for:

- SQLite/native;
- Avalonia/ReactiveUI major/minor;
- Win32 interop;
- TUF/update;
- signing/installer.

## 24.4. Native dependency provenance

For every shipped native binary:

```text
name
version
source repository/release
source/archive hash
build provenance
compiler/options where built
license
final shipped hash
signature
```

Includes:

- sqlite3.dll;
- Z2P-owned NativeAOT bootstrapper;
- winws2;
- WinDivert;
- other native app dependencies.

## 24.5. Release pipeline

```text
exact source tag
→ locked restore
→ build
→ tests/evidence
→ publish installed payload
→ publish portable bootstrapper/payload
→ sign Z2P-owned PE
→ build/sign MSI
→ build portable ZIP
→ SBOM
→ checksums
→ provenance/attestation
→ verify all artifacts independently
→ publish
```

## 24.6. Artifact attestations

Use GitHub artifact attestations where available.

If repository/account features do not permit them:

- produce signed provenance JSON;
- record workflow commit, source commit, toolchain, artifact hashes;
- document feature gap;
- do not claim unsupported SLSA level.

## 24.7. Security scanning

Where available:

- dependency review;
- CodeQL C#;
- secret scanning;
- Dependabot alerts/PRs.

Unavailable paid features are replaced with documented manual/SARIF controls, not silently assumed.

## 24.8. Real privileged tests

Real `winws2`/WinDivert tests run only on:

```text
isolated disposable Windows VM
known snapshot
no production secrets
post-run revert
network isolation appropriate to scenario
```

Never on ordinary shared GitHub-hosted runner.

## 24.9. Release evidence

Release Evidence Pack includes:

- commit/tag;
- dependency lock hashes;
- native manifests;
- SQLite runtime version;
- test matrix;
- real-runtime smoke;
- installer/portable reports;
- signatures;
- SBOM;
- checksums;
- attestation/provenance;
- known limitations;
- approvers.

---

# 25. Packaging technologies

## 25.1. Installer

Decision:

```text
WiX Toolset 7
plain per-machine MSI
```

Burn is added only if a real prerequisite chain appears.

MSIX is not selected for MVP.

MSI responsibilities:

- Program Files app payload;
- ProgramData layout/ACL;
- shortcuts;
- ARP entry;
- repair;
- upgrade;
- downgrade block;
- uninstall;
- running app/runtime checks;
- optional data cleanup;
- no service;
- no hidden autostart.

## 25.2. Portable bootstrapper

Decision:

```text
Zapret2Pilot.PortableBootstrapper
NativeAOT
single signed win-x64 executable
minimal BCL/platform surface
```

A mandatory architecture spike in `0.0.28` proves:

- NativeAOT publish;
- signature verification;
- embedded manifest access;
- UAC relaunch;
- protected whole-app staging;
- no unexpected dynamic dependency;
- acceptable binary size/startup;
- no extraction to untrusted temporary directory.

If spike fails, portable release is blocked until an equally strong native/minimal bootstrap design is approved. Falling back to elevated `z2p.exe` from ZIP root is not allowed.

## 25.3. Main application publish

```text
win-x64
self-contained
folder deployment
not trimmed initially
not NativeAOT
```

Installer and staged portable app use the same app payload bits where deployment metadata permits.

## 25.4. Artifact names

```text
Zapret2Pilot-0.1.0-win-x64.msi
Zapret2Pilot-0.1.0-win-x64-portable.zip
SHA256SUMS.txt
SBOM.spdx.json
THIRD-PARTY-NOTICES.txt
runtime-manifest.json
provenance.json / attestations
```

---

# 26. Roadmap summary after architecture review

| Version | Milestone | Required outcome |
|---|---|---|
| `0.0.24` | Runtime Kernel Lifecycle Closure | One reducer-driven authority; no stale completion; safe publication |
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

## 26.1. Mandatory gates

```text
No real winws2 before 0.0.24–0.0.28 are complete.

No public portable before whole-app staging is proven.

No Stable runtime updater before TUF conformance/security review.

No 0.1.0 with vulnerable/suppressed SQLite native dependency.
```

---

# 27. `0.0.24 — Runtime Kernel Lifecycle Closure`

## Goal

Заменить текущие конкурирующие `RuntimeSupervisor.SemaphoreSlim` и generic `RuntimeKernelWorker` одним доказуемым lifecycle authority.

Real `winws2` запрещён.

## Scope A — state model

Introduce:

```text
RuntimeKernelLoop
RuntimeKernelState
RuntimeKernelCommand
RuntimeKernelReducer
RuntimeTransition
RuntimeEffectIntent
RuntimeOperationId
RuntimeGeneration
RuntimeStatePublisher
```

`IRuntimeSupervisor` становится façade над loop.

## Scope B — pure reducer

Reducer:

- pure/deterministic;
- no I/O;
- no DB;
- no process APIs;
- no observer callbacks;
- returns next state, effect intents, durable/public events and command outcome.

## Scope C — command loop

- bounded channel;
- single reader;
- lifecycle commands never silently drop;
- observations may coalesce;
- explicit admission close on shutdown;
- no generic `Func<CancellationToken, Task>` as lifecycle contract;
- no false async thread-affinity guarantee.

## Scope D — effect completion

Every external effect returns:

```text
OperationId
Generation
EffectKind
Result
```

Apply only when operation/generation/state still match.

Stale completion:

```text
record event
cleanup effect-owned resource if needed
do not mutate current state
```

## Scope E — thread-affine ownership

Kernel thread owns:

- ownership mutex lease;
- Job handle;
- process handle registration;
- current resource table;
- reducer state commit.

No sync-over-async workspace/probe operation on owner thread.

## Scope F — state publication

`RuntimeStatePublisher`:

- publishes outside kernel thread;
- replays latest immutable snapshot;
- isolates throwing/slow subscribers;
- coalesces high-frequency observations;
- never lets UI callback block Runtime Kernel.

Remove direct lifecycle dependence on raw `BehaviorSubject`.

## Scope G — cancellation/deadlines

Add:

- cancellation reason;
- monotonic deadline;
- irreversible boundary;
- rollback/recovery outcomes after irreversible work;
- idempotent operation identity.

## Scope H — error catalog foundation

Normalize runtime error codes and correlation.

No raw exception text in user-facing errors.

## Scope I — dispose/shutdown

- explicit `StopCore`/`DisposeCore`;
- no `disposed=true` before required cleanup;
- idempotent shutdown;
- bounded effect drain;
- no unobserved fire-and-forget automatic stop;
- terminal invariant report.

## Scope J — automation ownership

Introduce:

```text
AutomationOwner
  None
  User
  AutoDoctor
  Autopilot
  Recovery
  RuntimeInternalExperimental
```

MVP rules:

- one owner at a time;
- no overlapping production/candidate runtime;
- RuntimeInternalExperimental is unavailable in Stable;
- operation coordinator rejects conflicting automation intent.

## Tests

```text
LateStartCannotOverwriteStopped
StopDuringStartSupersedesStart
ExitedDuringStartCannotPublishRunning
RepeatedExitTriggersOneCleanup
ShutdownDuringEveryState
StaleCompletionIsIgnored
SubscriberFailureDoesNotBreakKernel
QueueAdmissionAfterShutdownRejected
DuplicateOperationIdIsIdempotent
OverlappingAutomationOwnersRejected
SecondProductionRuntimeRejected
CancellationAfterIrreversibleBoundaryRequiresRecovery
DisposeReleasesAllResources
```

Additionally:

- exhaustive state × command table;
- generated sequences with persisted seeds;
- thread/task leak checks;
- FakeRuntime process-tree checks.

## Docs corrections

Update Architecture/Decision Log/Critical Review:

- remove claim that awaited delegates preserve dedicated-thread affinity;
- identify RuntimeKernelLoop as single authority;
- mark old Supervisor/Worker design superseded.

## Acceptance

- no invalid state;
- no late `Running`;
- no lifecycle mutation outside loop;
- no direct observer execution on kernel thread;
- no orphan FakeRuntime;
- no dual serialization authority;
- Evidence Pack complete.

---

# 28. `0.0.25 — Bootstrap, Platform Boundaries & UI Composition`

## Goal

Создать production composition root, trusted startup и формальный UI contract без real runtime.

## Scope A — Generic Host

- migrate to `Host.CreateApplicationBuilder`;
- construct approved configuration sources explicitly;
- security-critical options ignore environment/ordinary CLI/appsettings;
- source-generated options validation;
- validate service graph/scopes on startup.

## Scope B — startup lifecycle

Introduce:

```text
Z2PApplicationLifecycleCoordinator
```

Phases:

```text
ProcessBootstrap
ShellVisible
StorageRecovery
DeploymentVerification
OwnershipRecovery
CompatibilityPreflight
Ready
Stopping
```

Shell appears before non-critical probes/update checks.

## Scope C — platform TFMs

Split:

```text
Core/Application/Engine/Storage → net10.0
App/Runtime/Platform.Windows    → net10.0-windows10.0.26100.0
```

Enable/analyze platform compatibility.

## Scope D — DI composition

Register explicitly:

- deployment context;
- path providers;
- navigation router/page factory;
- Avalonia UI scheduler;
- localization;
- app version;
- main shell/window;
- lifecycle coordinator;
- feature facades;
- exception policy.

Remove:

```text
AppHost.Services
production parameterless ViewModel constructors
IServiceProvider usage in ViewModels
```

## Scope E — Application API migration start

Introduce compile-time typed use-case facades.

Keep old CommandBus only behind temporary adapter for not-yet-migrated non-critical features.

No reflection dispatch in Runtime critical path.

## Scope F — ReactiveUI lifecycle

- `WhenActivated`;
- `CompositeDisposable`;
- centralized UI scheduler;
- observe `ReactiveCommand.ThrownExceptions`;
- configure `RxApp.DefaultExceptionHandler`;
- no permanent anonymous subscription.

## Scope G — visual contract

Implement:

```text
theme tokens
Fluent icons
hero state matrix
expanded/medium/compact layouts
freshness states
Design Lab
dynamic version
```

No `Служба Z2P`, no mock version, no emoji icons.

## Scope H — privileged input baseline

- file import boundaries;
- CLI allowlist;
- no WebView/plugins/custom XAML;
- safe external-link abstraction;
- no ShellExecute on untrusted input.

## Scope I — global exception foundation

Subscribe/classify:

- Avalonia dispatcher;
- AppDomain;
- TaskScheduler;
- ReactiveUI default handler;
- hosted/lifecycle fatal paths.

State-compromising exception does not allow blind continuation.

## Tests

- complete DI graph resolution;
- no static locator;
- trusted configuration source tests;
- Windows TFM architecture tests;
- activation/disposal;
- compiled bindings;
- XAML resource load;
- Design Lab states;
- headless/visual baseline;
- exception classification.

## Acceptance

- shell starts through production DI;
- all runtime actions disabled until Ready;
- environment/CLI cannot replace runtime/TUF roots;
- no mock production dashboard values;
- no real `winws2`;
- Evidence Pack complete.

---

# 29. `0.0.26 — Privileged Boundary & Secure Process Launch`

## Goal

Гарантировать, что verified process не исполняет ни одной инструкции вне Job containment и не получает untrusted launch inputs.

## Scope A — Platform.Windows

Create/extract:

```text
Zapret2Pilot.Platform.Windows
```

Move/own:

- process creation;
- Job Objects;
- SafeHandles;
- ACL;
- final path/file identity;
- Authenticode;
- application manifest/elevation;
- OS compatibility primitives.

## Scope B — native binding policy

- `LibraryImport` primary for maintained declarations;
- existing explicit interfaces/fakes preserved;
- CsWin32 selective only after review;
- no P/Invoke outside Windows boundary;
- immediate last-error capture;
- layout/handle tests.

## Scope C — secure launcher

Implement `STARTUPINFOEX` path:

```text
Job configured first
→ JOB_LIST attribute
→ optional HANDLE_LIST
→ CreateProcessW suspended
→ validate process
→ register process wait
→ start bounded log pumps
→ ResumeThread
```

Fallback suspended assign only by explicit policy.

Remove production `Process.Start`.

## Scope D — launch contract

```text
VerifiedRuntimeBundle
RawArgumentTokens
ControlledWorkingDirectory
MinimalEnvironment
ExactStdIoPolicy
OperationId/Generation
PlanHash
RuntimeSessionId
```

## Scope E — command line/environment

- tested Windows encoder;
- no `string.Join`;
- minimal environment allowlist;
- explicit Unicode environment block;
- no arbitrary inherited PATH/current directory;
- no user-provided environment keys.

## Scope F — handle inheritance and logs

- `bInheritHandles=false` by default;
- exact pipe handles through HANDLE_LIST;
- bounded stdout/stderr pumps;
- child cannot inherit ownership/DB/TUF handles;
- failure injection after each handle acquisition.

## Scope G — file identity/TOCTOU

- final path by handle;
- volume/file ID;
- reparse checks;
- ACL;
- hash/signature;
- executable root;
- final verification immediately before launch.

## Scope H — monitoring and stop

- process-handle wait is primary;
- Job completion port supplemental;
- classify graceful/forced outcomes;
- no unproven CTRL_BREAK claim.

## Scope I — graceful stop and conflict conformance

Add:

```text
GracefulStopMethod
  ConsoleCtrlBreak
  ConsoleCtrlC
  RuntimeSpecificSignal
  NotSupported
```

Prove behavior with hidden process, process group, redirected stdio and Job Object before enabling a method.

Add bounded technical detectors:

```text
RuntimeConflictDetector
DriverLifecycleInspector
```

They may classify and block Start, but never kill/delete third-party components automatically.

## Scope J — portable bootstrap spike

Build a separate proof:

```text
NativeAOT signed bootstrapper
→ verify complete app payload
→ UAC relaunch
→ protected whole-app staging
→ launch staged app
```

No public portable yet. Spike result becomes DEC and release gate.

## Tests

- process cannot execute before containment;
- child tree stays in Job;
- app crash kills tree;
- all handle/failure paths;
- command-line golden corpus;
- environment allowlist;
- junction/hardlink/replacement race;
- unexpected DLL/path rejected;
- graceful-stop conformance matrix;
- foreign process/driver conflict evidence matrix;
- NativeAOT bootstrap proof.

## Acceptance

- no production `Process.Start` runtime path;
- zero process outside Job;
- no handle leaks;
- verified bundle/file identity required;
- portable bootstrap architecture proven or portable release explicitly blocked;
- Evidence Pack complete.

---

# 30. `0.0.27 — Safe SQLite, Durable State & Recovery`

## Goal

Закрыть storage supply-chain risk и сделать irreversible runtime/update/deployment transitions восстанавливаемыми.

## Scope A — SQLite package remediation

- replace bundled deprecated native SQLite graph;
- use `Microsoft.Data.Sqlite.Core`;
- choose explicit provider/native sqlite3;
- pin source/version/hash;
- sign shipped Z2P-controlled native binary where applicable;
- remove related audit suppression;
- add runtime `sqlite_version()` minimum gate;
- record compile options/provenance in diagnostics/SBOM.

## Scope B — storage concurrency

Introduce:

```text
StorageWriteCoordinator
```

- bounded single-writer queue;
- short transactions;
- separate short-lived read connections;
- no shared connection/command/reader;
- queue depth/timeout metrics.

## Scope C — online backup/migrations

- use SQLite Online Backup;
- verify backup before migration;
- forward-only schema migrations;
- migration checksum;
- no live-WAL File.Copy backup;
- no silent DB recreate.

## Scope D — schema

Add/complete:

```text
runtime_state
runtime_sessions
runtime_crash_history
runtime_transactions
runtime_transaction_events
recovery_events
runtime_bundle_installations
runtime_bundle_leases
runtime_activation_history
trusted_repository_state
runtime_downloads
staged_app_payloads
app_payload_leases
```

## Scope E — durable journals

Projection + append-only journal update atomically.

Transitions idempotent/version checked.

## Scope F — recovery

Classify/reconcile:

```text
PreviousAppCrash
StaleDb
StaleLock
ForeignRuntime
OwnedRuntimeUnexpectedlyAlive
DatabaseCorrupt
MigrationFailed
AclInvalid
BundleStateInvalid
StagedAppIncomplete
DeletingArtifactIncomplete
```

No PID-only action.

## Scope G — quick check and corruption UX

After unclean shutdown:

```sql
PRAGMA quick_check;
```

Corruption produces recovery screen/export/backup choice, not empty database.

## Scope H — crash budgets and leases

Persist separate production/bundle/Doctor/candidate budgets.

Recover stale bundle/app/workspace leases through facts, not timestamps alone.

## Scope I — downgrade

- detect newer schema;
- block normal downgrade;
- preserve export/recovery;
- no automatic schema rollback.

## Tests

- exact native SQLite version;
- WAL writer/checkpoint concurrency;
- disk-full/low-space;
- busy timeout;
- online backup/restore;
- crash at every migration/journal boundary;
- quick_check path;
- single-writer ordering;
- lease recovery;
- installed/portable data isolation.

## Acceptance

- no vulnerable/deprecated suppressed SQLite native package;
- safe version verified at runtime;
- backup/recovery proven;
- one active session and legal state constraints;
- restart converges after injected crashes;
- Evidence Pack complete.

---

# 31. `0.0.28 — Deployment Trust, Bundle Compatibility & Preflight`

## Goal

До первого real runtime доказать trust приложения, deployment flavor, runtime bundle и машины.

## Scope A — deployment context

Implement:

```text
IDeploymentContext
IAppDataLayout
IAppPayloadVerifier
IDeploymentSecurityEvaluator
```

Flavor comes from build metadata.

## Scope B — installed trust

- verify Program Files payload/signatures;
- verify ProgramData roots/ACL;
- detect unexpected writable executable locations;
- block unsafe installation state.

## Scope C — portable whole-app path

Turn `0.0.26` spike into production architecture foundation:

```text
z2p-portable.exe
→ package inventory/signature
→ protected versioned app staging
→ staged app verification
→ bootstrap handoff
→ staged z2p.exe
```

Implement:

- AppPayloadId;
- staged app records;
- AppPayloadLease;
- incomplete staging recovery;
- restricted mode;
- old app retention seam.

Public package remains later in `0.0.42`.

## Scope D — runtime bundle

- immutable side-by-side store;
- exact manifest/inventory;
- Current/Previous/Candidate metadata;
- opaque `VerifiedRuntimeBundle`;
- final identity/ACL/hash/signature;
- bundle leases.

## Scope E — compatibility

Check:

- Windows build/x64;
- elevation;
- app payload trust;
- deployment ACL;
- runtime bundle;
- WinDivert assets;
- BFE;
- HVCI hints;
- filesystem/volume;
- storage;
- ownership conflict;
- strategy/compiler/Lua compatibility.

Result:

```text
Compatible
CompatibleWithWarnings
Incompatible
Unknown
```

Unknown blocks runtime.

## Scope F — foreign process

Detect and block own launch.

No mandatory command-line retrieval, takeover or automatic kill.

## Scope G — trusted configuration/preflight read model

Show:

- deployment flavor;
- app payload ID/trust;
- SQLite native version;
- bundle/upstream/version/hash/signatures;
- compatibility details;
- licenses;
- recovery actions.

## Scope H — runtime capabilities and upstream state

Add:

```text
RuntimeCapabilityManifest
RuntimeCapabilityConformanceSuite
UpstreamPromotionState
```

Compatibility is evaluated from exact capabilities and version ranges. Stable preflight rejects:

- capability unknown but required;
- Strategy Pack requiring unsupported feature;
- runtime built from unapproved upstream state;
- `ObservedInMaster`/`PublishedUpstream` without Z2P promotion.

## Tests

- installed payload tamper;
- portable package/staging tamper;
- unexpected DLL;
- ACL failure;
- network/removable/read-only source;
- bundle/strategy/capability mismatch;
- upstream master-vs-release-vs-approved state;
- unsupported SQLite/runtime/Windows;
- foreign process;
- stale staging/leases.

## Acceptance

- app trust and runtime trust both required;
- portable main app never executes elevated from package root;
- no unknown compatibility launches;
- secure SQLite version displayed/verified;
- previous known-good bundle retained;
- Evidence Pack complete.

---

# 32. `0.0.29 — Real winws2 Developer Smoke`

## Goal

Первый запуск настоящего `winws2` только после доказанного выполнения всех P0 gates.

## Hard prerequisites

Evidence Packs `0.0.24–0.0.28` complete:

```text
single Runtime authority
secure contained process creation
safe SQLite native runtime
durable recovery
trusted configuration
installed/portable app trust foundation
verified compatible bundle
bounded stdout/stderr
global exception policy
```

## Gate

- Developer channel;
- explicit internal build capability;
- isolated disposable Windows VM;
- verified exact Runtime Bundle;
- approved fixed Strategy Pack/profile;
- no Stable/public UI Start path;
- no ordinary shared CI runner.

## Smoke flow

```text
machine preflight
→ baseline network evidence
→ bundle verification
→ compile fixed plan
→ create session
→ secure contained launch
→ readiness
→ bounded logs
→ service/control probes
→ stop
→ verify process tree gone
→ reconcile DB/lock/leases
```

## Scenarios

- normal start/stop;
- repeated cycles;
- forced `winws2` exit;
- forced `z2p.exe` exit;
- child process attempt;
- driver conflict;
- bundle tamper;
- executable replacement;
- stale lock/session;
- network loss;
- sleep/resume;
- disk/log pressure;
- installed development payload;
- staged portable development payload.

## Evidence

- process creation timeline;
- Job/process tree;
- handle accounting;
- bundle/app payload identities;
- SQLite native version;
- session/journal events;
- bounded runtime output;
- cleanup/recovery report.

## Acceptance

- zero orphan;
- zero process instruction outside containment;
- zero unsafe SQLite dependency;
- deterministic recovery;
- no unknown cleanup outcome;
- public runtime launch remains disabled;
- Red Team approval for advancing to `0.0.30`.

---

# 33. `0.0.30 — Application Runtime UX & Dashboard SSoT`

## Goal

Подключить доказанный runtime lifecycle к UI только через compile-time typed Application API.

## Scope A — feature facades

Implement:

```text
IRuntimeUseCases
ICompatibilityUseCases
IDashboardQueries
```

Migrate runtime critical flow off reflection `CommandBus`.

## Scope B — requests/outcomes

```text
StartRuntimeRequest
StopRuntimeRequest
RestartRuntimeRequest
RecoverRuntimeRequest
ResetCrashGuardRequest
```

Every request carries/correlates `OperationId`, deadline and cancellation semantics.

## Scope C — DashboardSnapshot

Single read model:

```text
ProcessLiveness
RuntimeReadiness
BypassEfficacy
SystemSafety
EvidenceAge
EvidenceSource
Confidence
CurrentProfile
CurrentBundle
DeploymentFlavor
AvailableActions
RecentEvents
```

ViewModels do not derive health or permissions.

## Scope D — hero policy

`DashboardHeroPolicy` maps the four health dimensions and freshness to:

- title;
- description;
- accent;
- primary action;
- secondary action;
- blocked reason.

No stale green state.

## Scope E — uptime/time

- monotonic elapsed runtime;
- wall clock only for display;
- no `DateTime.Now` duration logic.

## Scope F — network hooks baseline

On availability/address/DNS/sleep changes:

- mark efficacy stale;
- publish warning;
- schedule bounded recheck;
- no automatic profile switch yet.

## Scope G — UI thread/lifetime

- scheduler abstraction;
- activation-scoped subscriptions;
- command exceptions observed;
- snapshots coalesced;
- no Runtime/Storage concrete types in ViewModels.

## Acceptance

- FakeRuntime complete flow;
- internal real-runtime flow;
- no reflection dispatch in runtime critical path;
- no direct ViewModel process/storage dependency;
- stale/fault/blocked states represented honestly;
- architecture tests green.

---

# 34. `0.0.31 — Profiles, Rules & Deterministic Compiler`

## Goal

Завершить typed profile model и устранить placeholder/ambiguous compiler behavior до массового runtime применения.

## Scope A — profile boundary

```text
ProfileDocument
→ bounded JSON validation
→ migration
→ ProfileDefinition
→ compatibility validation
→ compilation
→ CompiledZapretPlan
```

Profile types/trust:

```text
BuiltIn / UserClone / Imported / Migrated
TrustedBuiltIn / UserLocal / ImportedUntrusted / ImportedVerified
```

## Scope B — Rules/hostlists

- include/exclude hostlists;
- service groups;
- normalization;
- duplicate removal;
- bounded size/count;
- import/export;
- preview/fingerprint;
- no arbitrary paths;
- no raw WinDivert filters;
- no arbitrary Lua.

## Scope C — Strategy Packs

- built-in immutable;
- exact version/hash;
- compatible bundle/runtime/Lua/compiler ranges;
- allowlisted parameters;
- exact required assets;
- no dynamic code/plugin loading.

## Scope D — real compiler outputs

Remove placeholders:

- real generated config where needed;
- actual hostlist contents supplied through typed inputs/orchestrator contract;
- raw argument tokens;
- args-file output;
- materialization manifest;
- no placeholder executable path as executable truth.

## Scope E — version axes

Introduce explicit constants/types:

```text
CompilerCompatibilityVersion
CompilerOptionsVersion
CanonicalizationVersion
ProfileSchemaVersion
StrategyPackSchemaVersion
```

Do not use app informational version as compiler compatibility.

## Scope F — canonical hashing

Replace delimiter-based string concatenation with:

```text
CanonicalHashWriter
  typed field IDs
  deterministic ordering
  length-prefixed UTF-8/binary values
  explicit null/default semantics
  version header
```

Cache key includes:

```text
CanonicalizationVersion
CompilerCompatibilityVersion
CompilerOptionsVersion
ProfileDocumentHash
StrategyPackHashes
HostlistFingerprints
RuntimeBundleManifestHash
```

## Scope G — source-generated JSON

Use source-generated contexts.

Enforce:

- max bytes/depth;
- duplicate property rejection;
- unknown property policy;
- no polymorphic type activation;
- deterministic migration/golden files.

## Scope H — Advanced Mode

Read-only:

- generated tokens/args file;
- plan hash;
- canonicalization/compiler versions;
- bundle/strategy IDs;
- PlanDiff;
- materialization inventory.

## Scope I — Traffic Impact Analyzer

`Zapret2Pilot.Engine.Zapret2` derives from typed plan:

```text
TrafficImpactReport
  Protocols
  Ports
  Directions
  HostlistScoped
  IpSetScoped
  PayloadScoped
  AllTlsTraffic
  AllQuicTraffic
  IncludesLoopback
  IncludesPrivateNetworks
  IncludesGamePortRange
  RawFilterComplexity
  EstimatedCaptureBreadth
  CompatibilityRisk
```

No parsing of arbitrary user WinDivert expressions in MVP.

## Scope J — curated Strategy Catalog and provenance

UI-facing families:

```text
Balanced
ConservativeTls
TlsSplit
TlsFake
YouTubeQuic
DiscordRealtime
CompatibilityFallback
Experimental
```

Each variant stores source provenance, risk class, required runtime capabilities, known limitations, evidence revision and last validation time.

Community candidates are manually translated into typed definitions; no direct Lua/BAT import.

## Scope K — hostlist overlay and adaptive policy

```text
EffectiveHostlist = BuiltIn + UserAdditions - UserExclusions
```

Built-in content is immutable. User overlays survive updates.

Add `InternallyAdaptive` to strategy metadata. Stable MVP rejects `true` to preserve one automation owner and reproducible plan semantics.

## Tests

- golden compile vectors;
- deterministic cross-process/platform-relevant results;
- cache invalidation for every input/version;
- ambiguous input corpus;
- hostlist property tests;
- schema migration;
- import fuzzing;
- real materialization round-trip;
- traffic-impact golden vectors;
- broad-filter/game-port regression corpus;
- provenance completeness;
- hostlist overlay determinism;
- internally adaptive Stable rejection.

## Acceptance

- compiler comment/docs equal real key inputs;
- no placeholder hostlist/config in approved runtime flow;
- no ambiguous canonical encoding;
- deterministic plan;
- incompatible pack/bundle/capability blocked;
- every approved strategy has traffic impact, provenance and risk metadata;
- Evidence Pack complete.

---

# 35. `0.0.32 — Apply Profile, PlanDiff & Rollback`


## Goal

Crash-recoverable profile change.

## UI flow

- validate;
- compile;
- PlanDiff;
- traffic-impact/risk delta;
- downtime warning;
- confirm;
- progress;
- result/rollback.

PlanDiff explicitly shows:

```text
new/removed protocol and port capture
hostlist-scoped ↔ all-traffic change
TLS/QUIC breadth
loopback/private-network effect
game-port risk
strategy risk increase/decrease
runtime capability requirements
```

## Durable flow

Every step journaled.

Cancellation:

- before old stop: simple cancel;
- after old stop: rollback/recovery, not pretend cancellation.

## Crash recovery examples

- crash after old stopped;
- crash after new process created;
- crash after readiness;
- crash before DB commit;
- crash during rollback.

## Terminal states

Only:

```text
Running(target)
Running(previous)
Stopped
Crashed/RecoveryRequired
```

## Acceptance

Fault injection at every journal event.

One rollback attempt maximum.

---

# 36. `0.0.33 — Observability & Network Hooks`

## Goal

Сделать runtime/update/storage диагностируемыми без unbounded logs, telemetry backend и влияния observers на critical loops.

## Scope A — logging API

- `ILogger`;
- source-generated `[LoggerMessage]`;
- central EventId registry;
- correlation scope;
- no raw exception text in user UI.

## Scope B — local provider

Select through ADR/package review.

Requirements:

- structured;
- rolling;
- hard quota;
- bounded retention;
- crash flush best effort;
- no cloud exporter;
- no sensitive URL/packet data.

## Scope C — runtime output

Implement bounded stdout/stderr pumps:

- exact inherited handles;
- readers before ResumeThread;
- max line length;
- max bytes/rate;
- bounded channel;
- drop-oldest;
- dropped counter;
- drain deadline;
- no direct DB/UI callbacks.

## Scope D — ActivitySource/Meter

Activities:

```text
RuntimeStart/Stop
Apply/Rollback
BundleVerify/Activate
Probe
AutoDoctor
Migration
DiagnosticsExport
PortableAppStaging
```

Local metrics include kernel/storage queue depth and stale completions.

## Scope E — event journal

- typed event IDs;
- batching through StorageWriteCoordinator;
- correlation IDs;
- retention;
- reader pagination/virtualization;
- no high-frequency direct writes.

## Scope F — redaction

Upgrade from regex-only to typed policy:

- field allowlist;
- URL parsing;
- path masking;
- stable salted host/IP hashes;
- max length;
- binary rejection;
- privacy class metadata.

## Scope G — network hooks

Normalize:

- availability;
- address/DNS changes;
- adapter facts;
- suspend/resume.

No automatic profile switch.

## Scope H — logs UI

- virtualized;
- filters;
- pause/live;
- copy selected;
- export;
- dropped/stale stream state.

## Acceptance

- verbose FakeRuntime cannot deadlock;
- memory bounded;
- subscriber/backpressure isolated;
- 100k event UI smoke;
- redaction secret corpus;
- retention and disk-full behavior;
- no cloud traffic.

---

# 37. `0.0.34 — Key Services Probe Engine`


## Goal

Versioned real efficacy checks.

## Project

Create `Zapret2Pilot.Probing`.

## Policy manifest

Built-in signed/hashed `ProbePolicyVersion`.

## Pipeline

- explicit `ProbeContext`;
- control targets;
- DNS;
- TCP;
- TLS;
- HTTP/media-safe bounded checks;
- classification;
- latency;
- freshness;
- evidence/confidence/limitations.

Contexts:

```text
BaselineWithoutBypass
ProductionHealth
CandidateEvaluation
ControlNetwork
```

## Service capability model

Bounded MVP kinds:

```text
WebAccess
MediaDelivery
RealtimeUdp
NativeClient
```

Probeability:

```text
Automated
PartiallyAutomated
ManualConfirmation
Unsupported
```

Aggregate service status never claims more than the evidence proves.

## Privacy

- known built-in endpoints;
- no arbitrary user URLs in MVP;
- no auth/cookies/custom headers;
- query removed;
- bounded frequency;
- manual confirmations stored as local user evidence, not telemetry.

## Health integration

Populate `BypassEfficacy` and per-capability observations with TTL.

## Acceptance

- fake network servers and failure classification matrix;
- baseline result cannot be produced while production bypass is silently active;
- Telegram Web does not imply NativeClient success;
- YouTube Web does not imply MediaDelivery success;
- unsupported capability remains Unknown;
- no application HTTP error increases aggressiveness by itself.

---

# 38. `0.0.35 — Auto Doctor Quick/Full`

## Goal

Bounded explainable recommendation.

## Separate budgets

```text
Global safety
Production plan
Bundle
Doctor run
Candidate
```

## Quick

≤45 s target.

- uses current production evidence and controls where valid;
- tests a small conservative candidate set;
- does not take a fresh no-bypass baseline unless user accepts disruption;
- never changes profile silently.

## Full

≤120 s target.

```text
capture current intent
→ stop production runtime
→ baseline without bypass
→ validate controls
→ derive compatible candidate set
→ test conservative/scoped candidates first
→ repeat top candidates
→ restore original intent
→ present recommendation
```

## Selection model

Do not select the first perfect candidate.

### Hard gates

```text
runtime/readiness passed
control evidence valid
required capabilities passed
no crash or severe regression
within time/safety budgets
```

### Pareto dimensions

```text
Efficacy
TrafficScope
Risk
Stability
Cost
Confidence
```

### Deterministic tie-break

```text
smaller capture breadth
lower risk class
higher repeatability
lower latency/CPU regression
simpler plan
stable catalog order only as final tie-break
```

Scoring policy is versioned, but a single weighted sum cannot override hard safety gates.

## Candidate isolation

- temporary session;
- separate journal;
- separate crash budget;
- cleanup;
- original profile preservation.

## Result

- Recommended;
- Acceptable;
- Uncertain;
- Rejected.

## Acceptance

- deterministic simulation;
- all-fail;
- regression;
- crash;
- cancellation;
- time budgets;
- explainability;
- first-perfect candidate is not automatically selected;
- equally effective scoped candidate beats all-traffic candidate;
- original runtime intent is restored after Full Doctor.

---

# 39. `0.0.36 — Autopilot & Network Binding`

## Goal

No profile flapping.

## Priority

```text
Pinned
Last known good for network
High-confidence Doctor result
Default balanced
```

## Policy

- debounce;
- cooldown;
- max switches/window;
- no switch during transaction;
- no switch while network unstable;
- no switch while Auto Doctor/Recovery owns automation;
- no internally adaptive Stable strategy;
- known-good evidence must be fresh and capability-compatible;
- visible disruptive action;
- offline not runtime crash.

## Privacy

Network fingerprint salted/technical; no readable SSID by default.

## Acceptance

Adapter/network/sleep/VPN/captive portal matrix.

---

# 40. `0.0.37 — Full MVP UI, Onboarding & Accessibility`

## Goal

Complete reference design and product screens.

## Presentation modes

```text
Simple
Advanced
```

Both are projections of the same `DashboardSnapshot` and typed use cases. Simple mode hides detail; it does not bypass safety, confirmation or transaction policy.

## Screens

- Dashboard;
- Profiles;
- Rules;
- Auto Doctor;
- Diagnostics;
- Logs;
- zapret2 Module;
- Settings;
- About;
- Onboarding;
- Recovery;
- internal Design Lab.

Capability and impact UI includes:

- per-service capability details;
- Automated/Partial/Manual/Unsupported label;
- evidence freshness/confidence;
- traffic-impact explanation;
- conflict evidence and safe recovery guidance;
- strategy family, provenance, risk and known limitations.

`Модуль zapret2` includes update-ready states:

- current/local bundle;
- last check;
- update unavailable/not implemented before `0.0.40`;
- download/activation UI hidden behind feature readiness.

## Onboarding

Includes installer/portable distinction.

Portable explanation:

- no install;
- runtime staging;
- machine data cleanup;
- restricted mode possible.

## Accessibility

- keyboard;
- focus;
- automation;
- text + color;
- high contrast;
- 200% scale;
- reduced animation;
- screen-reader announcements.

## Custom titlebar

Ship only after Windows 11 Snap/Appium/manual gate; otherwise native titlebar.

## Usability study preparation

Define scripts and tasks for target users.

## Acceptance

- no mock;
- visual baselines;
- compact/expanded;
- all states;
- RU complete;
- pseudo-localization;
- accessibility checks.

---

# 41. `0.0.38 — Tray & Desktop Lifecycle`

## Goal

Complete Windows lifecycle.

## Tray

Real state mapping.

## Close/Exit

```text
X → tray
Exit → stop runtime + shutdown
```

No runtime-after-exit.

## Windows events

- shutdown;
- logoff;
- sleep;
- resume;
- monitor topology;
- session changes where relevant.

## Portable

Portable Exit may additionally offer:

```text
Очистить временно staged portable runtime
```

but never deletes user data without explicit confirmation.

## Acceptance

Appium + manual Windows lifecycle matrix.

---

# 42. `0.0.39 — Crash Recovery, Support Bundle & Data Management`

## Goal

Complete supportability.

## Global exception sources

- Avalonia dispatcher;
- AppDomain;
- TaskScheduler;
- Rx command/default handler;
- hosted service fatal;
- kernel invariant;
- storage startup.

## Crash marker/report

Typed severity:

```text
RecoverableFeatureFailure
RuntimeFailure
ApplicationStateCompromised
FatalBootstrapFailure
```

Do not continue after state-compromising exception.

## Support bundle

Versioned schema, checksums, redaction.

## Data management

Installed and portable have separate clear/export actions.

Portable cleanup reports exactly what remains in ProgramData/LocalAppData.

## Incident evidence

Bundle includes deployment flavor/package ID.

## Acceptance

Fault injection and secret corpus.

---


# 43. `0.0.40 — TUF Runtime Repository Trust & Catalog`

## Goal

Реализовать проверяемое обнаружение только одобренных Z2P Runtime Bundles. Загрузка и activation остаются в `0.0.41`.

## MVP authority boundary

`0.0.40` authorizes only `RuntimeBundle` targets.

Strategy Packs, Probe Policy and built-in hostlist data remain versioned bundled assets. No extra delegated online content channels are added before `0.1.0`.

Unknown content types are rejected by Stable client.

## Scope A — formal Z2P TUF POUF

Create:

```text
docs/Z2P-RUNTIME-UPDATE-POUF.md
```

It fixes:

```text
supported TUF specification version
metadata format/canonicalization
accepted key types and curves
thresholds
top-level/delegated roles
consistent snapshots
maximum metadata/target sizes
maximum delegation depth
trusted root bootstrap
root rotation
high-water metadata versions
trusted-time/clock behavior
error semantics
repository/channel separation
```

Stable client trusts only Stable root. No setting/CLI/env channel switch.

## Scope B — key operations

Document/test:

- root 2-of-3 offline;
- targets 2-of-3 offline/hardware-backed where practical;
- online snapshot/timestamp;
- key ownership/backup/rotation;
- metadata expiry calendar;
- emergency root/targets procedure;
- no root/targets private keys as ordinary CI secret.

## Scope C — repository tooling

Use pinned official reference tooling in release infrastructure.

Requirements:

- exact tooling lock/provenance;
- small Z2P wrapper;
- generate metadata;
- independent second verification;
- repository consistency check;
- no user-machine Python dependency;
- tooling upgrade security review.

## Scope D — .NET verifier

Implement narrow POUF client:

```text
TrustedRootStore
MetadataTransport
BoundedMetadataParser
SignatureThresholdVerifier
RootRotationVerifier
SnapshotConsistencyVerifier
TargetResolver
HighWaterStateStore
TrustedClockPolicy
```

Rules:

- source-generated bounded JSON DTOs;
- reject duplicate properties and excessive depth/size;
- fixed accepted crypto algorithms;
- no custom crypto primitives;
- no vague “TUF-inspired” shortcuts;
- signed bytes/objects verified according to POUF;
- safe persistence of trusted root/high-water state.

## Scope E — compatibility resolver

Target metadata resolves to:

```text
Compatible
CompatibleWithWarnings
RequiresNewStrategyPack
RequiresNewZ2P
UnsupportedWindows
UnsupportedArchitecture
Revoked
Unknown
```

Unknown/incompatible targets are not downloadable for activation.

## Scope F — metadata check

- starts only after app Ready;
- default 24h + jitter;
- manual check supported;
- metadata-only;
- privacy-minimal;
- failure does not block current runtime;
- clock rollback/expired metadata explained.

## Scope G — UI/read model

Module screen shows:

- Current/PreviousKnownGood;
- latest approved target;
- upstream tag/commit;
- compatibility;
- metadata freshness;
- release notes;
- why target is unavailable;
- security/revocation status.

No Download button until catalog trust and compatibility are valid.

## Scope H — conformance/security tests

- official/python-tuf generated repositories;
- sequential root rotation;
- threshold success/failure;
- rollback;
- freeze/expiry;
- mix-and-match;
- fast-forward/high-water;
- malformed/duplicate JSON;
- oversized metadata;
- unknown key/algorithm;
- clock rollback;
- role/target-path boundaries;
- RuntimeBundle-only content type enforcement;
- Stable/Development root separation.

## Scope I — independent review

Before marking complete:

- focused cryptographic/protocol code review;
- threat model review;
- attack corpus evidence;
- key-operations dry run;
- recovery from lost/rotated online key;
- Evidence Pack.

## Acceptance

- no trust in GitHub `latest`/filename alone;
- no unsigned/local catalog override;
- root rotation and rollback/freeze defenses proven;
- metadata parser bounded;
- Stable cannot select Development root;
- no target download/activation yet;
- Evidence Pack/security approval complete.

---

# 44. `0.0.41 — Runtime Download, Activation & Rollback`

## Goal

Безопасно загрузить TUF-approved target, установить immutable Candidate и активировать его транзакционно с PreviousKnownGood rollback.

## Scope A — HTTP transport

Dedicated named client:

- HTTPS/allowlisted hosts;
- validated redirects;
- `ResponseHeadersRead`;
- range resume;
- signed length cap;
- incremental + final SHA-256;
- disk-space preflight;
- cancellation;
- no cookies/credentials forwarding;
- DNS-refresh handler lifetime.

`Microsoft.Extensions.Http.Resilience` allowed only for idempotent metadata/target GETs.

No generic retry around activation/state transitions.

## Scope B — download state

Persist:

```text
RuntimeUpdateId
TargetIdentity
ExpectedLength/Hash
DownloadedLength
ETag/LastModified
PartialPath
Created/UpdatedUtc
State
```

Partial file is never final integrity evidence.

## Scope C — archive security

Reject:

- absolute/device/traversal/ADS paths;
- symlink/reparse entries;
- duplicate normalized/case-colliding entries;
- unsupported types;
- excessive entry count/size/ratio;
- unexpected executable/DLL;
- file absent from internal manifest.

Extraction only after TUF target hash succeeds.

## Scope D — immutable installation

```text
verified target
→ secure staging
→ exact internal inventory/hash
→ compatibility
→ ACL
→ atomic same-volume move
→ final file identity verification
→ InstalledCandidate
```

Existing BundleId directory is reusable only after full exact verification.

## Scope E — leases

Use:

```text
DownloadedArtifactLease
RuntimeBundleLease
RuntimeWorkspaceLease
```

GC/update cannot delete Current/Previous/Candidate or any leased artifact.

## Scope F — activation transaction

```text
reverify Candidate
→ acquire ApplicationOperationLease
→ acquire Candidate/Previous bundle leases
→ preserve current running/stopped intent
→ stop Current if needed
→ start Candidate by exact BundleId
→ readiness
→ controlled efficacy/safety gate
→ commit Current/PreviousKnownGood atomically
→ probation
```

Global Current pointer changes only after successful candidate gate.

## Scope G — failure/rollback

On failure:

```text
Candidate → Quarantined
→ cleanup candidate runtime/workspace
→ reverify PreviousKnownGood
→ restart previous if prior intent was Running
→ durable rollback result
```

One automatic rollback/restart maximum.

Efficacy-only degradation does not silently roll back without policy/user evidence.

## Scope H — manual rollback

Only local verified PreviousKnownGood offered in MVP.

Rollback uses the same journal/lease/recovery path.

## Scope I — retention/GC

Deletion is a state machine:

```text
Retired
→ Deleting
→ reference/lease recheck
→ delete
→ Deleted
```

Crash during GC reconciled at startup.

## Scope J — Application/UI

Typed facades:

```text
IRuntimeUpdateUseCases
Check
Download
CancelDownload
Activate
Rollback
RemoveRetired
```

UI clearly separates:

```text
available
downloading
downloaded Candidate
activation required
testing
probation
rollback
quarantined
```

## Scope K — installed + portable

Both use the same protected RuntimeBundleStore.

Portable source directory is never execution root.

## Tests

- redirects/proxy/timeouts;
- range mismatch/resume;
- short/long body;
- partial corruption;
- disk full;
- archive attack corpus;
- candidate crash/readiness fail;
- app crash at every activation journal event;
- PreviousKnownGood unavailable/corrupt;
- lease/GC races;
- runtime was Running vs Stopped;
- installed/portable parity.

## Acceptance

- only TUF-approved compatible target installs;
- no unverified extraction execution;
- Current changes only after gate;
- failed Candidate cannot strand runtime;
- PreviousKnownGood rollback proven;
- all leases/recovery durable;
- Evidence Pack complete.

---

# 45. `0.0.42 — MSI + Secure Portable Packaging, Signing & Upgrade`

## Goal

Произвести два поддерживаемых public artifacts с разными deployment mechanics и одинаковыми runtime guarantees.

## 45.1. Shared app payload

Build once from same:

```text
source commit
app version
dependency locks
compiler versions
runtime bundle manifest
```

Installed and portable staged application payload differ only in explicitly declared deployment metadata.

Main app publish:

```text
win-x64
self-contained
folder deployment
not trimmed initially
not NativeAOT
```

## 45.2. Installer

Technology:

```text
WiX Toolset 7
per-machine MSI
```

Responsibilities:

- Program Files payload;
- ProgramData roots/ACL;
- Start Menu/optional desktop shortcut;
- ARP;
- repair;
- upgrade;
- downgrade block;
- running app/runtime checks;
- uninstall;
- optional data removal;
- no service/autostart/runtime launch during install.

Artifact:

```text
Zapret2Pilot-0.1.0-win-x64.msi
```

## 45.3. Portable bootstrapper

Project:

```text
Zapret2Pilot.PortableBootstrapper
```

Publish:

```text
NativeAOT
win-x64
single signed native executable
```

Responsibilities:

- package root detection;
- package/app payload inventory verification;
- signature/hash policy;
- non-elevated preflight;
- controlled UAC relaunch;
- protected versioned whole-app staging;
- ACL;
- final staged file identity verification;
- allowlisted bootstrap handoff;
- launch only staged `z2p.exe`;
- no remote code/runtime launch.

## 45.4. Portable ZIP

Artifact:

```text
Zapret2Pilot-0.1.0-win-x64-portable.zip
```

Contains:

```text
z2p-portable.exe
payload\
portable-package.manifest.json
licenses/notices
README-PORTABLE.txt
bundled Runtime Bundle payload
```

Unsupported direct entry:

```text
payload\z2p.exe
```

Main app verifies bootstrap handoff/AppPayloadId and rejects unsafe direct portable launch in Stable flavor.

## 45.5. Portable migration/update

```text
new ZIP
→ new bootstrapper verification
→ new app staging directory
→ startup/database compatibility preflight
→ new staged app Current
→ old staged app Previous/Retired
```

No in-place overwrite.

AppPayloadLease protects running/current/recovery payloads.

## 45.6. Signing/build order

Avoid circular manifests:

```text
1. Build unsigned payload.
2. Generate pre-sign inventory where needed.
3. Sign Z2P-owned PE files.
4. Generate final app/package manifests from signed bytes.
5. Bind/embed signed manifest roots according to bootstrap design.
6. Build/sign bootstrapper if final embedding step permits reproducible layout.
7. Build portable ZIP.
8. Build MSI.
9. Sign MSI.
10. Generate checksums/SBOM/provenance.
11. Independently verify signatures, manifests and packages.
```

Exact order is documented and tested; a manifest never hashes itself recursively.

## 45.7. Installer matrix

- clean install;
- repair;
- same-version reinstall;
- upgrade;
- downgrade block;
- uninstall preserve data;
- uninstall remove data;
- runtime active;
- portable state present;
- invalid/tampered package;
- non-admin;
- path/ACL verification;
- recovery after interrupted install where supported.

## 45.8. Portable matrix

- local NTFS path;
- spaces/Unicode/long path;
- moved package;
- source tamper;
- extra DLL;
- package race during staging;
- read-only/network/removable/unsupported filesystem;
- UAC cancel;
- staging ACL failure;
- staged payload tamper;
- installed app present;
- previous portable state;
- app payload migration;
- cleanup;
- old app retention/lease;
- runtime update parity.

## 45.9. Distribution artifacts

```text
Zapret2Pilot-0.1.0-win-x64.msi
Zapret2Pilot-0.1.0-win-x64-portable.zip
SHA256SUMS.txt
SBOM.spdx.json
THIRD-PARTY-NOTICES.txt
runtime-manifest.json
native-dependencies-manifest.json
provenance.json / artifact attestations
```

## Acceptance

- both artifacts from exact source/dependency graph;
- installer app runs only from trusted installed root;
- portable main app runs only from protected staged root;
- all Z2P-owned PE signed;
- SQLite/native/runtime provenance complete;
- upgrade/migration/cleanup proven;
- Evidence Pack complete.

---

# 46. `0.0.43 — Release Candidate Hardening`

## Feature freeze

No new product features.

Only:

- P0/P1 correctness/security fixes;
- performance regressions;
- documentation/evidence;
- release tooling fixes.

## Required automated matrix

- all unit/reducer/property tests;
- architecture conformance;
- storage native-version/WAL/backup tests;
- FakeRuntime;
- Windows native/process tests;
- Headless UI/visual regression;
- Appium critical paths;
- TUF/update attack corpus;
- ecosystem regression corpus;
- traffic-impact golden vectors;
- capability/probeability aggregation;
- baseline-vs-active-bypass isolation;
- conflict detector false-positive/negative matrix;
- installer;
- portable bootstrap/whole-app staging;
- redaction/fuzz corpus;
- performance regression.

## Real runtime/soak

- 100+ controlled real start/stop cycles or evidence-adjusted target;
- forced app/runtime kills;
- apply/rollback cycles;
- candidate activation/rollback;
- 24-hour run;
- reconnect/sleep/resume;
- low disk/log quota;
- DB busy/checkpoint;
- bundle/app payload tamper;
- shutdown at every transition.

## Windows matrix

- Windows 11 24H2 x64;
- Windows 11 25H2 x64;
- current supported Windows 11 release at RC;
- Defender;
- Memory Integrity on/off;
- Wi-Fi/Ethernet/offline;
- conflicting WinDivert;
- multi-monitor;
- 100–200% scale.

## Usability

5–8 target users plus heuristic review.

Tasks:

- understand hero/status;
- start/stop;
- choose/change profile;
- distinguish Check Now and Auto Doctor;
- diagnose blocked state;
- update runtime/rollback;
- exit/tray;
- install and portable launch;
- portable cleanup.

## Security reviews

Required sign-off:

- Runtime Kernel;
- CreateProcess/Job/SafeHandle;
- SQLite native/runtime;
- portable bootstrap/staging;
- TUF client/key operations;
- update extraction/activation;
- diagnostics privacy;
- CI/signing/provenance.

## Performance gates

Named reference machines and baseline regression:

```text
shell visible p50/p95
critical preflight
dashboard ready
bundle verification
profile compilation
runtime start/readiness
portable staging
DB migration/backup
memory 1/8/24h
UI long tasks
```

## Release blockers

- orphan runtime;
- process instruction outside Job;
- unsafe/suppressed SQLite native dependency;
- invalid DB backup/recovery;
- dual Runtime authority;
- stale completion mutation;
- untrusted config changes runtime/TUF root;
- false green/stale/overclaimed service capability dashboard;
- all-traffic strategy selected over equally effective scoped strategy;
- ambiguous compiler cache;
- portable main app runs elevated from package root;
- unexpected DLL accepted;
- TUF conformance/security review incomplete;
- Current bundle changes before candidate gate;
- rollback/lease recovery failure;
- unsigned/unprovenanced artifact;
- open P0;
- incomplete Evidence Pack.

---

# 47. `0.1.0 — Full MVP`

## Release assets

Mandatory:

```text
signed per-machine MSI
signed portable ZIP with NativeAOT bootstrapper
checksums
SBOM
third-party notices
runtime/native manifests
provenance/attestations
```

Stable Runtime Repository has completed at least one full metadata refresh/root-operations rehearsal and serves the bundled default target.

## Public guidance

```text
Installer — рекомендуется большинству пользователей.

Portable — без MSI; запускается через z2p-portable.exe,
который проверяет и размещает всё elevated приложение
в защищённом versioned staging root.
```

## Stable product claims

Allowed:

- local Windows manager for zapret2/winws2;
- typed profiles/rules;
- verified immutable runtime bundles;
- bounded Auto Doctor/Autopilot;
- installer and secure portable;
- TUF-approved runtime updates;
- transactional activation/rollback;
- privacy-first local diagnostics;
- no Windows Service.

Forbidden claims:

- universal bypass;
- all providers/regions;
- invisible background service;
- arbitrary runtime compatibility;
- cloud telemetry guarantees;
- unattended automatic code execution.

## Final release gate

- all P0 blockers closed;
- no vulnerable/suppressed SQLite native dependency;
- portable whole-app staging approved;
- TUF conformance/security review approved;
- real runtime soak passed;
- installer/portable signatures verified;
- canonical docs synced;
- exact source tag/provenance published.

---

# 48. Incident response


Add:

```text
docs/security/incident-response.md
docs/security/release-revocation.md
```

Scenarios:

- signing key compromise;
- malicious/corrupt bundle;
- compromised upstream release;
- unsafe built-in strategy;
- privacy leak;
- installer issue;
- portable payload tampering weakness;
- TUF key compromise;
- metadata freeze/rollback incident;
- malicious or incorrectly promoted Runtime Bundle;
- target storage compromise;
- update client canonicalization flaw.

Response:

- preserve evidence;
- revoke release;
- publish signed advisory;
- rotate key;
- ship fixed version;
- block bad BundleId/ProfileId in next release;
- no generic remote kill switch;
- offline-threshold-signed critical revocation may block future starts/activation of a compromised bundle.

---

# 49. Performance program

Benchmarks:

- canonical serializer;
- profile compile;
- hostlist normalization;
- bundle SHA verification;
- redaction;
- dashboard snapshot building.

Runtime measurements:

- shell visible;
- preflight;
- dashboard ready;
- start/stop/readiness;
- DB migration;
- support export;
- memory 1/8/24h;
- queue high-water.

No performance claim without recorded machine/build/evidence.

---

# 50. Documentation changes

## Existing canonical docs

Update on each architecture-changing milestone:

- `Z2P-CANON.md`;
- `Z2P-ARCHITECTURE.md`;
- `Z2P-CRITICAL-REVIEW.md`;
- `Z2P-ROADMAP.md`;
- `Z2P-DECISION-LOG.md`;
- `Z2P-IMPLEMENTATION-STATUS.md`;
- `Z2P-UI-DESIGN.md`;
- `README.md`.

## New/required documents

```text
Z2P-MVP-SCOPE.md
Z2P-UI-VISUAL-CONTRACT.md
Z2P-SECURITY-MODEL.md
Z2P-APPLICATION-API.md
Z2P-ERROR-CATALOG.md
Z2P-OPERATION-CANCELLATION.md
Z2P-CONFIGURATION-SOURCE-POLICY.md
Z2P-SUPPORTED-PLATFORMS.md
Z2P-DATA-LAYOUT-ACL.md
Z2P-DEPLOYMENT-FLAVORS.md
Z2P-PORTABLE-BOOTSTRAP-CONTRACT.md
Z2P-PORTABLE-SECURITY.md
Z2P-APP-PAYLOAD-MANIFEST.md
Z2P-RUNTIME-BUNDLE-CONTRACT.md
Z2P-RUNTIME-BUNDLE-LIFECYCLE.md
Z2P-RUNTIME-CAPABILITIES.md
Z2P-UPSTREAM-PROMOTION.md
Z2P-ECOSYSTEM-INTAKE.md
Z2P-AUTOMATION-OWNERSHIP.md
Z2P-STRATEGY-CATALOG.md
Z2P-TRAFFIC-IMPACT.md
Z2P-SERVICE-CAPABILITY-MODEL.md
Z2P-CONFLICT-DETECTION.md
Z2P-HOSTLIST-OVERLAY.md
Z2P-RUNTIME-UPDATE-POUF.md
Z2P-RUNTIME-UPDATE-THREAT-MODEL.md
Z2P-RUNTIME-REPOSITORY-OPERATIONS.md
Z2P-RUNTIME-KEY-MANAGEMENT.md
Z2P-SQLITE-NATIVE-POLICY.md
Z2P-STORAGE-RECOVERY.md
Z2P-COMPATIBILITY-MATRIX.md
Z2P-PROBE-POLICY.md
Z2P-OBSERVABILITY.md
Z2P-TESTING.md
Z2P-MANUAL-QA.md
Z2P-DIAGNOSTICS-PRIVACY.md
Z2P-RELEASE-CHECKLIST.md
```

## Decisions to record

```text
Single reducer-driven Runtime authority
Four-dimensional health
Safe state publisher
Typed feature facades over reflection CommandBus
Central error catalog
Cancellation/deadline/irreversible-boundary semantics
OS-specific target framework split
Trusted configuration source policy
Durable transaction journal
Single-writer SQLite model
Controlled fixed native SQLite runtime
Online SQLite backup
Foreign runtime detect-only policy
Immutable side-by-side runtime bundles
Runtime/App/Download leases
Opaque VerifiedRuntimeBundle
DeploymentFlavor embedded at build
Installer + portable public distributions
Portable whole-app staging
NativeAOT portable bootstrapper
Portable restricted mode
WiX Toolset 7 MSI
LibraryImport/native boundary
Secure STARTUPINFOEX process creation
Versioned canonical hash writer
Source-generated logging/options/JSON
Probe Policy Manifest
ActivitySource/Meter local-only
Evidence Pack
Release channels
Downgrade policy
Artifact provenance
TUF Runtime Repository/POUF
Stable/development trusted-root separation
Runtime update check/download/activation split
Candidate probation/PreviousKnownGood rollback
Offline-threshold-signed critical bundle revocation
Single production runtime
Single Z2P automation owner
Runtime Capability Manifest
Upstream master/release/promotion state model
Traffic Impact Analyzer
Bounded Service Capability Model
Probe context separation
Hard-gate + Pareto Auto Doctor selection
Generic non-destructive conflict detection
Curated Strategy Catalog and provenance
Built-in hostlist + user overlay
Runtime-only remote content scope for MVP
Community intake only through release infrastructure
```

---

# 51. Official documentation baseline

The implementation agent must re-check these sources at milestone start; URLs are the baseline, not a permanent cached answer.

## .NET hosting/platform

- Generic Host:
  https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host

- Target frameworks and OS-specific TFMs:
  https://learn.microsoft.com/en-us/dotnet/standard/frameworks

- Platform compatibility analyzer:
  https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1416

- Native AOT deployment:
  https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/

- Options validation source generator:
  https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator

- P/Invoke source generation:
  https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation

- System.Text.Json source generation:
  https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation

- LoggerMessage source generator:
  https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator

## .NET networking/resilience

- HttpClient guidelines:
  https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines

- HTTP resilience:
  https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience

- HttpCompletionOption:
  https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0

- RangeHeaderValue:
  https://learn.microsoft.com/en-us/dotnet/api/system.net.http.headers.rangeheadervalue?view=net-10.0

- IncrementalHash:
  https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash?view=net-10.0

## SQLite / Microsoft.Data.Sqlite

- Custom SQLite versions/providers:
  https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions

- Database errors and retry considerations:
  https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors

- Connection strings:
  https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings

- `BackupDatabase`:
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlite.sqliteconnection.backupdatabase

- SQLite WAL:
  https://www.sqlite.org/wal.html

- SQLite Online Backup API:
  https://www.sqlite.org/backup.html

- SQLite security/news/advisories:
  https://www.sqlite.org/security.html

## Windows process/security

- Job Objects:
  https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects

- Process creation flags:
  https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags

- UpdateProcThreadAttribute:
  https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute

- AssignProcessToJobObject:
  https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject

- DLL security:
  https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security

- Dynamic-link library search order:
  https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order

- GetFinalPathNameByHandle:
  https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew

- WinVerifyTrust:
  https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust

- SignTool:
  https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool

- Application manifests:
  https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests

## Avalonia / ReactiveUI

- Avalonia dependency injection:
  https://docs.avaloniaui.net/docs/app-development/dependency-injection

- Compiled bindings:
  https://docs.avaloniaui.net/docs/data-binding/compiled-bindings

- Threading:
  https://docs.avaloniaui.net/docs/app-development/threading

- Testing:
  https://docs.avaloniaui.net/docs/testing/

- Accessibility:
  https://docs.avaloniaui.net/docs/app-development/accessibility

- Unhandled exceptions:
  https://docs.avaloniaui.net/docs/app-development/setting-unhandled-exceptions

- ReactiveUI handbook:
  https://www.reactiveui.net/docs/handbook/

## Secure updates

- The Update Framework:
  https://theupdateframework.io/

- TUF specification:
  https://theupdateframework.github.io/specification/latest/

- python-tuf reference implementation:
  https://github.com/theupdateframework/python-tuf

## Packaging/supply chain

- WiX Toolset:
  https://docs.firegiant.com/wix/

- GitHub Actions security hardening:
  https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions

- Artifact attestations:
  https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations

- Dependency review:
  https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-review

- CodeQL for C#:
  https://docs.github.com/en/code-security/code-scanning/introduction-to-code-scanning/about-code-scanning-with-codeql

## Runtime

- zapret2:
  https://github.com/bol-van/zapret2

- zapret2 releases:
  https://github.com/bol-van/zapret2/releases

- WinDivert:
  https://reqrypt.org/windivert-doc.html

## Ecosystem evidence sources — non-normative

Re-check exact commit and license before deriving any fixture or strategy candidate:

- official Windows bundle/blockcheck:
  https://github.com/bol-van/zapret-win-bundle

- Flowseal community configuration corpus:
  https://github.com/Flowseal/zapret-discord-youtube

- zapret2 Windows GUI product patterns:
  https://github.com/Asterlike/zapret2UI

- DPI diagnostics:
  https://github.com/Runnin4ik/dpi-detector
  https://github.com/hyperion-cs/dpi-checkers

These sources cannot override official upstream documentation or Z2P conformance evidence.

---

# 52. Immediate execution order

```text
1. 0.0.24 — Runtime Kernel Lifecycle Closure
2. 0.0.25 — Bootstrap, Platform Boundaries & UI Composition
3. 0.0.26 — Privileged Boundary & Secure Process Launch
4. 0.0.27 — Safe SQLite, Durable State & Recovery
5. 0.0.28 — Deployment Trust, Bundle Compatibility & Preflight
6. 0.0.29 — Real winws2 Developer Smoke
7. 0.0.30 — Application Runtime UX & Dashboard SSoT
8. 0.0.31 — Profiles, Rules & Deterministic Compiler
9. 0.0.32 — Apply Profile, PlanDiff & Rollback
10. 0.0.33 — Observability & Network Hooks
11. 0.0.34 — Key Services Probe Engine
12. 0.0.35 — Auto Doctor Quick/Full
13. 0.0.36 — Autopilot & Network Binding
14. 0.0.37 — Full MVP UI
15. 0.0.38 — Tray/Desktop Lifecycle
16. 0.0.39 — Crash Recovery/Support/Data
17. 0.0.40 — TUF Runtime Repository Trust & Catalog
18. 0.0.41 — Runtime Download/Activation/Rollback
19. 0.0.42 — MSI + Secure Portable Packaging
20. 0.0.43 — Release Candidate
21. 0.1.0 — Full MVP
```

## Immediate repository actions before implementation starts

1. Replace the stale repository `Z2P-ROADMAP.md` with the accepted master-plan through a docs-only PR.
2. Record the architecture-review and ecosystem findings in `Z2P-CRITICAL-REVIEW.md`.
3. Record accepted/rejected/deferred ecosystem decisions in `Z2P-DECISION-LOG.md`.
4. Correct `Z2P-ARCHITECTURE.md` thread-affinity claims.
5. Add P0 SQLite native remediation decision.
6. Add whole-app portable staging decision.
7. Add runtime capabilities, traffic impact, probe contexts and automation ownership decisions.
8. Update canonical roadmap/version mapping and implementation status.
9. Create the `0.0.24` implementation plan.
10. Do not mix `0.0.24` with UI/SQLite/portable/ecosystem feature changes in one PR.

## Forbidden before `0.0.29`

- user-facing real runtime Start;
- Auto Doctor candidate launch;
- profile apply to real runtime;
- tray real-runtime control;
- public installer/portable claiming runtime readiness;
- runtime updater activation.

## Forbidden before `0.0.42`

- public portable ZIP;
- direct elevated launch from portable package root;
- claims that portable has installer-equivalent trust without whole-app staging.

---

# 53. Final verdict

Редакция 6 сохраняет исходное направление Z2P, устраняет архитектурные иллюзии редакции 5 и добавляет ecosystem-опыт только через typed, bounded и evidence-backed contracts:

```text
1. Две serialization layers не равны одному Runtime authority.
2. Hash-проверка внутри уже запущенного portable app не защищает loader boundary.
3. WAL/SQLite abstraction не безопасна при уязвимом native engine.
4. Signed catalog недостаточен без формального update protocol/conformance.
```

Целевая архитектура:

```text
Installer
  → trusted Program Files app
                     \
Portable Bootstrapper
  → verify complete package
  → protected whole-app staging
                       \
                        → staged/installed elevated z2p.exe
                           → trusted Generic Host/configuration
                           → compile-time Application facades
                           → one RuntimeKernelLoop
                           → durable single-writer storage
                           → safe SQLite native runtime
                           → verified immutable Runtime Bundle
                           → STARTUPINFOEX + Job containment
                           → winws2 + WinDivert
```

Runtime updates:

```text
official upstream release
→ Z2P quarantine/review/test
→ TUF-approved target
→ resilient bounded download
→ safe extraction
→ immutable Candidate
→ controlled test
→ Current
→ PreviousKnownGood rollback
```

Release quality is proven by:

```text
code
+ invariants
+ architecture tests
+ native/storage/update attack corpus
+ Evidence Packs
+ signed/provenanced installer and portable artifacts
```

После этих коррекций план соответствует production-grade Windows privileged desktop architecture значительно лучше и не требует смены основных продуктовых решений:

```text
no Windows Service
no IPC
one UAC per app session
installer + portable
local-first
typed profiles
verified zapret2 runtime
bounded Auto Doctor
safe runtime updates
```

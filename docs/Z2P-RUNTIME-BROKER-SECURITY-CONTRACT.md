# Z2P v7 Runtime Broker security contract and threat model

Status: **v7-C contract baseline for #16; production broker implementation remains #17**.  
Scope: protocol, semantic validation, peer admission, serialized pre-authentication, replay, framing, bounds, lifecycle ingress and test seams.  
Out of scope: production `z2p-broker.exe`, Runtime Kernel relocation, real `winws2`, secure child creation (#18), immutable staging (#19).

## 1. Security objective

The Runtime Broker is a narrow privileged boundary. Its security property is **not** “the caller is the same Windows user”. Same-user access to a Named Pipe is only a coarse object-access filter and is not sufficient authorization for privileged runtime mutation.

A request may reach privileged lifecycle authority only after all applicable checks succeed:

1. local-only Named Pipe transport;
2. explicit DACL/object access;
3. actual connected client PID obtained from Windows, never trusted from serialized input;
4. process-object binding including PID reuse protection;
5. user/session/logon/integrity identity checks;
6. bounded serialized server challenge;
7. strict syntactic decode and explicit semantic validation of hostile wire values;
8. one-use HMAC proof bound to the negotiated protocol, sessions and peer identity;
9. request freshness, sequence/operation replay and RuntimeGeneration checks;
10. bounded ingress/concurrency admission;
11. mutation dispatch only to the existing `RuntimeKernelLoop` authority.

IPC is ingress and observation plumbing. It is never a second lifecycle authority.

## 2. Trust boundaries and assets

```text
untrusted / unelevated                                privileged

z2p.exe Control Plane
  UI / storage / network
          |
          | untrusted serialized bytes
          v
+------------------------------------+
| local pipe object + peer probe     |
| frame bounds + strict codecs       |
| semantic validation                |
| bounded challenge/HMAC admission   |
| replay/freshness/generation        |
| bounded queues/concurrency         |
+------------------------------------+
          |
          | validated typed requests only
          v
+------------------------------------+
| existing RuntimeKernelLoop         |
| sole lifecycle mutation authority  |
| generation / supersede / recovery  |
+------------------------------------+
          |
          v
privileged runtime ownership / child process
(#17/#18; not implemented by #16)
```

Protected assets include the elevated token, Runtime Kernel lifecycle authority, generation ordering, future runtime process/Job ownership, prepared bundle/plan identities, operation receipts/replay state, AppSession/BrokerSession continuity, bootstrap secret material, and broker CPU/memory/queue availability.

## 3. Attacker model

The connected peer and every serialized byte are untrusted. The model includes:

- unrelated or malicious same-user processes;
- forged/wrong PID values;
- PID reuse;
- wrong Windows/RDS session;
- wrong user SID, logon session or integrity level;
- stale AppSession/BrokerSession values;
- stolen, stale or replayed bootstrap material;
- stale/reused OperationId and request sequence;
- remote Named Pipe clients;
- malformed, oversized, partial or fragmented frames;
- undefined enum values and typed wrappers containing invalid values;
- protocol downgrade/version skew;
- connect/challenge/request flooding and queue exhaustion;
- slow writers/readers;
- disconnect during mutation and reconnect after mutation;
- broker shutdown races and application hard crash;
- attempts to obtain arbitrary process/shell/file/network/plugin/DLL/Lua authority.

Compromise or code injection into the exact admitted `z2p.exe` process is outside what IPC authentication can distinguish from that process itself. Installed-distribution executable/publisher verification is defense in depth, not a replacement for process/session/secret binding.

## 4. Windows transport and peer identity

### 4.1 Named Pipe object

#17 MUST create the control pipe with:

- `PIPE_REJECT_REMOTE_CLIENTS`;
- an explicit security descriptor/DACL, never the default descriptor;
- at most **2** concurrent pipe instances;
- at most **1** authenticated connection;
- no `Everyone` or anonymous authorization grant;
- only the initiating user/logon context needed as the coarse pipe-open filter.

Passing the DACL is not authorization. A same-logon malicious process may pass the object ACL and still MUST fail before receiving privileged request authority.

### 4.2 Actual peer identity

The broker MUST obtain the connected process ID with `GetNamedPipeClientProcessId`. A serialized PID is never authoritative.

Using the actual PID, #17 MUST hold a non-inheritable process handle with only required rights and establish at least:

- process creation time (`GetProcessTimes`);
- Windows session (`ProcessIdToSessionId` and/or consistent token session evidence);
- user SID (`TokenUser`);
- logon `AuthenticationId` (`TokenStatistics.AuthenticationId`);
- integrity level (`TokenIntegrityLevel`).

The resulting `BrokerPeerIdentity` is compared with the bootstrap-time `BrokerClientBinding` before challenge issuance and again before proof acceptance.

### 4.3 PID reuse

Numeric PID is insufficient. The binding is:

`PID + process creation FILETIME + retained process handle + Windows session + user SID + AuthenticationId + integrity`.

#17 MUST retain the verified process handle for the AppSession lifetime and must not silently reopen/rebind by numeric PID. A creation-time mismatch is a hard admission failure even when the PID number matches.

### 4.4 UAC/integrity

The expected Control Plane is the unelevated process identity captured for the session. Its integrity is part of the binding. The elevated broker and unelevated UI belonging to the same account does not authorize any other same-user process.

### 4.5 Impersonation

Client impersonation is not required for v7-C authentication. If #17 adds `ImpersonateNamedPipeClient` as an additional check, it MUST verify the call result, execute no privileged request after impersonation failure, and always restore the broker security context with `RevertToSelf`.

## 5. Bootstrap secret lifetime

The Control Plane creates fresh cryptographically random **32-byte / 256-bit AppSession material**.

The #16 broker-side contract has one explicit mutable secret owner: `BrokerPreAuthenticationSession` receives a `byte[]` by ownership transfer and does not create an additional stored copy. HMAC uses span-based `HMACSHA256.HashData`; the owned buffer is zeroed with `CryptographicOperations.ZeroMemory` on `Dispose`. Temporary expected-HMAC and serialized transcript buffers are also cleared where practical.

This is memory-hygiene hardening, **not** a claim that managed-memory zeroization makes a secret unrecoverable. Other caller copies, client-side copies, crash dumps, paging, runtime/OS behavior or copies created before ownership transfer are outside what `ZeroMemory` can prove.

Concrete Windows bootstrap-secret transfer/storage is intentionally #17 work. #17 MUST avoid treating command-line or environment visibility as a secret-storage boundary and MUST not invent a different handshake protocol.

A stolen secret alone is insufficient because admission also requires the exact expected process identity. Process identity alone is insufficient because a fresh HMAC proof is required.

## 6. Complete serialized pre-authentication protocol

### 6.1 Server challenge

After a local connection is accepted and the actual peer identity matches the expected client binding, the broker may issue a `BrokerChallenge` frame.

The frame is length-prefixed and capped at **1,024 bytes**. Its strict JSON body contains only:

- handshake protocol version;
- server-supported broker protocol range;
- `BrokerSessionId`;
- fresh **32-byte server nonce**;
- challenge lifetime in milliseconds, currently **3,000 ms**.

Unknown members, malformed JSON, invalid/empty BrokerSessionId, invalid nonce length, invalid protocol range/version, non-positive lifetime or lifetime above 3 seconds fail closed.

### 6.2 Client Hello

The client strictly decodes the challenge, negotiates a common version, creates a fresh **32-byte client nonce**, creates the HMAC proof and sends the normal v1 `Hello` request.

Before authentication, the framed Hello payload is capped at **4,096 bytes**, not the ordinary 65,536-byte maximum.

The broker performs, in order:

1. bounded frame decode;
2. strict JSON decode with unknown-member rejection;
3. `BrokerRequestSemanticValidator` on every decoded request representation;
4. lookup of the opaque server-side challenge handle;
5. atomic removal/consumption of that challenge;
6. monotonic challenge-expiry check;
7. peer identity revalidation;
8. AppSession/BrokerSession and protocol checks;
9. one HMAC verification;
10. authenticated-connection transition.

The client never supplies or chooses the server-side `BrokerChallengeHandle`; it is transport/session correlation state. It is not a privileged wire capability.

### 6.3 HMAC transcript

The HMAC-SHA256 transcript binds:

`selected protocol`
`|| client supported protocol range`
`|| server supported protocol range`
`|| AppSessionId`
`|| BrokerSessionId`
`|| actual client PID`
`|| process creation FILETIME`
`|| Windows session ID`
`|| user SID`
`|| logon AuthenticationId`
`|| integrity level`
`|| server nonce`
`|| client nonce`.

The proof key is the 32-byte AppSession secret. Proof comparison is constant-time.

### 6.4 One-use and retry policy

A challenge permits at most **one HMAC verification attempt**. It is removed atomically before attacker-controlled Hello/proof evaluation. Reuse of the same handle is `ReplayedChallenge`.

A bad first Hello does **not** poison the whole Broker/AppSession. It burns only that challenge. A fresh challenge can be issued if the session remains within the bounded challenge budgets below.

Across one `BrokerPreAuthenticationSession`, at most **8 challenges are ever issued**. This total session budget does not refill with elapsed time. After it is exhausted, challenge issuance returns `SessionBudgetExhausted`; recovery requires a new bootstrap/AppSession rather than an unbounded proof loop.

Successful admission atomically marks the session authenticated and invalidates all other outstanding challenges. Therefore parallel challenge races cannot create two authenticated authorities.

Reconnect after a released authenticated connection requires a fresh challenge while the same pre-auth session still has remaining issuance budget. Old challenge handles remain invalid.

## 7. Pre-authentication DoS bounds

The contract fixes all pre-auth challenge work:

| Pre-auth bound | v1 value |
|---|---:|
| maximum concurrent pipe connections | 2 |
| maximum authenticated connections | 1 |
| maximum live challenges | 2 |
| maximum challenge issues per pre-auth session | 8 |
| challenge frame payload | 1,024 bytes |
| pre-auth Hello payload | 4,096 bytes |
| challenge lifetime | 3 seconds |
| challenge issue rate | 2 / second |
| challenge issue burst | 4 |
| deterministic retry interval after rate exhaustion | 500 ms |
| HMAC verifications per challenge | 1 |
| maximum HMAC verification opportunities per pre-auth session | 8 |

Wrong process/session/SID/logon/integrity peers are rejected **before** challenge issuance and therefore do not consume challenge/HMAC budget. This does not replace the transport-level two-instance cap; #17 must enforce both layers.

For the expected peer, abandoned/bad challenges consume both issuance tokens and the non-refilling total session budget. At burst exhaustion the broker returns rate-limited behavior without allocating another challenge. The token bucket refills from monotonic elapsed time; the eight-issue session ceiling does not.

There is no unlimited proof oracle for one AppSession/BrokerSession: every proof attempt consumes a one-use challenge, live challenges are capped, creation is rate-limited, and no more than eight challenges can ever be issued by the pre-auth session.

## 8. Semantic validation boundary

Strict JSON syntax and typed record structs are not sufficient for hostile serialized input. `BrokerProtocolCodec.DecodeRequest` performs representation-level validation **before returning a successful `BrokerRequestEnvelope`**.

Current v1 invariants include:

- envelope protocol exactly v1;
- non-empty AppSessionId, BrokerSessionId and OperationId;
- positive request sequence;
- valid absolute timestamp representation and `DeadlineUtc > IssuedAtUtc`;
- request window no longer than 15 seconds;
- valid Hello protocol range and selected/envelope protocol membership;
- Hello body AppSessionId equals envelope AppSessionId;
- client nonce exactly 32 bytes;
- HMAC proof exactly 32 bytes;
- `RuntimeBundleId` contains a canonical lowercase `sha256:` + 64-hex digest;
- `PreparedBundleId != Guid.Empty`;
- canonical plan SHA-256 digest;
- `PreparedPlanId != Guid.Empty`;
- `RuntimeGeneration > 0` for generation-bearing requests;
- enum values such as `BrokerStopReason` must be defined;
- unknown request/body shapes fail closed.

No current v1 request body exposes variable user-controlled path/argument/string/collection authority. If a future version adds such a field, an explicit byte/count/character bound and semantic invariant are required before decode can return success.

Context-dependent authorization checks that require trusted broker state — expected AppSession/BrokerSession, actual Windows peer identity, current RuntimeGeneration, freshness-at-receipt and replay ledger state — occur after semantic decode but still before admission/dispatch.

## 9. Privileged protocol surface

Protocol v1 request families remain exactly:

1. `Hello` / negotiate;
2. `GetCapabilities`;
3. `GetRuntimeSnapshot`;
4. `PrepareBundle(RuntimeBundleId)`;
5. `PreparePlan(PreparedBundleId, PlanHash)`;
6. `StartPreparedPlan(PreparedPlanId, ExpectedGeneration)`;
7. `StopGeneration(RuntimeGeneration, Reason)`;
8. `ShutdownBroker`.

The server `BrokerChallenge` is a pre-auth security frame, not an additional privileged command.

The broker contract MUST NOT acquire any equivalent of:

- `RunProcess(path,args)` or `ExecuteCommand`;
- cmd/PowerShell/shell execution;
- arbitrary executable path or command-line authority;
- arbitrary file write/delete;
- `Download(url)` or arbitrary URL fetch;
- arbitrary plugin/DLL load;
- arbitrary Lua/script path supplied by the UI.

`RuntimeBundleId`, `PreparedBundleId` and `PreparedPlanId` are identities, not raw filesystem authority. Bundle/staging realization and artifact revalidation remain #19.

## 10. Framing, limits and backpressure

| Limit | v1 value |
|---|---:|
| length prefix | 4-byte little-endian signed length |
| ordinary maximum payload | 65,536 bytes |
| challenge payload | 1,024 bytes |
| pre-auth Hello payload | 4,096 bytes |
| concurrent pipe connections | 2 |
| authenticated connections | 1 |
| in-flight queries | 8 |
| concurrent mutations | 1 |
| ingress buffer | 32 requests |
| response buffer | 16 responses |
| operation ledger | 256 entries |
| operation ledger TTL | 2 minutes |
| authenticated request rate | 16 requests/second |
| authenticated request burst | 32 |
| frame header deadline | 2 seconds |
| frame body deadline | 5 seconds |
| ordinary query deadline | 5 seconds |
| maximum request lifetime | 15 seconds |
| response enqueue deadline | 1 second |
| response write deadline | 5 seconds |
| broker shutdown budget | 5 seconds |

Transport must not assume one read equals one message. Fragmented frames remain partial until complete. Zero/negative lengths fail. Oversize is rejected from the prefix before allocating the declared payload.

Full ingress/response buffers produce bounded busy/backpressure behavior. Slow writers/readers cannot create unbounded queues or indefinite frame ownership.

## 11. Timing model

Elapsed security/availability semantics use `TimeProvider.GetTimestamp()` / `GetElapsedTime()` so wall-clock adjustment does not refill budgets or extend/expire challenges unexpectedly. This applies to:

- challenge lifetime;
- challenge issuance rate/refill and retry interval;
- authenticated request-rate refill;
- operation-ledger TTL.

The total eight-challenge pre-auth session budget is counter-based and never refills from either wall clock or monotonic elapsed time.

Absolute UTC remains wire evidence for cross-process `IssuedAtUtc`/`DeadlineUtc` freshness. Windows process creation FILETIME remains absolute process-identity evidence. Those are intentionally separate from monotonic elapsed timers.

## 12. Operation, duplicate and replay semantics

`BrokerOperationId` is **not** an eternal or globally sufficient anti-replay token.

Replay safety is the combination of:

- AppSessionId/BrokerSessionId binding;
- strictly monotonic positive `RequestSequence` high-water mark within the AppSession;
- OperationId plus request fingerprint;
- bounded operation ledger;
- request freshness/deadline;
- RuntimeGeneration checks where applicable.

The operation ledger returns deterministic states:

- unseen operation + fresh sequence -> `New`;
- same OperationId/sequence/fingerprint in flight -> `DuplicateInFlight`;
- same tuple completed -> `DuplicateCompleted`;
- same OperationId with different sequence/fingerprint -> `Conflict`;
- unseen OperationId with sequence at/below high-water -> `Stale`;
- full bounded ledger with no expired entry -> `CapacityExceeded`.

Ledger TTL is storage retention, not permission to replay. The sequence high-water mark survives entry expiry, so an expired ledger row does not make an old sequence fresh again. Reconnect does not reset sequence or operation state.

## 13. Runtime generation and one-authority rule

Before `StartPreparedPlan`, the expected RuntimeGeneration is compared with current RuntimeGeneration. Lower is stale; higher is an invalid future reference; neither may dispatch.

Existing Runtime Kernel invariants remain authoritative:

- one `RuntimeKernelLoop` mutation authority;
- stale completions cannot mutate newer generations;
- Stop supersedes/cancels Start as already defined;
- lifecycle delivery remains guaranteed while observation may coalesce;
- typed cancellation remains;
- irreversible-boundary recovery semantics remain;
- `RuntimeAffinityOwner` remains authoritative until #17 performs the approved relocation.

External ingress may reject/queue, but it never transitions runtime lifecycle state directly.

## 14. Disconnect, reconnect and process lifetime

If disconnect occurs before validated dispatch, no lifecycle authority has been created. If disconnect occurs after Runtime Kernel accepts a mutation, transport does not roll back or repeat the mutation; Runtime Kernel owns completion/recovery.

Reconnect requires fresh pre-authentication against the same retained process identity and does not reset operation/generation state.

#17 MUST retain the admitted Control Plane process handle as the AppSession lifetime lease. On process death it must stop admitting requests and route any terminal cleanup through the sole Runtime Kernel authority, preserving existing Stop-supersedes-Start and recovery rules. The broker must not survive as an independent privileged product service.

## 15. Threat disposition summary

| Threat | Required disposition |
|---|---|
| same-user malicious process | DACL alone insufficient; exact process/token binding + fresh proof required |
| wrong/forged PID | use actual `GetNamedPipeClientProcessId`; serialized PID is not authority |
| PID reuse | creation FILETIME + retained process handle; no numeric-PID rebinding |
| wrong Windows session/user/logon/integrity | reject before challenge |
| stale AppSession/BrokerSession | reject before admission |
| bad first Hello | consumes only one challenge; bounded fresh rotation remains possible |
| replayed successful challenge | consumed handle; reject as replay |
| parallel bad/valid challenge race | at most one successful admission; success invalidates all outstanding handles |
| pre-auth flood | 2 live challenges, 8 total issues, 2/s rate, burst 4, 500 ms retry, max 2 connections |
| HMAC oracle abuse | one verification per challenge and maximum 8 proof opportunities per pre-auth session |
| stolen secret | still requires exact peer binding; publisher verification may add defense in depth |
| malformed/undefined wire values | strict syntax + semantic validation before successful decode |
| oversized frame | prefix rejection before declared-payload allocation |
| partial frame | no dispatch until complete bounded frame |
| protocol downgrade/skew | explicit range negotiation; both ranges + selected version bound in HMAC |
| duplicate/stale operation | session/sequence/fingerprint/ledger/freshness model; no second mutation |
| remote Named Pipe client | `PIPE_REJECT_REMOTE_CLIENTS` plus explicit DACL |
| queue/request flood | bounded connections, rates, queues, query/mutation concurrency |
| slow writer/reader | frame/write deadlines + bounded response queue |
| disconnect/reconnect | transport never becomes lifecycle authority |
| broker/app shutdown race | one terminal intent through Runtime Kernel |
| arbitrary command/path attempt | impossible in v1 typed privileged surface |

## 16. RED -> GREEN evidence requirements

The committed #16 tests cover or require:

- malformed/fragmented/oversized framing;
- unknown protocol/message/member rejection;
- semantic rejection of every currently representable invalid request body state;
- serialized challenge -> client decode -> proof -> serialized Hello -> broker decode -> admission;
- malformed/oversized/version-skew challenge rejection;
- bad first Hello followed by bounded successful rotation;
- monotonic challenge expiry and wall-clock-jump immunity;
- replay after successful admission;
- parallel attacker/expected-client challenge race with at most one success;
- wrong peer rejected without challenge-budget consumption;
- live-challenge capacity, token-bucket burst, deterministic retry interval and non-refilling eight-issue session ceiling;
- one HMAC attempt per challenge by consumption-before-proof-validation;
- owned bootstrap-secret zeroing on session disposal;
- monotonic request-rate and operation-ledger timing;
- deterministic duplicate/stale operation behavior;
- bounded ingress/response and one mutation at a time;
- stale RuntimeGeneration rejection;
- disconnect/reconnect FakeBroker evidence showing no second mutation authority;
- absence of a generic privileged execution primitive.

## 17. Residual risks / #17 obligations

Passing #16 contract tests does not prove a future Windows broker implementation secure. #17 still owns:

1. exact Named Pipe DACL/ACE construction and `PIPE_REJECT_REMOTE_CLIENTS` use;
2. safe Win32 PID/process/token/SID buffer handling;
3. retaining the verified process object without accidental PID rebinding;
4. concrete bootstrap-secret transfer/storage and avoidance of logs/command-line exposure;
5. implementing the fixed pre-auth connection/challenge budgets in the actual pipe pump;
6. request/read/write timeout enforcement and cancellation around real I/O;
7. optional installed executable/publisher verification policy;
8. process-death/shutdown integration with Runtime Kernel ownership.

#18 remains owner of fail-closed child creation/Job containment. #19 remains owner of immutable staging and privileged artifact revalidation. Neither concern expands the v1 broker RPC surface.

## 18. Official sources

Primary Microsoft sources checked for the contract:

- Named Pipe Security and Access Rights: https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
- CreateNamedPipe / `PIPE_REJECT_REMOTE_CLIENTS`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea
- GetNamedPipeClientProcessId: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid
- Process Handles and Identifiers: https://learn.microsoft.com/en-us/windows/win32/procthread/process-handles-and-identifiers
- OpenProcess: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess
- GetProcessTimes: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes
- ProcessIdToSessionId: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-processidtosessionid
- OpenProcessToken: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocesstoken
- GetTokenInformation: https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation
- TOKEN_INFORMATION_CLASS: https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class
- TOKEN_STATISTICS / `AuthenticationId`: https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_statistics
- Mandatory Integrity Control: https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control
- ImpersonateNamedPipeClient: https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-impersonatenamedpipeclient
- RevertToSelf: https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-reverttoself
- .NET `TimeProvider`: https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider
- .NET `HMACSHA256.HashData`: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hmacsha256.hashdata
- SHELLEXECUTEINFO / `runas` / `SEE_MASK_NOCLOSEPROCESS` (downstream evidence only): https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfoa

No blog is normative where Microsoft platform documentation specifies the required semantics.

# Z2P v7 Runtime Broker security contract and threat model

Status: **v7-C contract baseline for #16; production broker implementation remains #17**.  
Scope: protocol, admission, replay, framing, bounds, lifecycle ingress and test seams.  
Out of scope here: production `z2p-broker.exe`, runtime relocation, real `winws2`, secure child creation (#18), immutable staging (#19).

## 1. Security objective

The v7 Runtime Broker is a narrow privileged runtime boundary. Its security property is **not** “the caller is the same Windows user”. Same-user access to a named pipe is only a coarse object-access filter and is not sufficient authorization for privileged runtime mutation.

A request may reach privileged lifecycle authority only after all of the following have succeeded:

1. local-only transport admission;
2. explicit pipe DACL access check;
3. actual connected client PID acquisition from Windows, not from serialized input;
4. binding to the expected application process object identity;
5. Windows user/session/logon/integrity identity validation;
6. fresh per-connection challenge/response proof using per-AppSession secret material;
7. protocol/version validation;
8. frame/message validation and bounds;
9. request freshness, operation/replay and generation checks;
10. ingress/concurrency admission.

Only then may a validated mutation be dispatched to the existing `RuntimeKernelLoop` authority. IPC never becomes a second lifecycle authority.

## 2. Trust boundaries and assets

### Trust boundaries

```text
untrusted / unelevated                       privileged

z2p.exe Control Plane
  UI / config / storage / network
          |
          | untrusted serialized IPC
          v
+---------------------------+
| transport + admission     |
| frame bounds / protocol   |
| peer identity / HMAC      |
| replay / generation       |
| queue / deadline bounds   |
+---------------------------+
          |
          | validated typed requests only
          v
+---------------------------+
| Runtime Kernel authority  |
| one serialized mutation   |
| generation / supersede    |
| cancellation / recovery   |
+---------------------------+
          |
          v
privileged runtime ownership / child process
(#17/#18; not implemented by #16)
```

### Protected assets

- administrator/elevated token authority;
- Runtime Kernel lifecycle authority and its generation ordering;
- runtime process ownership and Job containment;
- prepared runtime bundle/plan identities;
- operation receipts and replay state;
- AppSession/BrokerSession continuity;
- bootstrap secret material and challenge nonces;
- broker availability and bounded memory/CPU/queue resources;
- future immutable runtime artifacts from #19.

## 3. Attacker model

The pipe peer and every serialized byte are untrusted. The model explicitly includes:

- unrelated same-user processes, including malicious same-user processes;
- a peer with a forged/incorrect PID in its serialized message;
- PID reuse after an earlier process exits;
- another Windows/RDS session;
- another user/SID or logon session;
- stale AppSession/BrokerSession material;
- stolen, stale or replayed bootstrap material;
- stolen/stale OperationId or request sequence;
- remote named-pipe clients;
- malformed, oversized, partial and fragmented messages;
- duplicate/stale requests and protocol downgrade/version skew;
- request flooding, ingress exhaustion, slow writers and slow readers;
- disconnect during mutation and reconnect after mutation;
- broker shutdown races and application hard crashes;
- attempts to turn the broker into arbitrary command/process/file/download/plugin/DLL/Lua execution authority.

Compromise/injection of the exact admitted `z2p.exe` process is outside what an IPC authentication protocol can distinguish from that process itself. Installed-distribution executable/publisher verification is therefore defense in depth, not a substitute for process/session/secret binding.

## 4. Windows transport and admission requirements

### 4.1 Named Pipe object

#17 MUST create the control pipe with:

- `PIPE_REJECT_REMOTE_CLIENTS`;
- an explicit security descriptor/DACL, never the default descriptor;
- a bounded instance count matching the contract (`2` total, at most `1` authenticated connection);
- no `Everyone` or anonymous authorization grant;
- the initiating logon SID/user context permitted only as the coarse pipe-open filter required for the unelevated client to connect.

The DACL is **not** final authorization. A malicious process in the same logon session may pass the DACL and must still fail admission unless it is the expected process identity and possesses the fresh bootstrap proof.

Microsoft documents that a named pipe security descriptor controls access to both ends, that the default descriptor grants broader access including Everyone/anonymous read access, and that a logon SID can be used to restrict access to a login/session context. Microsoft also documents that `PIPE_REJECT_REMOTE_CLIENTS` automatically rejects remote clients.

### 4.2 Actual client process identity

The broker MUST call `GetNamedPipeClientProcessId` on the connected server-side pipe handle. A PID transmitted by the peer is never authoritative.

Using that actual PID, #17 MUST open a non-inheritable process handle with only the rights needed for identity/lifetime checks (normally `PROCESS_QUERY_LIMITED_INFORMATION` plus synchronization/wait semantics as required by the final implementation). From that process/token it MUST establish at least:

- process creation time (`GetProcessTimes`);
- Windows session (`ProcessIdToSessionId` and/or token session information, with consistency checked);
- user SID (`TokenUser`);
- logon authentication LUID (`TokenStatistics.AuthenticationId`);
- token integrity level (`TokenIntegrityLevel`).

The resulting `BrokerPeerIdentity` is compared with the bootstrap-time `BrokerClientBinding`.

### 4.3 PID reuse

Expected PID alone is insufficient. Windows process identifiers identify a process while that process exists; process handles remain references to the process object until closed. Therefore #17 MUST bind:

`PID + process creation time + held process handle + Windows session + user SID + AuthenticationId + integrity`.

The handle is retained for the broker/AppSession lifetime. Creation-time mismatch is a hard admission failure even when the numeric PID is equal. Closing/reopening by numeric PID is not allowed to silently rebind the session to a replacement process.

### 4.4 UAC/integrity boundary

The expected client is the unelevated Control Plane identity captured before broker admission. Its integrity level is part of the binding and a different integrity context is rejected. The broker itself is elevated; the client is not authorized merely because both tokens correspond to the same account.

Microsoft Mandatory Integrity Control is additive to DACL checks and distinguishes standard medium-integrity and elevated high-integrity contexts. The broker must not treat UAC elevation as evidence that a pipe peer is trusted.

### 4.5 Impersonation

Client impersonation is **not required** for v7-C authentication. The primary design uses the actual client process handle and token. If #17 uses `ImpersonateNamedPipeClient` for an additional check, it MUST:

- call it only on the server end after a client message has been read;
- check the return value;
- execute no privileged client request after a failed impersonation call;
- always call `RevertToSelf` after the check.

This avoids accidentally continuing under the broker's privileged token when impersonation failed.

## 5. Bootstrap and authentication handshake

### 5.1 Session identities

Typed identities are:

- `BrokerProtocolVersion` / `BrokerProtocolRange`;
- `AppSessionId`;
- `BrokerSessionId`;
- `BrokerOperationId`;
- `RequestSequence`;
- `RuntimeGeneration`;
- `RuntimeBundleId`;
- `PreparedBundleId`;
- `PreparedPlanId`;
- canonical `Sha256Digest`;
- `LogonSessionId` (`AuthenticationId` LUID representation).

IDs are opaque authority-correlation values. They do not confer privilege on their own.

### 5.2 Bootstrap material

The Control Plane creates a fresh cryptographically random **32-byte AppSession secret**. #17 must transfer only the minimum bootstrap context necessary to the elevated broker and must avoid treating command-line/environment visibility as a security boundary.

A stolen secret alone is insufficient because admission also requires the exact Windows process binding. Conversely, process identity alone is insufficient because a fresh HMAC proof is required.

Installed distribution SHOULD additionally verify the expected Control Plane executable identity/publisher before completing admission. The exact Authenticode/publisher pinning policy is a #17 packaging/implementation decision and must fail closed when configured.

### 5.3 Challenge/response

For each connection attempt the broker creates:

- `BrokerSessionId` (stable for the broker process/session);
- a fresh 32-byte challenge nonce;
- challenge expiry of **3 seconds**.

The client supplies:

- `AppSessionId`;
- supported protocol range;
- fresh 32-byte client nonce;
- HMAC-SHA256 proof.

The proof transcript binds:

`selected protocol || AppSessionId || BrokerSessionId || actual PID || process creation time || Windows session || broker nonce || client nonce`.

The proof key is the 32-byte AppSession secret. Verification uses constant-time comparison. A challenge is consumed atomically exactly once; replay or expiry is rejected.

A new connection/reconnect receives a new challenge. Reconnect does **not** reset the RuntimeGeneration, request-sequence high-water mark or operation ledger. Therefore reconnect cannot replay a completed mutation as a new authority.

## 6. Protocol surface

Protocol v1 exposes exactly these request families:

1. `Hello` / negotiate;
2. `GetCapabilities`;
3. `GetRuntimeSnapshot`;
4. `PrepareBundle(RuntimeBundleId)`;
5. `PreparePlan(PreparedBundleId, PlanHash)`;
6. `StartPreparedPlan(PreparedPlanId, ExpectedGeneration)`;
7. `StopGeneration(RuntimeGeneration, Reason)`;
8. `ShutdownBroker`.

The privileged protocol contains no generic execution/file/network/plugin primitive.

### Explicitly forbidden surface

The broker contract MUST NOT acquire any equivalent of:

- `RunProcess(path,args)`;
- `ExecuteCommand`;
- shell/cmd/PowerShell execution;
- arbitrary executable path or command-line authority;
- arbitrary file write/delete;
- `Download(url)` or arbitrary URL fetch;
- arbitrary plugin/DLL load;
- arbitrary Lua/script path supplied by the Control Plane.

Unknown message kinds and unknown JSON members are rejected. This is intentional: an attacker cannot smuggle a future/privileged field into an older decoder and have it silently ignored.

`RuntimeBundleId`, `PreparedBundleId` and `PreparedPlanId` are identity-based capabilities. They are not raw filesystem paths. Bundle/staging realization is deferred to #19; the broker must later revalidate the identity before privileged execution.

## 7. Framing, limits and backpressure

The current v1 contract fixes:

| Limit | Value |
|---|---:|
| length prefix | 4-byte little-endian signed length |
| maximum payload/frame body | 65,536 bytes |
| maximum concurrent pipe connections | 2 |
| maximum authenticated connections | 1 |
| maximum in-flight queries | 8 |
| maximum concurrent mutations | 1 |
| ingress buffer | 32 requests |
| response buffer | 16 responses |
| operation ledger | 256 entries |
| operation ledger TTL | 2 minutes |
| sustained request rate target | 16 requests/second |
| burst capacity | 32 requests |
| handshake deadline | 3 seconds |
| frame header deadline | 2 seconds |
| frame body deadline | 5 seconds |
| ordinary query deadline | 5 seconds |
| maximum request lifetime | 15 seconds |
| response enqueue deadline | 1 second |
| response write deadline | 5 seconds |
| broker shutdown budget | 5 seconds |

Transport must not assume one `ReadFile`/stream read equals one logical message. The length-prefixed decoder returns `NeedMoreData` without consuming partial input. Invalid zero/negative lengths are rejected. Lengths above 65,536 bytes are rejected **before payload allocation**.

A full ingress/response buffer results in backpressure/rejection; it must never become an unbounded allocation path. A slow reader cannot cause infinite response accumulation. A slow writer cannot hold a frame forever: header/body deadlines terminate the connection. A request whose own deadline or maximum lifetime has expired is rejected before lifecycle dispatch.

The v1 constants `MaxRequestsPerSecond=16` and `RequestBurstCapacity=32` are normative bounds for #17's connection admission/rate gate. #16 does not implement the production pipe pump; #17 must enforce them rather than treating them as diagnostics only.

## 8. Operation, duplicate and replay semantics

Within an AppSession, request sequence is monotonically increasing. The broker maintains a bounded operation ledger keyed by `BrokerOperationId` and request fingerprint.

- first unseen operation with a fresh sequence -> `New`;
- same OperationId + same sequence + same fingerprint while executing -> `DuplicateInFlight`;
- same tuple after completion -> `DuplicateCompleted`;
- same OperationId with different sequence/fingerprint -> `Conflict`;
- unseen OperationId with sequence at/below the high-water mark -> `Stale`;
- ledger full with no expired entry -> `CapacityExceeded` / busy; no unbounded growth.

Duplicate mutation requests are never executed twice. `DuplicateCompleted` means “the earlier operation already owns the mutation outcome”; a reconnecting client must reconcile via the operation response if retained by #17 and/or `GetRuntimeSnapshot`, not re-run the mutation.

The ledger TTL is not a replay window: the monotonically increasing sequence high-water mark survives entry expiry for the AppSession, so expiry of an old entry does not make its old sequence fresh again.

## 9. Runtime generation and one-authority rule

Lifecycle mutation stays serialized through existing Runtime Kernel authority.

Before `StartPreparedPlan`, the broker compares `ExpectedGeneration` with the current RuntimeGeneration. A lower generation is stale; a higher generation is an invalid future reference; both fail before dispatch.

`StopGeneration` names the generation it intends to stop. Existing Runtime Kernel rules remain authoritative:

- one `RuntimeKernelLoop` lifecycle authority;
- stale effect completions cannot mutate a newer generation;
- Stop can supersede/cancel an in-flight Start;
- lifecycle delivery is guaranteed while observations may be coalesced;
- typed cancellation is retained;
- cancellation after an irreversible boundary keeps recovery-required semantics;
- `RuntimeAffinityOwner` remains the process/ownership affinity authority until relocated by #17.

IPC admission may reject or queue a request, but it may not independently transition runtime state.

## 10. Disconnect, reconnect and lifetime

### Disconnect before dispatch

If the client disconnects before a validated mutation is handed to Runtime Kernel, the request has no lifecycle authority and is discarded/rejected.

### Disconnect after dispatch

Once Runtime Kernel has accepted a mutation, pipe disconnect does **not** roll it back, re-run it or create transport-owned state. Runtime Kernel continues to its deterministic terminal/recovery state. The operation ledger remains correlated with that operation. Reconnection reconciles through snapshot/receipt semantics.

### Reconnect

Reconnect requires a fresh challenge and the same AppSession process binding while the held process handle is still valid. It does not reset operation or generation state.

### Application hard crash

#17 MUST retain a handle to the admitted Control Plane process as a lifetime lease. If that process object becomes signaled/exits:

1. stop admitting new requests;
2. collapse any simultaneous explicit shutdown/app-death race to one terminal shutdown intent;
3. route required runtime stop/cleanup through the sole Runtime Kernel authority;
4. observe existing Stop-supersedes-Start and irreversible-boundary recovery semantics;
5. exit the session-scoped broker within the bounded shutdown policy or surface the existing recovery path.

The broker must not remain behind as an independent privileged product service.

## 11. Threats and mitigations

| Threat | Required disposition |
|---|---|
| same-user unrelated/malicious process | DACL is insufficient; reject unless exact process object + token/session identity + fresh HMAC proof match |
| wrong serialized PID | ignored as authority; use `GetNamedPipeClientProcessId` |
| PID reuse | creation-time check + retained process handle; no numeric-PID rebinding |
| wrong Windows session | reject |
| wrong user/SID | reject |
| wrong logon session | reject by AuthenticationId/logon identity |
| stale AppSession | reject |
| replayed bootstrap proof/challenge | fresh nonce, 3 s expiry, atomic single-use challenge |
| stolen secret | still requires exact process binding; installed publisher verification is defense in depth |
| stale/stolen OperationId | fingerprint + sequence + bounded high-water-mark ledger |
| remote pipe client | `PIPE_REJECT_REMOTE_CLIENTS` plus DACL |
| malformed frame/JSON | fail closed; close/reject connection as appropriate |
| oversized frame | reject from prefix before allocation |
| fragmented message | bounded incremental framing; no partial dispatch |
| duplicate request | deterministic duplicate status; no second mutation |
| stale request | request deadline + max lifetime + sequence ledger |
| downgrade/version skew | explicit range negotiation; unknown major rejected; protocol bound into authentication transcript |
| flooding/queue exhaustion | connection/query/mutation/buffer/rate bounds; fail busy/close rather than allocate unboundedly |
| slow writer | header/body deadlines |
| slow reader | bounded response buffer + enqueue/write deadlines |
| disconnect during mutation | Runtime Kernel remains owner; no transport rollback or second state machine |
| reconnect after mutation | fresh auth challenge; existing ledger/generation retained |
| broker shutdown race | one serialized terminal shutdown intent through Runtime Kernel |
| app hard crash | held process-handle lease triggers bounded broker shutdown |
| arbitrary path/argument attempt | impossible in typed v1 request surface; unknown members/messages rejected |

## 12. RED/GREEN evidence contract

RED was deliberately committed before production contract implementation. The RED suite requires:

- oversized frame rejection;
- fragmented/malformed input rejection;
- unknown message/field rejection;
- unknown protocol rejection;
- stale operation and deterministic duplicate handling;
- no generic privileged process/file/network/script request type;
- wrong process/PID reuse/session/SID/logon/integrity rejection;
- invalid/replayed/expired bootstrap proof rejection;
- bounded ingress and response backpressure;
- one mutation at a time and bounded query concurrency;
- stale generation rejection;
- dependency-light Contracts assembly.

The test/fake seam may model admission and dispatch in memory. It must not implement a second Runtime Kernel or real `winws2` process.

## 13. Residual risks / #17 obligations

This contract does **not** claim the future broker is secure merely because the tests pass. Remaining implementation risks include:

1. constructing the exact pipe DACL/ACE rights incorrectly;
2. mishandling Windows token buffers/SIDs or comparing textual/normalized identities incorrectly;
3. accidentally reopening by PID and losing the retained process-object binding;
4. leaking bootstrap material through logs, diagnostics, command line or crash dumps;
5. omitting the normative request-rate gate from the real pipe pump;
6. authentication/operation state being reset incorrectly on reconnect;
7. shutdown/app-death watchers racing Runtime Kernel ownership;
8. installed-distribution executable/publisher verification policy not yet pinned;
9. immutable bundle/plan storage and broker-side artifact revalidation are not solved until #19;
10. child-process fail-closed creation/Job containment is not solved until #18.

These are implementation obligations for downstream milestones, not reasons to widen the privileged RPC surface.

## 14. Official Microsoft sources checked for #16

Primary Win32 sources, checked 2026-09-10:

- Named Pipe Security and Access Rights: https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
- CreateNamedPipe / `PIPE_REJECT_REMOTE_CLIENTS`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea
- GetNamedPipeClientProcessId: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid
- Process Handles and Identifiers: https://learn.microsoft.com/en-us/windows/win32/procthread/process-handles-and-identifiers
- OpenProcess: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess
- GetProcessTimes: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes
- ProcessIdToSessionId: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-processidtosessionid
- OpenProcessToken: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocesstoken
- GetTokenInformation: https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation
- TOKEN_INFORMATION_CLASS (`TokenUser`, `TokenStatistics`, `TokenSessionId`, `TokenIntegrityLevel`, `TokenLogonSid`): https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class
- TOKEN_STATISTICS / `AuthenticationId`: https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_statistics
- Mandatory Integrity Control: https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control
- TOKEN_MANDATORY_LABEL: https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_mandatory_label
- ImpersonateNamedPipeClient: https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-impersonatenamedpipeclient
- RevertToSelf: https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-reverttoself
- SHELLEXECUTEINFO / `runas` / `SEE_MASK_NOCLOSEPROCESS` (downstream #17 launch/lifetime evidence): https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfoa

No blog is normative for this security contract when Win32 documentation covers the semantic.

using System;

namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// A typed side-effect instruction emitted by the
/// <see cref="RuntimeKernelReducer"/> and consumed by
/// <see cref="RuntimeKernelLoop"/>. Each intent carries the
/// identity pair (<see cref="OperationId"/>,
/// <see cref="Generation"/>) used by the loop to match
/// asynchronous completions back to their in-flight
/// operation, plus the deadline and cancellation reason
/// captured at the moment the intent was created.
/// </summary>
/// <param name="OperationId">
/// Identifier of the kernel operation the effect belongs to.
/// </param>
/// <param name="Generation">
/// Generation of the kernel state at the moment the intent was
/// emitted. A completion whose generation is older than the
/// loop's current generation is treated as a stale result and
/// rejected by the reducer.
/// </param>
/// <param name="Kind">
/// The kind of side-effect the loop must dispatch.
/// </param>
/// <param name="Payload">
/// Optional kind-specific payload. For
/// <see cref="RuntimeEffectKind.StartProcess"/> the payload
/// must be a <see cref="Hosting.RuntimeProcessStartContext"/>;
/// for <see cref="RuntimeEffectKind.StopProcess"/> the payload
/// is reserved for diagnostics and may be <c>null</c>.
/// </param>
/// <param name="RequestedAtUtc">
/// Wall-clock timestamp captured by the reducer when the
/// intent was created. Persisted with the intent so downstream
/// surfaces can report how long the operation has been
/// outstanding; it MUST NOT be used for duration logic.
/// </param>
/// <param name="Deadline">
/// Optional wall-clock instant by which the effect is expected
/// to complete. <c>null</c> means "no explicit deadline".
/// Duration enforcement is the runner's responsibility; it
/// uses a monotonic <see cref="TimeProvider"/> to detect
/// expiry.
/// </param>
/// <param name="CancellationReason">
/// Cancellation reason recorded at intent creation time. The
/// runner may use it as a tie-breaker when neither the outer
/// cancellation token nor the deadline has clearly caused the
/// cancellation, and the reducer uses it to populate the
/// resulting <see cref="RuntimeKernelState"/>.
/// </param>
public sealed record RuntimeEffectIntent(
    RuntimeOperationId OperationId,
    RuntimeGeneration Generation,
    RuntimeEffectKind Kind,
    object? Payload,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? Deadline,
    RuntimeCancellationReason CancellationReason);

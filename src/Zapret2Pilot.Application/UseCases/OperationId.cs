namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Correlation identifier for a single use-case invocation. Every
/// request record carries an <see cref="OperationId"/> so that
/// observability and audit layers can group log entries, telemetry
/// and progress events for a single logical operation even when the
/// caller has dispatched it through async fan-out.
/// </summary>
/// <param name="Value">The unique identifier value.</param>
public sealed record OperationId(Guid Value);

using System;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Runtime.Transactions;

/// <summary>
/// Outcome of a state transition on a <see cref="RuntimeTransaction"/>.
/// The result is a validation/factory record: callers must use one of the
/// static factory methods (<see cref="Committed"/>, <see cref="RolledBack"/>
/// or <see cref="Failed(ErrorInfo)"/>), and the private constructor enforces
/// the invariant that an <see cref="ErrorInfo"/> is present if and only if
/// <see cref="State"/> is <see cref="RuntimeTransactionState.Failed"/>.
/// </summary>
public sealed record class RuntimeTransactionResult
{
    private RuntimeTransactionResult(RuntimeTransactionState state, ErrorInfo? error)
    {
        if (state == RuntimeTransactionState.Failed && error is null)
        {
            throw new ArgumentException(
                "Failed transaction result must carry an error.",
                nameof(error));
        }

        if (state != RuntimeTransactionState.Failed && error is not null)
        {
            throw new ArgumentException(
                "Non-failed transaction result must not carry an error.",
                nameof(error));
        }

        State = state;
        Error = error;
    }

    /// <summary>
    /// Terminal state the transaction reached.
    /// </summary>
    public RuntimeTransactionState State { get; }

    /// <summary>
    /// Error describing the failure. Non-null only when
    /// <see cref="State"/> is <see cref="RuntimeTransactionState.Failed"/>;
    /// <c>null</c> for successful outcomes.
    /// </summary>
    public ErrorInfo? Error { get; }

    /// <summary>
    /// Successful commit outcome. The transaction is in
    /// <see cref="RuntimeTransactionState.Committed"/>.
    /// </summary>
    public static RuntimeTransactionResult Committed()
    {
        return new RuntimeTransactionResult(
            RuntimeTransactionState.Committed,
            error: null);
    }

    /// <summary>
    /// Successful rollback outcome. The transaction is in
    /// <see cref="RuntimeTransactionState.RolledBack"/>.
    /// </summary>
    public static RuntimeTransactionResult RolledBack()
    {
        return new RuntimeTransactionResult(
            RuntimeTransactionState.RolledBack,
            error: null);
    }

    /// <summary>
    /// Failure outcome. The transaction is in
    /// <see cref="RuntimeTransactionState.Failed"/> and the supplied
    /// <paramref name="error"/> describes what went wrong.
    /// </summary>
    public static RuntimeTransactionResult Failed(ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error, nameof(error));

        return new RuntimeTransactionResult(
            RuntimeTransactionState.Failed,
            error);
    }
}

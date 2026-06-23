using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;

namespace Zapret2Pilot.Runtime.Transactions;

/// <summary>
/// A single transaction in the Runtime Kernel transaction model. A
/// transaction tracks a unit of runtime state change that was proposed by
/// the manager and can either be <see cref="Commit"/>ted (made permanent)
/// or <see cref="Rollback"/>n (undone to the pre-transaction state).
///
/// Transactions are created exclusively by
/// <see cref="IRuntimeTransactionManager"/>; the constructor is
/// <c>internal</c> to keep creation under the manager's control.
/// </summary>
public sealed class RuntimeTransaction
{
    internal RuntimeTransaction(
        RuntimeTransactionId id,
        CompiledZapretPlan? plan,
        RuntimeTransactionState initialState)
    {
        Id = id;
        Plan = plan;
        State = initialState;
    }

    /// <summary>
    /// Stable identifier of this transaction. Assigned by the manager at
    /// creation time and never changed.
    /// </summary>
    public RuntimeTransactionId Id { get; }

    /// <summary>
    /// Current state of the transaction. Mutated only by
    /// <see cref="Commit"/>, <see cref="Rollback"/> and
    /// <see cref="MarkFailed"/>.
    /// </summary>
    public RuntimeTransactionState State { get; private set; }

    /// <summary>
    /// The compiled plan this transaction carries, or <c>null</c> for
    /// transactions that do not transport a plan (notably
    /// <c>BeginStop</c>).
    /// </summary>
    public CompiledZapretPlan? Plan { get; }

    /// <summary>
    /// Attempts to commit the transaction. Valid only when
    /// <see cref="State"/> is <see cref="RuntimeTransactionState.Active"/>;
    /// on success the transaction transitions
    /// <c>Active -&gt; Committing -&gt; Committed</c>.
    /// </summary>
    /// <returns>
    /// <see cref="RuntimeTransactionResult.Committed"/> on success, or
    /// <see cref="RuntimeTransactionResult.Failed(ErrorInfo)"/> when the
    /// transaction is not in <see cref="RuntimeTransactionState.Active"/>.
    /// </returns>
    public RuntimeTransactionResult Commit()
    {
        if (State != RuntimeTransactionState.Active)
        {
            return RuntimeTransactionResult.Failed(new ErrorInfo(
                "RuntimeTransactionInvalidState",
                $"Cannot commit transaction {Id.Value}: expected state Active, was {State}.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        State = RuntimeTransactionState.Committing;
        State = RuntimeTransactionState.Committed;

        return RuntimeTransactionResult.Committed();
    }

    /// <summary>
    /// Attempts to roll back the transaction. Valid only when
    /// <see cref="State"/> is <see cref="RuntimeTransactionState.Active"/>;
    /// on success the transaction transitions
    /// <c>Active -&gt; RollingBack -&gt; RolledBack</c>.
    /// </summary>
    /// <returns>
    /// <see cref="RuntimeTransactionResult.RolledBack"/> on success, or
    /// <see cref="RuntimeTransactionResult.Failed(ErrorInfo)"/> when the
    /// transaction is not in <see cref="RuntimeTransactionState.Active"/>.
    /// </returns>
    public RuntimeTransactionResult Rollback()
    {
        if (State != RuntimeTransactionState.Active)
        {
            return RuntimeTransactionResult.Failed(new ErrorInfo(
                "RuntimeTransactionInvalidState",
                $"Cannot roll back transaction {Id.Value}: expected state Active, was {State}.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        State = RuntimeTransactionState.RollingBack;
        State = RuntimeTransactionState.RolledBack;

        return RuntimeTransactionResult.RolledBack();
    }

    /// <summary>
    /// Activates the transaction, transitioning it from
    /// <see cref="RuntimeTransactionState.Pending"/> to
    /// <see cref="RuntimeTransactionState.Active"/>. Intended for use by
    /// the manager immediately after constructing the transaction in the
    /// <c>Pending</c> state and applying the corresponding runtime state
    /// changes.
    /// </summary>
    internal void Activate()
    {
        if (State != RuntimeTransactionState.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot activate transaction {Id.Value}: expected state Pending, was {State}.");
        }

        State = RuntimeTransactionState.Active;
    }

    /// <summary>
    /// Marks the transaction as terminally failed. Intended for use by the
    /// manager when an unrecoverable error occurs (for example, a rollback
    /// action threw an exception).
    /// </summary>
    /// <param name="error">Error describing the failure.</param>
    /// <returns>
    /// <see cref="RuntimeTransactionResult.Failed(ErrorInfo)"/> with
    /// <paramref name="error"/>.
    /// </returns>
    internal RuntimeTransactionResult MarkFailed(ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error, nameof(error));

        State = RuntimeTransactionState.Failed;

        return RuntimeTransactionResult.Failed(error);
    }
}

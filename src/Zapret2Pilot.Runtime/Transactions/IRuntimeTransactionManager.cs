using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;

namespace Zapret2Pilot.Runtime.Transactions;

/// <summary>
/// Coordinates runtime state changes through explicit transactions.
///
/// All three <c>Begin*</c> operations create a <see cref="RuntimeTransaction"/>
/// in the <see cref="RuntimeTransactionState.Active"/> state, apply the
/// corresponding change to the runtime, and record a rollback action so
/// that <see cref="Rollback"/> can undo it. The caller is then responsible
/// for either <see cref="Commit"/>ting the transaction (making the change
/// permanent) or <see cref="Rollback"/>ing it (restoring the previous
/// state).
///
/// In 0.0.16 the manager is intentionally simple: it is single-threaded by
/// convention, no explicit locking is performed, and the <c>Begin*</c>
/// operations only mutate manager-local state (no process launch, no I/O).
/// Thread-safety will be hardened in a later milestone.
/// </summary>
public interface IRuntimeTransactionManager
{
    /// <summary>
    /// Begins a transaction that starts the runtime with the supplied plan.
    /// </summary>
    /// <param name="plan">Compiled plan to install as the current plan.</param>
    /// <returns>
    /// A new <see cref="RuntimeTransaction"/> in
    /// <see cref="RuntimeTransactionState.Active"/> state, or a failure when
    /// the runtime is already running or <paramref name="plan"/> is null.
    /// </returns>
    Result<RuntimeTransaction> BeginStart(CompiledZapretPlan plan);

    /// <summary>
    /// Begins a transaction that stops the runtime.
    /// </summary>
    /// <returns>
    /// A new <see cref="RuntimeTransaction"/> in
    /// <see cref="RuntimeTransactionState.Active"/> state, or a failure when
    /// the runtime is not currently running.
    /// </returns>
    Result<RuntimeTransaction> BeginStop();

    /// <summary>
    /// Begins a transaction that replaces the current plan with a new one
    /// while the runtime is running.
    /// </summary>
    /// <param name="newPlan">Compiled plan to install.</param>
    /// <returns>
    /// A new <see cref="RuntimeTransaction"/> in
    /// <see cref="RuntimeTransactionState.Active"/> state, or a failure when
    /// the runtime is not running or <paramref name="newPlan"/> is null.
    /// </returns>
    Result<RuntimeTransaction> BeginApply(CompiledZapretPlan newPlan);

    /// <summary>
    /// Commits a transaction previously returned by one of the
    /// <c>Begin*</c> methods.
    /// </summary>
    /// <param name="transaction">Transaction to commit.</param>
    /// <returns>
    /// <see cref="Unit"/> on success, or a failure when the transaction is
    /// unknown to this manager or its own commit transition failed.
    /// </returns>
    Result<Unit> Commit(RuntimeTransaction transaction);

    /// <summary>
    /// Rolls back a transaction previously returned by one of the
    /// <c>Begin*</c> methods. Executes the recorded rollback actions in
    /// LIFO order and transitions the transaction to
    /// <see cref="RuntimeTransactionState.RolledBack"/>.
    /// </summary>
    /// <param name="transaction">Transaction to roll back.</param>
    /// <returns>
    /// <see cref="Unit"/> on success, or a failure when the transaction is
    /// unknown to this manager or one of its rollback actions threw.
    /// </returns>
    Result<Unit> Rollback(RuntimeTransaction transaction);
}

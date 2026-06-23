using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;

namespace Zapret2Pilot.Runtime.Transactions;

/// <summary>
/// In-memory implementation of <see cref="IRuntimeTransactionManager"/> for
/// 0.0.16. Tracks whether the runtime is running, the currently installed
/// <see cref="CompiledZapretPlan"/>, and the rollback actions of every
/// transaction currently in flight.
///
/// <para>
/// <b>Thread-safety (0.0.16):</b> this implementation is intentionally
/// simple and does not perform explicit locking. Callers must serialize
/// access to the manager from a single thread. Explicit synchronization
/// will be added in a later milestone; the public surface (this class and
/// the interface) is shaped so that change can be made without breaking
/// callers.
/// </para>
/// </summary>
public sealed class RuntimeTransactionManager : IRuntimeTransactionManager
{
    private readonly Dictionary<RuntimeTransaction, Stack<Action>> activeTransactions = new();

    private bool isRunning;
    private CompiledZapretPlan? currentPlan;

    /// <summary>
    /// Creates a new <see cref="RuntimeTransactionManager"/> in the
    /// stopped state with no current plan.
    /// </summary>
    public RuntimeTransactionManager()
    {
    }

    /// <summary>
    /// Test-only observation hook. Returns whether the manager currently
    /// considers the runtime as running. Exposed as <c>internal</c> so it
    /// is not part of the public API surface.
    /// </summary>
    internal bool IsRunning => isRunning;

    /// <summary>
    /// Test-only observation hook. Returns the compiled plan the manager
    /// currently considers installed, or <c>null</c> when the runtime is
    /// not running. Exposed as <c>internal</c> so it is not part of the
    /// public API surface.
    /// </summary>
    internal CompiledZapretPlan? CurrentPlan => currentPlan;

    public Result<RuntimeTransaction> BeginStart(CompiledZapretPlan plan)
    {
        if (plan is null)
        {
            return Result.Failure<RuntimeTransaction>(new ErrorInfo(
                "RuntimeTransactionStartPlanNull",
                "Cannot begin a Start transaction: plan is null.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        if (isRunning)
        {
            return Result.Failure<RuntimeTransaction>(new ErrorInfo(
                "RuntimeAlreadyRunning",
                "Cannot begin a Start transaction: the runtime is already running.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        RuntimeTransaction transaction = new(
            GenerateTransactionId(),
            plan,
            RuntimeTransactionState.Pending);

        Stack<Action> rollbackActions = new();
        rollbackActions.Push(RestoreStoppedState);
        activeTransactions.Add(transaction, rollbackActions);

        isRunning = true;
        currentPlan = plan;

        transaction.Activate();

        return Result.Success(transaction);
    }

    public Result<RuntimeTransaction> BeginStop()
    {
        if (!isRunning)
        {
            return Result.Failure<RuntimeTransaction>(new ErrorInfo(
                "RuntimeNotRunning",
                "Cannot begin a Stop transaction: the runtime is not running.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        CompiledZapretPlan? previousPlan = currentPlan;

        RuntimeTransaction transaction = new(
            GenerateTransactionId(),
            plan: null,
            RuntimeTransactionState.Pending);

        Stack<Action> rollbackActions = new();
        rollbackActions.Push(() => RestoreRunningState(previousPlan));
        activeTransactions.Add(transaction, rollbackActions);

        isRunning = false;
        currentPlan = null;

        transaction.Activate();

        return Result.Success(transaction);
    }

    public Result<RuntimeTransaction> BeginApply(CompiledZapretPlan newPlan)
    {
        if (newPlan is null)
        {
            return Result.Failure<RuntimeTransaction>(new ErrorInfo(
                "RuntimeTransactionApplyPlanNull",
                "Cannot begin an Apply transaction: new plan is null.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        if (!isRunning)
        {
            return Result.Failure<RuntimeTransaction>(new ErrorInfo(
                "RuntimeNotRunning",
                "Cannot begin an Apply transaction: the runtime is not running.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        CompiledZapretPlan? previousPlan = currentPlan;

        RuntimeTransaction transaction = new(
            GenerateTransactionId(),
            newPlan,
            RuntimeTransactionState.Pending);

        Stack<Action> rollbackActions = new();
        rollbackActions.Push(() => RestoreRunningState(previousPlan));
        activeTransactions.Add(transaction, rollbackActions);

        currentPlan = newPlan;

        transaction.Activate();

        return Result.Success(transaction);
    }

    public Result<Unit> Commit(RuntimeTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction, nameof(transaction));

        if (!activeTransactions.ContainsKey(transaction))
        {
            return Result.Failure<Unit>(new ErrorInfo(
                "RuntimeTransactionUnknown",
                $"Cannot commit transaction {transaction.Id.Value}: not known to this manager.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        RuntimeTransactionResult result = transaction.Commit();
        if (result.State == RuntimeTransactionState.Failed)
        {
            activeTransactions.Remove(transaction);

            return Result.Failure<Unit>(result.Error!);
        }

        activeTransactions.Remove(transaction);

        return Result.Success(Unit.Instance);
    }

    public Result<Unit> Rollback(RuntimeTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction, nameof(transaction));

        if (!activeTransactions.TryGetValue(transaction, out Stack<Action>? rollbackActions))
        {
            return Result.Failure<Unit>(new ErrorInfo(
                "RuntimeTransactionUnknown",
                $"Cannot roll back transaction {transaction.Id.Value}: not known to this manager.",
                ErrorSeverity.Error,
                ErrorCategory.Runtime));
        }

        while (rollbackActions.Count > 0)
        {
            Action action = rollbackActions.Pop();

            try
            {
                action();
            }
            catch (Exception ex)
            {
                ErrorInfo error = new(
                    "RuntimeTransactionRollbackFailed",
                    $"Rollback action for transaction {transaction.Id.Value} threw: {ex.Message}",
                    ErrorSeverity.Error,
                    ErrorCategory.Runtime);

                transaction.MarkFailed(error);

                activeTransactions.Remove(transaction);

                return Result.Failure<Unit>(error);
            }
        }

        RuntimeTransactionResult result = transaction.Rollback();
        if (result.State == RuntimeTransactionState.Failed)
        {
            activeTransactions.Remove(transaction);

            return Result.Failure<Unit>(result.Error!);
        }

        activeTransactions.Remove(transaction);

        return Result.Success(Unit.Instance);
    }

    /// <summary>
    /// Test seam that allows the test project to inject an additional rollback
    /// action for a transaction that is known to this manager. The action is
    /// pushed onto the transaction's rollback stack and will be executed in
    /// LIFO order during <see cref="Rollback"/>.
    /// </summary>
    /// <param name="transaction">Transaction that must be known to this manager.</param>
    /// <param name="action">Action to execute during rollback.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="transaction" /> or <paramref name="action" /> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The transaction is not tracked by this manager.
    /// </exception>
    internal void AddRollbackAction(RuntimeTransaction transaction, Action action)
    {
        ArgumentNullException.ThrowIfNull(transaction, nameof(transaction));
        ArgumentNullException.ThrowIfNull(action, nameof(action));

        if (!activeTransactions.TryGetValue(transaction, out Stack<Action>? rollbackActions))
        {
            throw new InvalidOperationException(
                $"Cannot add rollback action: transaction {transaction.Id.Value} is not known to this manager.");
        }

        rollbackActions.Push(action);
    }

    private void RestoreStoppedState()
    {
        isRunning = false;
        currentPlan = null;
    }

    private void RestoreRunningState(CompiledZapretPlan? previousPlan)
    {
        isRunning = true;
        currentPlan = previousPlan;
    }

    private static RuntimeTransactionId GenerateTransactionId()
    {
        return new RuntimeTransactionId(Guid.NewGuid().ToString("N"));
    }
}

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
/// <b>Thread-safety (0.0.20):</b> the implementation is thread-safe. All
/// mutations of the internal <c>isRunning</c>, <c>currentPlan</c> and
/// <c>activeTransactions</c> state are performed under a private lock.
/// <see cref="Rollback"/> releases the lock before invoking user-supplied
/// rollback actions, so an action may safely call back into this manager
/// (for example to invoke <see cref="BeginStop"/>) without deadlocking.
/// </para>
/// </summary>
public sealed class RuntimeTransactionManager : IRuntimeTransactionManager
{
    private readonly object _lock = new();
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
    internal bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return isRunning;
            }
        }
    }

    /// <summary>
    /// Test-only observation hook. Returns the compiled plan the manager
    /// currently considers installed, or <c>null</c> when the runtime is
    /// not running. Exposed as <c>internal</c> so it is not part of the
    /// public API surface.
    /// </summary>
    internal CompiledZapretPlan? CurrentPlan
    {
        get
        {
            lock (_lock)
            {
                return currentPlan;
            }
        }
    }

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

        lock (_lock)
        {
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
    }

    public Result<RuntimeTransaction> BeginStop()
    {
        lock (_lock)
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

        lock (_lock)
        {
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
    }

    public Result<Unit> Commit(RuntimeTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction, nameof(transaction));

        lock (_lock)
        {
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
    }

    public Result<Unit> Rollback(RuntimeTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction, nameof(transaction));

        Stack<Action> rollbackActions;
        lock (_lock)
        {
            if (!activeTransactions.TryGetValue(transaction, out Stack<Action>? found))
            {
                return Result.Failure<Unit>(new ErrorInfo(
                    "RuntimeTransactionUnknown",
                    $"Cannot roll back transaction {transaction.Id.Value}: not known to this manager.",
                    ErrorSeverity.Error,
                    ErrorCategory.Runtime));
            }

            rollbackActions = found;

            // Remove the transaction from the active set before
            // executing the rollback actions. This guarantees that
            // (a) a second Rollback call observes an unknown
            // transaction and returns the "RuntimeTransactionUnknown"
            // failure, and (b) any rollback action that re-enters
            // the manager (e.g. BeginStop) does not see this
            // transaction in activeTransactions.
            activeTransactions.Remove(transaction);
        }

        // Execute rollback actions OUTSIDE the lock. Rollback actions
        // are user-supplied and may legally call back into the
        // manager (e.g. to BeginStop); holding the lock here would
        // deadlock.
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

                return Result.Failure<Unit>(error);
            }
        }

        RuntimeTransactionResult result = transaction.Rollback();
        if (result.State == RuntimeTransactionState.Failed)
        {
            return Result.Failure<Unit>(result.Error!);
        }

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

        lock (_lock)
        {
            if (!activeTransactions.TryGetValue(transaction, out Stack<Action>? rollbackActions))
            {
                throw new InvalidOperationException(
                    $"Cannot add rollback action: transaction {transaction.Id.Value} is not known to this manager.");
            }

            rollbackActions.Push(action);
        }
    }

    private void RestoreStoppedState()
    {
        lock (_lock)
        {
            isRunning = false;
            currentPlan = null;
        }
    }

    private void RestoreRunningState(CompiledZapretPlan? previousPlan)
    {
        lock (_lock)
        {
            isRunning = true;
            currentPlan = previousPlan;
        }
    }

    private static RuntimeTransactionId GenerateTransactionId()
    {
        return new RuntimeTransactionId(Guid.NewGuid().ToString("N"));
    }
}

using System;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.Transactions;

namespace Zapret2Pilot.Runtime.Tests.Transactions;

// Test method names deliberately use snake_case (e.g. BeginStart_NullPlan_ReturnsFailure)
// to make scenarios readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// Focused xUnit tests for <see cref="RuntimeTransactionManager"/>. These
/// tests exercise the 0.0.16 Runtime Transaction Model in memory only — no
/// real process is launched and no filesystem is touched.
/// </summary>
public sealed class RuntimeTransactionManagerTests
{
    private static CompiledZapretPlan CreatePlan(string args = "args") =>
        new(
            generatedConfigContent: "config",
            argsContent: args,
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

    [Fact]
    public static void BeginStart_NullPlan_ReturnsFailure()
    {
        RuntimeTransactionManager manager = new();

        Result<RuntimeTransaction> result = manager.BeginStart(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeTransactionStartPlanNull", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.False(manager.IsRunning);
        Assert.Null(manager.CurrentPlan);
    }

    [Fact]
    public static void BeginStart_WhenRunning_ReturnsFailure()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan planA = CreatePlan("args-a");

        Result<RuntimeTransaction> firstStart = manager.BeginStart(planA);
        Assert.True(firstStart.IsSuccess);

        CompiledZapretPlan planB = CreatePlan("args-b");
        Result<RuntimeTransaction> secondStart = manager.BeginStart(planB);

        Assert.True(secondStart.IsFailure);
        Assert.Equal("RuntimeAlreadyRunning", secondStart.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, secondStart.Error.Category);
        Assert.True(manager.IsRunning);
        Assert.Same(planA, manager.CurrentPlan);
    }

    [Fact]
    public static void BeginStart_Success_TransactionIsActiveAndRuntimeRunning()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan plan = CreatePlan();

        Result<RuntimeTransaction> result = manager.BeginStart(plan);

        Assert.True(result.IsSuccess);
        RuntimeTransaction transaction = result.Value;
        Assert.Equal(RuntimeTransactionState.Active, transaction.State);
        Assert.Same(plan, transaction.Plan);
        Assert.True(manager.IsRunning);
        Assert.Same(plan, manager.CurrentPlan);
    }

    [Fact]
    public static void StartThenCommit_Succeeds()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan plan = CreatePlan();

        Result<RuntimeTransaction> begin = manager.BeginStart(plan);
        Assert.True(begin.IsSuccess);
        RuntimeTransaction transaction = begin.Value;

        Result<Unit> commit = manager.Commit(transaction);

        Assert.True(commit.IsSuccess);
        Assert.Equal(RuntimeTransactionState.Committed, transaction.State);
        Assert.True(manager.IsRunning);
        Assert.Same(plan, manager.CurrentPlan);
    }

    [Fact]
    public static void StartThenRollback_SucceedsAndStopsRuntime()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan plan = CreatePlan();

        Result<RuntimeTransaction> begin = manager.BeginStart(plan);
        Assert.True(begin.IsSuccess);
        RuntimeTransaction transaction = begin.Value;

        Result<Unit> rollback = manager.Rollback(transaction);

        Assert.True(rollback.IsSuccess);
        Assert.Equal(RuntimeTransactionState.RolledBack, transaction.State);
        Assert.False(manager.IsRunning);
        Assert.Null(manager.CurrentPlan);
    }

    [Fact]
    public static void BeginStop_WhenNotRunning_ReturnsFailure()
    {
        RuntimeTransactionManager manager = new();

        Result<RuntimeTransaction> result = manager.BeginStop();

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeNotRunning", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.False(manager.IsRunning);
        Assert.Null(manager.CurrentPlan);
    }

    [Fact]
    public static void StopThenRollback_RestoresRunningStateWithPreviousPlan()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan planA = CreatePlan("args-a");

        Result<RuntimeTransaction> start = manager.BeginStart(planA);
        Assert.True(start.IsSuccess);
        Assert.True(manager.Commit(start.Value).IsSuccess);

        Result<RuntimeTransaction> stop = manager.BeginStop();
        Assert.True(stop.IsSuccess);
        RuntimeTransaction stopTransaction = stop.Value;

        Result<Unit> rollback = manager.Rollback(stopTransaction);

        Assert.True(rollback.IsSuccess);
        Assert.Equal(RuntimeTransactionState.RolledBack, stopTransaction.State);
        Assert.True(manager.IsRunning);
        Assert.Same(planA, manager.CurrentPlan);
    }

    [Fact]
    public static void BeginApply_NullPlan_ReturnsFailure()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan planA = CreatePlan("args-a");

        Result<RuntimeTransaction> start = manager.BeginStart(planA);
        Assert.True(start.IsSuccess);
        Assert.True(manager.Commit(start.Value).IsSuccess);

        Result<RuntimeTransaction> result = manager.BeginApply(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeTransactionApplyPlanNull", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static void BeginApply_WhenNotRunning_ReturnsFailure()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan planB = CreatePlan("args-b");

        Result<RuntimeTransaction> result = manager.BeginApply(planB);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeNotRunning", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.False(manager.IsRunning);
        Assert.Null(manager.CurrentPlan);
    }

    [Fact]
    public static void ApplyThenRollback_RestoresPreviousPlan()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan planA = CreatePlan("args-a");
        CompiledZapretPlan planB = CreatePlan("args-b");

        Result<RuntimeTransaction> start = manager.BeginStart(planA);
        Assert.True(start.IsSuccess);
        Assert.True(manager.Commit(start.Value).IsSuccess);

        Result<RuntimeTransaction> apply = manager.BeginApply(planB);
        Assert.True(apply.IsSuccess);
        RuntimeTransaction applyTransaction = apply.Value;
        Assert.Same(planB, manager.CurrentPlan);

        Result<Unit> rollback = manager.Rollback(applyTransaction);

        Assert.True(rollback.IsSuccess);
        Assert.Equal(RuntimeTransactionState.RolledBack, applyTransaction.State);
        Assert.True(manager.IsRunning);
        Assert.Same(planA, manager.CurrentPlan);
    }

    [Fact]
    public static void Commit_UnknownTransaction_ReturnsFailure()
    {
        RuntimeTransactionManager manager = new();
        RuntimeTransaction foreign = new(
            new RuntimeTransactionId(Guid.NewGuid().ToString("N")),
            CreatePlan(),
            RuntimeTransactionState.Active);

        Result<Unit> result = manager.Commit(foreign);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeTransactionUnknown", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static void Rollback_UnknownTransaction_ReturnsFailure()
    {
        RuntimeTransactionManager manager = new();
        RuntimeTransaction foreign = new(
            new RuntimeTransactionId(Guid.NewGuid().ToString("N")),
            CreatePlan(),
            RuntimeTransactionState.Active);

        Result<Unit> result = manager.Rollback(foreign);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeTransactionUnknown", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static void Rollback_ThrowingAction_FailsTransactionAndReturnsError()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan plan = CreatePlan();

        Result<RuntimeTransaction> begin = manager.BeginStart(plan);
        Assert.True(begin.IsSuccess);
        RuntimeTransaction transaction = begin.Value;

        manager.AddRollbackAction(transaction, () => throw new InvalidOperationException("rollback boom"));

        Result<Unit> rollback = manager.Rollback(transaction);

        Assert.True(rollback.IsFailure);
        Assert.Equal("RuntimeTransactionRollbackFailed", rollback.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, rollback.Error.Category);
        Assert.Equal(RuntimeTransactionState.Failed, transaction.State);
        Assert.True(manager.IsRunning);
        Assert.Same(plan, manager.CurrentPlan);

        Result<Unit> secondRollback = manager.Rollback(transaction);
        Assert.True(secondRollback.IsFailure);
        Assert.Equal("RuntimeTransactionUnknown", secondRollback.Error.Code);
    }

    [Fact]
    public static void Transaction_Commit_FromNonActiveState_ReturnsFailedResult()
    {
        RuntimeTransaction transaction = new(
            new RuntimeTransactionId(Guid.NewGuid().ToString("N")),
            CreatePlan(),
            RuntimeTransactionState.Committed);

        RuntimeTransactionResult result = transaction.Commit();

        Assert.Equal(RuntimeTransactionState.Failed, result.State);
        Assert.NotNull(result.Error);
        Assert.Equal("RuntimeTransactionInvalidState", result.Error.Code);
    }

    [Fact]
    public static void Transaction_Rollback_FromNonActiveState_ReturnsFailedResult()
    {
        RuntimeTransaction transaction = new(
            new RuntimeTransactionId(Guid.NewGuid().ToString("N")),
            CreatePlan(),
            RuntimeTransactionState.RolledBack);

        RuntimeTransactionResult result = transaction.Rollback();

        Assert.Equal(RuntimeTransactionState.Failed, result.State);
        Assert.NotNull(result.Error);
        Assert.Equal("RuntimeTransactionInvalidState", result.Error.Code);
    }

    [Fact]
    public static void TransactionResult_Failed_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RuntimeTransactionResult.Failed(null!));
    }

    [Fact]
    public static void TransactionResult_Committed_HasNoError()
    {
        RuntimeTransactionResult result = RuntimeTransactionResult.Committed();

        Assert.Equal(RuntimeTransactionState.Committed, result.State);
        Assert.Null(result.Error);
    }

    [Fact]
    public static void TransactionResult_RolledBack_HasNoError()
    {
        RuntimeTransactionResult result = RuntimeTransactionResult.RolledBack();

        Assert.Equal(RuntimeTransactionState.RolledBack, result.State);
        Assert.Null(result.Error);
    }

    [Fact]
    public static void TransactionResult_Failed_CarriesError()
    {
        ErrorInfo error = new(
            "SomeCode",
            "Some message.",
            ErrorSeverity.Error,
            ErrorCategory.Runtime);

        RuntimeTransactionResult result = RuntimeTransactionResult.Failed(error);

        Assert.Equal(RuntimeTransactionState.Failed, result.State);
        Assert.NotNull(result.Error);
        Assert.Same(error, result.Error);
    }

    /// <summary>
    /// P0-2 contract: the manager's state-mutating entry points are
    /// thread-safe. Multiple threads racing on
    /// <see cref="RuntimeTransactionManager.BeginStart(CompiledZapretPlan)"/>
    /// must produce exactly one success and the rest must fail with
    /// <c>RuntimeAlreadyRunning</c>; the manager must end up running
    /// with the winning plan. The test uses a barrier to maximise the
    /// chance of a real race.
    /// </summary>
    [Fact]
    public static void Concurrent_BeginStart_FailsAfterFirstSuccess()
    {
        const int ThreadCount = 16;

        RuntimeTransactionManager manager = new();
        CompiledZapretPlan[] plans = new CompiledZapretPlan[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            plans[i] = new CompiledZapretPlan(
                generatedConfigContent: $"# config {i}\n",
                argsContent: $"--thread-{i}\n",
                hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());
        }

        using Barrier startBarrier = new(ThreadCount);
        Result<RuntimeTransaction>[] results = new Result<RuntimeTransaction>[ThreadCount];
        int[] failureCodes = new int[ThreadCount];
        object resultsLock = new();

        Thread[] threads = new Thread[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            int index = i;
            threads[i] = new Thread(() =>
            {
                startBarrier.SignalAndWait();
                Result<RuntimeTransaction> result = manager.BeginStart(plans[index]);
                lock (resultsLock)
                {
                    results[index] = result;
                    failureCodes[index] = result.IsFailure ? 1 : 0;
                }
            });
            threads[i].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        int successCount = 0;
        int alreadyRunningCount = 0;
        CompiledZapretPlan? winningPlan = null;
        for (int i = 0; i < ThreadCount; i++)
        {
            if (results[i].IsSuccess)
            {
                successCount++;
                winningPlan = results[i].Value.Plan;
            }
            else if (results[i].Error.Code == "RuntimeAlreadyRunning")
            {
                alreadyRunningCount++;
            }
        }

        Assert.Equal(1, successCount);
        Assert.Equal(ThreadCount - 1, alreadyRunningCount);
        Assert.True(manager.IsRunning);
        Assert.Same(winningPlan, manager.CurrentPlan);
    }

    /// <summary>
    /// Contract: a user-supplied rollback action may safely re-enter
    /// the manager. Because the manager's lock is released before
    /// invoking the action, calling <c>BeginStop</c> from inside a
    /// rollback action must not deadlock. The action runs while the
    /// manager is in the <c>Running</c> state (the default for a
    /// <c>Start</c> transaction), so <c>BeginStop</c> succeeds and
    /// transitions the manager to <c>Stopped</c>; the transaction
    /// itself is then rolled back, which under the new locking
    /// discipline must still complete cleanly.
    /// </summary>
    [Fact]
    public static void RollbackAction_CallingManager_DoesNotDeadlock()
    {
        RuntimeTransactionManager manager = new();
        CompiledZapretPlan plan = CreatePlan();

        Result<RuntimeTransaction> begin = manager.BeginStart(plan);
        Assert.True(begin.IsSuccess);
        RuntimeTransaction transaction = begin.Value;

        manager.AddRollbackAction(transaction, () =>
        {
            // Re-enter the manager: BeginStop takes the same lock.
            // If the manager were holding the lock during action
            // execution, this would deadlock.
            Result<RuntimeTransaction> nestedStop = manager.BeginStop();
            Assert.True(nestedStop.IsSuccess);
        });

        Result<Unit> rollback = manager.Rollback(transaction);

        Assert.True(rollback.IsSuccess);
        Assert.Equal(RuntimeTransactionState.RolledBack, transaction.State);
        Assert.False(manager.IsRunning);
        Assert.Null(manager.CurrentPlan);
    }
}
#pragma warning restore CA1707 // Identifiers should not contain underscores

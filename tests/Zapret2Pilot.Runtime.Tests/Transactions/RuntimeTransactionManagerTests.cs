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
}
#pragma warning restore CA1707 // Identifiers should not contain underscores

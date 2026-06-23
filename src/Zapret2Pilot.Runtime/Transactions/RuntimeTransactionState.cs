namespace Zapret2Pilot.Runtime.Transactions;

/// <summary>
/// State of a <see cref="RuntimeTransaction"/> in the Runtime Kernel
/// transaction model.
///
/// The valid transitions are:
/// <list type="bullet">
///   <item><c>Pending -&gt; Active</c> when the manager activates a freshly
///         created transaction.</item>
///   <item><c>Active -&gt; Committing -&gt; Committed</c> on a successful
///         <see cref="RuntimeTransaction.Commit"/>.</item>
///   <item><c>Active -&gt; RollingBack -&gt; RolledBack</c> on a successful
///         <see cref="RuntimeTransaction.Rollback"/>.</item>
///   <item>Any state may transition to <c>Failed</c> when a terminal error
///         occurs (for example, a rollback action threw an exception).</item>
/// </list>
/// </summary>
public enum RuntimeTransactionState
{
    /// <summary>
    /// The transaction has been created by the manager but has not yet been
    /// activated. This is a transient state used only inside the manager
    /// between <c>Begin*</c> and the <c>Active</c> transition.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The transaction is live: the manager has applied the corresponding
    /// state changes to the runtime and recorded the rollback actions. This
    /// is the only state from which <see cref="RuntimeTransaction.Commit"/>
    /// or <see cref="RuntimeTransaction.Rollback"/> can succeed.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Commit has been requested and is being applied. Reserved for future
    /// milestones that introduce multi-step commit work; in 0.0.16 the
    /// commit transition is a no-op aside from the state change.
    /// </summary>
    Committing = 2,

    /// <summary>
    /// The transaction has been committed successfully. The runtime state
    /// changes recorded by the transaction are now permanent.
    /// </summary>
    Committed = 3,

    /// <summary>
    /// Rollback has been requested and is being applied. Reserved for future
    /// milestones that introduce multi-step rollback work; in 0.0.16 the
    /// rollback transition is a no-op aside from the state change.
    /// </summary>
    RollingBack = 4,

    /// <summary>
    /// The transaction has been rolled back successfully. The runtime
    /// state has been restored to what it was before the transaction
    /// was activated.
    /// </summary>
    RolledBack = 5,

    /// <summary>
    /// Terminal failure state. Reached when the manager could not apply or
    /// roll back the transaction. The associated <c>ErrorInfo</c> is
    /// carried on the <see cref="RuntimeTransactionResult"/> returned by
    /// the failing call.
    /// </summary>
    Failed = 6,
}

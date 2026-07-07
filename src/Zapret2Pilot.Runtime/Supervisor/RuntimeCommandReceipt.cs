using System;
using System.Reactive.Linq;
using System.Threading;
using Zapret2Pilot.Runtime.Kernel;

namespace Zapret2Pilot.Runtime.Supervisor;

/// <summary>
/// Supervisor-internal tracking object for a single
/// <see cref="RuntimeKernelCommand"/> posted by
/// <see cref="RuntimeSupervisor"/> to the kernel loop. The
/// receipt binds a posted command to the projected
/// <see cref="RuntimeKernelState"/> that should resolve the
/// caller's <c>await</c>, and exposes a
/// <see cref="Completion"/> <see cref="TaskCompletionSource{TResult}"/>
/// that fires exactly once.
/// </summary>
/// <remarks>
/// <para>
/// The receipt is created by <see cref="RuntimeSupervisor"/>
/// when a <see cref="RuntimeKernelCommand.Start"/> or
/// <see cref="RuntimeKernelCommand.Stop"/> is about to be
/// posted. The supervisor hands the receipt a
/// <see cref="RuntimeGeneration"/> baseline captured before
/// the command was posted, a predicate that names the
/// terminal statuses for that command, a callback that
/// clears the supervisor's <c>pendingReceipt</c> slot, and
/// the caller's <see cref="CancellationToken"/>. The receipt
/// owns the subscription, the cancellation registration, and
/// the <see cref="Completion"/> TCS for the lifetime of the
/// in-flight command.
/// </para>
/// <para>
/// <b>Single completion.</b> Every path that resolves the
/// receipt (natural terminal, error, cancellation, supersede,
/// supervisor dispose) is funnelled through a single
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>
/// gate so the <see cref="Completion"/> TCS is set exactly
/// once and the supervisor's slot is cleared exactly once.
/// </para>
/// <para>
/// <b>Supersede.</b> <see cref="TrySetSuperseded"/> is the
/// only "manual" completion path. <see cref="RuntimeSupervisor.StopAsync"/>
/// uses it to fail an in-flight <see cref="RuntimeKernelCommand.Start"/>
/// receipt before posting its own <see cref="RuntimeKernelCommand.Stop"/>
/// command, so the in-flight start caller's <c>await</c>
/// returns with a typed <c>SupersededByStop</c> failure
/// instead of a stuck terminal-state wait.
/// </para>
/// </remarks>
internal sealed class RuntimeCommandReceipt
{
    /// <summary>
    /// The TCS the supervisor awaits. Resolved with a single
    /// <see cref="RuntimeKernelState"/> snapshot, or with a
    /// cancellation / exception when the receipt is
    /// superseded, the caller's <see cref="CancellationToken"/>
    /// fires, or the underlying observable errors out.
    /// </summary>
    public TaskCompletionSource<RuntimeKernelState> Completion { get; }

    /// <summary>
    /// Unique identifier of the receipt. Useful for log
    /// correlation and for tests that need to track the
    /// lifecycle of a specific receipt.
    /// </summary>
    public Guid ReceiptId { get; }

    /// <summary>
    /// The posted <see cref="RuntimeKernelCommand"/>. Stored
    /// for diagnostics and for the supervisor's
    /// "existing-receipt command" check.
    /// </summary>
    public RuntimeKernelCommand Command { get; }

    /// <summary>
    /// Wall-clock timestamp recorded at construction time.
    /// Sourced from the supervisor's
    /// <see cref="TimeProvider"/> so tests can drive a
    /// deterministic clock.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The cancellation registration that cancels
    /// <see cref="Completion"/> when the caller's
    /// <see cref="CancellationToken"/> fires. Disposed
    /// when the receipt completes through any path so the
    /// registration is not leaked.
    /// </summary>
    public CancellationTokenRegistration CancellationRegistration { get; private set; }

    /// <summary>
    /// <c>true</c> when the receipt was displaced by a
    /// later <see cref="RuntimeKernelCommand.Stop"/> via
    /// <see cref="TrySetSuperseded"/>. The supervisor reads
    /// this after <c>await</c>-ing <see cref="Completion"/>
    /// so it can map the outcome to a typed
    /// <c>SupersededByStop</c> failure.
    /// </summary>
    public bool IsSuperseded { get; private set; }

    /// <summary>
    /// <c>true</c> when the receipt has been completed
    /// through any path (terminal state, error,
    /// cancellation, supersede, or dispose). Used by the
    /// supervisor's slot-arbitration logic to detect
    /// already-completed receipts.
    /// </summary>
    public bool IsCompleted => Volatile.Read(ref completionFlag) != 0;

    private readonly IDisposable subscription;
    private readonly Action<RuntimeCommandReceipt> onTerminal;
    private int completionFlag; // 0 = active, 1 = completed

    /// <summary>
    /// Creates a new <see cref="RuntimeCommandReceipt"/>. The
    /// caller is responsible for capturing the
    /// <see cref="RuntimeGeneration"/> baseline (typically
    /// from <see cref="RuntimeKernelLoop.CurrentState"/>)
    /// before posting the command; the receipt uses the
    /// baseline to filter out the in-flight replay of the
    /// kernel's <see cref="System.Reactive.Subjects.BehaviorSubject{T}"/>
    /// on the very first emission.
    /// </summary>
    /// <param name="command">
    /// The posted <see cref="RuntimeKernelCommand"/>. Must
    /// not be <c>null</c>.
    /// </param>
    /// <param name="baselineGeneration">
    /// Generation captured before the command was posted.
    /// The receipt's subscription only considers states with
    /// a strictly newer generation so the awaited
    /// <see cref="Completion"/> is deterministic.
    /// </param>
    /// <param name="stateChanged">
    /// The kernel's projected state observable. The receipt
    /// subscribes once and disposes the subscription when
    /// the receipt completes. Must not be <c>null</c>.
    /// </param>
    /// <param name="isTerminalStatus">
    /// Predicate that returns <c>true</c> for the
    /// terminal <see cref="RuntimeKernelStatus"/> values
    /// relevant to <paramref name="command"/>. The
    /// supervisor supplies a different predicate for start
    /// and stop receipts. Must not be <c>null</c>.
    /// </param>
    /// <param name="onTerminal">
    /// Callback invoked exactly once when the receipt
    /// completes through any path. The supervisor uses the
    /// callback to clear its <c>pendingReceipt</c> slot via
    /// <see cref="Interlocked.CompareExchange(ref object, object, object)"/>.
    /// Must not be <c>null</c>.
    /// </param>
    /// <param name="createdAt">
    /// Wall-clock timestamp recorded at construction time.
    /// Must not be <c>null</c> in the sense of
    /// <see cref="DateTimeOffset"/>, but
    /// <see cref="DateTimeOffset"/> is a value type so the
    /// <c>null</c> check is implicit.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed while waiting for the
    /// terminal state. Firing the token cancels
    /// <see cref="Completion"/>; the supervisor catches
    /// <see cref="OperationCanceledException"/> and rethrows
    /// it as the caller's cancellation signal.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="command"/>,
    /// <paramref name="stateChanged"/>,
    /// <paramref name="isTerminalStatus"/> or
    /// <paramref name="onTerminal"/> is <c>null</c>.
    /// </exception>
    internal RuntimeCommandReceipt(
        RuntimeKernelCommand command,
        RuntimeGeneration baselineGeneration,
        IObservable<RuntimeKernelState> stateChanged,
        Func<RuntimeKernelStatus, bool> isTerminalStatus,
        Action<RuntimeCommandReceipt> onTerminal,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command, nameof(command));
        ArgumentNullException.ThrowIfNull(stateChanged, nameof(stateChanged));
        ArgumentNullException.ThrowIfNull(isTerminalStatus, nameof(isTerminalStatus));
        ArgumentNullException.ThrowIfNull(onTerminal, nameof(onTerminal));

        ReceiptId = Guid.NewGuid();
        Command = command;
        CreatedAt = createdAt;
        this.onTerminal = onTerminal;
        Completion = new TaskCompletionSource<RuntimeKernelState>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // The kernel's StateChanged is backed by a BehaviorSubject
        // observed on the task pool, so a new subscription
        // immediately receives the latest published state. The
        // initial state of a fresh loop is Stopped (which is in
        // the start-terminal set), so without the generation
        // filter the subscription would resolve the TCS with the
        // baseline snapshot before the kernel has even processed
        // the Start command. Restricting to states with a
        // strictly newer generation than the captured baseline
        // makes the await deterministic. The supervisor's
        // terminal-status predicate names the right terminal
        // statuses for the posted command.
        this.subscription = stateChanged
            .Where(state =>
                state.Generation.Value > baselineGeneration.Value
                && isTerminalStatus(state.Status))
            .Take(1)
            .Subscribe(
                state => CompleteWithState(state),
                ex => CompleteWithException(ex),
                () => { /* Take(1) guarantees no OnCompleted path. */ });

        CancellationRegistration = cancellationToken.Register(
            static receiptBox =>
            {
                RuntimeCommandReceipt receipt = (RuntimeCommandReceipt)receiptBox!;
                receipt.CompleteWithCancellation();
            },
            this);
    }

    /// <summary>
    /// Displaces the receipt as a result of a later
    /// <see cref="RuntimeKernelCommand.Stop"/> issued by
    /// <see cref="RuntimeSupervisor.StopAsync"/>. Sets
    /// <see cref="IsSuperseded"/> to <c>true</c> and
    /// cancels <see cref="Completion"/> so the in-flight
    /// start caller's <c>await</c> resumes with
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the receipt was still active and
    /// was successfully displaced. <c>false</c> when the
    /// receipt had already completed through another path
    /// (natural terminal, error, cancellation, or a prior
    /// supersede).
    /// </returns>
    public bool TrySetSuperseded()
    {
        if (Interlocked.CompareExchange(ref completionFlag, 1, 0) != 0)
        {
            return false;
        }

        IsSuperseded = true;
        DisposeSubscriptionAndRegistration();
        // The supervisor's StopAsync already swaps the slot
        // to its own stop receipt before the in-flight start
        // caller resumes, so the start caller's awaiter
        // observes TrySetCanceled -> OperationCanceledException
        // and checks IsSuperseded to return the typed
        // SupersededByStop failure.
        Completion.TrySetCanceled();
        InvokeOnTerminal();
        return true;
    }

    /// <summary>
    /// Cancels the receipt without setting the
    /// <see cref="IsSuperseded"/> flag. Used by the
    /// supervisor's <see cref="IDisposable.Dispose"/>
    /// path to release the pending slot when the
    /// supervisor is being torn down.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the receipt was still active and
    /// was successfully cancelled. <c>false</c> when the
    /// receipt had already completed through another
    /// path.
    /// </returns>
    public bool TryCancel()
    {
        if (Interlocked.CompareExchange(ref completionFlag, 1, 0) != 0)
        {
            return false;
        }

        DisposeSubscriptionAndRegistration();
        Completion.TrySetCanceled();
        InvokeOnTerminal();
        return true;
    }

    /// <summary>
    /// Manually completes the receipt with the supplied
    /// <see cref="RuntimeKernelState"/>. Used by the
    /// supervisor's stop path when the kernel loop
    /// reports it is not accepting the command: the
    /// supervisor completes the receipt with the loop's
    /// current state so the awaiter maps the outcome to
    /// <c>Success</c> (or a typed <c>LastError</c> failure
    /// if the current state carries one) without
    /// blocking on a future terminal snapshot.
    /// </summary>
    /// <param name="state">
    /// State to set on <see cref="Completion"/>. Must
    /// not be <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> when the receipt was still active
    /// and was successfully completed.
    /// <c>false</c> when the receipt had already
    /// completed through another path.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="state"/> is <c>null</c>.
    /// </exception>
    public bool TryCompleteWithState(RuntimeKernelState state)
    {
        ArgumentNullException.ThrowIfNull(state, nameof(state));
        return CompleteWithStateCore(state);
    }

    /// <summary>
    /// Completes the receipt with a terminal
    /// <see cref="RuntimeKernelState"/> when the kernel
    /// publishes a snapshot whose generation is strictly
    /// newer than the baseline and whose status matches
    /// the supervisor-supplied terminal predicate.
    /// </summary>
    private void CompleteWithState(RuntimeKernelState state)
    {
        CompleteWithStateCore(state);
    }

    private bool CompleteWithStateCore(RuntimeKernelState state)
    {
        if (Interlocked.CompareExchange(ref completionFlag, 1, 0) != 0)
        {
            return false;
        }

        DisposeSubscriptionAndRegistration();
        if (Completion.TrySetResult(state))
        {
            InvokeOnTerminal();
        }
        return true;
    }

    /// <summary>
    /// Completes the receipt with an exception when the
    /// kernel's state observable errors out. Mirrors the
    /// projection failure path the previous
    /// <see cref="RuntimeSupervisor"/> had via
    /// <c>tcs.TrySetException(ex)</c>.
    /// </summary>
    private void CompleteWithException(Exception exception)
    {
        if (Interlocked.CompareExchange(ref completionFlag, 1, 0) != 0)
        {
            return;
        }

        DisposeSubscriptionAndRegistration();
        if (Completion.TrySetException(exception))
        {
            InvokeOnTerminal();
        }
    }

    /// <summary>
    /// Completes the receipt with a cancellation when the
    /// caller's <see cref="CancellationToken"/> fires. The
    /// supervisor rethrows the resulting
    /// <see cref="OperationCanceledException"/> so the
    /// caller's <c>await</c> surfaces a normal
    /// cancellation signal.
    /// </summary>
    private void CompleteWithCancellation()
    {
        if (Interlocked.CompareExchange(ref completionFlag, 1, 0) != 0)
        {
            return;
        }

        DisposeSubscriptionAndRegistration();
        if (Completion.TrySetCanceled())
        {
            InvokeOnTerminal();
        }
    }

    private void DisposeSubscriptionAndRegistration()
    {
        // The subscription and the registration can be
        // disposed independently. Use try/catch around
        // each dispose so a misbehaving Rx subscription
        // does not prevent the cancellation registration
        // from being released (which would leak a
        // CancellationTokenSource hook).
        try
        {
            subscription.Dispose();
        }
        catch
        {
            // Swallow: the TCS is already marked as
            // completed; nothing useful to do here.
        }

        try
        {
            CancellationRegistration.Dispose();
        }
        catch
        {
            // Swallow: same rationale as above.
        }
    }

    private void InvokeOnTerminal()
    {
        try
        {
            onTerminal(this);
        }
        catch
        {
            // The terminal callback is the supervisor's
            // slot-clear seam; an exception here must
            // not propagate through the Rx subscription
            // (it would fault the kernel's projected
            // observable) or through the cancellation
            // registration callback (it would surface as
            // an unobserved exception on the token's
            // source). Swallow defensively.
        }
    }
}

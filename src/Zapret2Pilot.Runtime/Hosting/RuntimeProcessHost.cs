using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Locking;
using Zapret2Pilot.Runtime.Ownership;
using Zapret2Pilot.Runtime.Recovery;
using Zapret2Pilot.Runtime.Transactions;
using Zapret2Pilot.Runtime.Windows;
using Zapret2Pilot.Runtime.Workspace;

namespace Zapret2Pilot.Runtime.Hosting;

/// <summary>
/// Coordinates launch, containment and shutdown of the Zapret2 runtime
/// executable for Zapret2Pilot. This is the kernel-side adapter between
/// the in-process safety primitives (global ownership mutex, lock file
/// store, transaction manager, Windows job object) and the operating
/// system process model.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StartAsync"/> and <see cref="StopAsync"/> may be called
/// from any thread. Both enqueue their work onto the dedicated
/// <see cref="RuntimeKernelWorker"/> thread, so the start and stop
/// pipelines always execute on the same single kernel thread. This
/// preserves the ownership-mutex thread affinity required by
/// <see cref="RuntimeOwnershipLease"/>.
/// </para>
/// <para>
/// The start pipeline acquires the global ownership mutex, performs
/// stale lock recovery, materializes the workspace, begins a
/// <c>Start</c> transaction, creates a Windows Job Object with
/// kill-on-close, launches the runtime process, assigns it to the job
/// object, performs a readiness check, writes the recovery lock file,
/// and finally commits the transaction. Any failure between
/// <c>BeginStart</c> and <c>Commit</c> rolls the transaction back
/// and tears down the partially-constructed resources.
/// </para>
/// <para>
/// The stop pipeline reverses that pipeline: it begins a <c>Stop</c>
/// transaction, kills the running process (if any), waits for it to
/// exit (bounded by the constructor-supplied <c>stopTimeout</c>),
/// disposes the process and the job object, deletes the lock file,
/// disposes the ownership lease and finally commits the transaction.
/// </para>
/// <para>
/// This host does NOT execute the real <c>winws2</c> binary; it
/// launches only the verified path supplied in
/// <see cref="RuntimeProcessStartContext.RuntimeExecutablePath"/>,
/// which is a <see cref="VerifiedRuntimeExecutablePath"/> that can
/// only be constructed from a passing
/// <see cref="ZapretAssetVerificationSummary"/> produced by
/// <see cref="ZapretAssetVerifier"/>, closing P0-4. In milestone
/// 0.0.17 the production wiring uses the
/// <c>Zapret2Pilot.Testing.FakeRuntime</c> test executable as a
/// placeholder until <c>winws2</c> launch is approved by oracle.
/// </para>
/// <para>
/// Serialisation of concurrent <see cref="StartAsync"/> and
/// <see cref="StopAsync"/> calls is provided by the
/// <see cref="RuntimeKernelWorker"/>'s single-reader dispatch loop,
/// which executes the work items one at a time on the dedicated
/// worker thread.
/// </para>
/// </remarks>
public sealed class RuntimeProcessHost : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Default <c>stopTimeout</c> applied when the caller does not
    /// supply one. Five seconds is consistent with the kernel's
    /// documented graceful-stop window.
    /// </summary>
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time to wait for the runtime process to exit after a
    /// successful <c>CTRL_BREAK_EVENT</c> delivery before the host
    /// escalates to a forced <see cref="Process.Kill(bool)"/>. The
    /// remainder of <c>stopTimeout</c> is still available for the
    /// final post-kill wait.
    /// </summary>
    private const int GracefulStopTimeoutMs = 3000;

    private readonly RuntimeOwnershipMutex ownershipMutex;
    private readonly RuntimeStaleLockRecovery staleLockRecovery;
    private readonly IRuntimeWorkspaceMaterializer workspaceMaterializer;
    private readonly IRuntimeTransactionManager transactionManager;
    private readonly IRuntimeJobObjectProcessAssigner jobObjectAssigner;
    private readonly RuntimeLockFileStore lockFileStore;
    private readonly ILogger<RuntimeProcessHost> logger;
    private readonly RuntimeKernelWorker worker;
    private readonly TimeSpan stopTimeout;
    private readonly string ownerInstanceId;

    private readonly object stateLock = new();
    private RuntimeOwnershipLease? ownershipLease;
    private Process? process;
    private IRuntimeJobObject? jobObject;
    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="RuntimeProcessHost"/>.
    /// </summary>
    /// <param name="ownershipMutex">
    /// Global ownership mutex used to serialise runtime launches.
    /// </param>
    /// <param name="staleLockRecovery">
    /// Stale lock file recovery that runs immediately after the mutex
    /// is acquired.
    /// </param>
    /// <param name="workspaceMaterializer">
    /// Workspace materializer that writes the runtime's config, args
    /// and hostlists.
    /// </param>
    /// <param name="transactionManager">
    /// Transaction manager that coordinates the start / stop state
    /// changes.
    /// </param>
    /// <param name="jobObjectAssigner">
    /// Job object process assigner that wires a launched process into
    /// a kill-on-close job.
    /// </param>
    /// <param name="lockFileStore">
    /// Lock file store that writes and removes the recovery lock file.
    /// </param>
    /// <param name="logger">
    /// Logger that receives structured events for cleanup-path
    /// exceptions, graceful-stop fallbacks and stop-pipeline warnings.
    /// Pass <see cref="NullLogger{T}.Instance"/> when the host is used
    /// outside of a hosted service (e.g. in unit tests).
    /// </param>
    /// <param name="worker">
    /// The dedicated kernel worker thread that serialises and runs
    /// the start and stop pipelines. The host enqueues its work onto
    /// this worker so the pipelines always run on the same thread,
    /// preserving the ownership-mutex thread affinity required by
    /// <see cref="RuntimeOwnershipLease.Dispose()"/>.
    /// </param>
    /// <param name="stopTimeout">
    /// Maximum time to wait for the runtime process to exit after
    /// <see cref="StopAsync"/> requests termination. Defaults to 5
    /// seconds when <c>null</c>.
    /// </param>
    public RuntimeProcessHost(
        RuntimeOwnershipMutex ownershipMutex,
        RuntimeStaleLockRecovery staleLockRecovery,
        IRuntimeWorkspaceMaterializer workspaceMaterializer,
        IRuntimeTransactionManager transactionManager,
        IRuntimeJobObjectProcessAssigner jobObjectAssigner,
        RuntimeLockFileStore lockFileStore,
        ILogger<RuntimeProcessHost> logger,
        RuntimeKernelWorker worker,
        TimeSpan? stopTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(ownershipMutex, nameof(ownershipMutex));
        ArgumentNullException.ThrowIfNull(staleLockRecovery, nameof(staleLockRecovery));
        ArgumentNullException.ThrowIfNull(workspaceMaterializer, nameof(workspaceMaterializer));
        ArgumentNullException.ThrowIfNull(transactionManager, nameof(transactionManager));
        ArgumentNullException.ThrowIfNull(jobObjectAssigner, nameof(jobObjectAssigner));
        ArgumentNullException.ThrowIfNull(lockFileStore, nameof(lockFileStore));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(worker, nameof(worker));

        this.ownershipMutex = ownershipMutex;
        this.staleLockRecovery = staleLockRecovery;
        this.workspaceMaterializer = workspaceMaterializer;
        this.transactionManager = transactionManager;
        this.jobObjectAssigner = jobObjectAssigner;
        this.lockFileStore = lockFileStore;
        this.logger = logger;
        this.worker = worker;
        this.stopTimeout = stopTimeout ?? DefaultStopTimeout;
        ownerInstanceId = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Starts the runtime process. This method may be called from any
    /// thread; the actual start pipeline is marshalled onto the
    /// dedicated <see cref="RuntimeKernelWorker"/> thread and runs
    /// there to preserve ownership-mutex thread affinity.
    /// </summary>
    /// <param name="context">
    /// Start context carrying the compiled plan, asset manifest,
    /// workspace directory and runtime executable path.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed at well-defined points in the
    /// pipeline. The token is not honoured while the ownership mutex
    /// is held or while the operating system is performing
    /// non-cancellable work.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with a
    /// <see cref="RuntimeProcessHostResult"/> describing the launched
    /// process on success, or a <see cref="ErrorInfo"/> on failure.
    /// </returns>
    public async Task<Result<RuntimeProcessHostResult>> StartAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        cancellationToken.ThrowIfCancellationRequested();

        // Validate the start context before any side-effecting work.
        // A missing CacheKey would later cause RuntimeLockProcessMetadata
        // to throw ArgumentException because planHash cannot be whitespace.
        if (context.Plan.CacheKey is null)
        {
            return Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "RuntimePlanCacheKeyMissing",
                message: "Cannot start the runtime: the compiled plan has no cache key.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        return await worker.Enqueue(ct => StartOnWorkerAsync(context, ct), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the start pipeline on the dedicated kernel worker
    /// thread. This is the synchronous body of the old
    /// <c>StartAsync</c>: argument validation, mutex acquisition,
    /// stale lock recovery, workspace materialization, transaction
    /// start, job object creation, process launch, readiness check,
    /// lock file write and transaction commit. The async seams
    /// (materializer and readiness checker) are unwrapped via
    /// <see cref="RunSync{T}(Task{T})"/> so the entire pipeline runs
    /// on the calling worker thread, which is also the thread that
    /// acquired the ownership mutex and must therefore dispose the
    /// lease during cleanup paths.
    /// </summary>
    private Task<Result<RuntimeProcessHostResult>> StartOnWorkerAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            if (disposed)
            {
                return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                    code: "RuntimeProcessHostDisposed",
                    message: "Cannot start the runtime: the host has been disposed.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime)));
            }

            if (ownershipLease is not null)
            {
                return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                    code: "RuntimeAlreadyRunning",
                    message: "Cannot start the runtime: a previous start has not been stopped.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime)));
            }
        }

        // 1. Acquire the global ownership mutex (synchronous, non-blocking).
        RuntimeOwnershipAcquireResult acquireResult = ownershipMutex.TryAcquire(TimeSpan.Zero);
        if (!acquireResult.Acquired || acquireResult.Lease is null)
        {
            return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "RuntimeOwnershipNotAcquired",
                message: "Cannot start the runtime: ownership mutex is held by another process.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime)));
        }

        RuntimeOwnershipLease lease = acquireResult.Lease;
        RuntimeTransaction? transaction = null;
        IRuntimeJobObject? createdJobObject = null;
        Process? startedProcess = null;
        bool lockFileWritten = false;
        bool transactionCommitted = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 2. Stale lock recovery (must run on the owning thread).
            staleLockRecovery.RecoverAfterOwnershipAcquired(lease);

            // 3. Materialize the workspace. Called synchronously via
            //    GetAwaiter().GetResult() to preserve mutex thread affinity.
            Task<Result<RuntimeWorkspaceMaterializeResult>> materializeTask =
                workspaceMaterializer.MaterializeAsync(
                    context.Plan,
                    context.Manifest,
                    context.WorkspaceDirectory,
                    cancellationToken);
            Result<RuntimeWorkspaceMaterializeResult> materializeResult =
                RunSync(materializeTask);
            if (materializeResult.IsFailure)
            {
                ErrorInfo error = new(
                    code: "RuntimeWorkspaceMaterializationFailed",
                    message: $"Workspace materialization failed: {materializeResult.Error.Message}",
                    severity: ErrorSeverity.Error,
                    category: materializeResult.Error.Category);
                return FailStartAndCleanup(error, startedProcess: null, createdJobObject: null, transaction: null, lease);
            }

            // 4. Begin the start transaction.
            Result<RuntimeTransaction> beginResult = transactionManager.BeginStart(context.Plan);
            if (beginResult.IsFailure)
            {
                return FailStartAndCleanup(beginResult.Error, startedProcess: null, createdJobObject: null, transaction: null, lease);
            }
            transaction = beginResult.Value;

            // 5. Create the job object.
            RuntimeJobObjectCreateResult createResult;
            try
            {
                createResult = RuntimeJobObject.CreateWithKillOnClose();
            }
            catch (RuntimeJobObjectException ex)
            {
                ErrorInfo jobError = new(
                    code: "RuntimeJobObjectCreateFailed",
                    message: $"Failed to create a job object for runtime containment: {ex.Message}",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(jobError, startedProcess: null, createdJobObject: null, transaction, lease);
            }

            if (!createResult.Created || createResult.JobObject is null)
            {
                ErrorInfo jobError = new(
                    code: "RuntimeJobObjectUnsupportedPlatform",
                    message: "Cannot create a job object: the current platform is not supported.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(jobError, startedProcess: null, createdJobObject: null, transaction, lease);
            }
            createdJobObject = createResult.JobObject;

            // 6. Start the runtime process.
            string executablePath = context.RuntimeExecutablePath.AbsolutePath;
            string arguments = context.Plan.Arguments is null
                ? string.Empty
                : string.Join(" ", context.Plan.Arguments);

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = context.WorkspaceDirectory,
                // P0-5 (0.0.18): redirection is intentionally disabled.
                // A previous milestone redirected stdout/stderr without
                // ever wiring OutputDataReceived/ErrorDataReceived, which
                // deadlocks a verbose runtime (such as the real winws2)
                // once the OS pipe buffer fills. A bounded pump that
                // captures and drains output under a backpressure
                // contract will be added in a later milestone; until
                // then the runtime process owns its own console and the
                // host must not allocate a pipe.
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            try
            {
                startedProcess = Process.Start(startInfo);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or Win32Exception
                                       or ObjectDisposedException
                                       or PlatformNotSupportedException
                                       or IOException
                                       or ArgumentException)
            {
                ErrorInfo startError = new(
                    code: "RuntimeProcessStartFailed",
                    message: $"Failed to start the runtime process: {ex.Message}",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(startError, startedProcess: null, createdJobObject, transaction, lease);
            }

            if (startedProcess is null)
            {
                ErrorInfo startError = new(
                    code: "RuntimeProcessStartFailed",
                    message: "Failed to start the runtime process: Process.Start returned null.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(startError, startedProcess: null, createdJobObject, transaction, lease);
            }

            // 7. Capture process identity (used for both assignment and lock metadata).
            int processId;
            string processName;
            DateTimeOffset processStartedAtUtc;
            try
            {
                processId = startedProcess.Id;
                processName = startedProcess.ProcessName;
                processStartedAtUtc = new DateTimeOffset(startedProcess.StartTime.ToUniversalTime());
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                ErrorInfo infoError = new(
                    code: "RuntimeProcessInfoFailed",
                    message: $"Failed to read runtime process information: {ex.Message}",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(infoError, startedProcess, createdJobObject, transaction, lease);
            }

            IntPtr processHandle;
            try
            {
                processHandle = startedProcess.Handle;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                ErrorInfo handleError = new(
                    code: "RuntimeProcessHandleFailed",
                    message: $"Failed to obtain the runtime process handle: {ex.Message}",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(handleError, startedProcess, createdJobObject, transaction, lease);
            }

            // 8. Assign the process to the job object.
            RuntimeProcessHandle runtimeProcessHandle = new(processHandle);
            RuntimeJobObjectAssignmentResult assignResult =
                jobObjectAssigner.Assign(createdJobObject, runtimeProcessHandle);
            if (assignResult.Status != RuntimeJobObjectAssignmentStatus.Assigned)
            {
                ErrorInfo assignError = new(
                    code: "RuntimeJobObjectAssignFailed",
                    message: $"Failed to assign the runtime process to the job object: status={assignResult.Status}.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(assignError, startedProcess, createdJobObject, transaction, lease);
            }

            // 9. Readiness check (synchronous to preserve ownership-mutex thread
            //    affinity: the continuation of an awaited Task could resume on a
            //    different thread, breaking lease.Dispose() in cleanup paths).
            Result<Unit> readinessResult = RunSync(RuntimeReadinessChecker.CheckAsync(
                startedProcess,
                RuntimeReadinessChecker.DefaultReadinessTimeout,
                cancellationToken));
            if (readinessResult.IsFailure)
            {
                ErrorInfo readinessError = new(
                    code: "RuntimeNotReady",
                    message: readinessResult.Error.Message,
                    severity: ErrorSeverity.Error,
                    category: readinessResult.Error.Category);
                return FailStartAndCleanup(readinessError, startedProcess, createdJobObject, transaction, lease);
            }

            // 10. Write the lock metadata.
            string commandLineHash = ComputeCommandLineHash(executablePath, arguments);
            string planHash = context.Plan.CacheKey?.Value ?? string.Empty;
            RuntimeLockProcessMetadata processMetadata = new(
                processId: processId,
                processName: processName,
                executablePath: executablePath,
                commandLineHash: commandLineHash,
                planHash: planHash,
                processStartedAtUtc: processStartedAtUtc);
            RuntimeLockMetadata metadata = new(
                schemaVersion: 1,
                ownerInstanceId: ownerInstanceId,
                process: processMetadata,
                acquiredAtUtc: DateTimeOffset.UtcNow);

            try
            {
                lockFileStore.Write(metadata);
                lockFileWritten = true;
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException
                                       or System.Text.Json.JsonException)
            {
                ErrorInfo lockError = new(
                    code: "RuntimeLockWriteFailed",
                    message: $"Failed to write the runtime lock file: {ex.Message}",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Storage);
                return FailStartAndCleanup(lockError, startedProcess, createdJobObject, transaction, lease);
            }

            // 11. Commit the start transaction.
            Result<Unit> commitResult = transactionManager.Commit(transaction);
            if (commitResult.IsFailure)
            {
                ErrorInfo commitError = new(
                    code: "RuntimeStartCommitFailed",
                    message: $"Failed to commit the start transaction: {commitResult.Error.Message}",
                    severity: ErrorSeverity.Error,
                    category: commitResult.Error.Category);
                BestEffortKillAndDispose(startedProcess);
                if (lockFileWritten)
                {
                    BestEffortDeleteLock();
                }
                try
                {
                    createdJobObject.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "StartAsync: failed to dispose the job object during commit-failure cleanup.");
                }
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "StartAsync: failed to dispose the ownership lease during commit-failure cleanup.");
                }
                return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(commitError));
            }
            transactionCommitted = true;
            transaction = null;

            // 12. Commit state and return success.
            lock (stateLock)
            {
                ownershipLease = lease;
                process = startedProcess;
                jobObject = createdJobObject;
            }

            return Task.FromResult(Result.Success(new RuntimeProcessHostResult(
                processId: processId,
                processName: processName,
                executablePath: executablePath,
                plan: context.Plan)));
        }
        catch (OperationCanceledException)
        {
            CleanupAfterStartFailure(
                startedProcess,
                createdJobObject,
                transaction,
                transactionCommitted,
                lockFileWritten,
                lease);
            return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "RuntimeStartCancelled",
                message: "The runtime start was cancelled.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime)));
        }
        catch (Exception ex)
        {
            CleanupAfterStartFailure(
                startedProcess,
                createdJobObject,
                transaction,
                transactionCommitted,
                lockFileWritten,
                lease);
            return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "RuntimeStartUnexpectedError",
                message: $"Unexpected error while starting the runtime: {ex.Message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime)));
        }
    }

    /// <summary>
    /// Stops the runtime process. This method may be called from any
    /// thread; the actual stop pipeline is marshalled onto the
    /// dedicated <see cref="RuntimeKernelWorker"/> thread and runs
    /// there to preserve ownership-lease thread affinity.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token observed before stopping.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with <see cref="Unit.Instance"/> on
    /// success, or a <see cref="ErrorInfo"/> on failure.
    /// </returns>
    public async Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
    {
        return await worker.Enqueue(ct => StopOnWorkerAsync(ct), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the stop pipeline on the dedicated kernel worker
    /// thread. This is the synchronous body of the old
    /// <c>StopAsync</c>: cancellation check, state read, transaction
    /// start, process termination, job object disposal, lock file
    /// deletion, lease disposal and transaction commit.
    /// </summary>
    private Task<Result<Unit>> StopOnWorkerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RuntimeOwnershipLease? lease;
        Process? processToStop;
        IRuntimeJobObject? jobObjectToDispose;
        lock (stateLock)
        {
            if (disposed)
            {
                return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                    code: "RuntimeProcessHostDisposed",
                    message: "Cannot stop the runtime: the host has been disposed.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime)));
            }

            lease = ownershipLease;
            processToStop = process;
            jobObjectToDispose = jobObject;
        }

        if (lease is null || processToStop is null)
        {
            return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                code: "RuntimeNotRunning",
                message: "Cannot stop the runtime: the runtime is not running.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime)));
        }

        Result<RuntimeTransaction> beginStopResult = transactionManager.BeginStop();
        if (beginStopResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<Unit>(beginStopResult.Error));
        }

        RuntimeTransaction transaction = beginStopResult.Value;

        // P0-6 (0.0.18): the process is about to be killed. From this
        // point on we MUST NOT call transactionManager.Rollback — the
        // transaction was opened with BeginStop, and Rollback would
        // restore isRunning = true in the manager, leaving the host
        // and the manager out of sync with reality. Cleanup failures
        // are logged but never roll back. Host state is cleared
        // unconditionally before returning.
        Result<Unit>? finalResult = null;

        try
        {
            // 1. Stop the process. Try a graceful CTRL_BREAK first
            //    (3 s window), then fall back to Kill. See
            //    StopProcess for the full escalation contract.
            StopProcess(processToStop);

            // 2. Dispose the job object. Triggers kill-on-close for
            //    any remaining process or thread attached to it.
            //    Best-effort: the process is already gone, so a
            //    failure here is a kernel-resource leak we log but
            //    cannot recover from without a restart.
            if (jobObjectToDispose is not null)
            {
                try
                {
                    jobObjectToDispose.Dispose();
                }
                catch (RuntimeJobObjectException ex)
                {
                    logger.LogError(
                        ex,
                        "StopAsync: failed to dispose the job object after kill; resource may leak until process exit.");
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "StopAsync: unexpected error disposing the job object after kill.");
                }
            }

            // 3. Delete the lock file. Best-effort: a failure leaves
            //    a stale lock on disk that the next startup's stale
            //    lock recovery will surface and clear.
            try
            {
                lockFileStore.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(
                    ex,
                    "StopAsync: failed to delete the runtime lock file after kill; stale-lock recovery will clear it on next start.");
            }

            // 4. Dispose the ownership lease. Best-effort: a
            //    failure here means the mutex will only be released
            //    when the OS reaps the lease's SafeWaitHandle, which
            //    is acceptable.
            try
            {
                lease.Dispose();
            }
            catch (Exception ex) when (ex is RuntimeOwnershipThreadAffinityException or ObjectDisposedException)
            {
                logger.LogError(
                    ex,
                    "StopAsync: failed to dispose the ownership lease after kill; mutex will be released by the OS.");
            }

            // 5. Commit the stop transaction. We do NOT roll back
            //    on a commit failure: rolling back would put the
            //    manager back into Running, contradicting the fact
            //    that the process is dead. The host's local state
            //    is cleared unconditionally below.
            Result<Unit> commitResult = transactionManager.Commit(transaction);
            if (commitResult.IsFailure)
            {
                logger.LogError(
                    "StopAsync: commit of the stop transaction failed after kill; manager and host may be out of sync. Code={Code} Message={Message}",
                    commitResult.Error.Code,
                    commitResult.Error.Message);
                finalResult = commitResult;
            }
            else
            {
                finalResult = Result.Success(Unit.Instance);
            }
        }
        catch (Exception ex)
        {
            // Anything thrown after the kill is logged and surfaced
            // as a failure, but we never roll the transaction back
            // to Running. The process is already dead.
            logger.LogError(ex, "StopAsync: unexpected error after kill.");
            finalResult = Result.Failure<Unit>(new ErrorInfo(
                code: "RuntimeStopUnexpectedError",
                message: $"Unexpected error while stopping the runtime: {ex.Message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        // 6. Always clear host state. The process is dead
        //    regardless of how cleanup finished, so the host must
        //    not retain the old lease / process / job object
        //    references. A subsequent StartAsync must be allowed.
        lock (stateLock)
        {
            ownershipLease = null;
            process = null;
            jobObject = null;
        }

        return Task.FromResult(finalResult ?? Result.Success(Unit.Instance));
    }

    /// <summary>
    /// Disposes the host. Sets the <c>disposed</c> flag and dispatches
    /// the stop pipeline to the dedicated kernel worker thread so the
    /// ownership lease is disposed on the thread that acquired the
    /// mutex. If the worker is unavailable or rejects the work item,
    /// falls back to <see cref="BestEffortDispose"/> which performs
    /// the same cleanup on the calling thread (with the caveat that
    /// the lease dispose becomes a no-op off-thread, so the mutex
    /// is released by the OS instead). Idempotent.
    /// </summary>
    public void Dispose()
    {
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        if (worker is not null)
        {
            try
            {
                worker.Enqueue(ct => StopOnWorkerAsync(ct), CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dispose: failed to dispatch cleanup to worker; falling back to best-effort cleanup.");
                BestEffortDispose();
            }
        }
        else
        {
            BestEffortDispose();
        }
    }

    /// <summary>
    /// Performs the standard partial-state cleanup for a
    /// <see cref="Dispose"/> call on the calling thread. Captures
    /// the current lease / process / job object references under
    /// <c>stateLock</c> and clears the fields, then best-effort
    /// stops the process, disposes the job object, deletes the
    /// lock file and disposes the ownership lease. The lease
    /// dispose is a no-op when called off the thread that acquired
    /// the mutex; the OS will release the mutex when the
    /// <see cref="RuntimeOwnershipLease"/> is eventually
    /// finalised. This is the same cleanup the host performed
    /// pre-0.0.20 and is only used as a fallback when the kernel
    /// worker is unavailable.
    /// </summary>
    private void BestEffortDispose()
    {
        RuntimeOwnershipLease? lease;
        Process? processToStop;
        IRuntimeJobObject? jobObjectToDispose;
        lock (stateLock)
        {
            lease = ownershipLease;
            processToStop = process;
            jobObjectToDispose = jobObject;

            ownershipLease = null;
            process = null;
            jobObject = null;
        }

        if (processToStop is not null)
        {
            StopProcess(processToStop);
        }

        if (jobObjectToDispose is not null)
        {
            try
            {
                jobObjectToDispose.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dispose: failed to dispose the job object.");
            }
        }

        try
        {
            lockFileStore.Delete();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dispose: failed to delete the runtime lock file.");
        }

        if (lease is not null)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dispose: failed to dispose the ownership lease.");
            }
        }
    }

    /// <summary>
    /// Disposes the host asynchronously. Forwards to <see cref="Dispose"/>;
    /// the returned <see cref="ValueTask"/> completes synchronously.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when disposal is finished.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stops <paramref name="processToStop"/> gracefully and falls
    /// back to <see cref="Process.Kill(bool)"/> if it does not exit
    /// within the configured window. The graceful step is a
    /// <c>CTRL_BREAK_EVENT</c> delivered via
    /// <c>GenerateConsoleCtrlEvent</c> to the target's own process
    /// group (its process id, which only equals its process group id
    /// when the process was launched with
    /// <c>CREATE_NEW_PROCESS_GROUP</c>); a well-behaved console
    /// application can intercept this signal and shut down cleanly.
    /// Processes started without <c>CREATE_NEW_PROCESS_GROUP</c>
    /// (including our test FakeRuntime) inherit the caller's
    /// process group, so the targeted call is rejected by the
    /// kernel and the method falls back to a forced termination.
    /// All exceptions are swallowed (and logged at
    /// <see cref="LogLevel.Debug"/>) so the method is safe to call
    /// from best-effort cleanup paths.
    /// </summary>
    /// <param name="processToStop">The process to terminate and dispose.</param>
    private void StopProcess(Process processToStop)
    {
        // 1. Try a graceful CTRL_BREAK if the process is still alive.
        //    We address the signal to the target's own PID/process
        //    group, NOT to process group 0 (which is the calling
        //    process's console). Addressing group 0 would also
        //    deliver CTRL_BREAK to every other process attached to
        //    our console — including the host's own test runner
        //    when the runtime is launched by an xunit test — and
        //    crash it. Using the target's PID is safe: it succeeds
        //    when the target is in its own process group
        //    (CREATE_NEW_PROCESS_GROUP) and is rejected with
        //    ERROR_INVALID_PARAMETER otherwise, in which case we
        //    fall straight through to the Kill fallback.
        bool gracefulAttempted = false;
        try
        {
            if (!processToStop.HasExited && TrySendCtrlBreak(processToStop))
            {
                gracefulAttempted = true;
                if (processToStop.WaitForExit(GracefulStopTimeoutMs))
                {
                    logger.LogDebug(
                        "Runtime process {ProcessId} exited gracefully after CTRL_BREAK.",
                        SafeGetProcessId(processToStop));
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // already disposed or never started
            logger.LogDebug(ex, "StopProcess: race while waiting for graceful exit.");
        }
        catch (Win32Exception ex)
        {
            logger.LogDebug(ex, "StopProcess: race while waiting for graceful exit.");
        }

        // 2. Forced termination as a fallback. If the graceful step was
        //    attempted and the process is still alive, escalate.
        try
        {
            if (!processToStop.HasExited)
            {
                if (gracefulAttempted)
                {
                    logger.LogWarning(
                        "Runtime process {ProcessId} ignored CTRL_BREAK; falling back to Kill.",
                        SafeGetProcessId(processToStop));
                }
                processToStop.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "StopProcess: Kill on a disposed or non-started process.");
        }
        catch (Win32Exception ex)
        {
            logger.LogDebug(ex, "StopProcess: Kill failed (race or access denied).");
        }

        // 3. Final bounded wait so the process handle is fully released
        //    before we dispose it. Use the full stopTimeout here
        //    because the graceful step already consumed part of it.
        try
        {
            processToStop.WaitForExit((int)stopTimeout.TotalMilliseconds);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "StopProcess: WaitForExit on a disposed process.");
        }
        catch (Win32Exception ex)
        {
            logger.LogDebug(ex, "StopProcess: WaitForExit race.");
        }

        // 4. Dispose the process handle.
        try
        {
            processToStop.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "StopProcess: best-effort process dispose failed.");
        }
    }

    /// <summary>
    /// Delivers a <c>CTRL_BREAK_EVENT</c> to the target process's
    /// own process group so the target can intercept it via
    /// <see cref="Console.CancelKeyPress"/> while leaving the
    /// calling process and its peers untouched. Returns
    /// <c>false</c> on any failure (e.g. the platform is not
    /// Windows, the target has no assigned process group, the call
    /// is denied, or the target is already gone). The exception
    /// is swallowed because the method is invoked from a
    /// best-effort graceful-stop path.
    /// </summary>
    /// <param name="processToStop">
    /// The target process whose PID is used as the process group id.
    /// </param>
    private bool TrySendCtrlBreak(Process processToStop)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        int targetPid = SafeGetProcessId(processToStop);
        if (targetPid <= 0)
        {
            return false;
        }

        try
        {
            bool delivered = WindowsJobObjectNativeMethods.GenerateConsoleCtrlEvent(
                WindowsJobObjectNativeMethods.CtrlBreakEvent,
                dwProcessGroupId: (uint)targetPid);
            if (!delivered)
            {
                int errorCode = Marshal.GetLastPInvokeError();
                logger.LogDebug(
                    "GenerateConsoleCtrlEvent to process group {ProcessId} returned false (Win32 error {ErrorCode}); the runtime may not own a separate console group.",
                    targetPid,
                    errorCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "GenerateConsoleCtrlEvent threw; graceful stop is unavailable.");
            return false;
        }
    }

    /// <summary>
    /// Returns the target process's id without throwing. Used inside
    /// log messages to keep cleanup paths free of unhandled
    /// <see cref="InvalidOperationException"/>s.
    /// </summary>
    private static int SafeGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Best-effort kill + dispose of a process that may not have
    /// been fully started or assigned to the job object. Used by the
    /// failure paths in <see cref="StartOnWorkerAsync"/>.
    /// </summary>
    /// <param name="processToDispose">The process to terminate and dispose.</param>
    private void BestEffortKillAndDispose(Process? processToDispose)
    {
        if (processToDispose is null)
        {
            return;
        }

        StopProcess(processToDispose);
    }

    /// <summary>
    /// Best-effort lock file delete. Used by the failure paths in
    /// <see cref="StartOnWorkerAsync"/> and <see cref="BestEffortDispose"/>.
    /// </summary>
    private void BestEffortDeleteLock()
    {
        try
        {
            lockFileStore.Delete();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BestEffortDeleteLock: lock file delete failed.");
        }
    }

    /// <summary>
    /// Performs the standard partial-state cleanup for a
    /// <see cref="StartOnWorkerAsync"/> failure path and returns the
    /// corresponding failure <see cref="Result{T}"/>. Kills and
    /// disposes the started process (if any), disposes the job
    /// object (if any), rolls the transaction back (if any) and
    /// disposes the ownership lease. The lock file is NOT deleted
    /// here because it has not been written in the failure paths
    /// that use this helper; the commit-failure path that owns the
    /// lock-file cleanup uses inline code instead. All steps swallow
    /// exceptions because the method is invoked from
    /// already-failing paths.
    /// </summary>
    /// <param name="error">The error to surface to the caller.</param>
    /// <param name="startedProcess">The partially-started process, or <c>null</c>.</param>
    /// <param name="createdJobObject">The created job object, or <c>null</c>.</param>
    /// <param name="transaction">The open transaction, or <c>null</c>.</param>
    /// <param name="lease">The ownership lease acquired at the start of the pipeline.</param>
    /// <returns>A failed <see cref="Task{T}"/> wrapping a <see cref="Result{T}"/> around <paramref name="error"/>.</returns>
    private Task<Result<RuntimeProcessHostResult>> FailStartAndCleanup(
        ErrorInfo error,
        Process? startedProcess,
        IRuntimeJobObject? createdJobObject,
        RuntimeTransaction? transaction,
        RuntimeOwnershipLease lease)
    {
        if (startedProcess is not null)
        {
            BestEffortKillAndDispose(startedProcess);
        }

        if (createdJobObject is not null)
        {
            try
            {
                createdJobObject.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "FailStartAndCleanup: failed to dispose the job object.");
            }
        }

        if (transaction is not null)
        {
            try
            {
                transactionManager.Rollback(transaction);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "FailStartAndCleanup: transaction rollback failed.");
            }
        }

        try
        {
            lease.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "FailStartAndCleanup: failed to dispose the ownership lease.");
        }

        return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(error));
    }

    /// <summary>
    /// Tears down the partially-constructed state when
    /// <see cref="StartOnWorkerAsync"/> fails. Rolls the transaction back (if
    /// it was started and not yet committed), kills and disposes the
    /// process, disposes the job object, deletes the lock file (if
    /// it was written) and disposes the ownership lease. All steps
    /// swallow exceptions because the method is invoked from
    /// already-failing catch blocks.
    /// </summary>
    private void CleanupAfterStartFailure(
        Process? startedProcess,
        IRuntimeJobObject? createdJobObject,
        RuntimeTransaction? transaction,
        bool transactionCommitted,
        bool lockFileWritten,
        RuntimeOwnershipLease lease)
    {
        if (startedProcess is not null)
        {
            BestEffortKillAndDispose(startedProcess);
        }

        if (createdJobObject is not null)
        {
            try
            {
                createdJobObject.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "CleanupAfterStartFailure: failed to dispose the job object.");
            }
        }

        if (transaction is not null && !transactionCommitted)
        {
            try
            {
                transactionManager.Rollback(transaction);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "CleanupAfterStartFailure: transaction rollback failed.");
            }
        }

        if (lockFileWritten)
        {
            BestEffortDeleteLock();
        }

        try
        {
            lease.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "CleanupAfterStartFailure: failed to dispose the ownership lease.");
        }
    }

    /// <summary>
    /// Computes the lowercase hex SHA-256 digest of
    /// <c>executablePath + " " + joinedArgs</c>. The hash is the
    /// <c>CommandLineHash</c> recorded in the runtime lock metadata.
    /// </summary>
    /// <param name="executablePath">Absolute path of the runtime executable.</param>
    /// <param name="arguments">Joined command-line arguments (empty string when the plan has none).</param>
    /// <returns>Lowercase hex SHA-256 digest of the joined command line.</returns>
    private static string ComputeCommandLineHash(string executablePath, string arguments)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        string commandLine = arguments.Length == 0
            ? executablePath
            : executablePath + " " + arguments;

        byte[] bytes = Encoding.UTF8.GetBytes(commandLine);
        byte[] hashBytes = SHA256.HashData(bytes);

        StringBuilder builder = new(hashBytes.Length * 2);
        foreach (byte b in hashBytes)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Synchronously blocks on a <see cref="Task{T}"/> by unwrapping
    /// its result via <c>GetAwaiter().GetResult()</c>. Used in the
    /// start pipeline to preserve ownership-mutex thread affinity:
    /// an <c>await</c> may resume on a different thread, which would
    /// break <c>RuntimeOwnershipLease.Dispose()</c> cleanup paths.
    /// </summary>
    /// <typeparam name="T">Task result type.</typeparam>
    /// <param name="task">The task to wait for.</param>
    /// <returns>The task result.</returns>
    private static T RunSync<T>(Task<T> task) => task.GetAwaiter().GetResult();

    /// <summary>
    /// Synchronously blocks on a <see cref="Task"/>. See the
    /// generic overload for the rationale.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    private static void RunSync(Task task) => task.GetAwaiter().GetResult();
}

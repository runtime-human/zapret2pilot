using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
/// <see cref="StartAsync"/> runs the full start pipeline on the
/// calling thread to preserve ownership-mutex thread affinity:
/// acquire the global ownership mutex, perform stale lock recovery,
/// materialize the workspace, begin a <c>Start</c> transaction, create
/// a Windows Job Object with kill-on-close, launch the runtime
/// process, assign it to the job object, perform a readiness check,
/// write the recovery lock file, and finally commit the transaction.
/// Any failure between <c>BeginStart</c> and <c>Commit</c> rolls the
/// transaction back and tears down the partially-constructed
/// resources.
/// </para>
/// <para>
/// <see cref="StopAsync"/> reverses that pipeline on the calling
/// thread: it begins a <c>Stop</c> transaction, kills the running
/// process (if any), waits for it to exit (bounded by the
/// constructor-supplied <c>stopTimeout</c>), disposes the process and
/// the job object, deletes the lock file, disposes the ownership
/// lease and finally commits the transaction.
/// </para>
/// <para>
/// This host does NOT execute the real <c>winws2</c> binary; it
/// launches whatever path is supplied in
/// <see cref="RuntimeProcessStartContext.RuntimeExecutablePath"/>.
/// In milestone 0.0.17 the production wiring uses the
/// <c>Zapret2Pilot.Testing.FakeRuntime</c> test executable as a
/// placeholder until <c>winws2</c> launch is approved by oracle.
/// </para>
/// <para>
/// The host is not designed for concurrent <see cref="StartAsync"/>
/// or <see cref="StopAsync"/> calls. Callers should serialise access
/// to a single instance from a single thread.
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

    private readonly RuntimeOwnershipMutex ownershipMutex;
    private readonly RuntimeStaleLockRecovery staleLockRecovery;
    private readonly IRuntimeWorkspaceMaterializer workspaceMaterializer;
    private readonly IRuntimeTransactionManager transactionManager;
    private readonly IRuntimeJobObjectProcessAssigner jobObjectAssigner;
    private readonly RuntimeLockFileStore lockFileStore;
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
        TimeSpan? stopTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(ownershipMutex, nameof(ownershipMutex));
        ArgumentNullException.ThrowIfNull(staleLockRecovery, nameof(staleLockRecovery));
        ArgumentNullException.ThrowIfNull(workspaceMaterializer, nameof(workspaceMaterializer));
        ArgumentNullException.ThrowIfNull(transactionManager, nameof(transactionManager));
        ArgumentNullException.ThrowIfNull(jobObjectAssigner, nameof(jobObjectAssigner));
        ArgumentNullException.ThrowIfNull(lockFileStore, nameof(lockFileStore));

        this.ownershipMutex = ownershipMutex;
        this.staleLockRecovery = staleLockRecovery;
        this.workspaceMaterializer = workspaceMaterializer;
        this.transactionManager = transactionManager;
        this.jobObjectAssigner = jobObjectAssigner;
        this.lockFileStore = lockFileStore;
        this.stopTimeout = stopTimeout ?? DefaultStopTimeout;
        ownerInstanceId = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Starts the runtime process. Runs the full start pipeline on
    /// the calling thread to preserve ownership-mutex thread affinity.
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
    public Task<Result<RuntimeProcessHostResult>> StartAsync(
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
            return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "RuntimePlanCacheKeyMissing",
                message: "Cannot start the runtime: the compiled plan has no cache key.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime)));
        }

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
                materializeTask.GetAwaiter().GetResult();
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
            string executablePath = context.RuntimeExecutablePath;
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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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
                try { createdJobObject.Dispose(); } catch { /* best effort */ }
                try { lease.Dispose(); } catch { /* best effort */ }
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
    /// Stops the runtime process. Begins a <c>Stop</c> transaction,
    /// kills the running process (if any), waits for it to exit
    /// (bounded by the constructor-supplied <c>stopTimeout</c>),
    /// disposes the process and the job object, deletes the lock
    /// file, disposes the ownership lease and finally commits the
    /// transaction.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token observed before stopping.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with <see cref="Unit.Instance"/> on
    /// success, or a <see cref="ErrorInfo"/> on failure.
    /// </returns>
    public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
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

        try
        {
            // 1. Kill the process if it is still running, then wait.
            StopProcess(processToStop);

            // 2. Dispose the job object (triggers kill-on-close for any remaining process).
            if (jobObjectToDispose is not null)
            {
                try
                {
                    jobObjectToDispose.Dispose();
                }
                catch (RuntimeJobObjectException ex)
                {
                    try { transactionManager.Rollback(transaction); } catch { /* best effort */ }
                    return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                        code: "RuntimeJobObjectDisposeFailed",
                        message: $"Failed to dispose the job object: {ex.Message}",
                        severity: ErrorSeverity.Error,
                        category: ErrorCategory.Runtime)));
                }
            }

            // 3. Delete the lock file.
            try
            {
                lockFileStore.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { transactionManager.Rollback(transaction); } catch { /* best effort */ }
                return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                    code: "RuntimeLockDeleteFailed",
                    message: $"Failed to delete the runtime lock file: {ex.Message}",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Storage)));
            }

            // 4. Dispose the ownership lease.
            try
            {
                lease.Dispose();
            }
            catch (Exception ex) when (ex is RuntimeOwnershipThreadAffinityException or ObjectDisposedException)
            {
                try { transactionManager.Rollback(transaction); } catch { /* best effort */ }
                return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                    code: "RuntimeLeaseDisposeFailed",
                    message: $"Failed to dispose the ownership lease: {ex.Message}",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime)));
            }

            // 5. Commit the stop transaction.
            Result<Unit> commitResult = transactionManager.Commit(transaction);
            if (commitResult.IsFailure)
            {
                // Commit failed: best-effort rollback so the transaction
                // manager does not retain an active transaction.
                try { transactionManager.Rollback(transaction); } catch { /* best effort */ }
                return Task.FromResult(commitResult);
            }

            // 6. Clear state.
            lock (stateLock)
            {
                ownershipLease = null;
                process = null;
                jobObject = null;
            }

            return Task.FromResult(Result.Success(Unit.Instance));
        }
        catch
        {
            try
            {
                transactionManager.Rollback(transaction);
            }
            catch
            {
                // best effort
            }

            throw;
        }
    }

    /// <summary>
    /// Disposes the host. Best-effort cleans up any running process,
    /// job object, lock file and ownership lease. Idempotent.
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
            catch
            {
                // best effort
            }
        }

        try
        {
            lockFileStore.Delete();
        }
        catch
        {
            // best effort
        }

        if (lease is not null)
        {
            try
            {
                lease.Dispose();
            }
            catch
            {
                // best effort
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
    /// Kills <paramref name="processToStop"/> if it is still running,
    /// waits for it to exit (bounded by <c>stopTimeout</c>) and
    /// disposes it. All exceptions are swallowed because the method
    /// is used from best-effort cleanup paths.
    /// </summary>
    /// <param name="processToStop">The process to terminate and dispose.</param>
    private void StopProcess(Process processToStop)
    {
        try
        {
            if (!processToStop.HasExited)
            {
                processToStop.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already disposed or never started
        }
        catch (Win32Exception)
        {
            // race or access denied
        }

        try
        {
            processToStop.WaitForExit((int)stopTimeout.TotalMilliseconds);
        }
        catch (InvalidOperationException)
        {
            // already disposed
        }
        catch (Win32Exception)
        {
            // race
        }

        try
        {
            processToStop.Dispose();
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>
    /// Best-effort kill + dispose of a process that may not have
    /// been fully started or assigned to the job object. Used by the
    /// failure paths in <see cref="StartAsync"/>.
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
    /// <see cref="StartAsync"/> and <see cref="Dispose"/>.
    /// </summary>
    private void BestEffortDeleteLock()
    {
        try
        {
            lockFileStore.Delete();
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>
    /// Performs the standard partial-state cleanup for a
    /// <see cref="StartAsync"/> failure path and returns the
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
            catch
            {
                // best effort
            }
        }

        if (transaction is not null)
        {
            try
            {
                transactionManager.Rollback(transaction);
            }
            catch
            {
                // best effort
            }
        }

        try
        {
            lease.Dispose();
        }
        catch
        {
            // best effort
        }

        return Task.FromResult(Result.Failure<RuntimeProcessHostResult>(error));
    }

    /// <summary>
    /// Tears down the partially-constructed state when
    /// <see cref="StartAsync"/> fails. Rolls the transaction back (if
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
            catch
            {
                // best effort
            }
        }

        if (transaction is not null && !transactionCommitted)
        {
            try
            {
                transactionManager.Rollback(transaction);
            }
            catch
            {
                // best effort
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
        catch
        {
            // best effort
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

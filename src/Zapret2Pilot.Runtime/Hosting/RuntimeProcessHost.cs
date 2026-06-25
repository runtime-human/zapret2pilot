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
/// the in-process safety primitives and the operating system process model.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StartAsync"/> runs the start pipeline on the calling thread
/// to preserve ownership-mutex thread affinity. The executable path is not
/// caller-controlled: it is derived from the manifest executable entry after
/// successful manifest verification inside the workspace root.
/// </para>
/// <para>
/// <see cref="StopAsync"/> treats process termination as irreversible. After
/// the process has been stopped and resources are being cleaned up, cleanup
/// failures are surfaced to the caller but the host state is cleared and the
/// transaction manager is not rolled back to <c>Running</c>.
/// </para>
/// <para>
/// The host is not designed for concurrent <see cref="StartAsync"/> or
/// <see cref="StopAsync"/> calls. Callers should serialise access to a
/// single instance from a single thread.
/// </para>
/// </remarks>
public sealed class RuntimeProcessHost : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);

    private readonly RuntimeOwnershipMutex ownershipMutex;
    private readonly RuntimeStaleLockRecovery staleLockRecovery;
    private readonly IRuntimeWorkspaceMaterializer workspaceMaterializer;
    private readonly IRuntimeTransactionManager transactionManager;
    private readonly IRuntimeJobObjectProcessAssigner jobObjectAssigner;
    private readonly IRuntimeLockFileStore lockFileStore;
    private readonly TimeSpan stopTimeout;
    private readonly string ownerInstanceId;

    private readonly object stateLock = new();
    private RuntimeOwnershipLease? ownershipLease;
    private Process? process;
    private IRuntimeJobObject? jobObject;
    private bool disposed;

    public RuntimeProcessHost(
        RuntimeOwnershipMutex ownershipMutex,
        RuntimeStaleLockRecovery staleLockRecovery,
        IRuntimeWorkspaceMaterializer workspaceMaterializer,
        IRuntimeTransactionManager transactionManager,
        IRuntimeJobObjectProcessAssigner jobObjectAssigner,
        IRuntimeLockFileStore lockFileStore,
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

    public Task<Result<RuntimeProcessHostResult>> StartAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        cancellationToken.ThrowIfCancellationRequested();

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

            staleLockRecovery.RecoverAfterOwnershipAcquired(lease);

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

            RuntimeExecutableValidator executableValidator = new(materializeResult.Value.WorkspaceDirectory);
            Result<VerifiedRuntimeExecutable> executableResult = RunSync(
                executableValidator.VerifyAsync(context.Manifest, cancellationToken));
            if (executableResult.IsFailure)
            {
                return FailStartAndCleanup(executableResult.Error, startedProcess: null, createdJobObject: null, transaction: null, lease);
            }

            VerifiedRuntimeExecutable executable = executableResult.Value;

            Result<RuntimeTransaction> beginResult = transactionManager.BeginStart(context.Plan);
            if (beginResult.IsFailure)
            {
                return FailStartAndCleanup(beginResult.Error, startedProcess: null, createdJobObject: null, transaction: null, lease);
            }
            transaction = beginResult.Value;

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

            string executablePath = executable.FullPath;
            if (!File.Exists(executablePath))
            {
                ErrorInfo missingError = new(
                    code: "RuntimeExecutableMissing",
                    message: $"Cannot start the runtime: executable file does not exist: {executable.ManifestRelativePath}.",
                    severity: ErrorSeverity.Error,
                    category: ErrorCategory.Runtime);
                return FailStartAndCleanup(missingError, startedProcess: null, createdJobObject, transaction, lease);
            }

            string arguments = context.Plan.Arguments is null
                ? string.Empty
                : string.Join(" ", context.Plan.Arguments);

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = materializeResult.Value.WorkspaceDirectory,
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
        ErrorInfo? cleanupError = null;

        StopProcess(processToStop);

        try
        {
            jobObjectToDispose?.Dispose();
        }
        catch (Exception ex)
        {
            cleanupError ??= new ErrorInfo(
                code: "RuntimeJobObjectDisposeFailed",
                message: $"Runtime process was stopped, but job object cleanup failed: {ex.Message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);
        }

        try
        {
            lockFileStore.Delete();
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or InvalidOperationException
                                   or NotSupportedException
                                   or ArgumentException)
        {
            cleanupError ??= new ErrorInfo(
                code: "RuntimeLockDeleteFailed",
                message: $"Runtime process was stopped, but lock file cleanup failed: {ex.Message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Storage);
        }

        try
        {
            lease.Dispose();
        }
        catch (Exception ex) when (ex is RuntimeOwnershipThreadAffinityException or ObjectDisposedException)
        {
            cleanupError ??= new ErrorInfo(
                code: "RuntimeLeaseDisposeFailed",
                message: $"Runtime process was stopped, but ownership lease cleanup failed: {ex.Message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);
        }

        lock (stateLock)
        {
            ownershipLease = null;
            process = null;
            jobObject = null;
        }

        Result<Unit> commitResult = transactionManager.Commit(transaction);
        if (commitResult.IsFailure)
        {
            return Task.FromResult(commitResult);
        }

        if (cleanupError is not null)
        {
            return Task.FromResult(Result.Failure<Unit>(cleanupError));
        }

        return Task.FromResult(Result.Success(Unit.Instance));
    }

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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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

    private void BestEffortKillAndDispose(Process? processToDispose)
    {
        if (processToDispose is null)
        {
            return;
        }

        StopProcess(processToDispose);
    }

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

    private static T RunSync<T>(Task<T> task) => task.GetAwaiter().GetResult();
}

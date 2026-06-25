using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.FileSystem;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Infrastructure.FileSystem;

namespace Zapret2Pilot.Runtime.Workspace;

/// <summary>
/// Production implementation of <see cref="IRuntimeWorkspaceMaterializer"/>.
/// Writes the generated config, the args file and each hostlist payload into
/// the workspace directory using <see cref="AtomicFileWriter"/>. All paths
/// are resolved through an internal <see cref="SafePathResolver"/> rooted at
/// the workspace directory so that no caller-supplied path can ever escape
/// the workspace.
///
/// The materializer first verifies the manifest using
/// <see cref="ZapretAssetVerifier"/> with the same resolver. The verifier
/// must run on the assets root (which for 0.0.12 is the workspace root)
/// before any file is written, so a missing or tampered asset can never
/// reach the workspace.
/// </summary>
public sealed class RuntimeWorkspaceMaterializer : IRuntimeWorkspaceMaterializer
{
    internal const string GeneratedConfigRelativePath = "generated.cfg";
    internal const string ArgsFileRelativePath = "args.txt";
    internal const string HostlistsDirectoryRelativePath = "hostlists";

    private readonly ISafePathResolver pathResolver;

    /// <summary>
    /// Creates a new materializer. <paramref name="pathResolver"/> must be
    /// rooted at the same directory that will be passed as
    /// <c>workspaceDirectory</c> to <see cref="MaterializeAsync"/>.
    /// </summary>
    /// <param name="pathResolver">
    /// Safe path resolver rooted at the workspace directory.
    /// </param>
    public RuntimeWorkspaceMaterializer(ISafePathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver, nameof(pathResolver));

        this.pathResolver = pathResolver;
    }

    /// <summary>
    /// Convenience constructor that builds a <see cref="SafePathResolver"/>
    /// rooted at <paramref name="workspaceDirectory"/>. The directory is not
    /// created by this constructor; <see cref="MaterializeAsync"/> creates
    /// it on demand.
    /// </summary>
    public static RuntimeWorkspaceMaterializer CreateForRoot(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory, nameof(workspaceDirectory));

        return new RuntimeWorkspaceMaterializer(new SafePathResolver(workspaceDirectory));
    }

    public async Task<Result<RuntimeWorkspaceMaterializeResult>> MaterializeAsync(
        CompiledZapretPlan plan,
        ZapretAssetManifest manifest,
        string workspaceDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan, nameof(plan));
        ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory, nameof(workspaceDirectory));
        cancellationToken.ThrowIfCancellationRequested();

        string fullWorkspaceDirectory;
        try
        {
            fullWorkspaceDirectory = Path.GetFullPath(workspaceDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return Result.Failure<RuntimeWorkspaceMaterializeResult>(
                new ErrorInfo(
                    "WorkspacePathInvalid",
                    $"Workspace directory path is invalid: {workspaceDirectory}.",
                    ErrorSeverity.Error,
                    ErrorCategory.Storage));
        }

        Directory.CreateDirectory(fullWorkspaceDirectory);

        ZapretAssetVerifier verifier = new(pathResolver);
        Result<ZapretAssetVerificationSummary> verificationResult =
            await verifier.VerifyAsync(manifest, cancellationToken).ConfigureAwait(false);

        if (verificationResult.IsFailure)
        {
            return Result.Failure<RuntimeWorkspaceMaterializeResult>(
                new ErrorInfo(
                    "WorkspaceAssetVerificationFailed",
                    $"Asset verification failed: {verificationResult.Error.Message}",
                    ErrorSeverity.Error,
                    verificationResult.Error.Category));
        }

        ZapretAssetVerificationSummary verificationSummary = verificationResult.Value;

        List<string> writtenHostlistPaths = new();

        try
        {
            string generatedConfigPath = pathResolver.ResolveFilePath(GeneratedConfigRelativePath);
            AtomicFileWriter.WriteAllText(generatedConfigPath, plan.GeneratedConfigContent);

            string argsFilePath = pathResolver.ResolveFilePath(ArgsFileRelativePath);
            AtomicFileWriter.WriteAllText(argsFilePath, plan.ArgsContent);

            if (plan.Hostlists is not null)
            {
                foreach (CompiledZapretPlan.HostlistContent hostlist in plan.Hostlists)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string combinedRelativePath = string.Concat(
                        HostlistsDirectoryRelativePath,
                        Path.DirectorySeparatorChar,
                        hostlist.RelativePath);

                    string hostlistPath = pathResolver.ResolveFilePath(combinedRelativePath);
                    AtomicFileWriter.WriteAllText(hostlistPath, hostlist.Content);
                    writtenHostlistPaths.Add(hostlistPath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result.Failure<RuntimeWorkspaceMaterializeResult>(
                new ErrorInfo(
                    "WorkspacePathUnsafe",
                    $"Workspace path is unsafe: {ex.Message}",
                    ErrorSeverity.Error,
                    ErrorCategory.Runtime));
        }
        catch (IOException ex)
        {
            return Result.Failure<RuntimeWorkspaceMaterializeResult>(
                new ErrorInfo(
                    "WorkspaceIoFailure",
                    $"Failed to write workspace file: {ex.Message}",
                    ErrorSeverity.Error,
                    ErrorCategory.Storage));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure<RuntimeWorkspaceMaterializeResult>(
                new ErrorInfo(
                    "WorkspaceAccessDenied",
                    $"Access denied while writing workspace: {ex.Message}",
                    ErrorSeverity.Error,
                    ErrorCategory.Storage));
        }

        return Result.Success(new RuntimeWorkspaceMaterializeResult(
            workspaceDirectory: fullWorkspaceDirectory,
            argsFilePath: pathResolver.ResolveFilePath(ArgsFileRelativePath),
            generatedConfigPath: pathResolver.ResolveFilePath(GeneratedConfigRelativePath),
            writtenHostlistPaths: writtenHostlistPaths,
            verificationSummary: verificationSummary));
    }
}

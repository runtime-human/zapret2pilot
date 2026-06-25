using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Infrastructure.FileSystem;

namespace Zapret2Pilot.Runtime.Hosting;

public sealed class RuntimeExecutableValidator
{
    private readonly SafePathResolver pathResolver;

    public RuntimeExecutableValidator(string allowedRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRootDirectory);

        pathResolver = new SafePathResolver(allowedRootDirectory);
    }

    public async Task<Result<VerifiedRuntimeExecutable>> VerifyAsync(
        ZapretAssetManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        if (manifest.RuntimeExecutable.Kind != AssetKind.Executable)
        {
            return Result.Failure<VerifiedRuntimeExecutable>(new ErrorInfo(
                code: "RuntimeExecutableManifestKindInvalid",
                message: "Cannot start the runtime: the manifest runtime asset is not an executable.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        ZapretAssetVerifier verifier = new(pathResolver);
        Result<ZapretAssetVerificationSummary> verificationResult =
            await verifier.VerifyAsync(manifest, cancellationToken).ConfigureAwait(false);

        if (verificationResult.IsFailure)
        {
            return Result.Failure<VerifiedRuntimeExecutable>(new ErrorInfo(
                code: "RuntimeExecutableManifestVerificationFailed",
                message: $"Cannot start the runtime: executable manifest verification failed: {verificationResult.Error.Message}",
                severity: ErrorSeverity.Error,
                category: verificationResult.Error.Category));
        }

        ZapretAssetVerificationSummary summary = verificationResult.Value;
        if (!summary.VerifiedRelativePaths.Any(path =>
                string.Equals(path, manifest.RuntimeExecutable.RelativePath, StringComparison.Ordinal)))
        {
            return Result.Failure<VerifiedRuntimeExecutable>(new ErrorInfo(
                code: "RuntimeExecutableNotVerified",
                message: "Cannot start the runtime: the manifest executable was not present in the verification summary.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        string executablePath;
        try
        {
            executablePath = pathResolver.ResolveFilePath(manifest.RuntimeExecutable.RelativePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result.Failure<VerifiedRuntimeExecutable>(new ErrorInfo(
                code: "RuntimeExecutablePathUnsafe",
                message: $"Cannot start the runtime: executable path is outside the allowed root: {ex.Message}",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        if (!File.Exists(executablePath))
        {
            return Result.Failure<VerifiedRuntimeExecutable>(new ErrorInfo(
                code: "RuntimeExecutableMissing",
                message: $"Cannot start the runtime: executable file does not exist: {manifest.RuntimeExecutable.RelativePath}.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        return Result.Success(new VerifiedRuntimeExecutable(
            executablePath,
            manifest.RuntimeExecutable.RelativePath));
    }
}

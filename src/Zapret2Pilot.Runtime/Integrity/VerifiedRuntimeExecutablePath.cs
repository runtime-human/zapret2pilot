using System;
using System.IO;
using System.Linq;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Engine.Zapret2.Assets;

namespace Zapret2Pilot.Runtime.Integrity;

/// <summary>
/// Verified absolute path of the runtime executable to launch. This is the
/// single value-object the runtime kernel trusts when starting
/// <c>winws2</c> (or its placeholder fake): a value of this type can only
/// be obtained by combining an asset manifest with a passing
/// <see cref="ZapretAssetVerificationSummary"/> and by re-confirming that
/// the resolved file is still on disk.
/// </summary>
/// <remarks>
/// <para>
/// Construction is intentionally one-way: callers cannot fabricate a
/// <see cref="VerifiedRuntimeExecutablePath"/> from a raw string. The
/// <see cref="TryCreate"/> factory re-checks the relative path declared in
/// the manifest against the verification summary (case-insensitive) and
/// re-confirms the resolved absolute path exists on disk. Any failure
/// returns a <see cref="Result{T}"/> failure carrying an
/// <see cref="ErrorInfo"/> with a stable error code.
/// </para>
/// <para>
/// This type closes roadmap items P0-4 (verified executable integrity
/// binding) and P1-1 (start-context XML documentation promise) and is
/// the only input <c>RuntimeProcessHost</c> accepts for the runtime
/// executable path.
/// </para>
/// </remarks>
public sealed class VerifiedRuntimeExecutablePath
{
    private VerifiedRuntimeExecutablePath(string absolutePath)
    {
        AbsolutePath = absolutePath;
    }

    /// <summary>
    /// Absolute, on-disk path of the runtime executable. Guaranteed to
    /// have been present in the associated
    /// <see cref="ZapretAssetVerificationSummary.VerifiedRelativePaths"/>
    /// and to exist on the filesystem at the moment
    /// <see cref="TryCreate"/> ran.
    /// </summary>
    public string AbsolutePath { get; }

    /// <summary>
    /// Attempts to construct a <see cref="VerifiedRuntimeExecutablePath"/>
    /// from a manifest, its passing verification summary, and the assets
    /// root directory the verifier was rooted at.
    /// </summary>
    /// <param name="manifest">
    /// Manifest that declares the runtime executable relative path.
    /// Must be non-null and must declare a non-empty
    /// <see cref="ZapretRuntimeAsset.RelativePath"/>.
    /// </param>
    /// <param name="summary">
    /// Passing <see cref="ZapretAssetVerificationSummary"/> produced by
    /// <see cref="ZapretAssetVerifier"/>. The manifest's runtime
    /// executable relative path must be present in
    /// <see cref="ZapretAssetVerificationSummary.VerifiedRelativePaths"/>.
    /// </param>
    /// <param name="assetsRootDirectory">
    /// Absolute path of the directory the verifier was rooted at. The
    /// runtime executable absolute path is resolved relative to this
    /// directory using <see cref="Path.GetFullPath(Path.Combine(string, string))"/>.
    /// Must be non-null, non-whitespace and resolvable.
    /// </param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> wrapping the new
    /// <see cref="VerifiedRuntimeExecutablePath"/> on success; a failure
    /// carrying an <see cref="ErrorInfo"/> with one of the
    /// <c>RuntimeExecutable*</c> error codes on validation failure.
    /// </returns>
    public static Result<VerifiedRuntimeExecutablePath> TryCreate(
        ZapretAssetManifest manifest,
        ZapretAssetVerificationSummary summary,
        string assetsRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));
        ArgumentNullException.ThrowIfNull(summary, nameof(summary));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRootDirectory, nameof(assetsRootDirectory));

        if (manifest.RuntimeExecutable is null
            || string.IsNullOrWhiteSpace(manifest.RuntimeExecutable.RelativePath))
        {
            return Result.Failure<VerifiedRuntimeExecutablePath>(new ErrorInfo(
                code: "RuntimeExecutableRelativePathMissing",
                message: "Manifest does not declare a runtime executable relative path.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        string relativePath = manifest.RuntimeExecutable.RelativePath;

        if (summary.VerifiedRelativePaths is null
            || !summary.VerifiedRelativePaths.Any(p => string.Equals(p, relativePath, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<VerifiedRuntimeExecutablePath>(new ErrorInfo(
                code: "RuntimeExecutableNotVerified",
                message: $"Runtime executable relative path '{relativePath}' was not present in the asset verification summary.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        string absolutePath;
        try
        {
            absolutePath = Path.GetFullPath(Path.Combine(assetsRootDirectory, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return Result.Failure<VerifiedRuntimeExecutablePath>(new ErrorInfo(
                code: "RuntimeExecutablePathInvalid",
                message: $"Runtime executable path is invalid: '{relativePath}' under '{assetsRootDirectory}'.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        if (!File.Exists(absolutePath))
        {
            return Result.Failure<VerifiedRuntimeExecutablePath>(new ErrorInfo(
                code: "RuntimeExecutableMissing",
                message: $"Runtime executable is missing on disk: '{absolutePath}'.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));
        }

        return Result.Success(new VerifiedRuntimeExecutablePath(absolutePath));
    }
}

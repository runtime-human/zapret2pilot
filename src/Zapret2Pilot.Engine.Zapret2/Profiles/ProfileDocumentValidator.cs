using System;
using System.Collections.Generic;
using System.IO;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Engine.Zapret2.Profiles;

/// <summary>
/// Pure lexical/structural validator for <see cref="ProfileDocument"/>.
/// Does not access the filesystem and does not depend on
/// <see cref="Zapret2Pilot.Core.FileSystem.ISafePathResolver"/>.
/// Returns the first detected failure (fail-fast) wrapped in
/// <see cref="Result"/>.
/// </summary>
public sealed class ProfileDocumentValidator
{
    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    /// <summary>
    /// Validates <paramref name="document"/> structurally. Returns
    /// <see cref="Result.Success()"/> when the document is well-formed, or a
    /// failed <see cref="Result"/> carrying an <see cref="ErrorInfo"/> that
    /// describes the first detected problem.
    /// </summary>
    /// <param name="document">Profile document to validate. May be null.</param>
    /// <returns>Success, or a failure describing the first issue found.</returns>
#pragma warning disable CA1822 // Mark members as static; intentionally instance per the planned public API.
    public Result Validate(ProfileDocument? document)
    {
        if (document is null)
        {
            return Result.Failure(
                new ErrorInfo(
                    "ProfileDocumentMissing",
                    "Profile document is required.",
                    ErrorSeverity.Error,
                    ErrorCategory.Validation));
        }

        if (document.Id is null)
        {
            return Result.Failure(
                new ErrorInfo(
                    "ProfileIdMissing",
                    "Profile id is required.",
                    ErrorSeverity.Error,
                    ErrorCategory.Validation));
        }

        if (string.IsNullOrWhiteSpace(document.DisplayName))
        {
            return Result.Failure(
                new ErrorInfo(
                    "ProfileDisplayNameMissing",
                    "Profile display name is required and must not be whitespace.",
                    ErrorSeverity.Error,
                    ErrorCategory.Validation));
        }

        if (document.StrategyReferences is null)
        {
            return Result.Failure(
                new ErrorInfo(
                    "StrategyReferencesMissing",
                    "Profile strategy references collection is required.",
                    ErrorSeverity.Error,
                    ErrorCategory.Validation));
        }

        if (document.HostlistReferences is null)
        {
            return Result.Failure(
                new ErrorInfo(
                    "HostlistReferencesMissing",
                    "Profile hostlist references collection is required.",
                    ErrorSeverity.Error,
                    ErrorCategory.Validation));
        }

        Result strategyValidation = ValidateStrategyReferences(document.StrategyReferences);
        if (strategyValidation.IsFailure)
        {
            return strategyValidation;
        }

        return ValidateHostlistReferences(document.HostlistReferences);
    }
#pragma warning restore CA1822

    private static Result ValidateStrategyReferences(
        IReadOnlyList<StrategyReference> references)
    {
        // Validity is checked before duplication so that a null PackId or
        // blank StrategyName is reported as a structural problem rather than
        // as a (false-positive) duplicate of another null/blank reference.
        foreach (StrategyReference reference in references)
        {
            if (!IsStrategyReferenceValid(reference))
            {
                return Result.Failure(
                    new ErrorInfo(
                        "StrategyReferenceInvalid",
                        "Strategy reference must specify a non-null pack id and a non-blank strategy name.",
                        ErrorSeverity.Error,
                        ErrorCategory.Validation));
            }
        }

        HashSet<StrategyReference> seen = new(StrategyReferenceComparer.Instance);
        foreach (StrategyReference reference in references)
        {
            if (!seen.Add(reference))
            {
                return Result.Failure(
                    new ErrorInfo(
                        "DuplicateStrategyReference",
                        $"Strategy reference is duplicated: pack '{reference.PackId}', strategy '{reference.StrategyName}'.",
                        ErrorSeverity.Error,
                        ErrorCategory.Validation));
            }
        }

        return Result.Success();
    }

    private static bool IsStrategyReferenceValid(StrategyReference? reference)
    {
        if (reference is null)
        {
            return false;
        }

        if (reference.PackId is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(reference.StrategyName);
    }

    private static Result ValidateHostlistReferences(
        IReadOnlyList<HostlistReference> references)
    {
        foreach (HostlistReference reference in references)
        {
            if (!IsHostlistReferenceValid(reference))
            {
                return Result.Failure(
                    new ErrorInfo(
                        "HostlistReferenceInvalid",
                        "Hostlist reference must specify a non-null hostlist id and a non-blank relative path.",
                        ErrorSeverity.Error,
                        ErrorCategory.Validation));
            }
        }

        foreach (HostlistReference reference in references)
        {
            string path = reference.RelativePath!;
            if (IsHostlistPathUnsafe(path))
            {
                return Result.Failure(
                    new ErrorInfo(
                        "HostlistPathUnsafe",
                        $"Hostlist relative path is unsafe: '{path}'.",
                        ErrorSeverity.Error,
                        ErrorCategory.Security));
            }
        }

        return Result.Success();
    }

    private static bool IsHostlistReferenceValid(HostlistReference? reference)
    {
        if (reference is null)
        {
            return false;
        }

        if (reference.HostlistId is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(reference.RelativePath);
    }

    private static bool IsHostlistPathUnsafe(string path)
    {
        // Parent-directory traversal. Spec requires detection "as a path
        // segment or substring", so a plain substring check is sufficient.
        if (path.Contains("..", StringComparison.Ordinal))
        {
            return true;
        }

        // Rooted on Unix ('/...') or rooted/UNC-like on Windows ('\...').
        if (path.StartsWith('/') || path.StartsWith('\\'))
        {
            return true;
        }

        // Any drive-letter pattern ('X:').
        if (path.Contains(':', StringComparison.Ordinal))
        {
            return true;
        }

        // Platform-defined invalid path characters.
        if (path.IndexOfAny(InvalidPathChars) >= 0)
        {
            return true;
        }

        // Wildcard metacharacters must never appear in a profile-declared
        // hostlist path: a profile must address a concrete file.
        if (path.Contains('*', StringComparison.Ordinal) ||
            path.Contains('?', StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Equality for <see cref="StrategyReference"/> with ordinal,
    /// case-sensitive comparison of <see cref="StrategyReference.StrategyName"/>
    /// and record-class value equality of
    /// <see cref="StrategyReference.PackId"/>.
    /// </summary>
    private sealed class StrategyReferenceComparer : IEqualityComparer<StrategyReference>
    {
        public static readonly StrategyReferenceComparer Instance = new();

        public bool Equals(StrategyReference? x, StrategyReference? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (!Equals(x.PackId, y.PackId))
            {
                return false;
            }

            return string.Equals(x.StrategyName, y.StrategyName, StringComparison.Ordinal);
        }

        public int GetHashCode(StrategyReference obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            int packHash = obj.PackId?.GetHashCode() ?? 0;
            int nameHash = obj.StrategyName is null
                ? 0
                : StringComparer.Ordinal.GetHashCode(obj.StrategyName);

            return HashCode.Combine(packHash, nameHash);
        }
    }
}

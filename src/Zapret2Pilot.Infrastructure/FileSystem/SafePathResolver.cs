using System;
using System.IO;
using Zapret2Pilot.Core.FileSystem;

namespace Zapret2Pilot.Infrastructure.FileSystem;

public sealed class SafePathResolver : ISafePathResolver
{
    private readonly string rootDirectory;

    public SafePathResolver(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        this.rootDirectory = EnsureTrailingSeparator(Path.GetFullPath(rootDirectory));
    }

    public string ResolveFilePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string normalizedRelativePath = NormalizeSeparators(relativePath);

        if (Path.IsPathFullyQualified(relativePath)
            || Path.IsPathFullyQualified(normalizedRelativePath)
            || Path.IsPathRooted(relativePath)
            || Path.IsPathRooted(normalizedRelativePath)
            || IsWindowsFullyQualifiedPath(relativePath))
        {
            throw new InvalidOperationException("Only relative paths are allowed.");
        }

        if (normalizedRelativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Colon characters are not allowed in relative paths.");
        }

        RejectParentTraversalSegments(normalizedRelativePath);

        string fullPath = Path.GetFullPath(normalizedRelativePath, rootDirectory);

        if (!fullPath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path is outside the allowed root directory.");
        }

        return fullPath;
    }

    private static string NormalizeSeparators(string path)
    {
        char separator = Path.DirectorySeparatorChar;

        return path
            .Replace('\\', separator)
            .Replace('/', separator);
    }

    private static void RejectParentTraversalSegments(string normalizedRelativePath)
    {
        string[] segments = normalizedRelativePath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Parent traversal segments are not allowed.");
            }
        }
    }

    private static bool IsWindowsFullyQualifiedPath(string path)
    {
        if (path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (Path.EndsInDirectorySeparator(path))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}

namespace Zapret2Pilot.Core.FileSystem;

/// <summary>
/// Abstraction for resolving user-supplied relative paths against an allowed root
/// directory while rejecting absolute paths, parent traversal, drive-relative paths
/// and other unsafe path shapes. Implemented by Infrastructure's
/// <c>SafePathResolver</c> so that engine adapters can stay free of Infrastructure.
/// </summary>
public interface ISafePathResolver
{
    /// <summary>
    /// Resolves <paramref name="relativePath"/> to a fully qualified path that
    /// must lie inside the configured root directory. Implementations must
    /// reject absolute paths, rooted paths, parent traversal segments and other
    /// unsafe path shapes.
    /// </summary>
    /// <param name="relativePath">A relative path inside the allowed root.</param>
    /// <returns>The fully qualified path inside the root directory.</returns>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="relativePath"/> is null, empty or whitespace.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when the path is unsafe (absolute, rooted, contains a parent
    /// traversal segment, is a Windows drive-relative path, or escapes the
    /// allowed root for any other reason).
    /// </exception>
    string ResolveFilePath(string relativePath);
}

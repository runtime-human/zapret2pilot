namespace Zapret2Pilot.Runtime.Detection;

/// <summary>
/// Testable seam over the host process system. The production implementation
/// is <see cref="WindowsProcessSystemAccessor"/>; unit tests provide a fake.
/// </summary>
public interface IProcessSystemAccessor
{
    /// <summary>
    /// Attempts to read an immutable snapshot of the process identified by
    /// <paramref name="processId"/>. Returns <c>false</c> when the process
    /// no longer exists or the accessor cannot read its metadata; in that
    /// case <paramref name="snapshot"/> is <c>null</c>.
    /// </summary>
    bool TryGetProcessById(int processId, out IProcessSnapshot? snapshot);
}

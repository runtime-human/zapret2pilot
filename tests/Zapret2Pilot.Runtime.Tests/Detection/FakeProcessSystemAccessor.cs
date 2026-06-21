using System.Collections.Generic;
using Zapret2Pilot.Runtime.Detection;

namespace Zapret2Pilot.Runtime.Tests.Detection;

/// <summary>
/// Test-only <see cref="IProcessSystemAccessor"/> that returns a registered
/// snapshot for a given process id, or <c>false</c> when the process id is
/// not registered (mimicking a missing/terminated process).
/// </summary>
internal sealed class FakeProcessSystemAccessor : IProcessSystemAccessor
{
    private readonly Dictionary<int, IProcessSnapshot> snapshots = new();

    public IReadOnlyDictionary<int, IProcessSnapshot> RegisteredSnapshots => snapshots;

    public void Register(int processId, IProcessSnapshot snapshot)
    {
        snapshots[processId] = snapshot;
    }

    public bool TryGetProcessById(int processId, out IProcessSnapshot? snapshot)
    {
        if (snapshots.TryGetValue(processId, out IProcessSnapshot? found))
        {
            snapshot = found;
            return true;
        }

        snapshot = null;
        return false;
    }
}

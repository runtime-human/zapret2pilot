using System;

namespace Zapret2Pilot.App.Threading;

/// <summary>
/// Synchronous UI scheduler used by the initial shell navigation foundation.
/// </summary>
public sealed class ImmediateUiScheduler : IUiScheduler
{
    public void Schedule(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        action();
    }
}

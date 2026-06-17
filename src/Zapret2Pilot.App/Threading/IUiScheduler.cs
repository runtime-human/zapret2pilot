using System;

namespace Zapret2Pilot.App.Threading;

/// <summary>
/// Schedules UI-bound work.
/// </summary>
public interface IUiScheduler
{
    void Schedule(Action action);
}

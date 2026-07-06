using System;
using Avalonia.Threading;

namespace Zapret2Pilot.App.Threading;

public sealed class AvaloniaUiScheduler : IUiScheduler
{
    public void Schedule(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Dispatcher.UIThread.Post(action);
    }
}

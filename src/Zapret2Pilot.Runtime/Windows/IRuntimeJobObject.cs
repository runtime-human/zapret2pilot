using System;

namespace Zapret2Pilot.Runtime.Windows;

public interface IRuntimeJobObject : IDisposable
{
    bool KillOnCloseConfigured { get; }

    bool IsDisposed { get; }
}

using System;
using Microsoft.Win32.SafeHandles;

namespace Zapret2Pilot.Runtime.Windows;

internal sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeJobObjectHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return WindowsJobObjectNativeMethods.CloseHandle(handle);
    }
}

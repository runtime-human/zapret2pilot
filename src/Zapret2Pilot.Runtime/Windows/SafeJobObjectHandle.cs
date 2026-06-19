using System;
using Microsoft.Win32.SafeHandles;

namespace Zapret2Pilot.Runtime.Windows;

internal sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeJobObjectHandle()
        : base(ownsHandle: true)
    {
    }

    internal SafeJobObjectHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return WindowsJobObjectNativeMethods.CloseHandle(handle);
    }
}

using System.Runtime.InteropServices;

namespace Zapret2Pilot.Runtime.Windows;

internal sealed class JobObjectNativeApi : IJobObjectNativeApi
{
    public bool TryAssignProcessToJobObject(
        SafeJobObjectHandle jobHandle,
        RuntimeProcessHandle processHandle,
        out int nativeErrorCode)
    {
        bool assigned = WindowsJobObjectNativeMethods.AssignProcessToJobObject(
            jobHandle,
            processHandle.Value);

        if (!assigned)
        {
            nativeErrorCode = Marshal.GetLastPInvokeError();
            return false;
        }

        nativeErrorCode = 0;
        return true;
    }
}

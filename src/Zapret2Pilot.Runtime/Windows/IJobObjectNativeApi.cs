namespace Zapret2Pilot.Runtime.Windows;

internal interface IJobObjectNativeApi
{
    bool TryAssignProcessToJobObject(
        SafeJobObjectHandle jobHandle,
        RuntimeProcessHandle processHandle,
        out int nativeErrorCode);
}

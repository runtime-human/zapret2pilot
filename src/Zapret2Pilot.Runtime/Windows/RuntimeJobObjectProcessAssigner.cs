using System;

namespace Zapret2Pilot.Runtime.Windows;

public sealed class RuntimeJobObjectProcessAssigner : IRuntimeJobObjectProcessAssigner
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidHandle = 6;

    private readonly IJobObjectNativeApi nativeApi;

    public RuntimeJobObjectProcessAssigner()
        : this(new JobObjectNativeApi())
    {
    }

    internal RuntimeJobObjectProcessAssigner(IJobObjectNativeApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);

        this.nativeApi = nativeApi;
    }

    public RuntimeJobObjectAssignmentResult Assign(
        IRuntimeJobObject jobObject,
        RuntimeProcessHandle processHandle)
    {
        ArgumentNullException.ThrowIfNull(jobObject);

        if (!OperatingSystem.IsWindows())
        {
            return RuntimeJobObjectAssignmentResult.UnsupportedPlatform();
        }

        if (jobObject.IsDisposed)
        {
            return RuntimeJobObjectAssignmentResult.Failed(0);
        }

        if (processHandle.IsInvalid)
        {
            return RuntimeJobObjectAssignmentResult.InvalidHandle();
        }

        if (jobObject is not RuntimeJobObject concreteJobObject)
        {
            return RuntimeJobObjectAssignmentResult.Failed(0);
        }

        bool assigned = nativeApi.TryAssignProcessToJobObject(
            concreteJobObject.SafeHandle,
            processHandle,
            out int nativeErrorCode);

        if (assigned)
        {
            return RuntimeJobObjectAssignmentResult.Assigned();
        }

        return nativeErrorCode switch
        {
            ErrorAccessDenied => RuntimeJobObjectAssignmentResult.AccessDenied(nativeErrorCode),
            ErrorInvalidHandle => RuntimeJobObjectAssignmentResult.InvalidHandle(nativeErrorCode),
            _ => RuntimeJobObjectAssignmentResult.Failed(nativeErrorCode)
        };
    }
}

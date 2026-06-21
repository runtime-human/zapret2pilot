using System;
using System.Runtime.InteropServices;
using static Zapret2Pilot.Runtime.Windows.WindowsJobObjectNativeMethods;

namespace Zapret2Pilot.Runtime.Windows;

public sealed class RuntimeJobObject : IRuntimeJobObject
{
    private readonly SafeJobObjectHandle handle;
    private bool disposed;

    private RuntimeJobObject(SafeJobObjectHandle handle, bool killOnCloseConfigured)
    {
        ArgumentNullException.ThrowIfNull(handle);

        this.handle = handle;
        KillOnCloseConfigured = killOnCloseConfigured;
    }

    public bool KillOnCloseConfigured { get; }

    public bool IsDisposed => disposed;

    internal SafeJobObjectHandle SafeHandle => handle;

    public static RuntimeJobObjectCreateResult CreateWithKillOnClose(string? name = null)
    {
        if (name is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        }

        if (!OperatingSystem.IsWindows())
        {
            return RuntimeJobObjectCreateResult.UnsupportedPlatformResult();
        }

        IntPtr nativeHandle = CreateJobObject(IntPtr.Zero, name);

        if (nativeHandle == IntPtr.Zero)
        {
            int errorCode = Marshal.GetLastPInvokeError();
            throw new RuntimeJobObjectException("CreateJobObject", errorCode);
        }

        SafeJobObjectHandle jobHandle = new(nativeHandle);

        try
        {
            ConfigureKillOnJobClose(jobHandle);

            RuntimeJobObject jobObject = new(jobHandle, killOnCloseConfigured: true);
            return RuntimeJobObjectCreateResult.CreatedJobObject(jobObject);
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        handle.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void ConfigureKillOnJobClose(SafeJobObjectHandle jobHandle)
    {
        JobObjectExtendedLimitInformation limitInformation = new();
        limitInformation.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

        int informationLength = Marshal.SizeOf<JobObjectExtendedLimitInformation>();

        bool configured = SetInformationJobObject(
            jobHandle,
            JobObjectInformationClass.ExtendedLimitInformation,
            ref limitInformation,
            informationLength);

        if (!configured)
        {
            int errorCode = Marshal.GetLastPInvokeError();
            throw new RuntimeJobObjectException("SetInformationJobObject", errorCode);
        }
    }
}

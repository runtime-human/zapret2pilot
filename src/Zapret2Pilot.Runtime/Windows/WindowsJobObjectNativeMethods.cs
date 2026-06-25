using System;
using System.Runtime.InteropServices;

namespace Zapret2Pilot.Runtime.Windows;

internal static partial class WindowsJobObjectNativeMethods
{
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;

    /// <summary>
    /// <c>CTRL_BREAK_EVENT</c> value for <c>GenerateConsoleCtrlEvent</c>.
    /// Sends a CTRL+BREAK signal to the specified process group.
    /// </summary>
    internal const uint CtrlBreakEvent = 1;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CreateJobObject(
        IntPtr jobAttributes,
        string? name);

    /// <summary>
    /// Sends a specified signal to a console process group. A
    /// <paramref name="dwProcessGroupId"/> of <c>0</c> delivers the
    /// signal to every process attached to the calling process's
    /// console. The <c>CTRL_BREAK_EVENT</c> signal can be intercepted
    /// by a well-behaved console application via
    /// <see cref="Console.CancelKeyPress"/> so it can shut down
    /// gracefully; processes created with <c>CreateNoWindow=true</c>
    /// or without an attached console will not receive the signal,
    /// in which case callers must fall back to
    /// <see cref="System.Diagnostics.Process.Kill(bool)"/>.
    /// </summary>
    /// <param name="dwCtrlEvent">The signal to deliver (use <see cref="CtrlBreakEvent"/>).</param>
    /// <param name="dwProcessGroupId">The process group id (0 = current console).</param>
    /// <returns><c>true</c> on success, <c>false</c> otherwise. Use <c>Marshal.GetLastPInvokeError</c> to inspect the Win32 error code.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GenerateConsoleCtrlEvent", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GenerateConsoleCtrlEvent(
        uint dwCtrlEvent,
        uint dwProcessGroupId);

    [LibraryImport("kernel32.dll", EntryPoint = "SetInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetInformationJobObject(
        SafeJobObjectHandle jobHandle,
        JobObjectInformationClass informationClass,
        ref JobObjectExtendedLimitInformation jobObjectInformation,
        int jobObjectInformationLength);

    [LibraryImport("kernel32.dll", EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AssignProcessToJobObject(
        SafeJobObjectHandle jobHandle,
        IntPtr processHandle);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    internal enum JobObjectInformationClass
    {
        ExtendedLimitInformation = 9
    }

#pragma warning disable CA1815 // Native interop structs are layout-only.
    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
#pragma warning restore CA1815
}

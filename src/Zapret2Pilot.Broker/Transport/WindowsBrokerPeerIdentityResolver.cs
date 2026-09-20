using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Security;

namespace Zapret2Pilot.Broker.Transport;

public sealed record BrokerResolvedPeer(
    BrokerPeerIdentity Identity,
    IBrokerAppSessionLease ProcessLease);

/// <summary>
/// Resolves the actual local Named Pipe client identity from the pipe handle
/// and retains the exact opened process object as the AppSession lifetime
/// lease. Serialized PID/token claims are never used as authority.
/// </summary>
public interface IBrokerPeerIdentityResolver
{
    BrokerResolvedPeer Resolve(SafePipeHandle pipeHandle);
}

public sealed class WindowsBrokerPeerIdentityResolver : IBrokerPeerIdentityResolver
{
    public BrokerResolvedPeer Resolve(SafePipeHandle pipeHandle)
    {
        ArgumentNullException.ThrowIfNull(pipeHandle);
        if (pipeHandle.IsInvalid || pipeHandle.IsClosed)
        {
            throw new ArgumentException(
                "The Named Pipe handle must be open and valid.",
                nameof(pipeHandle));
        }

        if (!WindowsBrokerNativeMethods.GetNamedPipeClientProcessId(
                pipeHandle,
                out uint processId))
        {
            throw NewWin32Exception("GetNamedPipeClientProcessId");
        }

        IntPtr rawProcessHandle = WindowsBrokerNativeMethods.OpenProcess(
            WindowsBrokerNativeMethods.ProcessQueryLimitedInformation
                | WindowsBrokerNativeMethods.Synchronize,
            inheritHandle: false,
            processId);
        if (rawProcessHandle == IntPtr.Zero)
        {
            throw NewWin32Exception("OpenProcess");
        }

        SafeBrokerProcessHandle processHandle = new(rawProcessHandle);
        try
        {
            long creationTime = GetCreationTime(processHandle);

            if (!WindowsBrokerNativeMethods.ProcessIdToSessionId(
                    processId,
                    out uint windowsSessionId))
            {
                throw NewWin32Exception("ProcessIdToSessionId");
            }

            IntPtr rawTokenHandle;
            if (!WindowsBrokerNativeMethods.OpenProcessToken(
                    processHandle,
                    WindowsBrokerNativeMethods.TokenQuery,
                    out rawTokenHandle))
            {
                throw NewWin32Exception("OpenProcessToken");
            }

            using SafeBrokerTokenHandle tokenHandle = new(rawTokenHandle);

            string userSid = ReadUserSid(tokenHandle);
            LogonSessionId logonSessionId = ReadLogonSessionId(tokenHandle);
            int integrityLevelRid = ReadIntegrityLevelRid(tokenHandle);

            BrokerPeerIdentity identity = new(
                checked((int)processId),
                creationTime,
                windowsSessionId,
                userSid,
                logonSessionId,
                integrityLevelRid);

            WindowsBrokerAppProcessLease lease =
                new(processHandle);
            processHandle = null!;

            return new BrokerResolvedPeer(identity, lease);
        }
        finally
        {
            processHandle?.Dispose();
        }
    }

    private static long GetCreationTime(SafeBrokerProcessHandle processHandle)
    {
        if (!WindowsBrokerNativeMethods.GetProcessTimes(
                processHandle,
                out NativeFileTime creation,
                out _,
                out _,
                out _))
        {
            throw NewWin32Exception("GetProcessTimes");
        }

        ulong value = ((ulong)creation.HighDateTime << 32)
            | creation.LowDateTime;
        return unchecked((long)value);
    }

    private static string ReadUserSid(SafeBrokerTokenHandle tokenHandle)
    {
        using TokenInformationBuffer buffer = TokenInformationBuffer.Read(
            tokenHandle,
            TokenInformationClass.TokenUser);

        TokenUser tokenUser = Marshal.PtrToStructure<TokenUser>(buffer.Pointer);
        if (tokenUser.User.Sid == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "TokenUser returned a null SID.");
        }

        if (!WindowsBrokerNativeMethods.ConvertSidToStringSid(
                tokenUser.User.Sid,
                out IntPtr sidString))
        {
            throw NewWin32Exception("ConvertSidToStringSidW");
        }

        try
        {
            return Marshal.PtrToStringUni(sidString)
                ?? throw new InvalidOperationException(
                    "ConvertSidToStringSidW returned an empty SID.");
        }
        finally
        {
            _ = WindowsBrokerNativeMethods.LocalFree(sidString);
        }
    }

    private static LogonSessionId ReadLogonSessionId(
        SafeBrokerTokenHandle tokenHandle)
    {
        using TokenInformationBuffer buffer = TokenInformationBuffer.Read(
            tokenHandle,
            TokenInformationClass.TokenStatistics);

        TokenStatistics statistics =
            Marshal.PtrToStructure<TokenStatistics>(buffer.Pointer);

        return new LogonSessionId(
            statistics.AuthenticationId.LowPart,
            statistics.AuthenticationId.HighPart);
    }

    private static int ReadIntegrityLevelRid(
        SafeBrokerTokenHandle tokenHandle)
    {
        using TokenInformationBuffer buffer = TokenInformationBuffer.Read(
            tokenHandle,
            TokenInformationClass.TokenIntegrityLevel);

        TokenMandatoryLabel label =
            Marshal.PtrToStructure<TokenMandatoryLabel>(buffer.Pointer);
        if (label.Label.Sid == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "TokenIntegrityLevel returned a null SID.");
        }

        IntPtr countPointer =
            WindowsBrokerNativeMethods.GetSidSubAuthorityCount(label.Label.Sid);
        if (countPointer == IntPtr.Zero)
        {
            throw NewWin32Exception("GetSidSubAuthorityCount");
        }

        byte count = Marshal.ReadByte(countPointer);
        if (count == 0)
        {
            throw new InvalidOperationException(
                "The integrity SID contains no sub-authorities.");
        }

        IntPtr ridPointer = WindowsBrokerNativeMethods.GetSidSubAuthority(
            label.Label.Sid,
            checked((uint)(count - 1)));
        if (ridPointer == IntPtr.Zero)
        {
            throw NewWin32Exception("GetSidSubAuthority");
        }

        return Marshal.ReadInt32(ridPointer);
    }

    private static Win32Exception NewWin32Exception(string operation)
        => new(
            Marshal.GetLastPInvokeError(),
            $"{operation} failed.");

    private sealed class TokenInformationBuffer : IDisposable
    {
        private TokenInformationBuffer(IntPtr pointer)
        {
            Pointer = pointer;
        }

        public IntPtr Pointer { get; }

        public static TokenInformationBuffer Read(
            SafeBrokerTokenHandle tokenHandle,
            TokenInformationClass informationClass)
        {
            _ = WindowsBrokerNativeMethods.GetTokenInformation(
                tokenHandle,
                informationClass,
                IntPtr.Zero,
                0,
                out uint requiredBytes);

            int firstError = Marshal.GetLastPInvokeError();
            if (requiredBytes == 0
                || firstError != WindowsBrokerNativeMethods.ErrorInsufficientBuffer)
            {
                throw new Win32Exception(
                    firstError,
                    $"GetTokenInformation({informationClass}) size query failed.");
            }

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)requiredBytes));
            try
            {
                if (!WindowsBrokerNativeMethods.GetTokenInformation(
                        tokenHandle,
                        informationClass,
                        buffer,
                        requiredBytes,
                        out _))
                {
                    throw NewWin32Exception(
                        $"GetTokenInformation({informationClass})");
                }

                return new TokenInformationBuffer(buffer);
            }
            catch
            {
                Marshal.FreeHGlobal(buffer);
                throw;
            }
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Pointer);
        }
    }
}

public sealed class WindowsBrokerAppProcessLease : IBrokerAppSessionLease
{
    private readonly SafeBrokerProcessHandle processHandle;
    private int disposed;

    internal WindowsBrokerAppProcessLease(
        SafeBrokerProcessHandle processHandle)
    {
        ArgumentNullException.ThrowIfNull(processHandle);
        this.processHandle = processHandle;
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        return Task.Run(
            () =>
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint result = WindowsBrokerNativeMethods.WaitForSingleObject(
                        processHandle,
                        milliseconds: 250);

                    if (result == WindowsBrokerNativeMethods.WaitObject0)
                    {
                        return;
                    }

                    if (result == WindowsBrokerNativeMethods.WaitTimeout)
                    {
                        continue;
                    }

                    if (result == WindowsBrokerNativeMethods.WaitFailed)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastPInvokeError(),
                            "WaitForSingleObject failed while observing the Control Plane process.");
                    }

                    throw new InvalidOperationException(
                        $"Unexpected WaitForSingleObject result: {result}.");
                }
            },
            CancellationToken.None);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        processHandle.Dispose();
    }
}

internal sealed class SafeBrokerProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeBrokerProcessHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
        => WindowsBrokerNativeMethods.CloseHandle(handle);
}

internal sealed class SafeBrokerTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeBrokerTokenHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
        => WindowsBrokerNativeMethods.CloseHandle(handle);
}

internal static partial class WindowsBrokerNativeMethods
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint Synchronize = 0x00100000;
    internal const uint TokenQuery = 0x0008;
    internal const int ErrorInsufficientBuffer = 122;
    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitTimeout = 0x00000102;
    internal const uint WaitFailed = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(
        SafeBrokerProcessHandle processHandle,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ProcessIdToSessionId(
        uint processId,
        out uint sessionId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(
        SafeBrokerProcessHandle processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        SafeBrokerTokenHandle tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [LibraryImport(
        "advapi32.dll",
        EntryPoint = "ConvertSidToStringSidW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertSidToStringSid(
        IntPtr sid,
        out IntPtr stringSid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    internal static partial IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    internal static partial IntPtr GetSidSubAuthority(
        IntPtr sid,
        uint subAuthority);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(
        SafeBrokerProcessHandle handle,
        uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);
}

internal enum TokenInformationClass
{
    TokenUser = 1,
    TokenStatistics = 10,
    TokenIntegrityLevel = 25,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFileTime
{
    internal uint LowDateTime;
    internal uint HighDateTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SidAndAttributes
{
    internal IntPtr Sid;
    internal uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TokenUser
{
    internal SidAndAttributes User;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeLuid
{
    internal uint LowPart;
    internal int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TokenStatistics
{
    internal NativeLuid TokenId;
    internal NativeLuid AuthenticationId;
    internal long ExpirationTime;
    internal int TokenType;
    internal int ImpersonationLevel;
    internal uint DynamicCharged;
    internal uint DynamicAvailable;
    internal uint GroupCount;
    internal uint PrivilegeCount;
    internal NativeLuid ModifiedId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TokenMandatoryLabel
{
    internal SidAndAttributes Label;
}

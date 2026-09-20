using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.App.Runtime;

public interface IAppProcessBindingProvider
{
    BrokerClientBinding Create(AppSessionId appSessionId);
}

public interface IBrokerExecutableLocator
{
    string ResolveBrokerExecutablePath();
}

public interface IElevatedBrokerProcess : IDisposable
{
    int ProcessId { get; }

    long ProcessCreationTimeFileTime { get; }

    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    bool TryTerminate();
}

public interface IElevatedBrokerLauncher
{
    IElevatedBrokerProcess Launch(
        string executablePath,
        string bootstrapPipeName,
        int appProcessId);
}

public interface IBrokerBootstrapServer : IAsyncDisposable
{
    Task WaitForVerifiedConnectionAsync(
        IElevatedBrokerProcess launchedBroker,
        CancellationToken cancellationToken);

    Task SendAsync(
        BrokerBootstrapMessage bootstrap,
        CancellationToken cancellationToken);
}

public interface IBrokerBootstrapServerFactory
{
    IBrokerBootstrapServer Create(
        string pipeName,
        string expectedUserSid);
}

public sealed class WindowsAppProcessBindingProvider :
    IAppProcessBindingProvider
{
    public BrokerClientBinding Create(AppSessionId appSessionId)
    {
        if (appSessionId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "AppSessionId must not be empty.",
                nameof(appSessionId));
        }

        using Process process = Process.GetCurrentProcess();
        long creationTime = WindowsAppBootstrapNativeMethods
            .GetCreationTimeFileTime(process.SafeHandle);

        if (!WindowsAppBootstrapNativeMethods.ProcessIdToSessionId(
                checked((uint)process.Id),
                out uint windowsSessionId))
        {
            throw NewWin32Exception("ProcessIdToSessionId");
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string userSid = identity.User?.Value
            ?? throw new InvalidOperationException(
                "The current Windows identity does not expose a user SID.");

        LogonSessionId logonSessionId =
            WindowsAppBootstrapNativeMethods.ReadLogonSessionId(
                identity.AccessToken);
        int integrityLevelRid =
            WindowsAppBootstrapNativeMethods.ReadIntegrityLevelRid(
                identity.AccessToken);

        return new(
            appSessionId,
            process.Id,
            creationTime,
            windowsSessionId,
            userSid,
            logonSessionId,
            integrityLevelRid);
    }

    private static Win32Exception NewWin32Exception(string operation)
        => new(
            Marshal.GetLastPInvokeError(),
            $"{operation} failed.");
}

public sealed class SiblingBrokerExecutableLocator :
    IBrokerExecutableLocator
{
    public string ResolveBrokerExecutablePath()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "z2p-broker.exe");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The session-scoped Runtime Broker executable is not present beside z2p.exe.",
                path);
        }

        return Path.GetFullPath(path);
    }
}

public sealed class WindowsElevatedBrokerLauncher :
    IElevatedBrokerLauncher
{
    public IElevatedBrokerProcess Launch(
        string executablePath,
        string bootstrapPipeName,
        int appProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapPipeName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appProcessId);

        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory =
                Path.GetDirectoryName(executablePath)
                ?? AppContext.BaseDirectory,
        };

        startInfo.ArgumentList.Add("--bootstrap-pipe");
        startInfo.ArgumentList.Add(bootstrapPipeName);
        startInfo.ArgumentList.Add("--app-pid");
        startInfo.ArgumentList.Add(
            appProcessId.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Windows shell did not return the launched Broker process.");

        try
        {
            long creationTime =
                WindowsAppBootstrapNativeMethods.GetCreationTimeFileTime(
                    process.SafeHandle);

            return new WindowsElevatedBrokerProcess(
                process,
                creationTime);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}

public sealed class WindowsBrokerBootstrapServerFactory :
    IBrokerBootstrapServerFactory
{
    public IBrokerBootstrapServer Create(
        string pipeName,
        string expectedUserSid)
        => new WindowsBrokerBootstrapServer(
            pipeName,
            expectedUserSid);
}

internal sealed class WindowsElevatedBrokerProcess :
    IElevatedBrokerProcess
{
    private readonly Process process;
    private int disposed;

    public WindowsElevatedBrokerProcess(
        Process process,
        long processCreationTimeFileTime)
    {
        ArgumentNullException.ThrowIfNull(process);
        this.process = process;
        ProcessCreationTimeFileTime =
            processCreationTimeFileTime;
    }

    public int ProcessId => process.Id;

    public long ProcessCreationTimeFileTime { get; }

    public bool HasExited
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref disposed) != 0,
                this);
            return process.HasExited;
        }
    }

    public Task WaitForExitAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        return process.WaitForExitAsync(cancellationToken);
    }

    public bool TryTerminate()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        try
        {
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
                or Win32Exception
                or NotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        process.Dispose();
    }
}

internal sealed class WindowsBrokerBootstrapServer :
    IBrokerBootstrapServer
{
    private readonly NamedPipeServerStream pipe;
    private bool connected;
    private bool disposed;

    public WindowsBrokerBootstrapServer(
        string pipeName,
        string expectedUserSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedUserSid);

        pipe = CreateSecurePipe(
            pipeName,
            expectedUserSid);
    }

    public async Task WaitForVerifiedConnectionAsync(
        IElevatedBrokerProcess launchedBroker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launchedBroker);
        ObjectDisposedException.ThrowIf(disposed, this);

        await pipe.WaitForConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!WindowsAppBootstrapNativeMethods
            .GetNamedPipeClientProcessId(
                pipe.SafePipeHandle,
                out uint actualProcessId))
        {
            throw NewWin32Exception(
                "GetNamedPipeClientProcessId");
        }

        if (actualProcessId
            != checked((uint)launchedBroker.ProcessId)
            || launchedBroker.HasExited)
        {
            throw new UnauthorizedAccessException(
                "Bootstrap pipe client is not the Broker process launched by this Control Plane.");
        }

        using SafeAppProcessHandle actualProcess =
            WindowsAppBootstrapNativeMethods.OpenProcessForIdentity(
                actualProcessId);

        long actualCreationTime =
            WindowsAppBootstrapNativeMethods
                .GetCreationTimeFileTime(actualProcess);

        if (actualCreationTime
            != launchedBroker.ProcessCreationTimeFileTime)
        {
            throw new UnauthorizedAccessException(
                "Bootstrap pipe client PID was reused by a different process.");
        }

        connected = true;
    }

    public async Task SendAsync(
        BrokerBootstrapMessage bootstrap,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!connected)
        {
            throw new InvalidOperationException(
                "The bootstrap peer must be verified before secret transfer.");
        }

        byte[] frame =
            BrokerBootstrapFrameCodec.Encode(bootstrap);
        try
        {
            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            deadline.CancelAfter(
                BrokerProtocolLimits.ResponseWriteTimeout);

            await pipe.WriteAsync(frame, deadline.Token)
                .ConfigureAwait(false);
            await pipe.FlushAsync(deadline.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frame);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await pipe.DisposeAsync().ConfigureAwait(false);
    }

    private static NamedPipeServerStream CreateSecurePipe(
        string pipeName,
        string expectedUserSid)
    {
        ValidatePipeName(pipeName);

        string sddl =
            $"O:{expectedUserSid}D:P(A;;GA;;;SY)(A;;GR;;;{expectedUserSid})";

        if (!WindowsAppBootstrapNativeMethods
            .ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                WindowsAppBootstrapNativeMethods.SddlRevision1,
                out IntPtr securityDescriptor,
                out _))
        {
            throw NewWin32Exception(
                "ConvertStringSecurityDescriptorToSecurityDescriptorW");
        }

        try
        {
            AppSecurityAttributes attributes = new()
            {
                Length = Marshal.SizeOf<AppSecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = 0,
            };

            int bufferSize = checked(
                BrokerProtocolLimits.MaxBootstrapFrameBytes
                + BrokerProtocolLimits.LengthPrefixBytes);

            IntPtr rawHandle =
                WindowsAppBootstrapNativeMethods.CreateNamedPipe(
                    $@"\\.\pipe\{pipeName}",
                    WindowsAppBootstrapNativeMethods.PipeAccessOutbound
                        | WindowsAppBootstrapNativeMethods.FileFlagOverlapped
                        | WindowsAppBootstrapNativeMethods.FileFlagFirstPipeInstance,
                    WindowsAppBootstrapNativeMethods.PipeTypeByte
                        | WindowsAppBootstrapNativeMethods.PipeReadModeByte
                        | WindowsAppBootstrapNativeMethods.PipeWait
                        | WindowsAppBootstrapNativeMethods.PipeRejectRemoteClients,
                    maximumInstances: 1,
                    outBufferSize: checked((uint)bufferSize),
                    inBufferSize: 0,
                    defaultTimeoutMilliseconds: 0,
                    ref attributes);

            if (rawHandle == new IntPtr(-1))
            {
                throw NewWin32Exception("CreateNamedPipeW");
            }

            SafePipeHandle safeHandle =
                new(rawHandle, ownsHandle: true);
            try
            {
                NamedPipeServerStream stream = new(
                    PipeDirection.Out,
                    isAsync: true,
                    isConnected: false,
                    safeHandle);
                safeHandle = null!;
                return stream;
            }
            finally
            {
                safeHandle?.Dispose();
            }
        }
        finally
        {
            _ = WindowsAppBootstrapNativeMethods.LocalFree(
                securityDescriptor);
        }
    }

    private static void ValidatePipeName(string pipeName)
    {
        if (pipeName.Length is 0 or > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pipeName));
        }

        foreach (char character in pipeName)
        {
            if (!(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'))
            {
                throw new ArgumentException(
                    "Bootstrap pipe names may contain only ASCII letters, digits, '-' and '_'.",
                    nameof(pipeName));
            }
        }
    }

    private static Win32Exception NewWin32Exception(
        string operation)
        => new(
            Marshal.GetLastPInvokeError(),
            $"{operation} failed.");
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppSecurityAttributes
{
    internal int Length;
    internal IntPtr SecurityDescriptor;
    internal int InheritHandle;
}

internal sealed class SafeAppProcessHandle :
    SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeAppProcessHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
        => WindowsAppBootstrapNativeMethods.CloseHandle(
            handle);
}

internal static partial class WindowsAppBootstrapNativeMethods
{
    internal const uint ProcessQueryLimitedInformation =
        0x1000;
    internal const uint TokenQuery = 0x0008;
    internal const int ErrorInsufficientBuffer = 122;

    internal const uint PipeAccessOutbound = 0x00000002;
    internal const uint FileFlagFirstPipeInstance = 0x00080000;
    internal const uint FileFlagOverlapped = 0x40000000;
    internal const uint PipeTypeByte = 0;
    internal const uint PipeReadModeByte = 0;
    internal const uint PipeWait = 0;
    internal const uint PipeRejectRemoteClients = 0x00000008;
    internal const uint SddlRevision1 = 1;

    internal static SafeAppProcessHandle OpenProcessForIdentity(
        uint processId)
    {
        IntPtr rawHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);

        if (rawHandle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "OpenProcess failed.");
        }

        return new SafeAppProcessHandle(rawHandle);
    }

    internal static long GetCreationTimeFileTime(
        SafeProcessHandle processHandle)
    {
        if (!GetProcessTimes(
                processHandle,
                out AppNativeFileTime creationTime,
                out _,
                out _,
                out _))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "GetProcessTimes failed.");
        }

        return ToInt64(creationTime);
    }

    internal static long GetCreationTimeFileTime(
        SafeAppProcessHandle processHandle)
    {
        if (!GetProcessTimes(
                processHandle,
                out AppNativeFileTime creationTime,
                out _,
                out _,
                out _))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "GetProcessTimes failed.");
        }

        return ToInt64(creationTime);
    }

    internal static LogonSessionId ReadLogonSessionId(
        SafeAccessTokenHandle tokenHandle)
    {
        using AppTokenInformationBuffer buffer =
            AppTokenInformationBuffer.Read(
                tokenHandle,
                AppTokenInformationClass.TokenStatistics);

        AppTokenStatistics statistics =
            Marshal.PtrToStructure<AppTokenStatistics>(
                buffer.Pointer);

        return new(
            statistics.AuthenticationId.LowPart,
            statistics.AuthenticationId.HighPart);
    }

    internal static int ReadIntegrityLevelRid(
        SafeAccessTokenHandle tokenHandle)
    {
        using AppTokenInformationBuffer buffer =
            AppTokenInformationBuffer.Read(
                tokenHandle,
                AppTokenInformationClass.TokenIntegrityLevel);

        AppTokenMandatoryLabel label =
            Marshal.PtrToStructure<AppTokenMandatoryLabel>(
                buffer.Pointer);

        if (label.Label.Sid == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "TokenIntegrityLevel returned a null SID.");
        }

        IntPtr countPointer =
            GetSidSubAuthorityCount(label.Label.Sid);
        if (countPointer == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "GetSidSubAuthorityCount failed.");
        }

        byte count = Marshal.ReadByte(countPointer);
        if (count == 0)
        {
            throw new InvalidOperationException(
                "The integrity SID has no sub-authorities.");
        }

        IntPtr ridPointer = GetSidSubAuthority(
            label.Label.Sid,
            checked((uint)(count - 1)));

        if (ridPointer == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "GetSidSubAuthority failed.");
        }

        return Marshal.ReadInt32(ridPointer);
    }

    private static long ToInt64(AppNativeFileTime fileTime)
    {
        ulong value =
            ((ulong)fileTime.HighDateTime << 32)
            | fileTime.LowDateTime;
        return unchecked((long)value);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ProcessIdToSessionId(
        uint processId,
        out uint sessionId);

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
        SafeProcessHandle processHandle,
        out AppNativeFileTime creationTime,
        out AppNativeFileTime exitTime,
        out AppNativeFileTime kernelTime,
        out AppNativeFileTime userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(
        SafeAppProcessHandle processHandle,
        out AppNativeFileTime creationTime,
        out AppNativeFileTime exitTime,
        out AppNativeFileTime kernelTime,
        out AppNativeFileTime userTime);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        AppTokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    internal static partial IntPtr GetSidSubAuthorityCount(
        IntPtr sid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    internal static partial IntPtr GetSidSubAuthority(
        IntPtr sid,
        uint subAuthority);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateNamedPipeW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CreateNamedPipe(
        string name,
        uint openMode,
        uint pipeMode,
        int maximumInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeoutMilliseconds,
        ref AppSecurityAttributes securityAttributes);

    [LibraryImport(
        "advapi32.dll",
        EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(
        IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);
}

internal sealed class AppTokenInformationBuffer :
    IDisposable
{
    private AppTokenInformationBuffer(IntPtr pointer)
    {
        Pointer = pointer;
    }

    public IntPtr Pointer { get; }

    public static AppTokenInformationBuffer Read(
        SafeAccessTokenHandle tokenHandle,
        AppTokenInformationClass informationClass)
    {
        _ = WindowsAppBootstrapNativeMethods.GetTokenInformation(
            tokenHandle,
            informationClass,
            IntPtr.Zero,
            0,
            out uint requiredBytes);

        int firstError = Marshal.GetLastPInvokeError();
        if (requiredBytes == 0
            || firstError
                != WindowsAppBootstrapNativeMethods
                    .ErrorInsufficientBuffer)
        {
            throw new Win32Exception(
                firstError,
                $"GetTokenInformation({informationClass}) size query failed.");
        }

        IntPtr buffer =
            Marshal.AllocHGlobal(
                checked((int)requiredBytes));

        try
        {
            if (!WindowsAppBootstrapNativeMethods
                .GetTokenInformation(
                    tokenHandle,
                    informationClass,
                    buffer,
                    requiredBytes,
                    out _))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"GetTokenInformation({informationClass}) failed.");
            }

            return new AppTokenInformationBuffer(buffer);
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

internal enum AppTokenInformationClass
{
    TokenStatistics = 10,
    TokenIntegrityLevel = 25,
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppNativeFileTime
{
    internal uint LowDateTime;
    internal uint HighDateTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppNativeLuid
{
    internal uint LowPart;
    internal int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppTokenStatistics
{
    internal AppNativeLuid TokenId;
    internal AppNativeLuid AuthenticationId;
    internal long ExpirationTime;
    internal int TokenType;
    internal int ImpersonationLevel;
    internal uint DynamicCharged;
    internal uint DynamicAvailable;
    internal uint GroupCount;
    internal uint PrivilegeCount;
    internal AppNativeLuid ModifiedId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppSidAndAttributes
{
    internal IntPtr Sid;
    internal uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppTokenMandatoryLabel
{
    internal AppSidAndAttributes Label;
}

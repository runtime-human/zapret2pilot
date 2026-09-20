using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Broker.Transport;

/// <summary>
/// Creates local-only Broker pipe instances with an explicit protected DACL.
/// The factory intentionally uses CreateNamedPipeW so PIPE_REJECT_REMOTE_CLIENTS
/// is an OS-enforced property rather than a convention in higher-level code.
/// </summary>
public interface IBrokerNamedPipeFactory
{
    NamedPipeServerStream Create(
        string pipeName,
        string expectedUserSid,
        bool firstInstance);
}

public sealed class WindowsSecureNamedPipeFactory : IBrokerNamedPipeFactory
{
    private const int MaximumPipeNameLength = 128;

    public NamedPipeServerStream Create(
        string pipeName,
        string expectedUserSid,
        bool firstInstance)
    {
        ValidatePipeName(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedUserSid);

        SecurityIdentifier sid = new(expectedUserSid);
        string canonicalSid = sid.Value;

        // SYSTEM retains full control for the privileged Broker; the exact
        // expected user receives read/write access. Same-user DACL access is
        // only a coarse transport boundary: #16 HMAC + process/token binding
        // remains mandatory before any request is dispatched.
        string sddl =
            $"O:{canonicalSid}D:P(A;;GA;;;SY)(A;;GRGW;;;{canonicalSid})";

        if (!WindowsBrokerNativeMethods
            .ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                WindowsBrokerNativeMethods.SddlRevision1,
                out IntPtr securityDescriptor,
                out _))
        {
            throw NewWin32Exception(
                "ConvertStringSecurityDescriptorToSecurityDescriptorW");
        }

        try
        {
            SecurityAttributes attributes = new()
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = 0,
            };

            uint openMode =
                WindowsBrokerNativeMethods.PipeAccessDuplex
                | WindowsBrokerNativeMethods.FileFlagOverlapped;
            if (firstInstance)
            {
                openMode |= WindowsBrokerNativeMethods.FileFlagFirstPipeInstance;
            }

            uint pipeMode =
                WindowsBrokerNativeMethods.PipeTypeByte
                | WindowsBrokerNativeMethods.PipeReadModeByte
                | WindowsBrokerNativeMethods.PipeWait
                | WindowsBrokerNativeMethods.PipeRejectRemoteClients;

            int bufferSize = checked(
                BrokerProtocolLimits.MaxFrameBytes
                + BrokerProtocolLimits.LengthPrefixBytes);

            IntPtr rawHandle = WindowsBrokerNativeMethods.CreateNamedPipe(
                $@"\\.\pipe\{pipeName}",
                openMode,
                pipeMode,
                BrokerProtocolLimits.MaxConcurrentConnections,
                checked((uint)bufferSize),
                checked((uint)bufferSize),
                defaultTimeoutMilliseconds: 0,
                ref attributes);

            if (rawHandle == new IntPtr(-1))
            {
                throw NewWin32Exception("CreateNamedPipeW");
            }

            SafePipeHandle safeHandle = new(rawHandle, ownsHandle: true);
            try
            {
                NamedPipeServerStream stream = new(
                    PipeDirection.InOut,
                    isAsync: true,
                    isConnected: false,
                    safeHandle);

                // Ownership of safeHandle is now associated with the stream.
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
            _ = WindowsBrokerNativeMethods.LocalFree(securityDescriptor);
        }
    }

    private static void ValidatePipeName(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        if (pipeName.Length > MaximumPipeNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pipeName),
                pipeName.Length,
                $"Broker pipe names are limited to {MaximumPipeNameLength} characters.");
        }

        foreach (char character in pipeName)
        {
            if (!(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'))
            {
                throw new ArgumentException(
                    "Broker pipe names may contain only ASCII letters, digits, '-' and '_'.",
                    nameof(pipeName));
            }
        }
    }

    private static Win32Exception NewWin32Exception(string operation)
        => new(
            Marshal.GetLastPInvokeError(),
            $"{operation} failed.");
}

[StructLayout(LayoutKind.Sequential)]
internal struct SecurityAttributes
{
    internal int Length;
    internal IntPtr SecurityDescriptor;

    internal int InheritHandle;
}

internal static partial class WindowsBrokerNativeMethods
{
    internal const uint PipeAccessDuplex = 0x00000003;
    internal const uint FileFlagFirstPipeInstance = 0x00080000;
    internal const uint FileFlagOverlapped = 0x40000000;
    internal const uint PipeTypeByte = 0x00000000;
    internal const uint PipeReadModeByte = 0x00000000;
    internal const uint PipeWait = 0x00000000;
    internal const uint PipeRejectRemoteClients = 0x00000008;
    internal const uint SddlRevision1 = 1;

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
        ref SecurityAttributes securityAttributes);

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
}

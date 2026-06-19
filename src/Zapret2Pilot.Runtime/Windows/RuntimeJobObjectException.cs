using System;
using System.ComponentModel;

namespace Zapret2Pilot.Runtime.Windows;

public sealed class RuntimeJobObjectException : InvalidOperationException
{
    public RuntimeJobObjectException(string operation, int nativeErrorCode)
        : base(CreateMessage(operation, nativeErrorCode))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        Operation = operation;
        NativeErrorCode = nativeErrorCode;
    }

    public string Operation { get; }

    public int NativeErrorCode { get; }

    private static string CreateMessage(string operation, int nativeErrorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        Win32Exception win32Exception = new(nativeErrorCode);

        return $"Windows Job Object operation '{operation}' failed with native error {nativeErrorCode}: {win32Exception.Message}";
    }
}

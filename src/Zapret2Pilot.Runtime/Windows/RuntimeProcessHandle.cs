using System;

namespace Zapret2Pilot.Runtime.Windows;

public readonly struct RuntimeProcessHandle
{
    public RuntimeProcessHandle(IntPtr value)
    {
        Value = value;
    }

    public IntPtr Value { get; }

    public bool IsInvalid => Value == IntPtr.Zero || Value == new IntPtr(-1);

    public static RuntimeProcessHandle Invalid => new(IntPtr.Zero);
}

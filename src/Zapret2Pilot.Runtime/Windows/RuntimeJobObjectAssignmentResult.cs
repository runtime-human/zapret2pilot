using System;

namespace Zapret2Pilot.Runtime.Windows;

public sealed record class RuntimeJobObjectAssignmentResult
{
    private RuntimeJobObjectAssignmentResult(
        RuntimeJobObjectAssignmentStatus status,
        int? nativeErrorCode)
    {
        if (status == RuntimeJobObjectAssignmentStatus.Assigned && nativeErrorCode.HasValue)
        {
            throw new ArgumentException(
                "Assigned status must not carry a native error code.",
                nameof(nativeErrorCode));
        }

        Status = status;
        NativeErrorCode = nativeErrorCode;
    }

    public RuntimeJobObjectAssignmentStatus Status { get; }

    public int? NativeErrorCode { get; }

    public static RuntimeJobObjectAssignmentResult Assigned()
    {
        return new RuntimeJobObjectAssignmentResult(
            RuntimeJobObjectAssignmentStatus.Assigned,
            nativeErrorCode: null);
    }

    public static RuntimeJobObjectAssignmentResult UnsupportedPlatform()
    {
        return new RuntimeJobObjectAssignmentResult(
            RuntimeJobObjectAssignmentStatus.UnsupportedPlatform,
            nativeErrorCode: null);
    }

    public static RuntimeJobObjectAssignmentResult InvalidHandle()
    {
        return new RuntimeJobObjectAssignmentResult(
            RuntimeJobObjectAssignmentStatus.InvalidHandle,
            nativeErrorCode: null);
    }

    public static RuntimeJobObjectAssignmentResult InvalidHandle(int nativeErrorCode)
    {
        return new RuntimeJobObjectAssignmentResult(
            RuntimeJobObjectAssignmentStatus.InvalidHandle,
            nativeErrorCode);
    }

    public static RuntimeJobObjectAssignmentResult AccessDenied(int nativeErrorCode)
    {
        return new RuntimeJobObjectAssignmentResult(
            RuntimeJobObjectAssignmentStatus.AccessDenied,
            nativeErrorCode);
    }

    public static RuntimeJobObjectAssignmentResult AlreadyAssigned(int nativeErrorCode)
    {
        return new RuntimeJobObjectAssignmentResult(
            RuntimeJobObjectAssignmentStatus.AlreadyAssigned,
            nativeErrorCode);
    }

    public static RuntimeJobObjectAssignmentResult Failed(int nativeErrorCode)
    {
        return new RuntimeJobObjectAssignmentResult(
            RuntimeJobObjectAssignmentStatus.Failed,
            nativeErrorCode);
    }
}

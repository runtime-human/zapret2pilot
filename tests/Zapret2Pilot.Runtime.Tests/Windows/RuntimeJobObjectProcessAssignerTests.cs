using System;
using Xunit;
using Zapret2Pilot.Runtime.Windows;

namespace Zapret2Pilot.Runtime.Tests.Windows;

public sealed class RuntimeJobObjectProcessAssignerTests
{
    [Fact]
    public static void AssignRejectsNullJobObject()
    {
        RecordingNativeApi nativeApi = new();
        RuntimeJobObjectProcessAssigner assigner = new(nativeApi);
        RuntimeProcessHandle processHandle = new((IntPtr)42);

        Assert.Throws<ArgumentNullException>(() => assigner.Assign(null!, processHandle));
        Assert.Equal(0, nativeApi.CallCount);
    }

    [Fact]
    public static void AssignRejectsDisposedJobObject()
    {
        RecordingNativeApi nativeApi = new();
        RuntimeJobObjectProcessAssigner assigner = new(nativeApi);
        FakeJobObject disposedJobObject = new() { IsDisposedValue = true };
        RuntimeProcessHandle processHandle = new((IntPtr)42);

        RuntimeJobObjectAssignmentResult result = assigner.Assign(disposedJobObject, processHandle);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(RuntimeJobObjectAssignmentStatus.Failed, result.Status);
            Assert.Equal(0, result.NativeErrorCode);
        }
        else
        {
            Assert.Equal(RuntimeJobObjectAssignmentStatus.UnsupportedPlatform, result.Status);
        }

        Assert.Equal(0, nativeApi.CallCount);
    }

    [Fact]
    public static void AssignRejectsInvalidProcessHandle()
    {
        RecordingNativeApi nativeApi = new();
        RuntimeJobObjectProcessAssigner assigner = new(nativeApi);
        FakeJobObject jobObject = new() { IsDisposedValue = false };
        RuntimeProcessHandle invalidHandle = new(IntPtr.Zero);

        RuntimeJobObjectAssignmentResult result = assigner.Assign(jobObject, invalidHandle);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(RuntimeJobObjectAssignmentStatus.InvalidHandle, result.Status);
        }
        else
        {
            Assert.Equal(RuntimeJobObjectAssignmentStatus.UnsupportedPlatform, result.Status);
        }

        Assert.Equal(0, nativeApi.CallCount);
    }

    [Fact]
    public static void AssignReturnsUnsupportedPlatformOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        RecordingNativeApi nativeApi = new();
        RuntimeJobObjectProcessAssigner assigner = new(nativeApi);
        FakeJobObject jobObject = new() { IsDisposedValue = false };
        RuntimeProcessHandle processHandle = new((IntPtr)42);

        RuntimeJobObjectAssignmentResult result = assigner.Assign(jobObject, processHandle);

        Assert.Equal(RuntimeJobObjectAssignmentStatus.UnsupportedPlatform, result.Status);
        Assert.Equal(0, nativeApi.CallCount);
    }

    [Fact]
    public static void AssignMapsFakeSuccessToAssignedStatus()
    {
        FakeNativeApi nativeApi = new() { ReturnValue = true };
        RuntimeJobObjectProcessAssigner assigner = new(nativeApi);
        RuntimeProcessHandle processHandle = new((IntPtr)9999);

        RuntimeJobObjectCreateResult createResult = RuntimeJobObject.CreateWithKillOnClose(CreateUniqueJobName());

        if (!createResult.Created)
        {
            Assert.True(createResult.UnsupportedPlatform);
            RuntimeJobObjectAssignmentResult nonWindowsResult = assigner.Assign(
                new FakeJobObject { IsDisposedValue = false },
                processHandle);
            Assert.Equal(RuntimeJobObjectAssignmentStatus.UnsupportedPlatform, nonWindowsResult.Status);
            Assert.Equal(0, nativeApi.CallCount);
            return;
        }

        using RuntimeJobObject jobObject = (RuntimeJobObject)createResult.JobObject!;
        RuntimeJobObjectAssignmentResult result = assigner.Assign(jobObject, processHandle);

        Assert.Equal(RuntimeJobObjectAssignmentStatus.Assigned, result.Status);
        Assert.Equal(1, nativeApi.CallCount);
    }

    [Fact]
    public static void AssignMapsFakeAccessDeniedToAccessDeniedStatus()
    {
        FakeNativeApi nativeApi = new() { ReturnValue = false, ReturnedErrorCode = 5 };
        RuntimeJobObjectProcessAssigner assigner = new(nativeApi);
        RuntimeProcessHandle processHandle = new((IntPtr)9999);

        RuntimeJobObjectCreateResult createResult = RuntimeJobObject.CreateWithKillOnClose(CreateUniqueJobName());

        if (!createResult.Created)
        {
            Assert.True(createResult.UnsupportedPlatform);
            RuntimeJobObjectAssignmentResult nonWindowsResult = assigner.Assign(
                new FakeJobObject { IsDisposedValue = false },
                processHandle);
            Assert.Equal(RuntimeJobObjectAssignmentStatus.UnsupportedPlatform, nonWindowsResult.Status);
            Assert.Equal(0, nativeApi.CallCount);
            return;
        }

        using RuntimeJobObject jobObject = (RuntimeJobObject)createResult.JobObject!;
        RuntimeJobObjectAssignmentResult result = assigner.Assign(jobObject, processHandle);

        Assert.Equal(RuntimeJobObjectAssignmentStatus.AccessDenied, result.Status);
        Assert.Equal(5, result.NativeErrorCode);
        Assert.Equal(1, nativeApi.CallCount);
    }

    [Fact]
    public static void AssignMapsFakeInvalidHandleToInvalidHandleStatus()
    {
        FakeNativeApi nativeApi = new() { ReturnValue = false, ReturnedErrorCode = 6 };
        RuntimeJobObjectProcessAssigner assigner = new(nativeApi);
        RuntimeProcessHandle processHandle = new((IntPtr)9999);

        RuntimeJobObjectCreateResult createResult = RuntimeJobObject.CreateWithKillOnClose(CreateUniqueJobName());

        if (!createResult.Created)
        {
            Assert.True(createResult.UnsupportedPlatform);
            RuntimeJobObjectAssignmentResult nonWindowsResult = assigner.Assign(
                new FakeJobObject { IsDisposedValue = false },
                processHandle);
            Assert.Equal(RuntimeJobObjectAssignmentStatus.UnsupportedPlatform, nonWindowsResult.Status);
            Assert.Equal(0, nativeApi.CallCount);
            return;
        }

        using RuntimeJobObject jobObject = (RuntimeJobObject)createResult.JobObject!;
        RuntimeJobObjectAssignmentResult result = assigner.Assign(jobObject, processHandle);

        Assert.Equal(RuntimeJobObjectAssignmentStatus.InvalidHandle, result.Status);
        Assert.Equal(6, result.NativeErrorCode);
        Assert.Equal(1, nativeApi.CallCount);
    }

    private static string CreateUniqueJobName()
    {
        return OperatingSystem.IsWindows()
            ? $@"Local\Z2P_TEST_JOB_{Guid.NewGuid():N}"
            : $"Z2P_TEST_JOB_{Guid.NewGuid():N}";
    }

    private sealed class FakeJobObject : IRuntimeJobObject
    {
        public bool IsDisposedValue { get; init; }

        public bool KillOnCloseConfigured => true;

        public bool IsDisposed => IsDisposedValue;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingNativeApi : IJobObjectNativeApi
    {
        public int CallCount { get; private set; }

        public bool TryAssignProcessToJobObject(
            SafeJobObjectHandle jobHandle,
            RuntimeProcessHandle processHandle,
            out int nativeErrorCode)
        {
            CallCount++;
            nativeErrorCode = 0;
            return false;
        }
    }

    private sealed class FakeNativeApi : IJobObjectNativeApi
    {
        public bool ReturnValue { get; init; }

        public int ReturnedErrorCode { get; init; }

        public int CallCount { get; private set; }

        public bool TryAssignProcessToJobObject(
            SafeJobObjectHandle jobHandle,
            RuntimeProcessHandle processHandle,
            out int nativeErrorCode)
        {
            CallCount++;
            nativeErrorCode = ReturnedErrorCode;
            return ReturnValue;
        }
    }
}

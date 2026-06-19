using System;
using Xunit;
using Zapret2Pilot.Runtime.Windows;

namespace Zapret2Pilot.Runtime.Tests.Windows;

public sealed class RuntimeJobObjectTests
{
    [Fact]
    public static void CreateWithKillOnCloseCreatesJobObjectOnWindows()
    {
        RuntimeJobObjectCreateResult result = RuntimeJobObject.CreateWithKillOnClose(CreateUniqueJobName());

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(result.UnsupportedPlatform);
            Assert.False(result.Created);
            Assert.Null(result.JobObject);
            return;
        }

        using IRuntimeJobObject jobObject = Assert.NotNull(result.JobObject);

        Assert.True(result.Created);
        Assert.False(result.UnsupportedPlatform);
        Assert.True(jobObject.KillOnCloseConfigured);
        Assert.False(jobObject.IsDisposed);
    }

    [Fact]
    public static void DisposeIsIdempotent()
    {
        RuntimeJobObjectCreateResult result = RuntimeJobObject.CreateWithKillOnClose(CreateUniqueJobName());

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(result.UnsupportedPlatform);
            return;
        }

        IRuntimeJobObject jobObject = Assert.NotNull(result.JobObject);

        jobObject.Dispose();
        jobObject.Dispose();

        Assert.True(jobObject.IsDisposed);
    }

    [Fact]
    public static void KillOnCloseConfigurationIsAppliedOnWindows()
    {
        RuntimeJobObjectCreateResult result = RuntimeJobObject.CreateWithKillOnClose(CreateUniqueJobName());

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(result.UnsupportedPlatform);
            return;
        }

        using IRuntimeJobObject jobObject = Assert.NotNull(result.JobObject);

        Assert.True(jobObject.KillOnCloseConfigured);
    }

    [Fact]
    public static void CreateWithKillOnCloseReturnsUnsupportedPlatformOnNonWindows()
    {
        RuntimeJobObjectCreateResult result = RuntimeJobObject.CreateWithKillOnClose(CreateUniqueJobName());

        if (OperatingSystem.IsWindows())
        {
            using IRuntimeJobObject jobObject = Assert.NotNull(result.JobObject);
            Assert.True(result.Created);
            return;
        }

        Assert.True(result.UnsupportedPlatform);
        Assert.False(result.Created);
        Assert.Null(result.JobObject);
    }

    [Fact]
    public static void CreateWithKillOnCloseRejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => RuntimeJobObject.CreateWithKillOnClose(string.Empty));
    }

    private static string CreateUniqueJobName()
    {
        return OperatingSystem.IsWindows()
            ? $@"Local\Z2P_TEST_JOB_{Guid.NewGuid():N}"
            : $"Z2P_TEST_JOB_{Guid.NewGuid():N}";
    }
}

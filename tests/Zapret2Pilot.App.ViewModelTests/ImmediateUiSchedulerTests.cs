using System;
using Xunit;
using Zapret2Pilot.App.Threading;

namespace Zapret2Pilot.App.ViewModelTests;

public sealed class ImmediateUiSchedulerTests
{
    [Fact]
    public static void ScheduleRunsActionSynchronously()
    {
        ImmediateUiScheduler scheduler = new();
        bool executed = false;

        scheduler.Schedule(() =>
        {
            executed = true;
        });

        Assert.True(executed);
    }

    [Fact]
    public static void ScheduleRejectsNullAction()
    {
        ImmediateUiScheduler scheduler = new();

        Assert.Throws<ArgumentNullException>(() => scheduler.Schedule(null!));
    }
}

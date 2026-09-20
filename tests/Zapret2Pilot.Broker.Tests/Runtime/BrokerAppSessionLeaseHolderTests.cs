using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Broker.Runtime;

namespace Zapret2Pilot.Broker.Tests.Runtime;

public sealed class BrokerAppSessionLeaseHolderTests
{
    [Fact]
    public static async Task FirstVerifiedLeaseBecomesPermanentSessionOwner()
    {
        using BrokerAppSessionLeaseHolder holder = new();
        FakeLease first = new();
        FakeLease replacement = new();

        Assert.True(holder.TryBind(first));
        Assert.False(holder.TryBind(replacement));
        Assert.True(replacement.Disposed);
        Assert.False(first.Disposed);

        Task wait = holder.WaitForExitAsync(
            TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        first.SignalExit();
        await wait.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
    }

    private sealed class FakeLease : IBrokerAppSessionLease
    {
        private readonly TaskCompletionSource<bool> exited = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
            => exited.Task.WaitAsync(cancellationToken);

        public void SignalExit() => exited.TrySetResult(true);

        public void Dispose()
        {
            Disposed = true;
            exited.TrySetCanceled();
        }
    }
}

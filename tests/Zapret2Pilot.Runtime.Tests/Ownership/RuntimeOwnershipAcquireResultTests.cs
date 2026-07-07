using System;
using System.Reflection;
using System.Threading;
using Xunit;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Tests.Ownership;

public sealed class RuntimeOwnershipAcquireResultTests
{
    [Fact]
#pragma warning disable CA1707 // Identifiers should not contain underscores (behavior-descriptive test name)
    public static void NotAcquired_WithNoneReason_ThrowsArgumentException()
#pragma warning restore CA1707
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => RuntimeOwnershipAcquireResult.NotAcquired(RuntimeOwnershipAcquireFailureReason.None));

        Assert.Equal("reason", exception.ParamName);
    }

    [Fact]
#pragma warning disable CA1707 // Identifiers should not contain underscores (behavior-descriptive test name)
    public static void CreateAcquired_WithNonNoneFailureReason_ThrowsArgumentException()
#pragma warning restore CA1707
    {
        // The constructor validation that rejects an acquired result carrying
        // a non-None failure reason is private. Verify it through reflection so
        // a future regression that exposes the validation to the public surface
        // still has coverage. The lease instance is required to satisfy the
        // earlier "acquired result must contain a lease" check. The dummy mutex
        // is owned on construction so that lease.Dispose() can release it on
        // the same managed thread that created the lease.
        using Mutex dummyMutex = new(initiallyOwned: true);
        RuntimeOwnershipLease lease = new(
            mutexName: "runtime-ownership-acquire-result-validation",
            mutex: dummyMutex,
            wasAbandoned: false);

        ConstructorInfo? constructor = typeof(RuntimeOwnershipAcquireResult).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: new[]
            {
                typeof(bool),
                typeof(bool),
                typeof(RuntimeOwnershipLease),
                typeof(RuntimeOwnershipAcquireFailureReason),
            },
            modifiers: null);

        Assert.NotNull(constructor);

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => constructor!.Invoke(new object[]
            {
                true,
                false,
                lease,
                RuntimeOwnershipAcquireFailureReason.TimedOut,
            }));

        ArgumentException argumentException = Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal("failureReason", argumentException.ParamName);

        lease.Dispose();
    }
}

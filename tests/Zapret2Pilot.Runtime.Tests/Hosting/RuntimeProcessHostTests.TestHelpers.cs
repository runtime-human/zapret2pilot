using System;

namespace Zapret2Pilot.Runtime.Tests.Hosting;

/// <summary>
/// Test-only helpers that let other test files in
/// <c>Zapret2Pilot.Runtime.Tests</c> share the
/// <see cref="HostFixture"/> used by <see cref="RuntimeProcessHostTests"/>.
/// The helpers are intentionally isolated in a partial-class file
/// so the existing <see cref="RuntimeProcessHostTests"/> test bodies
/// stay focused on the host contract.
/// </summary>
public sealed partial class RuntimeProcessHostTests
{
    private HostFixture? sharedHostFixture;

    internal HostFixture HostFixtureInstance =>
        this.sharedHostFixture
            ?? throw new InvalidOperationException(
                "CreateHostFixture() must be called before accessing HostFixtureInstance.");

    internal HostFixture CreateHostFixture()
    {
        this.sharedHostFixture ??= HostFixture.Create();
        return this.sharedHostFixture;
    }

    internal void DisposeHostFixture()
    {
        this.sharedHostFixture?.Dispose();
        this.sharedHostFixture = null;
    }
}

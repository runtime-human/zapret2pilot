using System;

namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Public contract for the runtime health monitor. Exposes a hot
/// <see cref="IObservable{T}"/> of <see cref="RuntimeHealthSnapshot"/>
/// values plus a fast-path accessor for the most recent snapshot.
/// Subscribers receive the initial <see cref="RuntimeHealthState.Unknown"/>
/// snapshot synchronously, then a new snapshot on every probe tick
/// where the state changes (and additionally an
/// <see cref="RuntimeHealthState.Exited"/> snapshot whenever a
/// transition into <see cref="RuntimeHealthState.Exited"/> is
/// observed while the kernel state store has an active session, even
/// if the underlying state did not change between two consecutive
/// probes).
/// </summary>
public interface IRuntimeHealthMonitor
{
    /// <summary>
    /// Hot observable that emits the current snapshot on
    /// subscription and a new snapshot on every transition.
    /// Subscribers must add their own <c>ObserveOn</c> if they need
    /// a particular scheduler.
    /// </summary>
    IObservable<RuntimeHealthSnapshot> SnapshotChanged { get; }

    /// <summary>
    /// Most recent snapshot observed by the monitor. Returns
    /// <see cref="RuntimeHealthState.Unknown"/> before
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync"/>
    /// is called.
    /// </summary>
    RuntimeHealthSnapshot LatestSnapshot { get; }
}

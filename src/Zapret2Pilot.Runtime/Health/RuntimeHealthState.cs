namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Lifecycle state of the runtime process as observed by the
/// <see cref="RuntimeHealthMonitor"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Unknown"/> — the monitor has no live process
///         to probe (no runtime has been started, or the host has
///         cleared its state).</item>
///   <item><see cref="Healthy"/> — the runtime process is alive
///         and running.</item>
///   <item><see cref="Exited"/> — the runtime process has exited
///         unexpectedly. The monitor marks the active session as
///         <see cref="State.RuntimeSessionState.Failed"/> on the
///         transition into <see cref="Exited"/>.</item>
/// </list>
/// </remarks>
public enum RuntimeHealthState
{
    Unknown,
    Healthy,
    Exited,
}

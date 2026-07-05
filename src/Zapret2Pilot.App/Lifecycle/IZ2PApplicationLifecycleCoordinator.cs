using System;

namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Read-only view of the application startup lifecycle surfaced
/// to the shell. UI consumers (e.g. <c>MainWindowViewModel</c>)
/// bind runtime commands to <see cref="IsReady"/> so that no
/// runtime action can be triggered before the coordinator reaches
/// <see cref="ApplicationLifecyclePhase.Ready"/>. The full
/// implementation is <see cref="Z2PApplicationLifecycleCoordinator"/>,
/// which doubles as <c>IHostedService</c> and is registered in
/// <c>AppServiceCollectionExtensions.AddZ2PAppServices</c>.
/// </summary>
public interface IZ2PApplicationLifecycleCoordinator
{
    /// <summary>
    /// The most recent phase the coordinator has transitioned to.
    /// </summary>
    ApplicationLifecyclePhase CurrentPhase { get; }

    /// <summary>
    /// Convenience flag equivalent to
    /// <c>CurrentPhase == ApplicationLifecyclePhase.Ready</c>.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Hot observable that emits the new phase every time the
    /// coordinator advances. The current phase is replayed to
    /// late subscribers by the underlying <c>BehaviorSubject</c>.
    /// </summary>
    IObservable<ApplicationLifecyclePhase> PhaseChanged { get; }
}

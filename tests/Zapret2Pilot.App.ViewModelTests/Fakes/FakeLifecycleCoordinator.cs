using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Zapret2Pilot.App.Lifecycle;

namespace Zapret2Pilot.App.ViewModelTests.Fakes;

/// <summary>
/// Test double for
/// <see cref="IZ2PApplicationLifecycleCoordinator"/>. The fake
/// starts in <see cref="ApplicationLifecyclePhase.Ready"/> so the
/// default <c>MainWindowViewModel</c> helper enables runtime
/// commands; tests that need a different phase call
/// <see cref="SetPhase"/> before constructing the view model.
/// <see cref="SignalShellVisible"/> is a no-op for the fake —
/// tests do not need a real pipeline, and the production
/// coordinator's start signal is meaningless to a unit test
/// that drives the phase directly.
/// </summary>
public sealed class FakeLifecycleCoordinator : IZ2PApplicationLifecycleCoordinator, IDisposable
{
    private readonly BehaviorSubject<ApplicationLifecyclePhase> phase = new(ApplicationLifecyclePhase.Ready);

    public ApplicationLifecyclePhase CurrentPhase => phase.Value;

    public bool IsReady => CurrentPhase == ApplicationLifecyclePhase.Ready;

    public IObservable<ApplicationLifecyclePhase> PhaseChanged => phase.AsObservable();

    public void SetPhase(ApplicationLifecyclePhase nextPhase) => phase.OnNext(nextPhase);

    public void SignalShellVisible()
    {
        // No-op: the fake is driven directly by SetPhase; there is
        // no pipeline to start.
    }

    public void Dispose()
    {
        phase.Dispose();
    }
}

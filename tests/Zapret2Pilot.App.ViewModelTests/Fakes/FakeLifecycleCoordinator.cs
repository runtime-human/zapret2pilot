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
/// </summary>
public sealed class FakeLifecycleCoordinator : IZ2PApplicationLifecycleCoordinator, IDisposable
{
    private readonly BehaviorSubject<ApplicationLifecyclePhase> phase = new(ApplicationLifecyclePhase.Ready);

    public ApplicationLifecyclePhase CurrentPhase => phase.Value;

    public bool IsReady => CurrentPhase == ApplicationLifecyclePhase.Ready;

    public IObservable<ApplicationLifecyclePhase> PhaseChanged => phase.AsObservable();

    public void SetPhase(ApplicationLifecyclePhase nextPhase) => phase.OnNext(nextPhase);

    public void Dispose()
    {
        phase.Dispose();
    }
}

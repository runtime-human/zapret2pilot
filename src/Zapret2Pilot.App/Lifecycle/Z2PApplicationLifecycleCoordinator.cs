using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Default <see cref="IZ2PApplicationLifecycleCoordinator"/>
/// implementation. It is registered both as a singleton (so the
/// shell can observe <see cref="PhaseChanged"/>) and as an
/// <see cref="IHostedService"/> (so <see cref="StartAsync"/>
/// drives the canonical phase transitions on host startup).
///
/// Per <c>docs/Z2P-PLAN-0.0.25.md</c> Scope B, the coordinator
/// auto-advances through
/// <c>ProcessBootstrap</c> → <c>ShellVisible</c> →
/// <c>StorageRecovery</c> → <c>DeploymentVerification</c> →
/// <c>OwnershipRecovery</c> → <c>CompatibilityPreflight</c> →
/// <c>Ready</c>. <see cref="StopAsync"/> publishes
/// <c>Stopping</c> and is idempotent.
/// </summary>
public sealed class Z2PApplicationLifecycleCoordinator
    : IHostedService, IZ2PApplicationLifecycleCoordinator, IDisposable
{
    private readonly BehaviorSubject<ApplicationLifecyclePhase> phase =
        new(ApplicationLifecyclePhase.ProcessBootstrap);

    public ApplicationLifecyclePhase CurrentPhase => phase.Value;

    public bool IsReady => CurrentPhase == ApplicationLifecyclePhase.Ready;

    public IObservable<ApplicationLifecyclePhase> PhaseChanged => phase.AsObservable();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            await TransitionToAsync(ApplicationLifecyclePhase.ShellVisible).ConfigureAwait(false);
            await TransitionToAsync(ApplicationLifecyclePhase.StorageRecovery).ConfigureAwait(false);
            await TransitionToAsync(ApplicationLifecyclePhase.DeploymentVerification).ConfigureAwait(false);
            await TransitionToAsync(ApplicationLifecyclePhase.OwnershipRecovery).ConfigureAwait(false);
            await TransitionToAsync(ApplicationLifecyclePhase.CompatibilityPreflight).ConfigureAwait(false);
            await TransitionToAsync(ApplicationLifecyclePhase.Ready).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (CurrentPhase != ApplicationLifecyclePhase.Stopping)
        {
            phase.OnNext(ApplicationLifecyclePhase.Stopping);
        }

        return Task.CompletedTask;
    }

    private async Task TransitionToAsync(ApplicationLifecyclePhase nextPhase)
    {
        await Task.Yield();
        phase.OnNext(nextPhase);
    }

    public void Dispose()
    {
        phase.Dispose();
    }
}

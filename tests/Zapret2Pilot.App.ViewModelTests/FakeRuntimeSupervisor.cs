using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.ViewModelTests;

/// <summary>
/// In-memory <see cref="IRuntimeSupervisor"/> fake for view-model
/// tests. The state stream is a <see cref="BehaviorSubject{T}"/> so
/// <see cref="CurrentState"/> always reflects the most recently
/// published value, and tests can drive <see cref="Publish"/> to
/// trigger the view model's supervisor-state handler synchronously
/// on the calling thread.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StartAsync"/> and <see cref="StopAsync"/> are no-ops
/// that return <see cref="Result.Success{T}(T)"/> so the fake can
/// satisfy the interface contract without spinning up a real
/// runtime. View-model tests that need to observe a
/// <see cref="RuntimeSupervisorState"/> transition only have to
/// call <see cref="Publish"/> with the desired snapshot.
/// </para>
/// <para>
/// The fake seeds its subject with a <c>Stopped</c> snapshot at the
/// current <see cref="DateTimeOffset.UtcNow"/> so any subscriber
/// created before the first <see cref="Publish"/> call sees a
/// defined state. The view model under test never inspects the
/// seed, but the contract on <see cref="IRuntimeSupervisor.CurrentState"/>
/// is that the property is always readable.
/// </para>
/// </remarks>
public sealed class FakeRuntimeSupervisor : IRuntimeSupervisor, IDisposable
{
    private readonly BehaviorSubject<RuntimeSupervisorState> stateSubject = new(
        new RuntimeSupervisorState(
            status: RuntimeSupervisorStatus.Stopped,
            lastStartResult: null,
            guardResult: null,
            lastError: null,
            timestamp: DateTimeOffset.UtcNow));

    public RuntimeSupervisorState CurrentState => stateSubject.Value;

    public IObservable<RuntimeSupervisorState> StateChanged => stateSubject.AsObservable();

    public Task<Result<RuntimeProcessHostResult>> StartAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Success(new RuntimeProcessHostResult(
            processId: 1,
            processName: "fake",
            executablePath: "fake",
            plan: new CompiledZapretPlan(
                generatedConfigContent: string.Empty,
                argsContent: string.Empty,
                hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>()))));
    }

    public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Success(Unit.Instance));
    }

    /// <summary>
    /// Pushes a new <see cref="RuntimeSupervisorState"/> through the
    /// fake. Subscribers of <see cref="StateChanged"/> see the new
    /// snapshot synchronously on the calling thread.
    /// </summary>
    /// <param name="state">Snapshot to publish. Must not be <c>null</c>.</param>
    public void Publish(RuntimeSupervisorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        stateSubject.OnNext(state);
    }

    public void Dispose()
    {
        stateSubject.Dispose();
    }
}

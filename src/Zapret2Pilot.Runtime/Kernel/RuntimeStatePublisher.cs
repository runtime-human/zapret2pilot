using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;

namespace Zapret2Pilot.Runtime.Kernel;

public sealed class RuntimeStatePublisher : IDisposable
{
    private readonly BehaviorSubject<RuntimeKernelState> subject;
    private readonly IObservable<RuntimeKernelState> observable;
    private int disposed;

    public RuntimeStatePublisher(RuntimeKernelState initialState)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        subject = new BehaviorSubject<RuntimeKernelState>(initialState);
        observable = subject
            .AsObservable()
            .ObserveOn(TaskPoolScheduler.Default);
    }

    public RuntimeKernelState LatestState => subject.Value;

    public IObservable<RuntimeKernelState> StateChanged => observable;

    public void Publish(RuntimeKernelState state)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(state);
        subject.OnNext(state);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        subject.OnCompleted();
        subject.Dispose();
    }
}

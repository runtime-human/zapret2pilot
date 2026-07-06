namespace Zapret2Pilot.App.Diagnostics;

/// <summary>
/// Classification of an unhandled exception's impact on application state.
/// </summary>
public enum ExceptionSeverity
{
    Recoverable,
    StateCompromising,
    Fatal
}

/// <summary>
/// Source / origin of an unhandled exception, used for logging context.
/// </summary>
public enum ExceptionContext
{
    AppDomain,
    TaskScheduler,
    ReactiveUI,
    Dispatcher,
    HostedLifecycle
}

using System;
using Microsoft.Extensions.Logging;

namespace Zapret2Pilot.App.Diagnostics;

/// <summary>
/// Default <see cref="IExceptionPolicy"/>: classifies, logs, and decides whether
/// the process may continue. State-compromising exceptions do NOT allow blind
/// continuation; only <see cref="ExceptionSeverity.Recoverable"/> does.
/// </summary>
public sealed class ExceptionPolicy : IExceptionPolicy
{
    private const string UnhandledExceptionTemplate =
        "Unhandled exception in {Context}: {ExceptionType}";

    // CA1848 (LoggerMessage source generator) and CA1873 (expensive
    // logging argument) are suppressed narrowly. The three logger calls
    // run only on the global error handlers — never on a hot path — so
    // the allocation cost is acceptable. LoggerMessage migration is
    // tracked separately and out of scope for this packet.
#pragma warning disable CA1848, CA1873
    private readonly ILogger<ExceptionPolicy> logger;

    public ExceptionPolicy(ILogger<ExceptionPolicy> logger)
    {
        this.logger = logger;
    }

    public ExceptionSeverity Handle(Exception exception, ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        ExceptionSeverity severity = ExceptionClassifier.Classify(exception);
        string exceptionType = exception.GetType().Name;

        switch (severity)
        {
            case ExceptionSeverity.Fatal:
                logger.LogCritical(
                    exception,
                    UnhandledExceptionTemplate,
                    context,
                    exceptionType);
                break;
            case ExceptionSeverity.StateCompromising:
                logger.LogError(
                    exception,
                    UnhandledExceptionTemplate,
                    context,
                    exceptionType);
                break;
            default:
                logger.LogWarning(
                    exception,
                    UnhandledExceptionTemplate,
                    context,
                    exceptionType);
                break;
        }

        return severity;
    }

    public bool ShouldContinue(Exception exception)
    {
        return ExceptionClassifier.Classify(exception) == ExceptionSeverity.Recoverable;
    }
#pragma warning restore CA1848, CA1873
}

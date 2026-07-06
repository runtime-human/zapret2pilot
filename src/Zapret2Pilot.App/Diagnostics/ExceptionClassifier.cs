using System;
using System.Runtime.InteropServices;

namespace Zapret2Pilot.App.Diagnostics;

/// <summary>
/// Pure, allocation-light classification of unhandled exceptions.
/// Safe to call from <see cref="AppDomain.UnhandledException"/>.
/// </summary>
public static class ExceptionClassifier
{
    private const string StateCompromisedKey = "Z2P.StateCompromised";

    public static ExceptionSeverity Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OutOfMemoryException => ExceptionSeverity.Fatal,
            AccessViolationException => ExceptionSeverity.Fatal,
            SEHException => ExceptionSeverity.Fatal,
            InvalidOperationException ioe
                when ioe.Message.Contains("corrupt", StringComparison.OrdinalIgnoreCase)
                  || ioe.Message.Contains("corrupted", StringComparison.OrdinalIgnoreCase)
                  || ioe.Data.Contains(StateCompromisedKey) => ExceptionSeverity.StateCompromising,
            _ => ExceptionSeverity.Recoverable
        };
    }
}

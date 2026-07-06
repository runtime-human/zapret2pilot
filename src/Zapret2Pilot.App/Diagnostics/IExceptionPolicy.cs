using System;

namespace Zapret2Pilot.App.Diagnostics;

/// <summary>
/// Application-wide policy for handling unhandled exceptions.
/// </summary>
public interface IExceptionPolicy
{
    ExceptionSeverity Handle(Exception exception, ExceptionContext context);

    bool ShouldContinue(Exception exception);
}

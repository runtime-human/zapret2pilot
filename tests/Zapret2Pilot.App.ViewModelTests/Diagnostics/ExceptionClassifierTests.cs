using System;
using System.Runtime.InteropServices;
using Xunit;
using Zapret2Pilot.App.Diagnostics;

namespace Zapret2Pilot.App.ViewModelTests.Diagnostics;

// CA2201: this test deliberately constructs the runtime-reserved
// exception types (OutOfMemoryException, AccessViolationException,
// SEHException) and the plain base System.Exception to verify the
// classifier's behaviour for every branch. The classification
// contract requires these exact types, so the analyzer's
// "reserved / insufficiently specific" warning is expected here.
#pragma warning disable CA2201 // Do not raise reserved exception types

public sealed class ExceptionClassifierTests
{
    [Fact]
    public static void OutOfMemoryExceptionIsFatal()
    {
        ExceptionSeverity severity = ExceptionClassifier.Classify(new OutOfMemoryException());

        Assert.Equal(ExceptionSeverity.Fatal, severity);
    }

    [Fact]
    public static void AccessViolationExceptionIsFatal()
    {
        ExceptionSeverity severity = ExceptionClassifier.Classify(new AccessViolationException());

        Assert.Equal(ExceptionSeverity.Fatal, severity);
    }

    [Fact]
    public static void SEHExceptionIsFatal()
    {
        ExceptionSeverity severity = ExceptionClassifier.Classify(new SEHException());

        Assert.Equal(ExceptionSeverity.Fatal, severity);
    }

    [Fact]
    public static void GenericExceptionIsRecoverable()
    {
        ExceptionSeverity severity = ExceptionClassifier.Classify(new Exception("boom"));

        Assert.Equal(ExceptionSeverity.Recoverable, severity);
    }

    [Fact]
    public static void InvalidOperationExceptionWithCorruptMessageIsStateCompromising()
    {
        ExceptionSeverity severity = ExceptionClassifier.Classify(
            new InvalidOperationException("state is corrupt, refusing to continue"));

        Assert.Equal(ExceptionSeverity.StateCompromising, severity);
    }

    [Fact]
    public static void InvalidOperationExceptionWithCorruptedMessageIsStateCompromising()
    {
        ExceptionSeverity severity = ExceptionClassifier.Classify(
            new InvalidOperationException("storage corrupted after power loss"));

        Assert.Equal(ExceptionSeverity.StateCompromising, severity);
    }

    [Fact]
    public static void InvalidOperationExceptionWithStateCompromisedDataIsStateCompromising()
    {
        InvalidOperationException exception = new("transient failure");
        exception.Data["Z2P.StateCompromised"] = true;

        ExceptionSeverity severity = ExceptionClassifier.Classify(exception);

        Assert.Equal(ExceptionSeverity.StateCompromising, severity);
    }

    [Fact]
    public static void PlainInvalidOperationExceptionIsRecoverable()
    {
        ExceptionSeverity severity = ExceptionClassifier.Classify(
            new InvalidOperationException("transient failure"));

        Assert.Equal(ExceptionSeverity.Recoverable, severity);
    }

    [Fact]
    public static void NullExceptionIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            static () => ExceptionClassifier.Classify(null!));
    }
}

#pragma warning restore CA2201 // Do not raise reserved exception types

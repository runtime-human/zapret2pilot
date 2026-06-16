using System;
using Xunit;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Core.Tests;

public sealed class CoreResultsTests
{
    [Fact]
    public static void UnitExposesStableInstance()
    {
        Unit first = Unit.Instance;
        Unit second = Unit.Instance;

        Assert.Equal(first, second);
    }

    [Fact]
    public static void ErrorInfoExposesStableFields()
    {
        ErrorInfo error = new(
            "Z2P.PROFILE.INVALID",
            "Profile is invalid.",
            ErrorSeverity.Error,
            ErrorCategory.Profile);

        Assert.Equal("Z2P.PROFILE.INVALID", error.Code);
        Assert.Equal("Profile is invalid.", error.Message);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
        Assert.Equal(ErrorCategory.Profile, error.Category);
        Assert.Equal("Z2P.PROFILE.INVALID: Profile is invalid.", error.ToString());
    }

    [Fact]
    public static void ErrorInfoRejectsNullCode()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ErrorInfo(
                null!,
                "Message.",
                ErrorSeverity.Error,
                ErrorCategory.General));
    }

    [Fact]
    public static void ErrorInfoRejectsNullMessage()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ErrorInfo(
                "Z2P.TEST.ERROR",
                null!,
                ErrorSeverity.Error,
                ErrorCategory.General));
    }

    [Fact]
    public static void ErrorInfoRejectsEmptyCode()
    {
        Assert.Throws<ArgumentException>(() =>
            new ErrorInfo(
                string.Empty,
                "Message.",
                ErrorSeverity.Error,
                ErrorCategory.General));
    }

    [Fact]
    public static void ErrorInfoRejectsWhitespaceMessage()
    {
        Assert.Throws<ArgumentException>(() =>
            new ErrorInfo(
                "Z2P.TEST.ERROR",
                "   ",
                ErrorSeverity.Error,
                ErrorCategory.General));
    }

    [Fact]
    public static void ResultSuccessHasNoFailure()
    {
        Result result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public static void ResultFailureExposesError()
    {
        ErrorInfo error = CreateError();

        Result result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public static void ResultFailureRejectsNullError()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public static void GenericResultSuccessExposesValue()
    {
        Result<string> result = Result.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("ok", result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public static void GenericResultSuccessRejectsNullValue()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Fact]
    public static void GenericResultFailureExposesError()
    {
        ErrorInfo error = CreateError();

        Result<string> result = Result.Failure<string>(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public static void GenericResultFailureRejectsNullError()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure<string>(null!));
    }

    [Fact]
    public static void FailedGenericResultThrowsOnValue()
    {
        Result<string> result = Result.Failure<string>(CreateError());

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public static void SuccessfulGenericResultThrowsOnError()
    {
        Result<string> result = Result.Success("ok");

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    private static ErrorInfo CreateError()
    {
        return new ErrorInfo(
            "Z2P.TEST.ERROR",
            "Test error.",
            ErrorSeverity.Error,
            ErrorCategory.General);
    }
}

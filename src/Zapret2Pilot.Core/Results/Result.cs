using System;

namespace Zapret2Pilot.Core.Results;

/// <summary>
/// Represents an operation result without a payload and provides factory methods for generic results.
/// </summary>
public sealed class Result
{
    private readonly ErrorInfo? error;

    private Result()
    {
        IsSuccess = true;
    }

    private Result(ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IsSuccess = false;
        this.error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public ErrorInfo Error =>
        error ?? throw new InvalidOperationException("Successful result does not contain an error.");

    public static Result Success()
    {
        return new Result();
    }

    public static Result Failure(ErrorInfo error)
    {
        return new Result(error);
    }

    public static Result<T> Success<T>(T value)
        where T : notnull
    {
        return new Result<T>(value);
    }

    public static Result<T> Failure<T>(ErrorInfo error)
        where T : notnull
    {
        return new Result<T>(error);
    }
}

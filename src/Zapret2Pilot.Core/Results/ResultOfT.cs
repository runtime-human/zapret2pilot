using System;

namespace Zapret2Pilot.Core.Results;

/// <summary>
/// Represents an operation result with a payload.
/// </summary>
/// <typeparam name="T">Payload type.</typeparam>
public sealed class Result<T>
    where T : notnull
{
    private readonly ErrorInfo? error;
    private readonly T? value;

    internal Result(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        IsSuccess = true;
        this.value = value;
    }

    internal Result(ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IsSuccess = false;
        this.error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value =>
        IsSuccess
            ? value!
            : throw new InvalidOperationException("Failed result does not contain a value.");

    public ErrorInfo Error =>
        error ?? throw new InvalidOperationException("Successful result does not contain an error.");
}

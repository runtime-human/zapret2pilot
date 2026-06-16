using Zapret2Pilot.Core.Internal;

namespace Zapret2Pilot.Core.Results;

/// <summary>
/// Stable error descriptor used by domain and application results.
/// </summary>
public sealed record ErrorInfo
{
    public ErrorInfo(
        string code,
        string message,
        ErrorSeverity severity,
        ErrorCategory category)
    {
        Code = Guard.NotNullOrWhiteSpace(code, nameof(code));
        Message = Guard.NotNullOrWhiteSpace(message, nameof(message));
        Severity = severity;
        Category = category;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorSeverity Severity { get; }

    public ErrorCategory Category { get; }

    public override string ToString()
    {
        return $"{Code}: {Message}";
    }
}

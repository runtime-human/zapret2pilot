using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

/// <summary>
/// Shared helpers for use-case facades.
/// </summary>
internal static class UseCaseErrors
{
    /// <summary>
    /// Standard "not implemented" error returned by every typed
    /// use-case facade in 0.0.25 Packet 2 (Scope E). The facades are
    /// the long-term contract; the underlying production
    /// implementations will be wired in later packets. Until then,
    /// every method returns a <see cref="Result{T}"/> failure with
    /// this descriptor so callers can be compiled and resolved from
    /// DI without exposing half-implemented behaviour.
    /// </summary>
    /// <param name="feature">
    /// Human-readable name of the feature (typically the method
    /// name) used to compose the error message.
    /// </param>
    /// <returns>
    /// A stable <see cref="ErrorInfo"/> with code
    /// <c>Z2P.APPLICATION.NOT_IMPLEMENTED</c>.
    /// </returns>
    internal static ErrorInfo NotImplemented(string feature)
    {
        return new ErrorInfo(
            "Z2P.APPLICATION.NOT_IMPLEMENTED",
            $"Use case '{feature}' is not implemented in milestone 0.0.25.",
            ErrorSeverity.Warning,
            ErrorCategory.Application);
    }
}

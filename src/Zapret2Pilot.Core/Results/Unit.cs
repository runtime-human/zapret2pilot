namespace Zapret2Pilot.Core.Results;

/// <summary>
/// Represents an empty successful value.
/// </summary>
public readonly record struct Unit
{
    public static Unit Instance => default;
}

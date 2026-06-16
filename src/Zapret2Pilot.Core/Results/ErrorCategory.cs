namespace Zapret2Pilot.Core.Results;

/// <summary>
/// High-level category of a domain or application error.
/// </summary>
public enum ErrorCategory
{
    General = 0,
    Validation = 1,
    Profile = 2,
    StrategyPack = 3,
    Hostlist = 4,
    Runtime = 5,
    Probing = 6,
    Diagnostics = 7,
    Security = 8,
    Storage = 9,
    Application = 10,
}

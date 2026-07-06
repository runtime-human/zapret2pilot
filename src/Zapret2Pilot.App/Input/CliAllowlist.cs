using System;

namespace Zapret2Pilot.App.Input;

/// <summary>
/// Mode the CLI has selected. Anything that is not
/// <see cref="Default"/> short-circuits the host bootstrap in
/// <see cref="Program"/>.
/// </summary>
public enum CliLaunchMode
{
    Default = 0,
    Help = 1,
    Version = 2,
}

/// <summary>
/// Outcome of parsing and validating the application's command-line
/// arguments. The host bootstrap in <see cref="Program"/> is only
/// allowed to continue when <see cref="IsAllowed"/> is
/// <c>true</c>.
/// </summary>
public sealed record CliParseResult(
    bool IsAllowed,
    CliLaunchMode Mode,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Strict allowlist parser for the application's command-line
/// arguments. Anything outside the allowlist is rejected so an
/// attacker cannot pivot through the shell into the host bootstrap
/// (Generic Host, configuration sources, environment variables).
/// </summary>
public static class CliAllowlist
{
    internal const string ErrorCodeUnknownArgument = "Z2P.INPUT.CLI_UNKNOWN_ARGUMENT";
    internal const string ErrorCodeTooManyArguments = "Z2P.INPUT.CLI_TOO_MANY_ARGUMENTS";

    public static CliParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return new CliParseResult(IsAllowed: true, CliLaunchMode.Default, ErrorCode: null, ErrorMessage: null);
        }

        if (args.Length > 1)
        {
            return new CliParseResult(
                IsAllowed: false,
                CliLaunchMode.Default,
                ErrorCode: ErrorCodeTooManyArguments,
                ErrorMessage: "Only a single option is allowed. Use --help for usage.");
        }

        string option = args[0];

        return option switch
        {
            "--help" or "-h" or "/help" or "/?" => new CliParseResult(
                IsAllowed: true,
                CliLaunchMode.Help,
                ErrorCode: null,
                ErrorMessage: null),
            "--version" => new CliParseResult(
                IsAllowed: true,
                CliLaunchMode.Version,
                ErrorCode: null,
                ErrorMessage: null),
            _ => new CliParseResult(
                IsAllowed: false,
                CliLaunchMode.Default,
                ErrorCode: ErrorCodeUnknownArgument,
                ErrorMessage: $"Unknown command-line argument '{option}'. Use --help for usage."),
        };
    }
}

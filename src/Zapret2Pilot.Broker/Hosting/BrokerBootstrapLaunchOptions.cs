using System;

namespace Zapret2Pilot.Broker.Hosting;

/// <summary>
/// Non-secret bootstrap coordinates accepted by z2p-broker.exe.
/// Secret/session material is never carried on the command line.
/// </summary>
public sealed record BrokerBootstrapLaunchOptions(
    string BootstrapPipeName,
    int AppProcessId)
{
    private const int MaximumPipeNameLength = 128;

    public static bool TryParse(
        string[] args,
        out BrokerBootstrapLaunchOptions? options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = null;

        if (args.Length != 4)
        {
            error =
                "Expected exactly --bootstrap-pipe <name> --app-pid <pid>.";
            return false;
        }

        string? pipeName = null;
        int? appProcessId = null;

        for (int index = 0; index < args.Length; index += 2)
        {
            string key = args[index];
            string value = args[index + 1];

            switch (key)
            {
                case "--bootstrap-pipe" when pipeName is null:
                    pipeName = value;
                    break;

                case "--app-pid" when appProcessId is null:
                    if (!int.TryParse(
                            value,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out int parsedPid)
                        || parsedPid <= 0)
                    {
                        error = "App PID must be a positive decimal integer.";
                        return false;
                    }

                    appProcessId = parsedPid;
                    break;

                default:
                    error =
                        $"Unknown, duplicate, or malformed broker bootstrap option '{key}'.";
                    return false;
            }
        }

        if (!IsValidPipeName(pipeName)
            || appProcessId is null)
        {
            error = "Broker bootstrap coordinates are invalid.";
            return false;
        }

        options = new(pipeName!, appProcessId.Value);
        return true;
    }

    private static bool IsValidPipeName(string? pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName)
            || pipeName.Length > MaximumPipeNameLength)
        {
            return false;
        }

        foreach (char character in pipeName)
        {
            if (!(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}

using System;

namespace Zapret2Pilot.Core.Internal;

internal static class Guard
{
    public static string NotNullOrWhiteSpace(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        string normalized = value.Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Value must not be empty or whitespace.", parameterName);
        }

        return normalized;
    }
}

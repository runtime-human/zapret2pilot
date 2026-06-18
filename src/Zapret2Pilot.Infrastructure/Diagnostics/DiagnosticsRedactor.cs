using System;
using System.Text.RegularExpressions;

namespace Zapret2Pilot.Infrastructure.Diagnostics;

public static class DiagnosticsRedactor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex UrlQueryPattern = new(
        @"\b(?<prefix>[a-z][a-z0-9+.-]*://[^\s?#]+)(?<query>\?[^#\s]*)(?<fragment>#[^\s]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex KeyValuePattern = new(
        @"\b(token|api_key|password)\s*=\s*([^\s&;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex BearerPattern = new(
        @"\b(Authorization\s*:\s*Bearer\s+)([^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex WindowsUserProfilePattern = new(
        @"\b([A-Z]:\\Users\\)([^\\\r\n]+)(\\[^\r\n]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string redacted = UrlQueryPattern.Replace(
            text,
            match =>
            {
                string fragment = match.Groups["fragment"].Success
                    ? "#<redacted>"
                    : string.Empty;

                return $"{match.Groups["prefix"].Value}?<redacted>{fragment}";
            });

        redacted = KeyValuePattern.Replace(
            redacted,
            match => $"{match.Groups[1].Value}=<redacted>");

        redacted = BearerPattern.Replace(
            redacted,
            match => $"{match.Groups[1].Value}<redacted>");

        redacted = WindowsUserProfilePattern.Replace(
            redacted,
            match =>
            {
                string tail = match.Groups[3].Success
                    ? match.Groups[3].Value
                    : string.Empty;

                return $"{match.Groups[1].Value}<redacted>{tail}";
            });

        return redacted;
    }
}

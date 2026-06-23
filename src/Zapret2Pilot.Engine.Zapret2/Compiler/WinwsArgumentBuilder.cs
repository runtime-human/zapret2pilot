using System.Collections.Generic;
using System.Text;
using Zapret2Pilot.Core.Profiles;

namespace Zapret2Pilot.Engine.Zapret2.Compiler;

/// <summary>
/// Pure, dependency-free <c>winws2</c> argument builder used by
/// <see cref="ZapretPlanCompiler"/>. The builder does NOT access the
/// filesystem and does NOT load hostlist contents; the hostlist
/// <c>RelativePath</c> is the only input it consumes.
///
/// <para>
/// The output is a structured list of <c>argv</c> tokens and a
/// newline-joined string that is written verbatim into the
/// <c>args.txt</c> runtime artifact (matching the existing
/// <c>RuntimeWorkspaceMaterializer</c> test convention).
/// </para>
/// </summary>
public static class WinwsArgumentBuilder
{
    /// <summary>
    /// Builder output: structured <c>argv</c> tokens and the
    /// newline-joined args-file content.
    /// </summary>
    public sealed record Result(IReadOnlyList<string> Tokens, string ArgsContent);

    /// <summary>
    /// Builds the structured argument tokens and the args-file content
    /// for the supplied strategy and hostlist assignments.
    ///
    /// <para>
    /// For each strategy in order, every <c>StrategyDefinition.Parameters</c>
    /// token is appended verbatim. For each hostlist, a single token of
    /// the form <c>--hostlists=hostlists/&lt;relativePath&gt;</c> is
    /// appended. Tokens that contain shell metacharacters are wrapped
    /// in double-quotes with embedded <c>"</c> characters escaped as
    /// <c>\"</c>.
    /// </para>
    /// </summary>
    public static Result Build(
        IReadOnlyList<StrategyAssignment> strategies,
        IReadOnlyList<HostlistAssignment> hostlists)
    {
        ArgumentNullException.ThrowIfNull(strategies, nameof(strategies));
        ArgumentNullException.ThrowIfNull(hostlists, nameof(hostlists));

        List<string> tokens = new();

        foreach (StrategyAssignment assignment in strategies)
        {
            ArgumentNullException.ThrowIfNull(assignment, nameof(strategies));
            IReadOnlyList<string> parameters = assignment.Strategy.Parameters;
            if (parameters is null)
            {
                continue;
            }

            foreach (string parameter in parameters)
            {
                if (parameter is null)
                {
                    continue;
                }

                tokens.Add(QuoteIfNeeded(parameter));
            }
        }

        foreach (HostlistAssignment assignment in hostlists)
        {
            ArgumentNullException.ThrowIfNull(assignment, nameof(hostlists));
            string token = $"--hostlists=hostlists/{assignment.RelativePath}";
            tokens.Add(QuoteIfNeeded(token));
        }

        string content = tokens.Count == 0
            ? string.Empty
            : string.Join("\n", tokens);

        return new Result(tokens, content);
    }

    /// <summary>
    /// Wraps the token in double-quotes and escapes embedded
    /// <c>"</c> as <c>\"</c> when the token contains a space, a
    /// double-quote or another shell metacharacter. Space is the
    /// minimum trigger for quoting.
    /// </summary>
    internal static string QuoteIfNeeded(string token)
    {
        if (!NeedsQuoting(token))
        {
            return token;
        }

        StringBuilder builder = new(token.Length + 2);
        builder.Append('"');

        foreach (char c in token)
        {
            if (c == '"')
            {
                builder.Append('\\').Append('"');
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static bool NeedsQuoting(string token)
    {
        foreach (char c in token)
        {
            if (IsShellMetacharacter(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShellMetacharacter(char c)
    {
        // Minimum trigger: space. Additional triggers: double-quote
        // and a small set of well-known shell metacharacters that
        // would change how winws2 parses the line if left bare.
        return c == ' '
            || c == '"'
            || c == '\t'
            || c == '\n'
            || c == '\r'
            || c == '|'
            || c == '&'
            || c == '<'
            || c == '>'
            || c == ';'
            || c == '('
            || c == ')'
            || c == '*'
            || c == '?'
            || c == '['
            || c == ']'
            || c == '^'
            || c == '!'
            || c == '$'
            || c == '`'
            || c == '\\';
    }
}

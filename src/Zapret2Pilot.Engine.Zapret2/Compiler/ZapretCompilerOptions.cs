using System;
using System.Collections.Generic;

namespace Zapret2Pilot.Engine.Zapret2.Compiler;

/// <summary>
/// Compiler options that affect the compiled output and therefore
/// participate in the content-addressed <c>RuntimePlanCacheKey</c>.
///
/// Per <c>DEC-0011</c> the cache key MUST include the compiler options
/// (in addition to the compiler version, profile document hash, strategy
/// pack hashes, hostlist fingerprints and runtime manifest hash), so
/// any field that can change the compiled <c>winws2</c> argument list
/// or the generated config belongs here.
/// </summary>
public sealed record ZapretCompilerOptions
{
    public ZapretCompilerOptions(
        string compilerVersion,
        IReadOnlyDictionary<string, string>? extraOptions = null)
    {
        CompilerVersion = GuardNotBlank(compilerVersion, nameof(compilerVersion));
        ExtraOptions = NormalizeExtraOptions(extraOptions);
    }

    /// <summary>
    /// Semantic version of the compiler that produced the plan. Used
    /// both in the cache key and surfaced in diagnostics. Never null,
    /// empty or whitespace.
    /// </summary>
    public string CompilerVersion { get; }

    /// <summary>
    /// Free-form option flags (e.g. <c>"emit-quote-hostlists"</c> &rarr;
    /// <c>"on"</c>) that affect the compiled output. Never null;
    /// may be empty. Insertion order is preserved.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraOptions { get; }

    /// <summary>
    /// Canonical, deterministic string representation of the options
    /// suitable for hashing: keys sorted lexicographically, each
    /// <c>key=value</c> pair on its own line.
    /// </summary>
    public string ToCanonicalString()
    {
        List<string> keys = new(ExtraOptions.Keys);
        keys.Sort(StringComparer.Ordinal);

        System.Text.StringBuilder builder = new();
        builder.Append("version=").Append(CompilerVersion);

        foreach (string key in keys)
        {
            builder.Append('\n').Append(key).Append('=').Append(ExtraOptions[key]);
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> NormalizeExtraOptions(
        IReadOnlyDictionary<string, string>? extraOptions)
    {
        if (extraOptions is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(extraOptions, StringComparer.Ordinal);
    }

    private static string GuardNotBlank(string value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty or whitespace.", parameterName);
        }

        return value;
    }
}

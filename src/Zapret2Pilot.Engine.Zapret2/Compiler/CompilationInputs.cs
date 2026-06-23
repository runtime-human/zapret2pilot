using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Profiles;

namespace Zapret2Pilot.Engine.Zapret2.Compiler;

/// <summary>
/// All inputs required by the Zapret plan compiler to produce a
/// <c>CompiledZapretPlan</c>.
///
/// Per <c>DEC-0011</c> the cache key MUST be derived from:
/// <list type="bullet">
///   <item>profile document hash;</item>
///   <item>strategy pack hashes (one per referenced pack);</item>
///   <item>hostlist fingerprints (one per referenced hostlist);</item>
///   <item>runtime manifest hash;</item>
///   <item>compiler version and compiler options hash.</item>
/// </list>
/// All six inputs are carried on this record so the compiler can compute
/// the content-addressed <c>RuntimePlanCacheKey</c> deterministically.
/// Inputs are expected to be pre-computed by the caller; the compiler
/// itself does not touch the filesystem and does not hash payloads.
/// </summary>
public sealed record CompilationInputs
{
    public CompilationInputs(
        ProfileDefinition definition,
        string profileDocumentHash,
        IReadOnlyDictionary<StrategyPackId, string> strategyPackHashes,
        IReadOnlyDictionary<HostlistId, string> hostlistFingerprints,
        string runtimeManifestHash,
        ZapretCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
        ArgumentNullException.ThrowIfNull(strategyPackHashes, nameof(strategyPackHashes));
        ArgumentNullException.ThrowIfNull(hostlistFingerprints, nameof(hostlistFingerprints));
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        GuardHashValues(strategyPackHashes, nameof(strategyPackHashes));
        GuardHashValues(hostlistFingerprints, nameof(hostlistFingerprints));

        ProfileDocumentHash = GuardNotBlank(profileDocumentHash, nameof(profileDocumentHash));
        RuntimeManifestHash = GuardNotBlank(runtimeManifestHash, nameof(runtimeManifestHash));

        Definition = definition;
        StrategyPackHashes = strategyPackHashes;
        HostlistFingerprints = hostlistFingerprints;
        Options = options;
    }

    /// <summary>
    /// Validated domain model of the profile to compile. Never null.
    /// </summary>
    public ProfileDefinition Definition { get; }

    /// <summary>
    /// Pre-computed hash of the profile document (DTO). Never null,
    /// empty or whitespace.
    /// </summary>
    public string ProfileDocumentHash { get; }

    /// <summary>
    /// Pre-computed hashes of every strategy pack referenced by the
    /// profile, keyed by pack id. Never null; may be empty.
    /// </summary>
    public IReadOnlyDictionary<StrategyPackId, string> StrategyPackHashes { get; }

    /// <summary>
    /// Pre-computed fingerprints of every hostlist referenced by the
    /// profile, keyed by hostlist id. Never null; may be empty.
    /// </summary>
    public IReadOnlyDictionary<HostlistId, string> HostlistFingerprints { get; }

    /// <summary>
    /// Pre-computed hash of the runtime asset manifest. Never null,
    /// empty or whitespace.
    /// </summary>
    public string RuntimeManifestHash { get; }

    /// <summary>
    /// Compiler options (version + free-form flags) that participate
    /// in the cache key. Never null.
    /// </summary>
    public ZapretCompilerOptions Options { get; }

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

    private static void GuardHashValues<TKey>(
        IReadOnlyDictionary<TKey, string> hashValues,
        string parameterName)
        where TKey : notnull
    {
        foreach (KeyValuePair<TKey, string> entry in hashValues)
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                throw new ArgumentException(
                    $"Hash value for '{entry.Key}' must not be null or whitespace.",
                    parameterName);
            }
        }
    }
}

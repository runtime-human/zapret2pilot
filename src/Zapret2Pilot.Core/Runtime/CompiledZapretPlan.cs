using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;

namespace Zapret2Pilot.Core.Runtime;

/// <summary>
/// Output of the Zapret plan compiler: the minimum payload the runtime
/// workspace materializer needs to write a complete runtime workspace
/// (generated config, args file and hostlist payloads) plus, when produced
/// by the compiler, the structured command-line arguments, the
/// content-addressed cache key and the profile identity.
///
/// This type was introduced as a minimal placeholder for 0.0.12 and has
/// been extended additively for 0.0.15. The materializer API is designed
/// to remain stable across this evolution: it still consumes the
/// <c>GeneratedConfigContent</c>, <c>ArgsContent</c> and <c>Hostlists</c>
/// fields it always has.
/// </summary>
public sealed record CompiledZapretPlan
{
    /// <summary>
    /// Preserved 0.0.12 constructor: produces a plan with no compiler
    /// metadata (no <c>Id</c>, no <c>ProfileId</c>, no <c>CommandLine</c>,
    /// no <c>Arguments</c>, no <c>CacheKey</c>). Delegates to the full
    /// constructor with <c>null</c> for the new compiler-produced fields.
    /// </summary>
    public CompiledZapretPlan(
        string generatedConfigContent,
        string argsContent,
        IReadOnlyList<HostlistContent> hostlists)
        : this(
            generatedConfigContent,
            argsContent,
            hostlists,
            id: null,
            profileId: null,
            commandLine: null,
            arguments: null,
            cacheKey: null)
    {
    }

    /// <summary>
    /// Full constructor used by <c>ZapretPlanCompiler</c> (0.0.15+) to
    /// populate the compiler-produced metadata. The additional fields
    /// (<paramref name="id"/>, <paramref name="profileId"/>,
    /// <paramref name="commandLine"/>, <paramref name="arguments"/>,
    /// <paramref name="cacheKey"/>) are all optional; older callers can
    /// continue to use the 3-parameter overload.
    /// </summary>
    public CompiledZapretPlan(
        string generatedConfigContent,
        string argsContent,
        IReadOnlyList<HostlistContent> hostlists,
        RuntimePlanId? id,
        ProfileId? profileId,
        string? commandLine,
        IReadOnlyList<string>? arguments,
        RuntimePlanCacheKey? cacheKey)
    {
        ArgumentNullException.ThrowIfNull(generatedConfigContent, nameof(generatedConfigContent));
        ArgumentNullException.ThrowIfNull(argsContent, nameof(argsContent));
        ArgumentNullException.ThrowIfNull(hostlists, nameof(hostlists));

        GeneratedConfigContent = generatedConfigContent;
        ArgsContent = argsContent;
        Hostlists = hostlists;
        Id = id;
        ProfileId = profileId;
        CommandLine = commandLine;
        Arguments = arguments;
        CacheKey = cacheKey;
    }

    /// <summary>
    /// Content of the generated config file. Empty string is allowed.
    /// </summary>
    public string GeneratedConfigContent { get; }

    /// <summary>
    /// Content of the args file. Empty string is allowed.
    /// </summary>
    public string ArgsContent { get; }

    /// <summary>
    /// Hostlist payloads keyed by relative path (e.g. <c>default.txt</c>).
    /// </summary>
    public IReadOnlyList<HostlistContent> Hostlists { get; }

    /// <summary>
    /// Deterministic plan identifier derived from the content-addressed
    /// <see cref="CacheKey"/>. Populated by <c>ZapretPlanCompiler</c>
    /// (0.0.15+); <c>null</c> for plans produced via the legacy
    /// 3-parameter constructor.
    /// </summary>
    public RuntimePlanId? Id { get; }

    /// <summary>
    /// Profile identifier this plan was compiled for. Populated by
    /// <c>ZapretPlanCompiler</c> (0.0.15+); <c>null</c> otherwise.
    /// </summary>
    public ProfileId? ProfileId { get; }

    /// <summary>
    /// Resolved <c>winws2</c> command-line (executable path plus
    /// arguments). Populated by the runtime kernel after path resolution;
    /// the compiler emits a placeholder such as <c>"bin/winws2.exe"</c>
    /// and leaves absolute-path resolution to the host.
    /// </summary>
    public string? CommandLine { get; }

    /// <summary>
    /// Structured command-line argument tokens (one entry per <c>argv</c>
    /// slot), as built by the compiler. <c>null</c> for plans produced
    /// via the legacy 3-parameter constructor.
    /// </summary>
    public IReadOnlyList<string>? Arguments { get; }

    /// <summary>
    /// Content-addressed cache key covering the full DEC-0011 input set
    /// (profile document hash, strategy pack hashes, hostlist
    /// fingerprints, runtime manifest hash, compiler options, compiler
    /// version). Populated by <c>ZapretPlanCompiler</c> (0.0.15+);
    /// <c>null</c> otherwise.
    /// </summary>
    public RuntimePlanCacheKey? CacheKey { get; }

    /// <summary>
    /// Single hostlist payload: relative path inside the workspace's
    /// <c>hostlists/</c> directory and the file content.
    /// </summary>
    public sealed record HostlistContent
    {
        public HostlistContent(string relativePath, string content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath, nameof(relativePath));
            ArgumentNullException.ThrowIfNull(content, nameof(content));

            RelativePath = relativePath;
            Content = content;
        }

        public string RelativePath { get; }

        public string Content { get; }
    }
}

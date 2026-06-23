using System;
using System.Collections.Generic;

namespace Zapret2Pilot.Core.Runtime;

/// <summary>
/// Placeholder for the future profile compiler output. Carries the minimum
/// payload the runtime workspace materializer needs to write a complete
/// runtime workspace: the generated config, the args file and the hostlist
/// contents keyed by relative filename.
///
/// This is intentionally a minimal placeholder. The real profile compiler
/// (0.0.13+) will produce a richer record; the materializer API is designed
/// to remain stable across that evolution.
/// </summary>
public sealed record CompiledZapretPlan
{
    public CompiledZapretPlan(
        string generatedConfigContent,
        string argsContent,
        IReadOnlyList<HostlistContent> hostlists)
    {
        ArgumentNullException.ThrowIfNull(generatedConfigContent, nameof(generatedConfigContent));
        ArgumentNullException.ThrowIfNull(argsContent, nameof(argsContent));
        ArgumentNullException.ThrowIfNull(hostlists, nameof(hostlists));

        GeneratedConfigContent = generatedConfigContent;
        ArgsContent = argsContent;
        Hostlists = hostlists;
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

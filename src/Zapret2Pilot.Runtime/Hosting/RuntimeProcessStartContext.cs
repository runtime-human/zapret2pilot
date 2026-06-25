using System;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;

namespace Zapret2Pilot.Runtime.Hosting;

/// <summary>
/// Input DTO for <c>RuntimeProcessHost.StartAsync</c>. Captures the
/// compiled Zapret plan, the resolved runtime asset manifest, and the
/// absolute workspace directory that contains the manifest-verified
/// runtime executable and generated runtime files.
/// </summary>
/// <remarks>
/// The executable path is intentionally not supplied independently by
/// callers. <c>RuntimeProcessHost</c> derives the executable from the
/// manifest executable entry after successful asset verification inside
/// the workspace root.
/// </remarks>
public sealed class RuntimeProcessStartContext
{
    public RuntimeProcessStartContext(
        CompiledZapretPlan plan,
        ZapretAssetManifest manifest,
        string workspaceDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan, nameof(plan));
        ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory, nameof(workspaceDirectory));

        Plan = plan;
        Manifest = manifest;
        WorkspaceDirectory = workspaceDirectory;
    }

    /// <summary>
    /// Compiled Zapret plan that was materialized into the workspace.
    /// </summary>
    public CompiledZapretPlan Plan { get; }

    /// <summary>
    /// Resolved manifest of Zapret2 runtime assets backing the run.
    /// </summary>
    public ZapretAssetManifest Manifest { get; }

    /// <summary>
    /// Absolute path to the workspace root that contains the verified
    /// runtime executable, args file, generated config and hostlists.
    /// </summary>
    public string WorkspaceDirectory { get; }
}

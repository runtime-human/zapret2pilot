using System;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;

namespace Zapret2Pilot.Runtime.Hosting;

/// <summary>
/// Input DTO for <c>RuntimeProcessHost.StartAsync</c>. Captures the
/// compiled Zapret plan, the resolved runtime asset manifest, the
/// absolute workspace directory that was materialized for the run, and
/// the absolute path of the runtime executable to launch.
/// </summary>
/// <remarks>
/// Introduced as part of milestone 0.0.17 to provide a stable
/// start-context contract for <c>RuntimeProcessHost</c> before the host
/// itself is implemented. The host will validate the path on disk and
/// fail fast on a non-existent or non-executable file.
/// </remarks>
public sealed class RuntimeProcessStartContext
{
    public RuntimeProcessStartContext(
        CompiledZapretPlan plan,
        ZapretAssetManifest manifest,
        string workspaceDirectory,
        string runtimeExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(plan, nameof(plan));
        ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory, nameof(workspaceDirectory));
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeExecutablePath, nameof(runtimeExecutablePath));

        Plan = plan;
        Manifest = manifest;
        WorkspaceDirectory = workspaceDirectory;
        RuntimeExecutablePath = runtimeExecutablePath;
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
    /// Absolute path to the workspace root that contains the args file,
    /// generated config, and hostlists consumed by the runtime
    /// executable.
    /// </summary>
    public string WorkspaceDirectory { get; }

    /// <summary>
    /// Absolute path to the runtime executable to be launched by
    /// <c>RuntimeProcessHost</c>.
    /// </summary>
    public string RuntimeExecutablePath { get; }
}

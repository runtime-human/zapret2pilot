using System;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Integrity;

namespace Zapret2Pilot.Runtime.Hosting;

/// <summary>
/// Input DTO for <c>RuntimeProcessHost.StartAsync</c>. Captures the
/// compiled Zapret plan, the resolved runtime asset manifest, the
/// absolute workspace directory that was materialized for the run, and
/// a <see cref="VerifiedRuntimeExecutablePath"/> whose
/// <see cref="VerifiedRuntimeExecutablePath.AbsolutePath"/> points at
/// a file on disk that <see cref="ZapretAssetVerifier"/> has already
/// approved.
/// </summary>
/// <remarks>
/// <para>
/// The runtime executable path is delivered as a
/// <see cref="VerifiedRuntimeExecutablePath"/> (a value object that can
/// only be obtained by combining a <see cref="ZapretAssetManifest"/>
/// with a passing <see cref="ZapretAssetVerificationSummary"/> and
/// re-confirming the file is on disk). The host therefore refuses to
/// launch any path that has not been proven by the verifier: this
/// closes roadmap item P0-4 (verified executable integrity binding).
/// </para>
/// <para>
/// Introduced as part of milestone 0.0.17 to provide a stable
/// start-context contract for <c>RuntimeProcessHost</c> before the host
/// itself is implemented. The host launches only what it receives
/// (closes roadmap item P1-1).
/// </para>
/// </remarks>
public sealed class RuntimeProcessStartContext
{
    public RuntimeProcessStartContext(
        CompiledZapretPlan plan,
        ZapretAssetManifest manifest,
        string workspaceDirectory,
        VerifiedRuntimeExecutablePath runtimeExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(plan, nameof(plan));
        ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory, nameof(workspaceDirectory));
        ArgumentNullException.ThrowIfNull(runtimeExecutablePath, nameof(runtimeExecutablePath));

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
    /// Verified runtime executable path to be launched by
    /// <c>RuntimeProcessHost</c>. Its
    /// <see cref="VerifiedRuntimeExecutablePath.AbsolutePath"/> is
    /// guaranteed to have been present in the workspace's
    /// <see cref="ZapretAssetVerificationSummary"/> and to exist on disk
    /// at the time the value was constructed.
    /// </summary>
    public VerifiedRuntimeExecutablePath RuntimeExecutablePath { get; }
}

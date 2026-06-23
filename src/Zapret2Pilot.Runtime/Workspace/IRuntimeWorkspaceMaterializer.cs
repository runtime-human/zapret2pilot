using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;

namespace Zapret2Pilot.Runtime.Workspace;

/// <summary>
/// Materializes a Zapret2 runtime workspace directory from a compiled plan
/// and a verified asset manifest. Writes all files through the safe path and
/// atomic write infrastructure, so a partial write never leaves the workspace
/// in an inconsistent state and relative paths can never escape the workspace
/// root.
/// </summary>
public interface IRuntimeWorkspaceMaterializer
{
    Task<Result<RuntimeWorkspaceMaterializeResult>> MaterializeAsync(
        CompiledZapretPlan plan,
        ZapretAssetManifest manifest,
        string workspaceDirectory,
        CancellationToken cancellationToken);
}

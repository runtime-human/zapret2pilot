using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Zapret2Pilot.Runtime.Workspace;

/// <summary>
/// Result of a successful <see cref="IRuntimeWorkspaceMaterializer.MaterializeAsync"/>
/// call. Exposes the absolute paths of the files that were written into the
/// workspace and the workspace root itself.
/// </summary>
public sealed class RuntimeWorkspaceMaterializeResult
{
    public RuntimeWorkspaceMaterializeResult(
        string workspaceDirectory,
        string argsFilePath,
        string generatedConfigPath,
        IReadOnlyList<string> writtenHostlistPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory, nameof(workspaceDirectory));
        ArgumentException.ThrowIfNullOrWhiteSpace(argsFilePath, nameof(argsFilePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedConfigPath, nameof(generatedConfigPath));
        ArgumentNullException.ThrowIfNull(writtenHostlistPaths, nameof(writtenHostlistPaths));

        WorkspaceDirectory = workspaceDirectory;
        ArgsFilePath = argsFilePath;
        GeneratedConfigPath = generatedConfigPath;
        WrittenHostlistPaths = new ReadOnlyCollection<string>(
            new List<string>(writtenHostlistPaths));
    }

    /// <summary>
    /// Absolute path to the workspace root directory.
    /// </summary>
    public string WorkspaceDirectory { get; }

    /// <summary>
    /// Absolute path to the materialized args file.
    /// </summary>
    public string ArgsFilePath { get; }

    /// <summary>
    /// Absolute path to the materialized generated config file.
    /// </summary>
    public string GeneratedConfigPath { get; }

    /// <summary>
    /// Absolute paths of all hostlists that were written into the workspace.
    /// </summary>
    public IReadOnlyList<string> WrittenHostlistPaths { get; }
}

using System;
using System.IO;

namespace Zapret2Pilot.Runtime.Hosting;

public sealed record class VerifiedRuntimeExecutable
{
    public VerifiedRuntimeExecutable(
        string fullPath,
        string manifestRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestRelativePath);

        string normalizedFullPath = Path.GetFullPath(fullPath);

        if (!Path.IsPathFullyQualified(normalizedFullPath))
        {
            throw new ArgumentException("Verified runtime executable path must be fully qualified.", nameof(fullPath));
        }

        FullPath = normalizedFullPath;
        ManifestRelativePath = manifestRelativePath;
    }

    public string FullPath { get; }

    public string ManifestRelativePath { get; }
}

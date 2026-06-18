using System;
using System.IO;

namespace Zapret2Pilot.Runtime.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    private bool disposed;

    public TemporaryDirectory()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "z2p-runtime-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string GetPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return Path.Combine(DirectoryPath, relativePath);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

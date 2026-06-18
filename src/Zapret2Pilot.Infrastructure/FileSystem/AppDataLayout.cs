using System;
using System.IO;

namespace Zapret2Pilot.Infrastructure.FileSystem;

public sealed class AppDataLayout
{
    public AppDataLayout(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        DataDirectory = Path.Combine(RootDirectory, "data");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        DiagnosticsDirectory = Path.Combine(RootDirectory, "diagnostics");
        RuntimeDirectory = Path.Combine(RootDirectory, "runtime");
        TempDirectory = Path.Combine(RootDirectory, "temp");
    }

    public string RootDirectory { get; }

    public string DataDirectory { get; }

    public string LogsDirectory { get; }

    public string DiagnosticsDirectory { get; }

    public string RuntimeDirectory { get; }

    public string TempDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
        Directory.CreateDirectory(RuntimeDirectory);
        Directory.CreateDirectory(TempDirectory);
    }
}

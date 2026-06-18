using System;
using System.IO;

namespace Zapret2Pilot.Infrastructure.FileSystem;

public static class AppDataPathProvider
{
    private const string ProductDirectoryName = "Zapret2Pilot";

    public static AppDataLayout GetDefaultLayout()
    {
        string programDataPath = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(programDataPath))
        {
            throw new InvalidOperationException("Common application data directory is not available.");
        }

        return new AppDataLayout(Path.Combine(programDataPath, ProductDirectoryName));
    }

    public static AppDataLayout CreateLayout(string rootDirectory)
    {
        return new AppDataLayout(rootDirectory);
    }
}

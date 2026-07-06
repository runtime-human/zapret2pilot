using System;
using System.IO;

namespace Zapret2Pilot.App.Hosting;

internal static class Z2PConfigurationDefaults
{
    public const string TrustedTufRoot = "embedded-trusted-root-v1";

    public static string DefaultStorageDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Zapret2Pilot",
            "z2p.db");
    }
}

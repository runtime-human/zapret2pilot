using System;
using System.IO;
using System.Text;

namespace Zapret2Pilot.Infrastructure.FileSystem;

public static class AtomicFileWriter
{
    public static void WriteAllText(string destinationPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);

        string fullPath = Path.GetFullPath(destinationPath);
        string? directoryPath = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException("Destination directory is not available.");
        }

        Directory.CreateDirectory(directoryPath);

        string tempPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            using (StreamWriter writer = new(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            VerifyReadable(fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void VerifyReadable(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        _ = stream.Length;
    }
}

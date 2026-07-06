using System;
using System.IO;
using Xunit;
using Zapret2Pilot.App.Input;

namespace Zapret2Pilot.App.ViewModelTests.Input;

public sealed class FileImportBoundaryTests : IDisposable
{
    private readonly string tempDirectory;

    public FileImportBoundaryTests()
    {
        tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "Z2P-FileImportBoundaryTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void JsonObjectStartIsAllowed()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(path, new byte[] { (byte)'{', (byte)'"', (byte)'a', (byte)'"' });

        try
        {
            FileImportResult result = FileImportBoundary.Validate(path);

            Assert.True(result.IsAllowed);
            Assert.Equal(ImportRisk.Safe, result.Risk);
            Assert.Null(result.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void JsonArrayStartIsAllowed()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(path, new byte[] { (byte)'[', (byte)'1', (byte)',', (byte)'2' });

        try
        {
            FileImportResult result = FileImportBoundary.Validate(path);

            Assert.True(result.IsAllowed);
            Assert.Equal(ImportRisk.Safe, result.Risk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ZipLocalFileHeaderMagicIsAllowed()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-fixture-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        try
        {
            FileImportResult result = FileImportBoundary.Validate(path);

            Assert.True(result.IsAllowed);
            Assert.Equal(ImportRisk.Safe, result.Risk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ZipEmptyArchiveMagicIsAllowed()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-fixture-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0 });

        try
        {
            FileImportResult result = FileImportBoundary.Validate(path);

            Assert.True(result.IsAllowed);
            Assert.Equal(ImportRisk.Safe, result.Risk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DisallowedExtensionIsBlocked()
    {
        string path = Path.Combine(tempDirectory, "evil.exe");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A });

        FileImportResult result = FileImportBoundary.Validate(path);

        Assert.False(result.IsAllowed);
        Assert.Equal(ImportRisk.Blocked, result.Risk);
        Assert.Equal("Z2P.INPUT.FILE_EXTENSION_BLOCKED", result.ErrorCode);
    }

    [Fact]
    public void JsonWithBadMagicIsBlocked()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0, 0 });

        try
        {
            FileImportResult result = FileImportBoundary.Validate(path);

            Assert.False(result.IsAllowed);
            Assert.Equal(ImportRisk.Blocked, result.Risk);
            Assert.Equal("Z2P.INPUT.FILE_MAGIC_MISMATCH", result.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ZipWithBadMagicIsBlocked()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-fixture-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0, 0 });

        try
        {
            FileImportResult result = FileImportBoundary.Validate(path);

            Assert.False(result.IsAllowed);
            Assert.Equal(ImportRisk.Blocked, result.Risk);
            Assert.Equal("Z2P.INPUT.FILE_MAGIC_MISMATCH", result.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OversizeFileIsBlocked()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-fixture-" + Guid.NewGuid().ToString("N") + ".json");

        // Write a JSON header (4 bytes) and a 1 KiB payload that pushes us over
        // a 512-byte limit. We use FileStream so we can size the file precisely.
        using (FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        {
            stream.Write(new byte[] { (byte)'{', (byte)'"', (byte)'a', (byte)'"' });
            stream.Write(new byte[1024]);
        }

        try
        {
            FileImportResult result = FileImportBoundary.Validate(path, maxBytes: 512);

            Assert.False(result.IsAllowed);
            Assert.Equal(ImportRisk.Blocked, result.Risk);
            Assert.Equal("Z2P.INPUT.FILE_OVERSIZE", result.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileIsBlocked()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Z2P-missing-" + Guid.NewGuid().ToString("N") + ".json");

        FileImportResult result = FileImportBoundary.Validate(path);

        Assert.False(result.IsAllowed);
        Assert.Equal(ImportRisk.Blocked, result.Risk);
        Assert.Equal("Z2P.INPUT.FILE_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public void NullPathIsBlocked()
    {
        FileImportResult result = FileImportBoundary.Validate(null!);

        Assert.False(result.IsAllowed);
        Assert.Equal(ImportRisk.Blocked, result.Risk);
        Assert.Equal("Z2P.INPUT.FILE_PATH_MISSING", result.ErrorCode);
    }

    [Fact]
    public void EmptyPathIsBlocked()
    {
        FileImportResult result = FileImportBoundary.Validate(string.Empty);

        Assert.False(result.IsAllowed);
        Assert.Equal(ImportRisk.Blocked, result.Risk);
        Assert.Equal("Z2P.INPUT.FILE_PATH_MISSING", result.ErrorCode);
    }

    [Fact]
    public void WhitespacePathIsBlocked()
    {
        FileImportResult result = FileImportBoundary.Validate("   ");

        Assert.False(result.IsAllowed);
        Assert.Equal(ImportRisk.Blocked, result.Risk);
        Assert.Equal("Z2P.INPUT.FILE_PATH_MISSING", result.ErrorCode);
    }
}

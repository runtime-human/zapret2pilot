using System;
using System.IO;
using Xunit;
using Zapret2Pilot.Infrastructure.FileSystem;

namespace Zapret2Pilot.Infrastructure.Tests;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public static void WriteAllTextCreatesNewFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        AtomicFileWriter writer = new();
        string destinationPath = temporaryDirectory.GetPath("runtime/config.txt");

        writer.WriteAllText(destinationPath, "first");

        Assert.Equal("first", File.ReadAllText(destinationPath));
    }

    [Fact]
    public static void WriteAllTextReplacesExistingFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        AtomicFileWriter writer = new();
        string destinationPath = temporaryDirectory.GetPath("runtime/config.txt");

        writer.WriteAllText(destinationPath, "first");
        writer.WriteAllText(destinationPath, "second");

        Assert.Equal("second", File.ReadAllText(destinationPath));
    }

    [Fact]
    public static void WriteAllTextCreatesParentDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        AtomicFileWriter writer = new();
        string destinationPath = temporaryDirectory.GetPath("runtime/generated/config.txt");

        writer.WriteAllText(destinationPath, "content");

        Assert.True(Directory.Exists(Path.GetDirectoryName(destinationPath)));
        Assert.True(File.Exists(destinationPath));
    }

    [Fact]
    public static void WriteAllTextWritesReadableFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        AtomicFileWriter writer = new();
        string destinationPath = temporaryDirectory.GetPath("runtime/config.txt");

        writer.WriteAllText(destinationPath, "content");

        using FileStream stream = new(
            destinationPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        Assert.True(stream.Length > 0);
    }

    [Fact]
    public static void WriteAllTextRemovesTemporaryFileAfterSuccess()
    {
        using TemporaryDirectory temporaryDirectory = new();
        AtomicFileWriter writer = new();
        string destinationPath = temporaryDirectory.GetPath("runtime/config.txt");

        writer.WriteAllText(destinationPath, "content");

        string directoryPath = Assert.IsType<string>(Path.GetDirectoryName(destinationPath));
        string fileName = Path.GetFileName(destinationPath);
        string[] temporaryFiles = Directory.GetFiles(directoryPath, $".{fileName}.*.tmp");

        Assert.Empty(temporaryFiles);
    }

    [Fact]
    public static void WriteAllTextRejectsEmptyDestination()
    {
        AtomicFileWriter writer = new();

        Assert.Throws<ArgumentException>(() => writer.WriteAllText(string.Empty, "content"));
    }

    [Fact]
    public static void WriteAllTextRejectsNullContent()
    {
        AtomicFileWriter writer = new();

        Assert.Throws<ArgumentNullException>(() => writer.WriteAllText("file.txt", null!));
    }
}

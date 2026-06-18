using System;
using System.IO;
using Xunit;
using Zapret2Pilot.Infrastructure.FileSystem;

namespace Zapret2Pilot.Infrastructure.Tests;

public sealed class SafePathResolverTests
{
    [Fact]
    public static void ResolveFilePathReturnsPathInsideRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);

        string resolvedPath = resolver.ResolveFilePath("logs/app.log");
        string expectedPath = Path.GetFullPath(
            Path.Combine(temporaryDirectory.DirectoryPath, "logs", "app.log"));

        Assert.Equal(expectedPath, resolvedPath);
    }

    [Fact]
    public static void ResolveFilePathRejectsParentTraversal()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath("../escape.txt"));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath(@"..\escape.txt"));
    }

    [Fact]
    public static void ResolveFilePathRejectsNestedParentTraversalInsideRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath("logs/../runtime/config.txt"));
    }

    [Fact]
    public static void ResolveFilePathRejectsWindowsNestedParentTraversalInsideRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath(@"logs\..\runtime\config.txt"));
    }

    [Fact]
    public static void ResolveFilePathRejectsAbsolutePath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);
        string absolutePath = Path.GetFullPath(
            Path.Combine(temporaryDirectory.DirectoryPath, "..", "escape.txt"));

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath(absolutePath));
    }

    [Fact]
    public static void ResolveFilePathRejectsWindowsDriveRelativePath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath("C:relative.txt"));
    }

    [Fact]
    public static void ResolveFilePathRejectsAlternateDataStreamPath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath("logs/app.log:secret"));
    }

    [Fact]
    public static void ResolveFilePathRejectsSiblingPrefixEscape()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string rootDirectory = Path.Combine(temporaryDirectory.DirectoryPath, "z2p");
        SafePathResolver resolver = new(rootDirectory);

        Assert.Throws<InvalidOperationException>(() =>
            resolver.ResolveFilePath("../z2p-evil/file.txt"));
    }

    [Fact]
    public static void ResolveFilePathRejectsEmptyPath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        SafePathResolver resolver = new(temporaryDirectory.DirectoryPath);

        Assert.Throws<ArgumentException>(() => resolver.ResolveFilePath(string.Empty));
    }
}

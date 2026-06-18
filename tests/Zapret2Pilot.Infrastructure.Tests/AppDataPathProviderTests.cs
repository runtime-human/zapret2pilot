using System;
using System.IO;
using Xunit;
using Zapret2Pilot.Infrastructure.FileSystem;

namespace Zapret2Pilot.Infrastructure.Tests;

public sealed class AppDataPathProviderTests
{
    [Fact]
    public static void CreateLayoutBuildsExpectedDirectories()
    {
        using TemporaryDirectory temporaryDirectory = new();

        AppDataLayout layout = AppDataPathProvider.CreateLayout(temporaryDirectory.DirectoryPath);

        Assert.Equal(Path.GetFullPath(temporaryDirectory.DirectoryPath), layout.RootDirectory);
        Assert.Equal(Path.Combine(layout.RootDirectory, "data"), layout.DataDirectory);
        Assert.Equal(Path.Combine(layout.RootDirectory, "logs"), layout.LogsDirectory);
        Assert.Equal(Path.Combine(layout.RootDirectory, "diagnostics"), layout.DiagnosticsDirectory);
        Assert.Equal(Path.Combine(layout.RootDirectory, "runtime"), layout.RuntimeDirectory);
        Assert.Equal(Path.Combine(layout.RootDirectory, "temp"), layout.TempDirectory);
    }

    [Fact]
    public static void EnsureCreatedCreatesAllDirectories()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string rootPath = Path.Combine(temporaryDirectory.DirectoryPath, "app-data");

        AppDataLayout layout = new(rootPath);

        layout.EnsureCreated();

        Assert.True(Directory.Exists(layout.RootDirectory));
        Assert.True(Directory.Exists(layout.DataDirectory));
        Assert.True(Directory.Exists(layout.LogsDirectory));
        Assert.True(Directory.Exists(layout.DiagnosticsDirectory));
        Assert.True(Directory.Exists(layout.RuntimeDirectory));
        Assert.True(Directory.Exists(layout.TempDirectory));
    }

    [Fact]
    public static void CreateLayoutRejectsEmptyRoot()
    {
        Assert.Throws<ArgumentException>(() => AppDataPathProvider.CreateLayout(string.Empty));
    }
}

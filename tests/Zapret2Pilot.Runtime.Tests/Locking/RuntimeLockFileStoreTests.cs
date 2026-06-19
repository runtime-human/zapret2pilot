using System;
using System.IO;
using Xunit;
using Zapret2Pilot.Runtime.Locking;

namespace Zapret2Pilot.Runtime.Tests.Locking;

public sealed class RuntimeLockFileStoreTests
{
    [Fact]
    public static void ReadReturnsMissingWhenLockFileDoesNotExist()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Missing, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void WriteCreatesReadableLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);

        store.Write(RuntimeTestData.CreateLockMetadata());

        Assert.True(File.Exists(store.LockFilePath));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(store.LockFilePath)));
    }

    [Fact]
    public static void ReadReturnsValidMetadataAfterWrite()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);
        RuntimeLockMetadata metadata = RuntimeTestData.CreateLockMetadata(
            ownerInstanceId: Guid.NewGuid().ToString("N"));

        store.Write(metadata);

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Valid, result.Status);
        Assert.Equal(metadata, result.Metadata);
    }

    [Fact]
    public static void ReadReturnsInvalidWhenJsonIsMalformed()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);

        File.WriteAllText(store.LockFilePath, "{ invalid json");

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void ReadReturnsInvalidWhenMetadataHasInvalidProcessId()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);

        File.WriteAllText(
            store.LockFilePath,
            RuntimeTestData.CreateLockMetadataJson(
                ownerInstanceId: "owner",
                processId: 0));

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void ReadReturnsInvalidWhenMetadataHasEmptyOwnerInstanceId()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);

        File.WriteAllText(
            store.LockFilePath,
            RuntimeTestData.CreateLockMetadataJson(
                ownerInstanceId: string.Empty,
                processId: RuntimeTestData.ProcessId));

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void ProcessMetadataRejectsInvalidProcessId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RuntimeTestData.CreateLockProcessMetadata(processId: 0));
    }

    [Fact]
    public static void MetadataRejectsEmptyOwnerInstanceId()
    {
        Assert.Throws<ArgumentException>(() =>
            RuntimeTestData.CreateLockMetadata(ownerInstanceId: string.Empty));
    }

    [Fact]
    public static void DeleteRemovesExistingLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);

        store.Write(RuntimeTestData.CreateLockMetadata());
        store.Delete();

        Assert.False(File.Exists(store.LockFilePath));
    }

    [Fact]
    public static void DeleteIgnoresMissingLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = CreateStore(temporaryDirectory);

        store.Delete();

        Assert.False(File.Exists(store.LockFilePath));
    }

    private static RuntimeLockFileStore CreateStore(TemporaryDirectory temporaryDirectory)
    {
        return new RuntimeLockFileStore(temporaryDirectory.DirectoryPath);
    }
}

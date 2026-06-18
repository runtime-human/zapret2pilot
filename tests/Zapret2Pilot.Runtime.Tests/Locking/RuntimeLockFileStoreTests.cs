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
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Missing, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void WriteCreatesReadableLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);

        store.Write(CreateMetadata());

        Assert.True(File.Exists(store.LockFilePath));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(store.LockFilePath)));
    }

    [Fact]
    public static void ReadReturnsValidMetadataAfterWrite()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);
        RuntimeLockMetadata metadata = CreateMetadata();

        store.Write(metadata);

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Valid, result.Status);
        Assert.Equal(metadata, result.Metadata);
    }

    [Fact]
    public static void ReadReturnsInvalidWhenJsonIsMalformed()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);

        Directory.CreateDirectory(temporaryDirectory.DirectoryPath);
        File.WriteAllText(store.LockFilePath, "{ invalid json");

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void ReadReturnsInvalidWhenMetadataHasInvalidProcessId()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);

        File.WriteAllText(
            store.LockFilePath,
            """
            {
              "SchemaVersion": 1,
              "OwnerInstanceId": "owner",
              "Process": {
                "ProcessId": 0,
                "ProcessName": "runtime-engine",
                "ExecutablePath": "runtime-engine.exe",
                "CommandLineHash": "command-line-hash",
                "PlanHash": "plan-hash",
                "ProcessStartedAtUtc": "2026-06-18T09:59:55.0000000+00:00"
              },
              "AcquiredAtUtc": "2026-06-18T10:00:00.0000000+00:00"
            }
            """);

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void ReadReturnsInvalidWhenMetadataHasEmptyOwnerInstanceId()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);

        File.WriteAllText(
            store.LockFilePath,
            """
            {
              "SchemaVersion": 1,
              "OwnerInstanceId": "",
              "Process": {
                "ProcessId": 1234,
                "ProcessName": "runtime-engine",
                "ExecutablePath": "runtime-engine.exe",
                "CommandLineHash": "command-line-hash",
                "PlanHash": "plan-hash",
                "ProcessStartedAtUtc": "2026-06-18T09:59:55.0000000+00:00"
              },
              "AcquiredAtUtc": "2026-06-18T10:00:00.0000000+00:00"
            }
            """);

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void ProcessMetadataRejectsInvalidProcessId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeLockProcessMetadata(
            processId: 0,
            processName: "runtime-engine",
            executablePath: "runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            processStartedAtUtc: DateTimeOffset.UtcNow));
    }

    [Fact]
    public static void MetadataRejectsEmptyOwnerInstanceId()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: string.Empty,
            process: CreateProcessMetadata(),
            acquiredAtUtc: DateTimeOffset.UtcNow));
    }

    [Fact]
    public static void DeleteRemovesExistingLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);

        store.Write(CreateMetadata());
        store.Delete();

        Assert.False(File.Exists(store.LockFilePath));
    }

    [Fact]
    public static void DeleteIgnoresMissingLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RuntimeLockFileStore store = new(temporaryDirectory.DirectoryPath);

        store.Delete();

        Assert.False(File.Exists(store.LockFilePath));
    }

    private static RuntimeLockMetadata CreateMetadata()
    {
        return new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: Guid.NewGuid().ToString("N"),
            process: CreateProcessMetadata(),
            acquiredAtUtc: DateTimeOffset.UtcNow);
    }

    private static RuntimeLockProcessMetadata CreateProcessMetadata()
    {
        return new RuntimeLockProcessMetadata(
            processId: 1234,
            processName: "runtime-engine",
            executablePath: "runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            processStartedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5));
    }
}

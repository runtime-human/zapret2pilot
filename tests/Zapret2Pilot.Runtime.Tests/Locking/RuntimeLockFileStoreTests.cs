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
              "ProcessId": 0,
              "ProcessName": "runtime-engine",
              "ExecutablePath": "C:\\ProgramData\\Zapret2Pilot\\runtime\\runtime-engine.exe",
              "CommandLineHash": "command-line-hash",
              "PlanHash": "plan-hash",
              "AcquiredAtUtc": "2026-06-18T10:00:00.0000000+00:00",
              "ProcessStartedAtUtc": "2026-06-18T09:59:55.0000000+00:00"
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
              "ProcessId": 1234,
              "ProcessName": "runtime-engine",
              "ExecutablePath": "C:\\ProgramData\\Zapret2Pilot\\runtime\\runtime-engine.exe",
              "CommandLineHash": "command-line-hash",
              "PlanHash": "plan-hash",
              "AcquiredAtUtc": "2026-06-18T10:00:00.0000000+00:00",
              "ProcessStartedAtUtc": "2026-06-18T09:59:55.0000000+00:00"
            }
            """);

        RuntimeLockFileReadResult result = store.Read();

        Assert.Equal(RuntimeLockFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public static void MetadataRejectsInvalidProcessId()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: "owner",
            processId: 0,
            processName: "runtime-engine",
            executablePath: @"C:\ProgramData\Zapret2Pilot\runtime\runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            acquiredAtUtc: now,
            processStartedAtUtc: now.AddSeconds(-5)));
    }

    [Fact]
    public static void MetadataRejectsEmptyOwnerInstanceId()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: string.Empty,
            processId: 1234,
            processName: "runtime-engine",
            executablePath: @"C:\ProgramData\Zapret2Pilot\runtime\runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            acquiredAtUtc: now,
            processStartedAtUtc: now.AddSeconds(-5)));
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
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new RuntimeLockMetadata(
            schemaVersion: 1,
            ownerInstanceId: Guid.NewGuid().ToString("N"),
            processId: 1234,
            processName: "runtime-engine",
            executablePath: @"C:\ProgramData\Zapret2Pilot\runtime\runtime-engine.exe",
            commandLineHash: "command-line-hash",
            planHash: "plan-hash",
            acquiredAtUtc: now,
            processStartedAtUtc: now.AddSeconds(-5));
    }
}

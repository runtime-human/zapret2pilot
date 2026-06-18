using System;
using System.IO;
using System.Text.Json;
using Zapret2Pilot.Infrastructure.FileSystem;
using Zapret2Pilot.Runtime.Ownership;

namespace Zapret2Pilot.Runtime.Locking;

public sealed class RuntimeLockFileStore
{
    public RuntimeLockFileStore(string runtimeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

        string fullRuntimeDirectory = Path.GetFullPath(runtimeDirectory);
        SafePathResolver resolver = new(fullRuntimeDirectory);

        LockFilePath = resolver.ResolveFilePath(RuntimeOwnershipNames.LockFileName);
    }

    public string LockFilePath { get; }

    public void Write(RuntimeLockMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string json = JsonSerializer.Serialize(
            metadata,
            RuntimeLockMetadataJsonContext.Default.RuntimeLockMetadata);

        AtomicFileWriter.WriteAllText(LockFilePath, json);
    }

    public RuntimeLockFileReadResult Read()
    {
        if (!File.Exists(LockFilePath))
        {
            return RuntimeLockFileReadResult.Missing();
        }

        try
        {
            string json = File.ReadAllText(LockFilePath);

            RuntimeLockMetadata? metadata = JsonSerializer.Deserialize(
                json,
                RuntimeLockMetadataJsonContext.Default.RuntimeLockMetadata);

            return metadata is null
                ? RuntimeLockFileReadResult.Invalid()
                : RuntimeLockFileReadResult.Valid(metadata);
        }
        catch (JsonException)
        {
            return RuntimeLockFileReadResult.Invalid();
        }
        catch (NotSupportedException)
        {
            return RuntimeLockFileReadResult.Invalid();
        }
        catch (ArgumentException)
        {
            return RuntimeLockFileReadResult.Invalid();
        }
        catch (IOException)
        {
            return RuntimeLockFileReadResult.Invalid();
        }
        catch (UnauthorizedAccessException)
        {
            return RuntimeLockFileReadResult.Invalid();
        }
    }

    public void Delete()
    {
        if (File.Exists(LockFilePath))
        {
            File.Delete(LockFilePath);
        }
    }
}

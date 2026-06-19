using System.Text.Json.Serialization;

namespace Zapret2Pilot.Runtime.Locking;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RuntimeLockMetadata))]
[JsonSerializable(typeof(RuntimeLockProcessMetadata))]
internal sealed partial class RuntimeLockMetadataJsonContext : JsonSerializerContext
{
}

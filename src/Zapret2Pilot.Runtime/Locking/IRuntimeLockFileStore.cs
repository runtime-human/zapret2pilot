namespace Zapret2Pilot.Runtime.Locking;

public interface IRuntimeLockFileStore
{
    string LockFilePath { get; }

    void Write(RuntimeLockMetadata metadata);

    RuntimeLockFileReadResult Read();

    void Delete();
}

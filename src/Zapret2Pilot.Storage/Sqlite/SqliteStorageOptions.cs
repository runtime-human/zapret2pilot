using System;

namespace Zapret2Pilot.Storage.Sqlite;

public sealed class SqliteStorageOptions
{
    public SqliteStorageOptions(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }
}

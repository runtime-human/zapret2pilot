using System;
using System.IO;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Runtime.Tests.State;

/// <summary>
/// Lightweight test fixture that provisions a fresh SQLite database
/// in a per-instance temp directory. Mirrors the helper in
/// <c>Zapret2Pilot.Storage.Tests</c> and applies the full
/// <see cref="SqliteDbInitializer"/> migration set so the runtime
/// state store can be exercised against the real schema.
/// </summary>
internal sealed class TemporarySqliteDatabase : IDisposable
{
    private bool disposed;

    public TemporarySqliteDatabase()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "z2p-runtime-tests",
            Guid.NewGuid().ToString("N"));

        DatabasePath = Path.Combine(DirectoryPath, "z2p.db");
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public SqliteConnectionFactory CreateFactory()
    {
        return new SqliteConnectionFactory(new SqliteStorageOptions(DatabasePath));
    }

    public void Initialize()
    {
        SqliteDbInitializer initializer = new(CreateFactory());

        initializer.Initialize();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

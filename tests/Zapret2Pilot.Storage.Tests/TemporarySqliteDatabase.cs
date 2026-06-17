using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Storage.Tests;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private bool disposed;

    public TemporarySqliteDatabase()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "z2p-storage-tests",
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

    public object? ExecuteScalar(
        string commandText,
        params SqliteParameter[] parameters)
    {
        using SqliteConnection connection = CreateFactory().OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = commandText;

        foreach (SqliteParameter parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        SqliteConnection.ClearAllPools();

        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

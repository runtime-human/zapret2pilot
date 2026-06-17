using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Zapret2Pilot.Storage.Sqlite;

public sealed class SqliteConnectionFactory
{
    private readonly SqliteStorageOptions options;

    public SqliteConnectionFactory(SqliteStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options;
    }

    public SqliteConnection OpenConnection()
    {
        string? directoryPath = Path.GetDirectoryName(options.DatabasePath);

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        SqliteConnectionStringBuilder connectionStringBuilder = new()
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 5
        };

        SqliteConnection connection = new(connectionStringBuilder.ToString());
        connection.Open();

        ConfigureConnection(connection);

        return connection;
    }

    private static void ConfigureConnection(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _ = ExecuteScalar(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000;");
        ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
        ExecuteNonQuery(connection, "PRAGMA foreign_keys=ON;");
    }

    private static object? ExecuteScalar(
        SqliteConnection connection,
        string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;

        return command.ExecuteScalar();
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}

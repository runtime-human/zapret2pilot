using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Zapret2Pilot.Storage.Sqlite;

public sealed class SqliteDbInitializer
{
    private const string BaselineMigrationId = "0001_storage_foundation";

    private static readonly SqliteSchemaMigration BaselineMigration = new(
        BaselineMigrationId,
        """
        CREATE TABLE IF NOT EXISTS app_settings (
            key TEXT NOT NULL PRIMARY KEY,
            value TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS event_journal (
            id TEXT NOT NULL PRIMARY KEY,
            occurred_utc TEXT NOT NULL,
            category TEXT NOT NULL,
            message TEXT NOT NULL,
            severity TEXT NOT NULL
        );
        """);

    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteDbInitializer(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        this.connectionFactory = connectionFactory;
    }

    public void Initialize()
    {
        using SqliteConnection connection = connectionFactory.OpenConnection();

        ApplyRequiredPragmas(connection);
        CreateMigrationsTable(connection);

        if (IsMigrationApplied(connection, BaselineMigration.Id))
        {
            return;
        }

        using SqliteTransaction transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, BaselineMigration.Sql);
        InsertMigration(connection, transaction, BaselineMigration.Id);

        transaction.Commit();
    }

    private static void ApplyRequiredPragmas(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _ = ExecuteScalar(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, transaction: null, "PRAGMA busy_timeout=5000;");
        ExecuteNonQuery(connection, transaction: null, "PRAGMA synchronous=NORMAL;");
        ExecuteNonQuery(connection, transaction: null, "PRAGMA foreign_keys=ON;");
    }

    private static void CreateMigrationsTable(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            transaction: null,
            """
            CREATE TABLE IF NOT EXISTS __z2p_schema_migrations (
                id TEXT NOT NULL PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            """);
    }

    private static bool IsMigrationApplied(
        SqliteConnection connection,
        string migrationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 1
            FROM __z2p_schema_migrations
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", migrationId);

        object? result = command.ExecuteScalar();

        return result is not null;
    }

    private static void InsertMigration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string migrationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO __z2p_schema_migrations (id, applied_utc)
            VALUES ($id, $appliedUtc);
            """;
        command.Parameters.AddWithValue("$id", migrationId);
        command.Parameters.AddWithValue(
            "$appliedUtc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        command.ExecuteNonQuery();
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
        SqliteTransaction? transaction,
        string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}

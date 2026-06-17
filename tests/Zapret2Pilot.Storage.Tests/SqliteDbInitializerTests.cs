using System;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Zapret2Pilot.Storage.Tests;

public sealed class SqliteDbInitializerTests
{
    [Fact]
    public static void InitializeAppliesJournalModeWal()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();

        object? result = database.ExecuteScalar("PRAGMA journal_mode;");
        string journalMode = Assert.IsType<string>(result);

        Assert.True(
            string.Equals("wal", journalMode, StringComparison.OrdinalIgnoreCase),
            $"Expected WAL journal mode, got '{journalMode}'.");
    }

    [Fact]
    public static void InitializeAppliesBusyTimeout()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();

        object? result = database.ExecuteScalar("PRAGMA busy_timeout;");
        long busyTimeout = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        Assert.Equal(5000L, busyTimeout);
    }

    [Fact]
    public static void InitializeAppliesSynchronousNormal()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();

        object? result = database.ExecuteScalar("PRAGMA synchronous;");
        long synchronous = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        Assert.Equal(1L, synchronous);
    }

    [Fact]
    public static void InitializeAppliesForeignKeys()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();

        object? result = database.ExecuteScalar("PRAGMA foreign_keys;");
        long foreignKeys = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        Assert.Equal(1L, foreignKeys);
    }

    [Fact]
    public static void InitializeCreatesMigrationsTable()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();

        Assert.True(TableExists(database, "__z2p_schema_migrations"));
    }

    [Fact]
    public static void InitializeCreatesSettingsTable()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();

        Assert.True(TableExists(database, "app_settings"));
    }

    [Fact]
    public static void InitializeCreatesEventJournalTable()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();

        Assert.True(TableExists(database, "event_journal"));
    }

    [Fact]
    public static void InitializeIsIdempotent()
    {
        using TemporarySqliteDatabase database = new();

        database.Initialize();
        database.Initialize();

        object? result = database.ExecuteScalar(
            """
            SELECT COUNT(*)
            FROM __z2p_schema_migrations
            WHERE id = $id;
            """,
            new SqliteParameter("$id", "0001_storage_foundation"));

        long migrationCount = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        Assert.Equal(1L, migrationCount);
    }

    private static bool TableExists(
        TemporarySqliteDatabase database,
        string tableName)
    {
        object? result = database.ExecuteScalar(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name
            LIMIT 1;
            """,
            new SqliteParameter("$name", tableName));

        return result is string;
    }
}

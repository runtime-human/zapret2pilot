using System;
using System.Data;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Storage.Tests;

public sealed class SqliteConnectionFactoryTests
{
    [Fact]
    public static void OpenConnectionCreatesDatabaseFile()
    {
        using TemporarySqliteDatabase database = new();
        SqliteConnectionFactory factory = database.CreateFactory();

        using SqliteConnection connection = factory.OpenConnection();

        Assert.True(File.Exists(database.DatabasePath));
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public static void OpenConnectionAppliesJournalModeWal()
    {
        using TemporarySqliteDatabase database = new();
        SqliteConnectionFactory factory = database.CreateFactory();

        using SqliteConnection connection = factory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "PRAGMA journal_mode;";

        object? result = command.ExecuteScalar();
        string journalMode = Assert.IsType<string>(result);

        Assert.True(
            string.Equals("wal", journalMode, StringComparison.OrdinalIgnoreCase),
            $"Expected WAL journal mode, got '{journalMode}'.");
    }

    [Fact]
    public static void OpenConnectionAppliesBusyTimeout()
    {
        using TemporarySqliteDatabase database = new();
        SqliteConnectionFactory factory = database.CreateFactory();

        using SqliteConnection connection = factory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "PRAGMA busy_timeout;";

        object? result = command.ExecuteScalar();
        long busyTimeout = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        Assert.Equal(5000L, busyTimeout);
    }

    [Fact]
    public static void OpenConnectionAppliesSynchronousNormal()
    {
        using TemporarySqliteDatabase database = new();
        SqliteConnectionFactory factory = database.CreateFactory();

        using SqliteConnection connection = factory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "PRAGMA synchronous;";

        object? result = command.ExecuteScalar();
        long synchronous = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        Assert.Equal(1L, synchronous);
    }

    [Fact]
    public static void OpenConnectionEnablesForeignKeys()
    {
        using TemporarySqliteDatabase database = new();
        SqliteConnectionFactory factory = database.CreateFactory();

        using SqliteConnection connection = factory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "PRAGMA foreign_keys;";

        object? result = command.ExecuteScalar();
        long value = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        Assert.Equal(1L, value);
    }

    [Fact]
    public static void OpenConnectionRejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new SqliteConnectionFactory(null!));
    }

    [Fact]
    public static void OptionsRejectsEmptyDatabasePath()
    {
        Assert.Throws<ArgumentException>(() => new SqliteStorageOptions(string.Empty));
    }
}

using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Zapret2Pilot.Storage.Sqlite;

public sealed class SqliteSettingsRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        this.connectionFactory = connectionFactory;
    }

    public void SetString(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO app_settings (key, value, updated_utc)
            VALUES ($key, $value, $updatedUtc)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_utc = excluded.updated_utc;
            """;

        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue(
            "$updatedUtc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        command.ExecuteNonQuery();
    }

    public string? GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT value
            FROM app_settings
            WHERE key = $key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", key);

        object? result = command.ExecuteScalar();

        return result is string value ? value : null;
    }
}

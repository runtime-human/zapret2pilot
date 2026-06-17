using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Zapret2Pilot.Storage.Sqlite;

public sealed class SqliteEventJournal
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteEventJournal(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        this.connectionFactory = connectionFactory;
    }

    public SqliteEventJournalEntry Append(
        string category,
        string message,
        string severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);

        SqliteEventJournalEntry entry = new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            category,
            message,
            severity);

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO event_journal (id, occurred_utc, category, message, severity)
            VALUES ($id, $occurredUtc, $category, $message, $severity);
            """;

        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue(
            "$occurredUtc",
            entry.OccurredUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$severity", entry.Severity);

        command.ExecuteNonQuery();

        return entry;
    }

    public IReadOnlyList<SqliteEventJournalEntry> GetRecent(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT id, occurred_utc, category, message, severity
            FROM event_journal
            ORDER BY occurred_utc DESC, rowid DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = command.ExecuteReader();

        List<SqliteEventJournalEntry> entries = [];
        while (reader.Read())
        {
            entries.Add(new SqliteEventJournalEntry(
                reader.GetString(0),
                DateTimeOffset.Parse(
                    reader.GetString(1),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return entries;
    }
}

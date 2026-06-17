using System;

namespace Zapret2Pilot.Storage.Sqlite;

public sealed record class SqliteEventJournalEntry
{
    public SqliteEventJournalEntry(
        string id,
        DateTimeOffset occurredUtc,
        string category,
        string message,
        string severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);

        Id = id;
        OccurredUtc = occurredUtc;
        Category = category;
        Message = message;
        Severity = severity;
    }

    public string Id { get; }

    public DateTimeOffset OccurredUtc { get; }

    public string Category { get; }

    public string Message { get; }

    public string Severity { get; }
}

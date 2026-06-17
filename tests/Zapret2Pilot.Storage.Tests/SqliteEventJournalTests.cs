using System;
using System.Collections.Generic;
using Xunit;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Storage.Tests;

public sealed class SqliteEventJournalTests
{
    [Fact]
    public static void AppendStoresEvent()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        SqliteEventJournal journal = new(database.CreateFactory());

        SqliteEventJournalEntry appended = journal.Append(
            "runtime",
            "Runtime placeholder event.",
            "Info");

        IReadOnlyList<SqliteEventJournalEntry> entries = journal.GetRecent(10);

        SqliteEventJournalEntry stored = Assert.Single(entries);
        Assert.Equal(appended.Id, stored.Id);
        Assert.Equal("runtime", stored.Category);
        Assert.Equal("Runtime placeholder event.", stored.Message);
        Assert.Equal("Info", stored.Severity);
    }

    [Fact]
    public static void GetRecentReturnsNewestFirst()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        SqliteEventJournal journal = new(database.CreateFactory());

        SqliteEventJournalEntry first = journal.Append("diagnostics", "First event.", "Info");
        SqliteEventJournalEntry second = journal.Append("diagnostics", "Second event.", "Warning");

        IReadOnlyList<SqliteEventJournalEntry> entries = journal.GetRecent(10);

        Assert.Equal(second.Id, entries[0].Id);
        Assert.Equal(first.Id, entries[1].Id);
    }

    [Fact]
    public static void GetRecentRespectsLimit()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        SqliteEventJournal journal = new(database.CreateFactory());

        journal.Append("diagnostics", "First event.", "Info");
        journal.Append("diagnostics", "Second event.", "Warning");
        journal.Append("diagnostics", "Third event.", "Error");

        IReadOnlyList<SqliteEventJournalEntry> entries = journal.GetRecent(2);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public static void AppendRejectsEmptyCategory()
    {
        using TemporarySqliteDatabase database = new();
        SqliteEventJournal journal = new(database.CreateFactory());

        Assert.Throws<ArgumentException>(() => journal.Append(string.Empty, "Message.", "Info"));
    }

    [Fact]
    public static void AppendRejectsEmptyMessage()
    {
        using TemporarySqliteDatabase database = new();
        SqliteEventJournal journal = new(database.CreateFactory());

        Assert.Throws<ArgumentException>(() => journal.Append("diagnostics", string.Empty, "Info"));
    }

    [Fact]
    public static void AppendRejectsEmptySeverity()
    {
        using TemporarySqliteDatabase database = new();
        SqliteEventJournal journal = new(database.CreateFactory());

        Assert.Throws<ArgumentException>(() => journal.Append("diagnostics", "Message.", string.Empty));
    }
}

using System;
using Xunit;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Storage.Tests;

public sealed class SqliteEventJournalEntryTests
{
    [Fact]
    public static void EntryRejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(() => new SqliteEventJournalEntry(
            string.Empty,
            DateTimeOffset.UtcNow,
            "diagnostics",
            "Message.",
            "Info"));
    }

    [Fact]
    public static void EntryRejectsEmptyCategory()
    {
        Assert.Throws<ArgumentException>(() => new SqliteEventJournalEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            string.Empty,
            "Message.",
            "Info"));
    }

    [Fact]
    public static void EntryRejectsEmptyMessage()
    {
        Assert.Throws<ArgumentException>(() => new SqliteEventJournalEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "diagnostics",
            string.Empty,
            "Info"));
    }

    [Fact]
    public static void EntryRejectsEmptySeverity()
    {
        Assert.Throws<ArgumentException>(() => new SqliteEventJournalEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "diagnostics",
            "Message.",
            string.Empty));
    }
}

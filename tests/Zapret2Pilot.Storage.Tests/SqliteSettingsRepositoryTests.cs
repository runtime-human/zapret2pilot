using System;
using Xunit;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Storage.Tests;

public sealed class SqliteSettingsRepositoryTests
{
    [Fact]
    public static void SetStringPersistsValue()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        SqliteSettingsRepository repository = new(database.CreateFactory());

        repository.SetString("theme", "light");

        Assert.Equal("light", repository.GetString("theme"));
    }

    [Fact]
    public static void SetStringOverwritesExistingValue()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        SqliteSettingsRepository repository = new(database.CreateFactory());

        repository.SetString("theme", "light");
        repository.SetString("theme", "dark");

        Assert.Equal("dark", repository.GetString("theme"));
    }

    [Fact]
    public static void GetStringReturnsNullWhenMissing()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        SqliteSettingsRepository repository = new(database.CreateFactory());

        Assert.Null(repository.GetString("missing"));
    }

    [Fact]
    public static void SetStringRejectsEmptyKey()
    {
        using TemporarySqliteDatabase database = new();
        SqliteSettingsRepository repository = new(database.CreateFactory());

        Assert.Throws<ArgumentException>(() => repository.SetString(string.Empty, "value"));
    }

    [Fact]
    public static void SetStringRejectsNullValue()
    {
        using TemporarySqliteDatabase database = new();
        SqliteSettingsRepository repository = new(database.CreateFactory());

        Assert.Throws<ArgumentNullException>(() => repository.SetString("key", null!));
    }
}

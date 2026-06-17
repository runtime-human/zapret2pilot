using System;

namespace Zapret2Pilot.Storage.Sqlite;

public sealed record class SqliteSchemaMigration
{
    public SqliteSchemaMigration(string id, string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        Id = id;
        Sql = sql;
    }

    public string Id { get; }

    public string Sql { get; }
}

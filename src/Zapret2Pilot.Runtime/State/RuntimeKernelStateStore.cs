using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Runtime.State;

/// <summary>
/// Default <see cref="IRuntimeKernelStateStore"/> implementation backed
/// by the SQLite tables created by migration
/// <c>0002_runtime_state_store</c>.
///
/// All access to the underlying database goes through
/// <see cref="SqliteConnectionFactory"/>, which already applies the
/// required <c>journal_mode=WAL</c>, <c>busy_timeout</c>,
/// <c>synchronous=NORMAL</c> and <c>foreign_keys=ON</c> PRAGMAs per
/// connection — the store deliberately does not re-apply them.
/// </summary>
public sealed class RuntimeKernelStateStore : IRuntimeKernelStateStore
{
    private const string ActiveState = "Active";
    private const string StoppedState = "Stopped";

    private readonly SqliteConnectionFactory connectionFactory;

    public RuntimeKernelStateStore(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        this.connectionFactory = connectionFactory;
    }

    public RuntimeSessionRecord StartSession(
        ProfileId profileId,
        RuntimePlanId planId,
        RuntimePlanCacheKey planCacheKey)
    {
        ArgumentNullException.ThrowIfNull(profileId, nameof(profileId));
        ArgumentNullException.ThrowIfNull(planId, nameof(planId));
        ArgumentNullException.ThrowIfNull(planCacheKey, nameof(planCacheKey));

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        string nowText = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        string startedText = nowText;
        RuntimeSessionId sessionId = new(Guid.NewGuid().ToString("N"));

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        EndActiveSessions(connection, transaction, nowText);
        InsertSession(connection, transaction, sessionId, startedText, profileId, planId, planCacheKey);
        UpsertRuntimeState(connection, transaction, sessionId.Value, isRunning: 1, nowText);

        transaction.Commit();

        return new RuntimeSessionRecord(
            id: sessionId,
            startedAtUtc: nowUtc,
            endedAtUtc: null,
            profileId: profileId,
            planId: planId,
            planCacheKey: planCacheKey,
            state: RuntimeSessionState.Active);
    }

    public void EndSession(RuntimeSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId, nameof(sessionId));

        EndSessionInternal(sessionId, StoppedState);
    }

    public void EndSession(RuntimeSessionId sessionId, RuntimeSessionState finalState)
    {
        ArgumentNullException.ThrowIfNull(sessionId, nameof(sessionId));

        if (finalState is not (RuntimeSessionState.Stopped or RuntimeSessionState.Failed))
        {
            throw new ArgumentException(
                $"EndSession final state must be Stopped or Failed; got '{finalState}'.",
                nameof(finalState));
        }

        EndSessionInternal(sessionId, StateToText(finalState));
    }

    private void EndSessionInternal(RuntimeSessionId sessionId, string finalStateText)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        string nowText = nowUtc.ToString("O", CultureInfo.InvariantCulture);

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        if (!SessionExists(connection, transaction, sessionId.Value))
        {
            throw new InvalidOperationException(
                $"No runtime session with id '{sessionId.Value}' exists.");
        }

        CloseSession(connection, transaction, sessionId.Value, nowText, finalStateText);
        ClearRuntimeState(connection, transaction, sessionId.Value, nowText);

        transaction.Commit();
    }

    private static string StateToText(RuntimeSessionState state)
    {
        return state switch
        {
            RuntimeSessionState.Active => ActiveState,
            RuntimeSessionState.Stopped => StoppedState,
            RuntimeSessionState.Failed => "Failed",
            _ => throw new ArgumentException(
                $"Unknown {nameof(RuntimeSessionState)} value: {state}.",
                nameof(state)),
        };
    }

    public RuntimeSessionRecord? GetCurrentSession()
    {
        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT session_id
            FROM runtime_state
            WHERE id = 1
            LIMIT 1;
            """;

        object? result = command.ExecuteScalar();

        if (result is not string sessionIdValue)
        {
            return null;
        }

        return ReadSessionById(connection, sessionIdValue);
    }

    public IReadOnlyList<RuntimeSessionRecord> GetRecentSessions(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count, nameof(count));

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT id, started_utc, ended_utc, profile_id, plan_id, plan_cache_key, state
            FROM runtime_sessions
            ORDER BY started_utc DESC, id DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        using SqliteDataReader reader = command.ExecuteReader();

        List<RuntimeSessionRecord> records = [];
        while (reader.Read())
        {
            records.Add(ReadSession(reader));
        }

        return records;
    }

    private static void EndActiveSessions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string endedUtcText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE runtime_sessions
            SET ended_utc = $endedUtc,
                state = $state
            WHERE state = $activeState;
            """;
        command.Parameters.AddWithValue("$endedUtc", endedUtcText);
        command.Parameters.AddWithValue("$state", StoppedState);
        command.Parameters.AddWithValue("$activeState", ActiveState);

        command.ExecuteNonQuery();
    }

    private static void InsertSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RuntimeSessionId sessionId,
        string startedUtcText,
        ProfileId profileId,
        RuntimePlanId planId,
        RuntimePlanCacheKey planCacheKey)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO runtime_sessions (
                id, started_utc, ended_utc, profile_id, plan_id, plan_cache_key, state
            )
            VALUES (
                $id, $startedUtc, NULL, $profileId, $planId, $planCacheKey, $state
            );
            """;
        command.Parameters.AddWithValue("$id", sessionId.Value);
        command.Parameters.AddWithValue("$startedUtc", startedUtcText);
        command.Parameters.AddWithValue("$profileId", profileId.Value);
        command.Parameters.AddWithValue("$planId", planId.Value);
        command.Parameters.AddWithValue("$planCacheKey", planCacheKey.Value);
        command.Parameters.AddWithValue("$state", ActiveState);

        command.ExecuteNonQuery();
    }

    private static void UpsertRuntimeState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionIdValue,
        long isRunning,
        string updatedUtcText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO runtime_state (id, session_id, is_running, updated_utc)
            VALUES (1, $sessionId, $isRunning, $updatedUtc)
            ON CONFLICT(id) DO UPDATE SET
                session_id = excluded.session_id,
                is_running = excluded.is_running,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionIdValue);
        command.Parameters.AddWithValue("$isRunning", isRunning);
        command.Parameters.AddWithValue("$updatedUtc", updatedUtcText);

        command.ExecuteNonQuery();
    }

    private static bool SessionExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionIdValue)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT 1
            FROM runtime_sessions
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", sessionIdValue);

        object? result = command.ExecuteScalar();

        return result is not null;
    }

    private static void CloseSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionIdValue,
        string endedUtcText,
        string state)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE runtime_sessions
            SET ended_utc = $endedUtc,
                state = $state
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$endedUtc", endedUtcText);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$id", sessionIdValue);

        command.ExecuteNonQuery();
    }

    private static void ClearRuntimeState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionIdValue,
        string updatedUtcText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE runtime_state
            SET session_id = NULL,
                is_running = 0,
                updated_utc = $updatedUtc
            WHERE id = 1
              AND session_id = $sessionId
              AND is_running = 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionIdValue);
        command.Parameters.AddWithValue("$updatedUtc", updatedUtcText);

        command.ExecuteNonQuery();
    }

    private static RuntimeSessionRecord? ReadSessionById(
        SqliteConnection connection,
        string sessionIdValue)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, started_utc, ended_utc, profile_id, plan_id, plan_cache_key, state
            FROM runtime_sessions
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", sessionIdValue);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadSession(reader) : null;
    }

    private static RuntimeSessionRecord ReadSession(SqliteDataReader reader)
    {
        RuntimeSessionId id = new(reader.GetString(0));
        DateTimeOffset startedAtUtc = ParseTimestamp(reader.GetString(1));
        DateTimeOffset? endedAtUtc = reader.IsDBNull(2)
            ? null
            : ParseTimestamp(reader.GetString(2));
        ProfileId? profileId = reader.IsDBNull(3)
            ? null
            : new ProfileId(reader.GetString(3));
        RuntimePlanId? planId = reader.IsDBNull(4)
            ? null
            : new RuntimePlanId(reader.GetString(4));
        RuntimePlanCacheKey? planCacheKey = reader.IsDBNull(5)
            ? null
            : new RuntimePlanCacheKey(reader.GetString(5));
        RuntimeSessionState state = ParseState(reader.GetString(6));

        return new RuntimeSessionRecord(
            id: id,
            startedAtUtc: startedAtUtc,
            endedAtUtc: endedAtUtc,
            profileId: profileId,
            planId: planId,
            planCacheKey: planCacheKey,
            state: state);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static RuntimeSessionState ParseState(string value)
    {
        return value switch
        {
            ActiveState => RuntimeSessionState.Active,
            StoppedState => RuntimeSessionState.Stopped,
            "Failed" => RuntimeSessionState.Failed,
            _ => throw new InvalidOperationException(
                $"Unknown runtime session state persisted in database: '{value}'.")
        };
    }
}

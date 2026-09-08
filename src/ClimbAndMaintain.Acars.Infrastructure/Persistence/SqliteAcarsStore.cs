using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Infrastructure.Serialization;
using Microsoft.Data.Sqlite;

namespace ClimbAndMaintain.Acars.Infrastructure.Persistence;

public sealed class SqliteAcarsStore : IFlightStateStore, IDisposable
{
    public const int SupportedSchemaVersion = 3;
    private const string PositionKind = "position";
    private const string EventKind = "event";
    private const string LogKind = "log";
    private const string CompletionKind = "completion";
    private const string CancellationKind = "cancellation";

    private static readonly JsonSerializerOptions JsonOptions = AcarsJsonSerializerOptions.Create();

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;
    private bool disposed;

    public SqliteAcarsStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized)
        {
            return;
        }

        await initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            string? directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            int version = await GetSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);

            if (version > SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"Database schema {version} is newer than supported schema {SupportedSchemaVersion}."));
            }

            if (version < 1)
            {
                await ApplyVersionOneMigrationAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 1;
            }

            if (version < 2)
            {
                await ApplyVersionTwoMigrationAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 2;
            }

            if (version < 3)
            {
                await ApplyVersionThreeMigrationAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT payload_json
            FROM flight_sessions
            WHERE is_active = 1
            ORDER BY updated_utc DESC
            LIMIT 1;
            """;

        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : DeserializeRequired<FlightSessionState>((string)value, "active flight session");
    }

    public async ValueTask<FlightSessionState?> GetAsync(
        FlightSessionId sessionId,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT payload_json
            FROM flight_sessions
            WHERE session_id = $session_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.Value.ToString("D"));

        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : DeserializeRequired<FlightSessionState>((string)value, "flight session");
    }

    public ValueTask SaveAsync(FlightSessionState session, CancellationToken cancellationToken) =>
        SaveWithOutboxAsync(session, [], cancellationToken);

    public async ValueTask SaveWithOutboxAsync(
        FlightSessionState session,
        IReadOnlyCollection<OutboxItem> outboxItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(outboxItems);
        foreach (OutboxItem item in outboxItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.SessionId != session.Id)
            {
                throw new ArgumentException(
                    "Every outbox item must belong to the session saved in the same transaction.",
                    nameof(outboxItems));
            }

            EnsureUtc(item.CreatedAtUtc, nameof(outboxItems));
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await SaveSessionAsync(connection, transaction, session, cancellationToken).ConfigureAwait(false);
        foreach (OutboxItem item in outboxItems)
        {
            await EnqueueAsync(connection, transaction, item, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(FlightSessionId sessionId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM flight_sessions WHERE session_id = $session_id;";
        command.Parameters.AddWithValue("$session_id", sessionId.Value.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask EnqueueAsync(OutboxItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureUtc(item.CreatedAtUtc, nameof(item));
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnqueueAsync(connection, transaction: null, item, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask EnqueueAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        OutboxItem item,
        CancellationToken cancellationToken)
    {
        string kind = GetKind(item);
        string payload = SerializeItem(item);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO outbox(
                item_id,
                session_id,
                item_kind,
                created_utc,
                payload_json,
                attempt_count,
                last_attempt_utc,
                last_error,
                delivered_utc)
            VALUES ($item_id, $session_id, $item_kind, $created_utc, $payload_json, 0, NULL, NULL, NULL)
            ON CONFLICT(item_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$item_id", item.Id.Value.ToString("D"));
        command.Parameters.AddWithValue("$session_id", item.SessionId.Value.ToString("D"));
        command.Parameters.AddWithValue("$item_kind", kind);
        command.Parameters.AddWithValue("$created_utc", FormatTimestamp(item.CreatedAtUtc));
        command.Parameters.AddWithValue("$payload_json", payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask SaveSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FlightSessionState session,
        CancellationToken cancellationToken)
    {
        int isActive = session.Status switch
        {
            FlightSessionStatus.Starting or FlightSessionStatus.Active or FlightSessionStatus.Paused => 1,
            FlightSessionStatus.Completed or FlightSessionStatus.Cancelled => 0,
            _ => throw new ArgumentOutOfRangeException(
                nameof(session),
                session.Status,
                "The flight session has an unknown lifecycle status."),
        };
        string payload = JsonSerializer.Serialize(session, JsonOptions);
        if (isActive == 1)
        {
            await using SqliteCommand deactivate = connection.CreateCommand();
            deactivate.Transaction = transaction;
            deactivate.CommandText = "UPDATE flight_sessions SET is_active = 0 WHERE is_active = 1;";
            await deactivate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using SqliteCommand upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText =
            """
            INSERT INTO flight_sessions(
                session_id,
                is_active,
                updated_utc,
                payload_json,
                lifecycle_status,
                start_request_marker,
                start_intent_state,
                prefile_attempted_utc)
            VALUES (
                $session_id,
                $is_active,
                $updated_utc,
                $payload_json,
                $lifecycle_status,
                $start_request_marker,
                $start_intent_state,
                $prefile_attempted_utc)
            ON CONFLICT(session_id) DO UPDATE SET
                is_active = excluded.is_active,
                updated_utc = excluded.updated_utc,
                payload_json = excluded.payload_json,
                lifecycle_status = excluded.lifecycle_status,
                start_request_marker = excluded.start_request_marker,
                start_intent_state = excluded.start_intent_state,
                prefile_attempted_utc = excluded.prefile_attempted_utc;
            """;
        upsert.Parameters.AddWithValue("$session_id", session.Id.Value.ToString("D"));
        upsert.Parameters.AddWithValue("$is_active", isActive);
        upsert.Parameters.AddWithValue("$updated_utc", FormatTimestamp(session.UpdatedAtUtc));
        upsert.Parameters.AddWithValue("$payload_json", payload);
        upsert.Parameters.AddWithValue("$lifecycle_status", (int)session.Status);
        upsert.Parameters.AddWithValue(
            "$start_request_marker",
            (object?)session.StartIntent?.RequestMarker ?? DBNull.Value);
        upsert.Parameters.AddWithValue(
            "$start_intent_state",
            session.StartIntent is { } intent ? (int)intent.State : DBNull.Value);
        upsert.Parameters.AddWithValue(
            "$prefile_attempted_utc",
            session.StartIntent?.LastAttemptAtUtc is { } attemptedAtUtc
                ? FormatTimestamp(attemptedAtUtc)
                : DBNull.Value);
        await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<PendingOutboxItem>> GetPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                item_id,
                session_id,
                item_kind,
                created_utc,
                payload_json,
                attempt_count,
                last_attempt_utc,
                last_error
            FROM outbox
            WHERE delivered_utc IS NULL
            ORDER BY created_utc, item_id
            LIMIT $maximum_count;
            """;
        command.Parameters.AddWithValue("$maximum_count", maximumCount);

        List<PendingOutboxItem> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            OutboxItem item = DeserializeItem(reader.GetString(2), reader.GetString(4));
            ValidateEnvelope(item, reader.GetString(0), reader.GetString(1), reader.GetString(3));
            DateTimeOffset? lastAttempt = reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6));
            string? lastError = reader.IsDBNull(7) ? null : reader.GetString(7);
            result.Add(new PendingOutboxItem(item, reader.GetInt32(5), lastAttempt, lastError));
        }

        return result;
    }

    public async ValueTask RecordFailedAttemptAsync(
        OutboxItemId id,
        DateTimeOffset attemptedAtUtc,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        EnsureUtc(attemptedAtUtc, nameof(attemptedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE outbox
            SET
                attempt_count = attempt_count + 1,
                last_attempt_utc = $attempted_utc,
                last_error = $last_error
            WHERE item_id = $item_id AND delivered_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$attempted_utc", FormatTimestamp(attemptedAtUtc));
        command.Parameters.AddWithValue("$last_error", failureMessage.Trim());
        command.Parameters.AddWithValue("$item_id", id.Value.ToString("D"));
        await EnsureUpdatedAsync(command, id, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask MarkDeliveredAsync(
        OutboxItemId id,
        DateTimeOffset deliveredAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureUtc(deliveredAtUtc, nameof(deliveredAtUtc));
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE outbox
            SET delivered_utc = $delivered_utc, last_error = NULL
            WHERE item_id = $item_id AND delivered_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$delivered_utc", FormatTimestamp(deliveredAtUtc));
        command.Parameters.AddWithValue("$item_id", id.Value.ToString("D"));
        await EnsureUpdatedAsync(command, id, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OutboxCounts> GetCountsAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COALESCE(SUM(CASE WHEN item_kind = 'position' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN item_kind = 'event' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN item_kind = 'log' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN item_kind IN ('completion', 'cancellation') THEN 1 ELSE 0 END), 0)
            FROM outbox
            WHERE delivered_utc IS NULL;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new OutboxCounts(0, 0, 0);
        }

        return new OutboxCounts(
            Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(2), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(3), CultureInfo.InvariantCulture));
    }

    public async ValueTask<int> ReadSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        initializationLock.Dispose();
    }

    private static async ValueTask EnsureUpdatedAsync(
        SqliteCommand command,
        OutboxItemId id,
        CancellationToken cancellationToken)
    {
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new KeyNotFoundException(
                FormattableString.Invariant($"Pending outbox item '{id.Value:D}' was not found."));
        }
    }

    private static string SerializeItem(OutboxItem item) => item switch
    {
        PositionOutboxItem position => JsonSerializer.Serialize(position, JsonOptions),
        EventOutboxItem flightEvent => JsonSerializer.Serialize(flightEvent, JsonOptions),
        LogOutboxItem log => JsonSerializer.Serialize(log, JsonOptions),
        CompletionOutboxItem completion => JsonSerializer.Serialize(completion, JsonOptions),
        CancellationOutboxItem cancellation => JsonSerializer.Serialize(cancellation, JsonOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.GetType(), "Unknown outbox item type."),
    };

    private static OutboxItem DeserializeItem(string kind, string payload) => kind switch
    {
        PositionKind => DeserializeRequired<PositionOutboxItem>(payload, PositionKind),
        EventKind => DeserializeRequired<EventOutboxItem>(payload, EventKind),
        LogKind => DeserializeRequired<LogOutboxItem>(payload, LogKind),
        CompletionKind => DeserializeRequired<CompletionOutboxItem>(payload, CompletionKind),
        CancellationKind => DeserializeRequired<CancellationOutboxItem>(payload, CancellationKind),
        _ => throw new InvalidDataException(FormattableString.Invariant($"Unknown outbox item kind '{kind}'.")),
    };

    private static string GetKind(OutboxItem item) => item switch
    {
        PositionOutboxItem => PositionKind,
        EventOutboxItem => EventKind,
        LogOutboxItem => LogKind,
        CompletionOutboxItem => CompletionKind,
        CancellationOutboxItem => CancellationKind,
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.GetType(), "Unknown outbox item type."),
    };

    private static void ValidateEnvelope(
        OutboxItem item,
        string databaseItemId,
        string databaseSessionId,
        string databaseCreatedUtc)
    {
        bool valid = item.Id.Value == Guid.Parse(databaseItemId)
            && item.SessionId.Value == Guid.Parse(databaseSessionId)
            && item.CreatedAtUtc == ParseTimestamp(databaseCreatedUtc);
        if (!valid)
        {
            throw new InvalidDataException("Outbox envelope columns do not match the stored payload.");
        }
    }

    private static T DeserializeRequired<T>(string json, string description)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidDataException(FormattableString.Invariant($"The stored {description} was empty."));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                FormattableString.Invariant($"The stored {description} is not valid JSON."),
                exception);
        }
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        EnsureUtc(timestamp, nameof(timestamp));
        return timestamp.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string timestamp) =>
        DateTimeOffset.ParseExact(timestamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void EnsureUtc(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must use UTC.", parameterName);
        }
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<SqliteConnection> OpenConfiguredConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA journal_mode = WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async ValueTask ApplyVersionOneMigrationAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS flight_sessions (
                session_id TEXT NOT NULL PRIMARY KEY,
                is_active INTEGER NOT NULL CHECK (is_active IN (0, 1)),
                updated_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_flight_sessions_one_active
                ON flight_sessions(is_active)
                WHERE is_active = 1;

            CREATE TABLE IF NOT EXISTS outbox (
                item_id TEXT NOT NULL PRIMARY KEY,
                session_id TEXT NOT NULL,
                item_kind TEXT NOT NULL CHECK (item_kind IN ('position', 'event', 'log')),
                created_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
                last_attempt_utc TEXT NULL,
                last_error TEXT NULL,
                delivered_utc TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_outbox_pending
                ON outbox(delivered_utc, created_utc, item_id);

            CREATE INDEX IF NOT EXISTS ix_outbox_session
                ON outbox(session_id, delivered_utc);

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ApplyVersionTwoMigrationAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            CREATE TABLE outbox_v2 (
                item_id TEXT NOT NULL PRIMARY KEY,
                session_id TEXT NOT NULL,
                item_kind TEXT NOT NULL CHECK (
                    item_kind IN ('position', 'event', 'log', 'completion', 'cancellation')),
                created_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
                last_attempt_utc TEXT NULL,
                last_error TEXT NULL,
                delivered_utc TEXT NULL
            );

            INSERT INTO outbox_v2(
                item_id,
                session_id,
                item_kind,
                created_utc,
                payload_json,
                attempt_count,
                last_attempt_utc,
                last_error,
                delivered_utc)
            SELECT
                item_id,
                session_id,
                item_kind,
                created_utc,
                payload_json,
                attempt_count,
                last_attempt_utc,
                last_error,
                delivered_utc
            FROM outbox;

            DROP TABLE outbox;
            ALTER TABLE outbox_v2 RENAME TO outbox;

            CREATE INDEX ix_outbox_pending
                ON outbox(delivered_utc, created_utc, item_id);

            CREATE INDEX ix_outbox_session
                ON outbox(session_id, delivered_utc);

            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ApplyVersionThreeMigrationAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            ALTER TABLE flight_sessions
                ADD COLUMN lifecycle_status INTEGER NOT NULL DEFAULT 0
                CHECK (lifecycle_status BETWEEN 0 AND 4);

            ALTER TABLE flight_sessions
                ADD COLUMN start_request_marker TEXT NULL;

            ALTER TABLE flight_sessions
                ADD COLUMN start_intent_state INTEGER NULL
                CHECK (start_intent_state IS NULL OR start_intent_state BETWEEN 0 AND 4);

            ALTER TABLE flight_sessions
                ADD COLUMN prefile_attempted_utc TEXT NULL;

            UPDATE flight_sessions
            SET lifecycle_status = CASE json_extract(payload_json, '$.status')
                WHEN 'paused' THEN 1
                WHEN 'completed' THEN 2
                WHEN 'cancelled' THEN 3
                WHEN 'starting' THEN 4
                ELSE 0
            END;

            CREATE UNIQUE INDEX ix_flight_sessions_start_request
                ON flight_sessions(start_request_marker)
                WHERE start_request_marker IS NOT NULL;

            PRAGMA user_version = 3;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}

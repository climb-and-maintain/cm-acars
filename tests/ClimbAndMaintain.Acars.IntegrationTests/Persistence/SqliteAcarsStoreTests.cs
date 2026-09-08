using System.Collections.Immutable;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace ClimbAndMaintain.Acars.IntegrationTests.Persistence;

public sealed class SqliteAcarsStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActiveSessionSurvivesStoreReopen()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "state", "acars.db");
        FlightSessionState expected = CreateSession();

        using (SqliteAcarsStore firstStore = new(databasePath))
        {
            await firstStore.SaveAsync(expected, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        using SqliteAcarsStore reopenedStore = new(databasePath);
        FlightSessionState? actual = await reopenedStore
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.FlightPlan.FlightNumber, actual.FlightPlan.FlightNumber);
        Assert.Equal(expected.Phase, actual.Phase);
        Assert.Equal(expected.LastTelemetry?.Position, actual.LastTelemetry?.Position);
    }

    [Fact]
    public async Task SessionJsonFromBeforeMetricsDefaultsToEmptyRestartSafeMetrics()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        FlightSessionState original = CreateSession();

        using (SqliteAcarsStore firstStore = new(databasePath))
        {
            await firstStore.SaveAsync(original, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        await using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE flight_sessions SET payload_json = json_remove(payload_json, '$.metrics') WHERE session_id = $session_id;";
            command.Parameters.AddWithValue("$session_id", original.Id.Value.ToString("D"));
            Assert.Equal(
                1,
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
        }

        using SqliteAcarsStore reopenedStore = new(databasePath);
        FlightSessionState? restored = await reopenedStore
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.NotNull(restored);
        Assert.Equal(new Distance(0), restored.Metrics.FlownDistance);
        Assert.Equal(TimeSpan.Zero, restored.Metrics.ActiveTrackingTime);
        Assert.Equal(TimeSpan.Zero, restored.Metrics.AirborneFlightTime);
        Assert.Null(restored.Metrics.FuelUsed);
        Assert.Null(restored.Metrics.LandingRate);
    }

    [Fact]
    public async Task PendingStartIntentAndReconciliationColumnsSurviveStoreReopen()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        const string requestMarker = "CM ACARS abcdefghijklmnop";
        FlightSessionState pending = new(
            FlightSessionId.New(),
            new FlightPlan("CMP", "N123CM", "100", "KSEA", "KLAX"),
            FlightPhase.Ready,
            Start,
            Start.AddSeconds(1))
        {
            Status = FlightSessionStatus.Starting,
            StartIntent = new FlightStartIntent(
                requestMarker,
                FlightStartIntentState.PrefileRequested,
                Start,
                Start.AddSeconds(1)),
        };

        using (SqliteAcarsStore firstStore = new(databasePath))
        {
            await firstStore.SaveAsync(pending, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        using SqliteAcarsStore reopenedStore = new(databasePath);
        FlightSessionState? restored = await reopenedStore
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(FlightSessionStatus.Starting, restored?.Status);
        Assert.Equal(requestMarker, restored?.StartIntent?.RequestMarker);
        Assert.Equal(FlightStartIntentState.PrefileRequested, restored?.StartIntent?.State);
        Assert.Equal(Start.AddSeconds(1), restored?.StartIntent?.LastAttemptAtUtc);

        await using SqliteConnection connection = new($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT lifecycle_status, start_request_marker, start_intent_state, prefile_attempted_utc
            FROM flight_sessions
            WHERE session_id = $session_id;
            """;
        command.Parameters.AddWithValue("$session_id", pending.Id.Value.ToString("D"));
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal((long)FlightSessionStatus.Starting, reader.GetInt64(0));
        Assert.Equal(requestMarker, reader.GetString(1));
        Assert.Equal((long)FlightStartIntentState.PrefileRequested, reader.GetInt64(2));
        Assert.Equal(
            Start.AddSeconds(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            reader.GetString(3));
    }

    [Fact]
    public async Task SavingAnotherSessionLeavesOnlyTheNewestActive()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState first = CreateSession();
        FlightSessionState second = new(
            FlightSessionId.New(),
            new FlightPlan("CMP", "N456CM", "CM200", "KLAX", "KSFO"),
            FlightPhase.Boarding,
            Start.AddHours(1),
            Start.AddHours(1));

        await store.SaveAsync(first, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(second, TestContext.Current.CancellationToken).ConfigureAwait(true);

        FlightSessionState? active = await store
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(second.Id, active?.Id);

        await store.DeleteAsync(second.Id, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Null(await store.GetActiveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task TerminalSessionIsRetainedWithoutBeingRestoredAsActive()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState completed = CreateSession() with
        {
            Status = FlightSessionStatus.Completed,
        };

        await store.SaveAsync(completed, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(await store.GetActiveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
        FlightSessionState? retained = await store
            .GetAsync(completed.Id, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(FlightSessionStatus.Completed, retained?.Status);
    }

    [Fact]
    public async Task UpdatingHistoricalTerminalSessionDoesNotDeactivateCurrentFlight()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState historical = CreateSession() with
        {
            Status = FlightSessionStatus.Completed,
        };
        FlightSessionState current = new(
            FlightSessionId.New(),
            new FlightPlan("CMP", "N456CM", "CM200", "KLAX", "KSFO"),
            FlightPhase.Boarding,
            Start.AddHours(1),
            Start.AddHours(1));
        await store.SaveAsync(historical, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(current, TestContext.Current.CancellationToken).ConfigureAwait(true);

        await store.SaveAsync(historical, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(current.Id, (await store
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true))?.Id);
    }

    [Fact]
    public async Task SessionAndOutboxItemsAreCommittedThroughOneStateStoreOperation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState session = CreateSession();
        EventOutboxItem flightEvent = new(
            OutboxItemId.New(),
            session.Id,
            Start.AddMinutes(6),
            FlightEvent.Create(session.Id, FlightEventType.Cruise, Start.AddMinutes(6)));

        await store.SaveWithOutboxAsync(
            session,
            [flightEvent],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(session.Id, (await store
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true))?.Id);
        PendingOutboxItem pending = Assert.Single(await store
            .GetPendingAsync(10, TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
        Assert.Equal(flightEvent.Id, pending.Item.Id);
    }

    [Fact]
    public async Task InvalidAtomicOutboxBatchDoesNotOverwriteSession()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState original = CreateSession();
        await store.SaveAsync(original, TestContext.Current.CancellationToken).ConfigureAwait(true);
        FlightSessionState updated = new(
            original.Id,
            original.FlightPlan,
            FlightPhase.Cruise,
            original.StartedAtUtc,
            Start.AddMinutes(10))
        {
            BackendFlightId = original.BackendFlightId,
            LastTelemetry = original.LastTelemetry,
            Status = original.Status,
        };
        FlightSessionId otherSession = FlightSessionId.New();
        EventOutboxItem mismatched = new(
            OutboxItemId.New(),
            otherSession,
            Start.AddMinutes(10),
            FlightEvent.Create(otherSession, FlightEventType.Cruise, Start.AddMinutes(10)));

        await Assert.ThrowsAsync<ArgumentException>(() => store
            .SaveWithOutboxAsync(
                updated,
                [mismatched],
                TestContext.Current.CancellationToken)
            .AsTask()).ConfigureAwait(true);

        FlightSessionState? persisted = await store
            .GetAsync(original.Id, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(original.Phase, persisted?.Phase);
        Assert.Equal(0, (await store
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).Total);
    }

    [Fact]
    public async Task OutboxPersistsTypedItemsAndAttemptBookkeepingAcrossReopen()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        FlightSessionState session = CreateSession();
        PositionOutboxItem position = new(
            OutboxItemId.New(),
            session.Id,
            Start,
            new PositionReport(PositionReportId.New(), session.Id, FlightPhase.Climb, CreateTelemetry()));
        EventOutboxItem flightEvent = new(
            OutboxItemId.New(),
            session.Id,
            Start.AddSeconds(1),
            FlightEvent.Create(session.Id, FlightEventType.Takeoff, Start.AddSeconds(1)));
        LogOutboxItem log = new(
            OutboxItemId.New(),
            session.Id,
            Start.AddSeconds(2),
            new FlightLogEntry(FlightEventId.New(), session.Id, Start.AddSeconds(2), "Airborne"));

        using (SqliteAcarsStore firstStore = new(databasePath))
        {
            await firstStore.EnqueueAsync(position, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await firstStore.EnqueueAsync(position, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await firstStore.EnqueueAsync(flightEvent, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await firstStore.EnqueueAsync(log, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await firstStore.RecordFailedAttemptAsync(
                position.Id,
                Start.AddSeconds(10),
                "Network unavailable",
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(new OutboxCounts(1, 1, 1), await firstStore
                .GetCountsAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(true));
        }

        using SqliteAcarsStore reopenedStore = new(databasePath);
        IReadOnlyList<PendingOutboxItem> pending = await reopenedStore
            .GetPendingAsync(10, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(3, pending.Count);
        PendingOutboxItem restoredPosition = Assert.Single(pending, item => item.Item.Id == position.Id);
        Assert.IsType<PositionOutboxItem>(restoredPosition.Item);
        Assert.Equal(1, restoredPosition.AttemptCount);
        Assert.Equal("Network unavailable", restoredPosition.LastError);
        Assert.Equal(Start.AddSeconds(10), restoredPosition.LastAttemptAtUtc);

        await reopenedStore.MarkDeliveredAsync(
            position.Id,
            Start.AddSeconds(20),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(new OutboxCounts(0, 1, 1), await reopenedStore
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
        Assert.Equal(2, (await reopenedStore
            .GetPendingAsync(10, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).Count);
    }

    [Fact]
    public async Task CompletionOutboxFilingContextSurvivesStoreReopen()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        FlightSessionId sessionId = FlightSessionId.New();
        FlightPlan filingPlan = new("CMP", "N789CM", "CM789", "KSEA", "KJFK")
        {
            SourceFlightId = "flight-789",
            AlternateAirport = "KBOS",
            Route = "HAROB6 HAROB Q818 FOD JHW Q82 PONCT J48 MOL WAVES CAPSS3",
            PlannedDistance = new Distance(2_151.75),
            PlannedDuration = TimeSpan.FromHours(5.75),
            PlannedFuel = new FuelMass(41_250.5),
        };
        DateTimeOffset trackingStartedAtUtc = Start.AddMinutes(-15);
        CompletedFlightReport report = new(
            new Distance(2_164.25),
            TimeSpan.FromHours(5.6),
            TimeSpan.FromHours(5.9),
            new FuelMass(39_875.25),
            new VerticalSpeed(-138),
            Start.AddHours(6))
        {
            FilingPlan = filingPlan,
            TrackingStartedAtUtc = trackingStartedAtUtc,
        };
        CompletionOutboxItem completion = new(
            OutboxItemId.New(),
            sessionId,
            report.CompletedAtUtc,
            report);

        using (SqliteAcarsStore firstStore = new(databasePath))
        {
            await firstStore.EnqueueAsync(completion, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        using SqliteAcarsStore reopenedStore = new(databasePath);
        PendingOutboxItem pending = Assert.Single(await reopenedStore
            .GetPendingAsync(10, TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
        CompletionOutboxItem restoredCompletion = Assert.IsType<CompletionOutboxItem>(pending.Item);
        CompletedFlightReport restored = restoredCompletion.Report;
        FlightPlan restoredPlan = Assert.IsType<FlightPlan>(restored.FilingPlan);

        Assert.Equal(trackingStartedAtUtc, restored.TrackingStartedAtUtc);
        Assert.Equal(filingPlan.AirlineId, restoredPlan.AirlineId);
        Assert.Equal(filingPlan.AircraftId, restoredPlan.AircraftId);
        Assert.Equal(filingPlan.FlightNumber, restoredPlan.FlightNumber);
        Assert.Equal(filingPlan.DepartureAirport, restoredPlan.DepartureAirport);
        Assert.Equal(filingPlan.ArrivalAirport, restoredPlan.ArrivalAirport);
        Assert.Equal(filingPlan.SourceFlightId, restoredPlan.SourceFlightId);
        Assert.Equal(filingPlan.AlternateAirport, restoredPlan.AlternateAirport);
        Assert.Equal(filingPlan.Route, restoredPlan.Route);
        Assert.Equal(filingPlan.PlannedDistance, restoredPlan.PlannedDistance);
        Assert.Equal(filingPlan.PlannedDuration, restoredPlan.PlannedDuration);
        Assert.Equal(filingPlan.PlannedFuel, restoredPlan.PlannedFuel);
    }

    [Fact]
    public async Task EmptyOutboxReturnsZeroCounts()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));

        OutboxCounts counts = await store
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public async Task InitializationAppliesVersionedSchemaIdempotently()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        using SqliteAcarsStore store = new(databasePath);

        await store.InitializeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.InitializeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await using SqliteConnection connection = new($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? schemaVersion = await command
            .ExecuteScalarAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(SqliteAcarsStore.SupportedSchemaVersion, Assert.IsType<long>(schemaVersion));
        Assert.Equal(
            SqliteAcarsStore.SupportedSchemaVersion,
            await store.ReadSchemaVersionAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private static FlightSessionState CreateSession() => new(
        FlightSessionId.New(),
        new FlightPlan("CMP", "N123CM", "CM100", "KSEA", "KLAX"),
        FlightPhase.Climb,
        Start,
        Start.AddMinutes(5))
    {
        BackendFlightId = "pirep-42",
        LastTelemetry = CreateTelemetry(),
    };

    private static TelemetrySnapshot CreateTelemetry() => new()
    {
        CollectedAtUtc = Start.AddMinutes(5),
        Position = new GeoPosition(47.2, -122.0),
        AltitudeMsl = new Altitude(12_000),
        AltitudeAgl = new Altitude(10_000),
        IndicatedAirspeed = new Speed(250),
        GroundSpeed = new Speed(310),
        VerticalSpeed = new VerticalSpeed(1_200),
        OnGround = false,
        Engines = ImmutableArray.Create(new EngineTelemetry(1, EngineOperatingState.Running)),
    };
}

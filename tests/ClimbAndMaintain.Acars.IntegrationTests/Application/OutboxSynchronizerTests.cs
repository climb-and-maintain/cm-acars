using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Synchronization;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.Infrastructure.Persistence;

namespace ClimbAndMaintain.Acars.IntegrationTests.Application;

public sealed class OutboxSynchronizerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FailedBatchRemainsDurableAndRetriesAfterBackoff()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState session = CreateSession("backend-42", FlightSessionStatus.Completed);
        await store.SaveAsync(session, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await EnqueueCompleteFlightAsync(store, session).ConfigureAwait(true);
        FakeBackend backend = new() { RemainingPositionFailures = 1 };
        ManualTimeProvider timeProvider = new(Start.AddMinutes(5));
        using OutboxSynchronizer synchronizer = new(
            store,
            store,
            backend,
            new OutboxSynchronizationOptions
            {
                BatchSize = 2,
                MaximumItemsPerRun = 20,
                InitialRetryDelay = TimeSpan.FromSeconds(2),
                MaximumRetryDelay = TimeSpan.FromSeconds(10),
            },
            timeProvider);

        OutboxSynchronizationResult failure = await synchronizer
            .SynchronizeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(OutboxSynchronizationStatus.Failed, failure.Status);
        Assert.Equal(0, failure.DeliveredCount);
        Assert.Equal(2, failure.FailedCount);
        Assert.Equal(6, failure.Remaining.Total);
        IReadOnlyList<PendingOutboxItem> afterFailure = await store
            .GetPendingAsync(20, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(
            2,
            afterFailure.Count(item => item.Item is PositionOutboxItem && item.AttemptCount == 1));
        Assert.Equal(
            1,
            afterFailure.Count(item => item.Item is PositionOutboxItem && item.AttemptCount == 0));

        OutboxSynchronizationResult deferred = await synchronizer
            .SynchronizeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(OutboxSynchronizationStatus.DeferredForRetry, deferred.Status);
        Assert.Equal(6, deferred.DeferredCount);
        Assert.Equal(1, backend.PositionAttemptCount);

        timeProvider.SetUtcNow(Start.AddMinutes(5).AddSeconds(2));
        OutboxSynchronizationResult succeeded = await synchronizer
            .SynchronizeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(OutboxSynchronizationStatus.Synchronized, succeeded.Status);
        Assert.Equal(6, succeeded.DeliveredCount);
        Assert.Equal(0, succeeded.Remaining.Total);
        Assert.Equal(3, backend.PositionAttemptCount);
        Assert.Equal(3, backend.AcceptedPositionCount);
        Assert.Equal(1, backend.AcceptedEventCount);
        Assert.Equal(1, backend.AcceptedLogCount);
        Assert.Single(backend.FiledReports);
        Assert.All(backend.ObservedBackendFlightIds, value => Assert.Equal("backend-42", value));
    }

    [Fact]
    public async Task MissingBackendAttachmentRetainsItemsWithoutAttemptingDelivery()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState session = CreateSession(null, FlightSessionStatus.Active);
        await store.SaveAsync(session, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.EnqueueAsync(
            CreatePositionItem(session, Start),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        FakeBackend backend = new();
        using OutboxSynchronizer synchronizer = new(store, store, backend);

        OutboxSynchronizationResult result = await synchronizer
            .SynchronizeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(OutboxSynchronizationStatus.MissingBackendFlightId, result.Status);
        Assert.Equal(1, result.Remaining.Total);
        Assert.Equal(0, backend.BackendCallCount);
        PendingOutboxItem pending = Assert.Single(await store
            .GetPendingAsync(10, TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
        Assert.Equal(0, pending.AttemptCount);
    }

    [Fact]
    public async Task HistoricalSessionRoutesToItsOwnBackendFlightAfterNewStart()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState oldSession = CreateSession("backend-old", FlightSessionStatus.Completed);
        await store.SaveAsync(oldSession, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.EnqueueAsync(
            new EventOutboxItem(
                OutboxItemId.New(),
                oldSession.Id,
                Start.AddSeconds(1),
                FlightEvent.Create(oldSession.Id, FlightEventType.Completed, Start.AddSeconds(1))),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        FlightSessionState currentSession = CreateSession("backend-current", FlightSessionStatus.Active);
        await store.SaveAsync(currentSession, TestContext.Current.CancellationToken).ConfigureAwait(true);
        FakeBackend backend = new();
        ManualTimeProvider timeProvider = new(Start.AddMinutes(1));
        using OutboxSynchronizer synchronizer = new(store, store, backend, timeProvider: timeProvider);

        OutboxSynchronizationResult result = await synchronizer
            .SynchronizeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(OutboxSynchronizationStatus.Synchronized, result.Status);
        Assert.Equal(0, result.Remaining.Total);
        Assert.Equal(["backend-old"], backend.ObservedBackendFlightIds);
        Assert.Equal(currentSession.Id, (await store
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true))?.Id);
    }

    [Fact]
    public async Task CancellationOperationIsDeliveredOnceAfterBackendAcceptance()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState session = CreateSession("backend-cancel", FlightSessionStatus.Cancelled);
        await store.SaveAsync(session, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.EnqueueAsync(
            new CancellationOutboxItem(OutboxItemId.New(), session.Id, Start.AddSeconds(1)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        FakeBackend backend = new();
        ManualTimeProvider timeProvider = new(Start.AddMinutes(1));
        using OutboxSynchronizer synchronizer = new(store, store, backend, timeProvider: timeProvider);

        OutboxSynchronizationResult first = await synchronizer
            .SynchronizeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        OutboxSynchronizationResult second = await synchronizer
            .SynchronizeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(OutboxSynchronizationStatus.Synchronized, first.Status);
        Assert.Equal(OutboxSynchronizationStatus.NothingPending, second.Status);
        Assert.Equal(["backend-cancel"], backend.CancelledBackendFlightIds);
    }

    private static async ValueTask EnqueueCompleteFlightAsync(
        SqliteAcarsStore store,
        FlightSessionState session)
    {
        for (int index = 0; index < 3; index++)
        {
            DateTimeOffset timestamp = Start.AddSeconds(index);
            await store.EnqueueAsync(
                CreatePositionItem(session, timestamp),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        await store.EnqueueAsync(
            new EventOutboxItem(
                OutboxItemId.New(),
                session.Id,
                Start.AddSeconds(3),
                FlightEvent.Create(session.Id, FlightEventType.OnBlock, Start.AddSeconds(3))),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.EnqueueAsync(
            new LogOutboxItem(
                OutboxItemId.New(),
                session.Id,
                Start.AddSeconds(4),
                new FlightLogEntry(
                    FlightEventId.New(),
                    session.Id,
                    Start.AddSeconds(4),
                    "Arrived at the gate")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.EnqueueAsync(
            new CompletionOutboxItem(
                OutboxItemId.New(),
                session.Id,
                Start.AddSeconds(5),
                CreateCompletedFlightReport(Start.AddSeconds(5))),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static PositionOutboxItem CreatePositionItem(
        FlightSessionState session,
        DateTimeOffset timestamp) => new(
            OutboxItemId.New(),
            session.Id,
            timestamp,
            new PositionReport(
                PositionReportId.New(),
                session.Id,
                FlightPhase.Climb,
                CreateTelemetry(timestamp)));

    private static FlightSessionState CreateSession(
        string? backendFlightId,
        FlightSessionStatus status) => new(
            FlightSessionId.New(),
            new FlightPlan("CMP", "N123CM", "CM100", "KSEA", "KLAX"),
            status == FlightSessionStatus.Completed ? FlightPhase.Completed : FlightPhase.Climb,
            Start,
            Start.AddMinutes(1))
        {
            BackendFlightId = backendFlightId,
            Status = status,
        };

    private static CompletedFlightReport CreateCompletedFlightReport(DateTimeOffset completedAtUtc) => new(
        new Distance(650),
        TimeSpan.FromHours(1.8),
        TimeSpan.FromHours(2),
        new FuelMass(2_500),
        new VerticalSpeed(-180),
        completedAtUtc);

    private static TelemetrySnapshot CreateTelemetry(DateTimeOffset collectedAtUtc) => new()
    {
        CollectedAtUtc = collectedAtUtc,
        Position = new GeoPosition(47.2, -122.0),
        AltitudeMsl = new Altitude(12_000),
        AltitudeAgl = new Altitude(10_000),
        IndicatedAirspeed = new Speed(250),
        GroundSpeed = new Speed(310),
        VerticalSpeed = new VerticalSpeed(1_200),
        OnGround = false,
    };

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value;
    }

    private sealed class FakeBackend : IFlightOperationsBackend
    {
        public string BackendId => "fake";

        public int RemainingPositionFailures { get; set; }

        public int PositionAttemptCount { get; private set; }

        public int AcceptedPositionCount { get; private set; }

        public int AcceptedEventCount { get; private set; }

        public int AcceptedLogCount { get; private set; }

        public int BackendCallCount { get; private set; }

        public List<string> ObservedBackendFlightIds { get; } = [];

        public List<CompletedFlightReport> FiledReports { get; } = [];

        public List<string> CancelledBackendFlightIds { get; } = [];

        public ValueTask<BackendConnectionResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BackendConnectionResult(true, "test", null));

        public ValueTask<BackendPilot> GetCurrentPilotAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BackendPilot("pilot", "CMP001", "Test Pilot"));

        public ValueTask<IReadOnlyList<BackendFlight>> GetAvailableFlightsAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<BackendFlight>>([]);

        public ValueTask<BackendPrefileResult> PrefileAsync(
            FlightPlan flightPlan,
            string requestMarker,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BackendPrefileResult("backend-prefiled", Start));

        public ValueTask<BackendPrefileResult?> FindPrefiledFlightAsync(
            FlightPlan flightPlan,
            string requestMarker,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<BackendPrefileResult?>(null);

        public ValueTask SendPositionsAsync(
            string backendFlightId,
            IReadOnlyList<PositionReport> positions,
            CancellationToken cancellationToken)
        {
            BackendCallCount++;
            PositionAttemptCount++;
            if (RemainingPositionFailures > 0)
            {
                RemainingPositionFailures--;
                throw new HttpRequestException("Temporary backend outage.");
            }

            ObservedBackendFlightIds.Add(backendFlightId);
            AcceptedPositionCount += positions.Count;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendEventsAsync(
            string backendFlightId,
            IReadOnlyList<FlightEvent> events,
            CancellationToken cancellationToken)
        {
            RecordCall(backendFlightId);
            AcceptedEventCount += events.Count;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendLogsAsync(
            string backendFlightId,
            IReadOnlyList<FlightLogEntry> logs,
            CancellationToken cancellationToken)
        {
            RecordCall(backendFlightId);
            AcceptedLogCount += logs.Count;
            return ValueTask.CompletedTask;
        }

        public ValueTask FileFlightAsync(
            string backendFlightId,
            CompletedFlightReport report,
            CancellationToken cancellationToken)
        {
            RecordCall(backendFlightId);
            FiledReports.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask CancelFlightAsync(
            string backendFlightId,
            CancellationToken cancellationToken)
        {
            RecordCall(backendFlightId);
            CancelledBackendFlightIds.Add(backendFlightId);
            return ValueTask.CompletedTask;
        }

        private void RecordCall(string backendFlightId)
        {
            BackendCallCount++;
            ObservedBackendFlightIds.Add(backendFlightId);
        }
    }
}

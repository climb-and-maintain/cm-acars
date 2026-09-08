using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Tracking;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Infrastructure.Persistence;

namespace ClimbAndMaintain.Acars.IntegrationTests.Application;

public sealed class FlightStartCoordinatorTests
{
    private const string RequestMarker = "CM ACARS abcdefghijklmnop";
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LostPrefileResponseIsReconciledAfterRestartWithoutSecondPost()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        ManualTimeProvider timeProvider = new(Start);
        FakeBackend backend = new() { CommitThenLoseResponse = true };
        BackendFlight flight = CreateBackendFlight();
        FlightSessionId sessionId;

        using (SqliteAcarsStore firstStore = new(databasePath))
        using (FlightTrackingCoordinator firstTracking = new(firstStore, timeProvider: timeProvider))
        using (FlightStartCoordinator firstStart = new(firstTracking, () => RequestMarker))
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => firstStart
                    .StartAsync(flight, backend, TestContext.Current.CancellationToken)
                    .AsTask()).ConfigureAwait(true);

            Assert.Contains("preserved", exception.Message, StringComparison.OrdinalIgnoreCase);
            FlightSessionState pending = Assert.IsType<FlightSessionState>(firstTracking.CurrentSession);
            sessionId = pending.Id;
            Assert.Equal(FlightSessionStatus.Starting, pending.Status);
            Assert.Equal(FlightStartIntentState.ReconciliationRequired, pending.StartIntent?.State);
            Assert.Equal(RequestMarker, pending.StartIntent?.RequestMarker);
            Assert.Equal(1, backend.PrefileCalls);
        }

        timeProvider.SetUtcNow(Start.AddMinutes(1));
        using SqliteAcarsStore reopenedStore = new(databasePath);
        using FlightTrackingCoordinator restoredTracking = new(reopenedStore, timeProvider: timeProvider);
        FlightSessionState? restored = await restoredTracking
            .RestoreAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(FlightSessionStatus.Starting, restored?.Status);
        using FlightStartCoordinator resumedStart = new(restoredTracking, () => "unused marker");

        FlightSessionState active = await resumedStart
            .ResumePendingStartAsync(backend, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(sessionId, active.Id);
        Assert.Equal(FlightSessionStatus.Active, active.Status);
        Assert.Equal("pirep-42", active.BackendFlightId);
        Assert.Equal(FlightStartIntentState.Completed, active.StartIntent?.State);
        Assert.Equal(RequestMarker, active.StartIntent?.RequestMarker);
        Assert.Equal(Start.AddMinutes(1), active.StartedAtUtc);
        Assert.Equal(1, backend.PrefileCalls);
        Assert.Equal(2, backend.FindCalls);
        FlightSessionState? persisted = await reopenedStore
            .GetActiveAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(FlightSessionStatus.Active, persisted?.Status);
        Assert.Equal("pirep-42", persisted?.BackendFlightId);
    }

    [Fact]
    public async Task AmbiguousPrefileWithoutMatchBlocksDuplicateAndDifferentStart()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        ManualTimeProvider timeProvider = new(Start);
        FakeBackend backend = new() { LoseResponseWithoutCommit = true };
        BackendFlight flight = CreateBackendFlight();

        using (SqliteAcarsStore firstStore = new(databasePath))
        using (FlightTrackingCoordinator firstTracking = new(firstStore, timeProvider: timeProvider))
        using (FlightStartCoordinator firstStart = new(firstTracking, () => RequestMarker))
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => firstStart
                .StartAsync(flight, backend, TestContext.Current.CancellationToken)
                .AsTask()).ConfigureAwait(true);
        }

        using SqliteAcarsStore reopenedStore = new(databasePath);
        using FlightTrackingCoordinator restoredTracking = new(reopenedStore, timeProvider: timeProvider);
        _ = await restoredTracking.RestoreAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using FlightStartCoordinator resumedStart = new(restoredTracking, () => "unused marker");

        InvalidOperationException repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => resumedStart
            .StartAsync(flight, backend, TestContext.Current.CancellationToken)
            .AsTask()).ConfigureAwait(true);
        Assert.Contains("No duplicate prefile was sent", repeated.Message, StringComparison.Ordinal);
        Assert.Equal(1, backend.PrefileCalls);

        BackendFlight differentFlight = new(
            "flight-2",
            new FlightPlan("CMP", "N456CM", "200", "KLAX", "KSFO"),
            null);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resumedStart
            .StartAsync(differentFlight, backend, TestContext.Current.CancellationToken)
            .AsTask()).ConfigureAwait(true);
        Assert.Equal(1, backend.PrefileCalls);
    }

    [Fact]
    public async Task PreparedIntentRecoveredBeforeDispatchCanSendExactlyOnce()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        ManualTimeProvider timeProvider = new(Start);
        BackendFlight flight = CreateBackendFlight();

        using (SqliteAcarsStore firstStore = new(databasePath))
        using (FlightTrackingCoordinator firstTracking = new(firstStore, timeProvider: timeProvider))
        {
            FlightSessionState prepared = await firstTracking
                .PrepareStartAsync(
                    flight.FlightPlan,
                    RequestMarker,
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            Assert.Equal(FlightStartIntentState.Prepared, prepared.StartIntent?.State);
        }

        FakeBackend backend = new();
        timeProvider.SetUtcNow(Start.AddSeconds(30));
        using SqliteAcarsStore reopenedStore = new(databasePath);
        using FlightTrackingCoordinator restoredTracking = new(reopenedStore, timeProvider: timeProvider);
        using FlightStartCoordinator resumedStart = new(restoredTracking, () => "unused marker");

        FlightSessionState active = await resumedStart
            .ResumePendingStartAsync(backend, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(FlightSessionStatus.Active, active.Status);
        Assert.Equal(1, backend.PrefileCalls);
        Assert.Equal(RequestMarker, Assert.Single(backend.PrefileMarkers));
    }

    [Fact]
    public async Task DiscardingPendingStartDoesNotQueueUnknownRemoteCancellation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        using FlightTrackingCoordinator tracking = new(store, timeProvider: new ManualTimeProvider(Start));
        _ = await tracking
            .PrepareStartAsync(
                CreateBackendFlight().FlightPlan,
                RequestMarker,
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        FlightSessionState cancelled = await tracking
            .CancelAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(FlightSessionStatus.Cancelled, cancelled.Status);
        Assert.Equal(FlightStartIntentState.Abandoned, cancelled.StartIntent?.State);
        Assert.Equal(0, (await store
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).Total);
        Assert.Null(await store.GetActiveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private static BackendFlight CreateBackendFlight() => new(
        "flight-1",
        new FlightPlan("CMP", "N123CM", "100", "KSEA", "KLAX")
        {
            SourceFlightId = "flight-1",
        },
        null);

    private sealed class FakeBackend : IFlightOperationsBackend
    {
        public string BackendId => "fake";

        public bool CommitThenLoseResponse { get; init; }

        public bool LoseResponseWithoutCommit { get; init; }

        public int PrefileCalls { get; private set; }

        public int FindCalls { get; private set; }

        public List<string> PrefileMarkers { get; } = [];

        private BackendPrefileResult? committedPrefile;

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
            CancellationToken cancellationToken)
        {
            PrefileCalls++;
            PrefileMarkers.Add(requestMarker);
            BackendPrefileResult result = new("pirep-42", Start.AddSeconds(1));
            if (CommitThenLoseResponse)
            {
                committedPrefile = result;
                throw new HttpRequestException("The response was lost after phpVMS committed the PIREP.");
            }

            if (LoseResponseWithoutCommit)
            {
                throw new HttpRequestException("The request outcome is unknown.");
            }

            committedPrefile = result;
            return ValueTask.FromResult(result);
        }

        public ValueTask<BackendPrefileResult?> FindPrefiledFlightAsync(
            FlightPlan flightPlan,
            string requestMarker,
            CancellationToken cancellationToken)
        {
            FindCalls++;
            return ValueTask.FromResult(committedPrefile);
        }

        public ValueTask SendPositionsAsync(
            string backendFlightId,
            IReadOnlyList<PositionReport> positions,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask SendEventsAsync(
            string backendFlightId,
            IReadOnlyList<FlightEvent> events,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask SendLogsAsync(
            string backendFlightId,
            IReadOnlyList<FlightLogEntry> logs,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask FileFlightAsync(
            string backendFlightId,
            CompletedFlightReport report,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask CancelFlightAsync(
            string backendFlightId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value;
    }
}

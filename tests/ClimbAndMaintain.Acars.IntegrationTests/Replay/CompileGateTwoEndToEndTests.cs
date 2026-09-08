using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Synchronization;
using ClimbAndMaintain.Acars.Application.Tracking;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.Infrastructure.Persistence;
using ClimbAndMaintain.Acars.Infrastructure.Replay;

namespace ClimbAndMaintain.Acars.IntegrationTests.Replay;

public sealed class CompileGateTwoEndToEndTests
{
    private static readonly DateTimeOffset RecordingStart =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly FlightPhase[] ExpectedProgression =
    [
        FlightPhase.Boarding,
        FlightPhase.Pushback,
        FlightPhase.TaxiOut,
        FlightPhase.Takeoff,
        FlightPhase.Climb,
        FlightPhase.Cruise,
        FlightPhase.Descent,
        FlightPhase.Approach,
        FlightPhase.Landing,
        FlightPhase.TaxiIn,
        FlightPhase.OnBlock,
    ];

    [Fact]
    public async Task CompleteRecordedFlightSurvivesOutageAndRestartThenSynchronizesExactlyOnce()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "gate-two.db");
        ManualTimeProvider trackingClock = new(RecordingStart);
        FlightSessionId sessionId;
        CompletedFlightReport completionReport;
        OutboxCounts queuedBeforeDelivery;

        await using (RecordedTelemetryProvider provider = new())
        {
            await using FileStream recording = File.OpenRead(FixturePath("normal-flight.jsonl"));
            await provider.LoadJsonLinesAsync(recording, cancellationToken).ConfigureAwait(true);
            await provider.SetPlaybackRateAsync(
                RecordedTelemetryProvider.InstantPlaybackRate,
                cancellationToken).ConfigureAwait(true);
            SimulatorConnectionResult connection = await provider
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(true);
            Assert.True(connection.Succeeded, connection.Message);
            SimulatorIdentity replaySimulator = Assert.IsType<SimulatorIdentity>(connection.Simulator);

            using SqliteAcarsStore store = new(databasePath);
            using FlightTrackingCoordinator coordinator = new(
                store,
                new FlightTrackingOptions { PositionReportInterval = TimeSpan.FromSeconds(1) },
                ImmediatePhaseOptions(),
                trackingClock);
            FlightSessionState session = await coordinator
                .StartAsync(CreateFlightPlan(), "phpvms-pirep-100", cancellationToken)
                .ConfigureAwait(true);
            sessionId = session.Id;

            List<FlightPhase> phases = [];
            int frameIndex = 0;
            DateTimeOffset lastRecordedAtUtc = RecordingStart;
            await foreach (TelemetrySnapshot recordedSnapshot in provider
                               .ReadTelemetryAsync(cancellationToken)
                               .ConfigureAwait(true))
            {
                DateTimeOffset collectedAtUtc = recordedSnapshot.CollectedAtUtc;
                lastRecordedAtUtc = collectedAtUtc;
                trackingClock.SetUtcNow(collectedAtUtc);
                TelemetrySnapshot playbackSnapshot = recordedSnapshot with
                {
                    CollectedAtUtc = collectedAtUtc,
                    SimulatorTime = recordedSnapshot.CollectedAtUtc,
                    Simulator = replaySimulator,
                };
                session = await coordinator
                    .ProcessTelemetryAsync(playbackSnapshot, cancellationToken)
                    .ConfigureAwait(true);
                phases.Add(session.Phase);
                frameIndex++;
            }

            Assert.Equal(ExpectedProgression, phases);
            Assert.Equal(ExpectedProgression.Length, frameIndex);
            Assert.Equal(FlightPhase.OnBlock, session.Phase);
            Assert.Equal(FlightSessionStatus.Active, session.Status);

            trackingClock.SetUtcNow(lastRecordedAtUtc.AddSeconds(1));
            completionReport = CreateCompletionReport(session, trackingClock.GetUtcNow());
            FlightSessionState completed = await coordinator
                .CompleteAsync(completionReport, cancellationToken)
                .ConfigureAwait(true);

            Assert.Equal(FlightPhase.Completed, completed.Phase);
            Assert.Equal(FlightSessionStatus.Completed, completed.Status);
            Assert.Null(await store.GetActiveAsync(cancellationToken).ConfigureAwait(true));

            queuedBeforeDelivery = await store.GetCountsAsync(cancellationToken).ConfigureAwait(true);
            Assert.True(queuedBeforeDelivery.Positions >= ExpectedProgression.Length);
            Assert.True(queuedBeforeDelivery.Events >= ExpectedProgression.Length + 1);
            Assert.True(queuedBeforeDelivery.Logs > 0);
            Assert.Equal(1, queuedBeforeDelivery.Operations);
        }

        GateTwoBackend backend = new() { RemainingPositionFailures = 1 };
        ManualTimeProvider synchronizationClock = new(RecordingStart.AddMinutes(10));
        OutboxSynchronizationOptions synchronizationOptions = new()
        {
            BatchSize = 100,
            MaximumItemsPerRun = 500,
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromSeconds(2),
        };

        using (SqliteAcarsStore failedAttemptStore = new(databasePath))
        using (OutboxSynchronizer firstProcess = new(
                   failedAttemptStore,
                   failedAttemptStore,
                   backend,
                   synchronizationOptions,
                   synchronizationClock))
        {
            OutboxSynchronizationResult failed = await firstProcess
                .SynchronizeAsync(cancellationToken)
                .ConfigureAwait(true);

            Assert.Equal(OutboxSynchronizationStatus.Failed, failed.Status);
            Assert.True(failed.FailedCount > 0);
            Assert.Equal(queuedBeforeDelivery, failed.Remaining);
            Assert.Empty(backend.Positions);
            Assert.Empty(backend.Events);
            Assert.Empty(backend.Logs);
            Assert.Empty(backend.FiledReports);
        }

        synchronizationClock.SetUtcNow(synchronizationClock.GetUtcNow().AddSeconds(2));
        using SqliteAcarsStore restartedStore = new(databasePath);
        FlightSessionState persisted = Assert.IsType<FlightSessionState>(
            await restartedStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(true));
        Assert.Equal(FlightSessionStatus.Completed, persisted.Status);
        using OutboxSynchronizer restartedProcess = new(
            restartedStore,
            restartedStore,
            backend,
            synchronizationOptions,
            synchronizationClock);

        OutboxSynchronizationResult synchronized = await restartedProcess
            .SynchronizeAsync(cancellationToken)
            .ConfigureAwait(true);
        int deliveredPositionCount = backend.Positions.Count;
        int deliveredEventCount = backend.Events.Count;
        int deliveredLogCount = backend.Logs.Count;
        int filedReportCount = backend.FiledReports.Count;
        OutboxSynchronizationResult repeated = await restartedProcess
            .SynchronizeAsync(cancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(OutboxSynchronizationStatus.Synchronized, synchronized.Status);
        Assert.Equal(queuedBeforeDelivery.Total, synchronized.DeliveredCount);
        Assert.Equal(0, synchronized.Remaining.Total);
        Assert.Equal(OutboxSynchronizationStatus.NothingPending, repeated.Status);
        Assert.Equal(deliveredPositionCount, backend.Positions.Count);
        Assert.Equal(deliveredEventCount, backend.Events.Count);
        Assert.Equal(deliveredLogCount, backend.Logs.Count);
        Assert.Equal(filedReportCount, backend.FiledReports.Count);

        Assert.Equal(ExpectedProgression.Length, backend.Positions.Count);
        Assert.All(backend.Positions, position => Assert.Equal(sessionId, position.SessionId));
        Assert.All(
            ExpectedProgression,
            phase => Assert.Contains(backend.Events, flightEvent => EventRepresentsPhase(flightEvent, phase)));
        Assert.Contains(backend.Events, flightEvent => flightEvent.Type == FlightEventType.Completed);
        Assert.Contains(backend.Logs, log => log.Message.Contains("Takeoff", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(backend.Logs, log => log.Message.Contains("OnBlock", StringComparison.OrdinalIgnoreCase));
        CompletedFlightReport filed = Assert.Single(backend.FiledReports);
        Assert.Equal(completionReport.Distance, filed.Distance);
        Assert.Equal(completionReport.FlightTime, filed.FlightTime);
        Assert.NotNull(filed.BlockTime);
        Assert.NotNull(filed.FuelUsed);
        Assert.NotNull(filed.LandingRate);
        Assert.All(backend.ObservedFlightIds, value => Assert.Equal("phpvms-pirep-100", value));
        Assert.Equal(2, backend.PositionAttempts);
    }

    private static bool EventRepresentsPhase(FlightEvent flightEvent, FlightPhase phase) =>
        flightEvent.Type.ToString().Equals(phase.ToString(), StringComparison.OrdinalIgnoreCase);

    private static CompletedFlightReport CreateCompletionReport(
        FlightSessionState session,
        DateTimeOffset completedAtUtc)
    {
        Assert.True(session.Metrics.FlownDistance.NauticalMiles > 0);
        Assert.True(session.Metrics.AirborneFlightTime > TimeSpan.Zero);
        Assert.True(session.Metrics.ActiveTrackingTime > TimeSpan.Zero);
        Assert.NotNull(session.Metrics.FuelUsed);
        Assert.NotNull(session.Metrics.LandingRate);
        return new(
            session.Metrics.FlownDistance,
            session.Metrics.AirborneFlightTime,
            null,
            null,
            null,
            completedAtUtc);
    }

    private static FlightPlan CreateFlightPlan() => new(
        "CMP",
        "N123CM",
        "CM100",
        "KSEA",
        "KPDT")
    {
        Route = "SEA DCT PDT",
        PlannedDistance = new Distance(190),
        PlannedDuration = TimeSpan.FromMinutes(50),
        PlannedFuel = new FuelMass(6_000),
    };

    private static FlightPhaseEngineOptions ImmediatePhaseOptions() => new()
    {
        GroundStateConfirmation = TimeSpan.Zero,
        TaxiConfirmation = TimeSpan.Zero,
        TakeoffRollConfirmation = TimeSpan.Zero,
        AirborneConfirmation = TimeSpan.Zero,
        CruiseConfirmation = TimeSpan.Zero,
        FlightPathConfirmation = TimeSpan.Zero,
        TouchdownConfirmation = TimeSpan.Zero,
        OnBlockConfirmation = TimeSpan.Zero,
    };

    private static string FixturePath(string fileName) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "replay", fileName);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value;
    }

    private sealed class GateTwoBackend : IFlightOperationsBackend
    {
        public string BackendId => "gate-two-fake";

        public int RemainingPositionFailures { get; set; }

        public int PositionAttempts { get; private set; }

        public List<string> ObservedFlightIds { get; } = [];

        public List<PositionReport> Positions { get; } = [];

        public List<FlightEvent> Events { get; } = [];

        public List<FlightLogEntry> Logs { get; } = [];

        public List<CompletedFlightReport> FiledReports { get; } = [];

        public ValueTask<BackendConnectionResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BackendConnectionResult(true, "gate-two", null));

        public ValueTask<BackendPilot> GetCurrentPilotAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BackendPilot("pilot-1", "CMP001", "Gate Two Pilot"));

        public ValueTask<IReadOnlyList<BackendFlight>> GetAvailableFlightsAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<BackendFlight>>([]);

        public ValueTask<BackendPrefileResult> PrefileAsync(
            FlightPlan flightPlan,
            string requestMarker,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BackendPrefileResult("phpvms-pirep-100", RecordingStart));

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
            PositionAttempts++;
            if (RemainingPositionFailures > 0)
            {
                RemainingPositionFailures--;
                throw new HttpRequestException("Simulated Gate 2 backend outage.");
            }

            RecordFlightId(backendFlightId);
            Positions.AddRange(positions);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendEventsAsync(
            string backendFlightId,
            IReadOnlyList<FlightEvent> events,
            CancellationToken cancellationToken)
        {
            RecordFlightId(backendFlightId);
            Events.AddRange(events);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendLogsAsync(
            string backendFlightId,
            IReadOnlyList<FlightLogEntry> logs,
            CancellationToken cancellationToken)
        {
            RecordFlightId(backendFlightId);
            Logs.AddRange(logs);
            return ValueTask.CompletedTask;
        }

        public ValueTask FileFlightAsync(
            string backendFlightId,
            CompletedFlightReport report,
            CancellationToken cancellationToken)
        {
            RecordFlightId(backendFlightId);
            FiledReports.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask CancelFlightAsync(string backendFlightId, CancellationToken cancellationToken)
        {
            RecordFlightId(backendFlightId);
            return ValueTask.CompletedTask;
        }

        private void RecordFlightId(string backendFlightId) => ObservedFlightIds.Add(backendFlightId);
    }
}

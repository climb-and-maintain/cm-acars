using System.Collections.Immutable;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Tracking;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.Infrastructure.Persistence;

namespace ClimbAndMaintain.Acars.IntegrationTests.Application;

public sealed class FlightTrackingCoordinatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TelemetryProducesPhaseEventsAndThrottledPositions()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        ManualTimeProvider timeProvider = new(Start);
        using FlightTrackingCoordinator coordinator = new(
            store,
            new FlightTrackingOptions { PositionReportInterval = TimeSpan.FromSeconds(10) },
            CreateImmediatePhaseOptions(),
            timeProvider);

        FlightSessionState started = await coordinator
            .StartAsync(CreateFlightPlan(), "backend-42", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightSessionState boarding = await coordinator
            .ProcessTelemetryAsync(CreateGroundTelemetry(Start, 0, false), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightSessionState unchanged = await coordinator
            .ProcessTelemetryAsync(CreateGroundTelemetry(Start.AddSeconds(1), 0, false), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightSessionState taxiing = await coordinator
            .ProcessTelemetryAsync(CreateGroundTelemetry(Start.AddSeconds(2), 8, true), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(started.Id, boarding.Id);
        Assert.Equal(FlightPhase.Boarding, boarding.Phase);
        Assert.Equal(FlightPhase.Boarding, unchanged.Phase);
        Assert.Equal(FlightPhase.TaxiOut, taxiing.Phase);
        Assert.Equal(new OutboxCounts(2, 2, 2), await store
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true));

        IReadOnlyList<PendingOutboxItem> pending = await store
            .GetPendingAsync(10, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(2, pending.Count(item => item.Item is PositionOutboxItem));
        Assert.Equal(2, pending.Count(item => item.Item is EventOutboxItem));
        Assert.Equal(2, pending.Count(item => item.Item is LogOutboxItem));
        Assert.Equal(6, pending.Select(item => item.Item.Id).Distinct().Count());
        Assert.Equal(
            2,
            pending.Select(item => item.Item)
                .OfType<LogOutboxItem>()
                .Select(item => item.LogEntry.Id)
                .Distinct()
                .Count());
    }

    [Fact]
    public async Task PauseAndResumeSurviveRestoreAndRejectTelemetryWhilePaused()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        ManualTimeProvider timeProvider = new(Start);
        using (FlightTrackingCoordinator first = new(store, timeProvider: timeProvider))
        {
            await first
                .StartAsync(CreateFlightPlan(), "backend-42", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            timeProvider.SetUtcNow(Start.AddSeconds(1));
            FlightSessionState paused = await first
                .PauseAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            Assert.Equal(FlightSessionStatus.Paused, paused.Status);
        }

        using FlightTrackingCoordinator restoredCoordinator = new(store, timeProvider: timeProvider);
        FlightSessionState? restored = await restoredCoordinator
            .RestoreAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(FlightSessionStatus.Paused, restored?.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => restoredCoordinator
                .ProcessTelemetryAsync(
                    CreateGroundTelemetry(Start.AddSeconds(2), 0, false),
                    TestContext.Current.CancellationToken)
                .AsTask()).ConfigureAwait(true);

        timeProvider.SetUtcNow(Start.AddSeconds(2));
        FlightSessionState resumed = await restoredCoordinator
            .ResumeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightSessionState repeatedResume = await restoredCoordinator
            .ResumeAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(FlightSessionStatus.Active, resumed.Status);
        Assert.Same(resumed, repeatedResume);
        Assert.Equal(new OutboxCounts(0, 2, 2), await store
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
    }

    [Fact]
    public async Task CompletionIsDurableIdempotentAndTerminal()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightSessionState onBlock = new(
            FlightSessionId.New(),
            CreateFlightPlan(),
            FlightPhase.OnBlock,
            Start,
            Start.AddHours(2))
        {
            BackendFlightId = "backend-42",
            Status = FlightSessionStatus.Active,
        };
        await store.SaveAsync(onBlock, TestContext.Current.CancellationToken).ConfigureAwait(true);
        ManualTimeProvider timeProvider = new(Start.AddHours(2));
        using FlightTrackingCoordinator coordinator = new(store, timeProvider: timeProvider);
        await coordinator.RestoreAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        CompletedFlightReport report = new(
            new Distance(650),
            TimeSpan.FromHours(1.8),
            TimeSpan.FromHours(2),
            new FuelMass(2_500),
            new VerticalSpeed(-180),
            Start.AddHours(2).AddSeconds(1));

        FlightSessionState completed = await coordinator
            .CompleteAsync(report, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightSessionState repeatedCompletion = await coordinator
            .CompleteAsync(report, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(FlightSessionStatus.Completed, completed.Status);
        Assert.Equal(FlightPhase.Completed, completed.Phase);
        Assert.Same(completed, repeatedCompletion);
        Assert.Equal(new OutboxCounts(0, 1, 1, 1), await store
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
        CompletionOutboxItem queuedCompletion = Assert.Single((await store
                .GetPendingAsync(10, TestContext.Current.CancellationToken)
                .ConfigureAwait(true))
            .Select(item => item.Item)
            .OfType<CompletionOutboxItem>());
        Assert.Equal(onBlock.FlightPlan, queuedCompletion.Report.FilingPlan);
        Assert.Equal(onBlock.StartedAtUtc, queuedCompletion.Report.TrackingStartedAtUtc);
        FlightSessionState? persisted = await store
            .GetAsync(onBlock.Id, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(FlightSessionStatus.Completed, persisted?.Status);
        Assert.Null(await store.GetActiveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.CancelAsync(TestContext.Current.CancellationToken).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task CancellationIsDurableIdempotentAndRejectsLaterTransitions()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        ManualTimeProvider timeProvider = new(Start);
        using FlightTrackingCoordinator coordinator = new(store, timeProvider: timeProvider);
        await coordinator
            .StartAsync(CreateFlightPlan(), "backend-42", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        timeProvider.SetUtcNow(Start.AddSeconds(1));

        FlightSessionState cancelled = await coordinator
            .CancelAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightSessionState repeatedCancellation = await coordinator
            .CancelAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(FlightSessionStatus.Cancelled, cancelled.Status);
        Assert.Same(cancelled, repeatedCancellation);
        Assert.Equal(new OutboxCounts(0, 1, 1, 1), await store
            .GetCountsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ResumeAsync(TestContext.Current.CancellationToken).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator
                .CompleteAsync(CreateCompletionReport(Start.AddSeconds(2)), TestContext.Current.CancellationToken)
                .AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task StartAndCompletionEnforceLifecyclePrerequisites()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        ManualTimeProvider timeProvider = new(Start);
        using FlightTrackingCoordinator first = new(store, timeProvider: timeProvider);
        await first
            .StartAsync(CreateFlightPlan(), "backend-42", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        using FlightTrackingCoordinator second = new(store, timeProvider: timeProvider);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => second
                .StartAsync(CreateFlightPlan(), "backend-43", TestContext.Current.CancellationToken)
                .AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => first
                .CompleteAsync(CreateCompletionReport(Start.AddSeconds(1)), TestContext.Current.CancellationToken)
                .AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task SimulatorPauseAndSlewEdgesProduceDurableEventsAndLogsOnce()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        using FlightTrackingCoordinator coordinator = new(
            store,
            phaseOptions: CreateImmediatePhaseOptions(),
            timeProvider: new ManualTimeProvider(Start));
        await coordinator
            .StartAsync(CreateFlightPlan(), "backend-42", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(1), 0, false) with { IsPaused = true },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(2), 0, false) with { IsPaused = true },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(3), 0, false),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(4), 60, true) with { IsSlewActive = true },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(5), 60, true) with { IsSlewActive = true },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<PendingOutboxItem> pending = await store
            .GetPendingAsync(100, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightEvent[] simulatorEvents = pending
            .Select(item => item.Item)
            .OfType<EventOutboxItem>()
            .Select(item => item.Event)
            .Where(flightEvent => flightEvent.Type is FlightEventType.Paused
                or FlightEventType.Resumed
                or FlightEventType.SlewDetected)
            .ToArray();

        Assert.Equal(
            [FlightEventType.Paused, FlightEventType.Resumed, FlightEventType.SlewDetected],
            simulatorEvents.Select(flightEvent => flightEvent.Type));
        Assert.Equal(3, pending
            .Select(item => item.Item)
            .OfType<LogOutboxItem>()
            .Count(item => item.LogEntry.Message.StartsWith("Simulator ", StringComparison.Ordinal)));
        Assert.Equal(FlightPhase.Boarding, coordinator.CurrentSession?.Phase);
    }

    [Fact]
    public async Task MetricsAccumulateAcrossPersistenceAndRestart()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string databasePath = System.IO.Path.Combine(temporaryDirectory.Path, "acars.db");
        FlightSessionId sessionId;

        using (SqliteAcarsStore firstStore = new(databasePath))
        using (FlightTrackingCoordinator first = new(
                   firstStore,
                   phaseOptions: CreateImmediatePhaseOptions(),
                   timeProvider: new ManualTimeProvider(Start)))
        {
            FlightSessionState started = await first
                .StartAsync(CreateFlightPlan(), "backend-42", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            sessionId = started.Id;
            _ = await first.ProcessTelemetryAsync(
                CreateAirborneTelemetry(Start, new GeoPosition(0, 0), 1_000, -100),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            FlightSessionState beforeRestart = await first.ProcessTelemetryAsync(
                CreateAirborneTelemetry(Start.AddSeconds(10), new GeoPosition(0, 1), 900, -200),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.InRange(beforeRestart.Metrics.FlownDistance.NauticalMiles, 60, 60.1);
            Assert.Equal(TimeSpan.FromSeconds(10), beforeRestart.Metrics.ActiveTrackingTime);
            Assert.Equal(TimeSpan.FromSeconds(10), beforeRestart.Metrics.AirborneFlightTime);
            Assert.Equal(100, beforeRestart.Metrics.FuelUsed?.Kilograms);
        }

        using SqliteAcarsStore reopenedStore = new(databasePath);
        using FlightTrackingCoordinator restored = new(
            reopenedStore,
            phaseOptions: CreateImmediatePhaseOptions(),
            timeProvider: new ManualTimeProvider(Start.AddSeconds(20)));
        FlightSessionState? recovered = await restored
            .RestoreAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(sessionId, recovered?.Id);

        FlightSessionState continued = await restored.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start.AddSeconds(20), new GeoPosition(0, 2), 850, -300),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.InRange(continued.Metrics.FlownDistance.NauticalMiles, 120, 120.2);
        Assert.Equal(TimeSpan.FromSeconds(20), continued.Metrics.ActiveTrackingTime);
        Assert.Equal(TimeSpan.FromSeconds(20), continued.Metrics.AirborneFlightTime);
        Assert.Equal(150, continued.Metrics.FuelUsed?.Kilograms);

        FlightSessionState? persisted = await reopenedStore
            .GetAsync(sessionId, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(continued.Metrics, persisted?.Metrics);
    }

    [Fact]
    public async Task ExplicitAndSimulatorPauseGapsAreExcludedFromMetrics()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        ManualTimeProvider timeProvider = new(Start);
        using FlightTrackingCoordinator coordinator = new(
            store,
            phaseOptions: CreateImmediatePhaseOptions(),
            timeProvider: timeProvider);
        _ = await coordinator.StartAsync(
            CreateFlightPlan(),
            "backend-42",
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start, new GeoPosition(0, 0), 1_000, -100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        timeProvider.SetUtcNow(Start.AddSeconds(10));
        _ = await coordinator.PauseAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        timeProvider.SetUtcNow(Start.AddSeconds(40));
        _ = await coordinator.ResumeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start.AddSeconds(50), new GeoPosition(0, 10), 900, -100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start.AddSeconds(60), new GeoPosition(0, 11), 890, -100) with
            {
                IsPaused = true,
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start.AddSeconds(90), new GeoPosition(0, 12), 880, -100) with
            {
                IsPaused = true,
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start.AddSeconds(100), new GeoPosition(0, 13), 870, -100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        FlightSessionState updated = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start.AddSeconds(110), new GeoPosition(0, 14), 860, -100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(TimeSpan.FromSeconds(40), updated.Metrics.ActiveTrackingTime);
        Assert.Equal(TimeSpan.FromSeconds(40), updated.Metrics.AirborneFlightTime);
        Assert.InRange(updated.Metrics.FlownDistance.NauticalMiles, 60, 60.1);
        Assert.Equal(20, updated.Metrics.FuelUsed?.Kilograms);
    }

    [Fact]
    public async Task LandingRateAndCumulativeFuelSurviveARefuelIncrease()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        using FlightTrackingCoordinator coordinator = new(
            store,
            phaseOptions: CreateImmediatePhaseOptions(),
            timeProvider: new ManualTimeProvider(Start));
        _ = await coordinator.StartAsync(
            CreateFlightPlan(),
            "backend-42",
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start, new GeoPosition(47, -122), 1_000, -100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateAirborneTelemetry(Start.AddSeconds(10), new GeoPosition(47, -121.9), 900, -620),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        FlightSessionState landed = await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(20), 0, false) with
            {
                Position = new GeoPosition(47, -121.8),
                FuelRemaining = new FuelMass(1_100),
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        FlightSessionState afterTaxi = await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(30), 8, true) with
            {
                Position = new GeoPosition(47, -121.7),
                FuelRemaining = new FuelMass(1_050),
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(FlightPhase.Landing, landed.Phase);
        Assert.Equal(-620, landed.Metrics.LandingRate?.FeetPerMinute);
        Assert.Equal(100, landed.Metrics.FuelUsed?.Kilograms);
        Assert.Equal(-620, afterTaxi.Metrics.LandingRate?.FeetPerMinute);
        Assert.Equal(150, afterTaxi.Metrics.FuelUsed?.Kilograms);

        FlightSessionState onBlock = await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(40), 0, false) with
            {
                Position = new GeoPosition(47, -121.7),
                FuelRemaining = new FuelMass(1_050),
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.CompleteAsync(
            new CompletedFlightReport(
                onBlock.Metrics.FlownDistance,
                onBlock.Metrics.AirborneFlightTime,
                null,
                null,
                null,
                Start.AddSeconds(41)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<PendingOutboxItem> pending = await store
            .GetPendingAsync(50, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        CompletionOutboxItem completion = Assert.Single(
            pending.Select(item => item.Item).OfType<CompletionOutboxItem>());
        Assert.Equal(150, completion.Report.FuelUsed?.Kilograms);
        Assert.Equal(-620, completion.Report.LandingRate?.FeetPerMinute);
        Assert.Equal(TimeSpan.FromSeconds(41), completion.Report.BlockTime);
        Assert.Equal(onBlock.FlightPlan, completion.Report.FilingPlan);
        Assert.Equal(onBlock.StartedAtUtc, completion.Report.TrackingStartedAtUtc);
    }

    [Fact]
    public async Task GeneratedEventsProduceAtomicDurableLogOutboxItemsWithUniqueIds()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        ManualTimeProvider timeProvider = new(Start);
        using FlightTrackingCoordinator coordinator = new(
            store,
            phaseOptions: CreateImmediatePhaseOptions(),
            timeProvider: timeProvider);
        _ = await coordinator.StartAsync(
            CreateFlightPlan(),
            "backend-42",
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start, 0, false),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        timeProvider.SetUtcNow(Start.AddSeconds(1));
        _ = await coordinator.PauseAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        timeProvider.SetUtcNow(Start.AddSeconds(2));
        _ = await coordinator.ResumeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        timeProvider.SetUtcNow(Start.AddSeconds(3));
        _ = await coordinator.CancelAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<PendingOutboxItem> pending = await store
            .GetPendingAsync(50, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        EventOutboxItem[] events = pending.Select(item => item.Item).OfType<EventOutboxItem>().ToArray();
        LogOutboxItem[] logs = pending.Select(item => item.Item).OfType<LogOutboxItem>().ToArray();

        Assert.Equal(4, events.Length);
        Assert.Equal(events.Length, logs.Length);
        Assert.All(logs, item => Assert.False(string.IsNullOrWhiteSpace(item.LogEntry.Message)));
        Assert.Equal(
            events.Length + logs.Length,
            events.Select(item => item.Event.Id)
                .Concat(logs.Select(item => item.LogEntry.Id))
                .Distinct()
                .Count());
        Assert.Equal(pending.Count, pending.Select(item => item.Item.Id).Distinct().Count());
    }

    [Fact]
    public async Task RecordedTimelineDoesNotMoveSessionClockAheadOrBlockLaterLiveTelemetry()
    {
        using TemporaryDirectory temporaryDirectory = new();
        using SqliteAcarsStore store = new(System.IO.Path.Combine(temporaryDirectory.Path, "acars.db"));
        FlightPhaseEngineOptions phaseOptions = CreateImmediatePhaseOptions() with
        {
            GroundStateConfirmation = TimeSpan.FromSeconds(5),
        };
        using FlightTrackingCoordinator coordinator = new(
            store,
            phaseOptions: phaseOptions,
            timeProvider: new ManualTimeProvider(Start));
        await coordinator
            .StartAsync(CreateFlightPlan(), "backend-42", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        SimulatorIdentity replay = new(SimulatorKind.RecordedTelemetry, "Recorded telemetry", null);
        DateTimeOffset recordingOrigin = Start.AddDays(30);
        _ = await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(1), 0, false) with
            {
                Simulator = replay,
                SimulatorTime = recordingOrigin,
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        FlightSessionState replayed = await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(2), 0, false) with
            {
                Simulator = replay,
                SimulatorTime = recordingOrigin.AddSeconds(6),
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(FlightPhase.Boarding, replayed.Phase);
        Assert.Equal(Start.AddSeconds(2), replayed.UpdatedAtUtc);

        SimulatorIdentity live = new(SimulatorKind.Msfs2024, "Microsoft Flight Simulator 2024", null);
        FlightSessionState liveUpdated = await coordinator.ProcessTelemetryAsync(
            CreateGroundTelemetry(Start.AddSeconds(3), 0, false) with { Simulator = live },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(Start.AddSeconds(3), liveUpdated.UpdatedAtUtc);
        IReadOnlyList<PendingOutboxItem> pending = await store
            .GetPendingAsync(20, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        EventOutboxItem boardingEvent = Assert.Single(
            pending.Select(item => item.Item).OfType<EventOutboxItem>());
        Assert.Equal(Start.AddSeconds(2), boardingEvent.Event.OccurredAtUtc);
    }

    private static FlightPlan CreateFlightPlan() =>
        new("CMP", "N123CM", "CM100", "KSEA", "KLAX");

    private static CompletedFlightReport CreateCompletionReport(DateTimeOffset completedAtUtc) => new(
        new Distance(650),
        TimeSpan.FromHours(1.8),
        TimeSpan.FromHours(2),
        new FuelMass(2_500),
        new VerticalSpeed(-180),
        completedAtUtc);

    private static TelemetrySnapshot CreateGroundTelemetry(
        DateTimeOffset collectedAtUtc,
        double groundSpeed,
        bool engineRunning) => new()
        {
            CollectedAtUtc = collectedAtUtc,
            Position = new GeoPosition(47.45, -122.31),
            AltitudeMsl = new Altitude(430),
            AltitudeAgl = new Altitude(0),
            IndicatedAirspeed = new Speed(groundSpeed),
            GroundSpeed = new Speed(groundSpeed),
            VerticalSpeed = new VerticalSpeed(0),
            OnGround = true,
            Engines = ImmutableArray.Create(
                new EngineTelemetry(
                    1,
                    engineRunning ? EngineOperatingState.Running : EngineOperatingState.Off)),
            Systems = new AircraftSystemsTelemetry { ParkingBrakeSet = !engineRunning },
        };

    private static TelemetrySnapshot CreateAirborneTelemetry(
        DateTimeOffset collectedAtUtc,
        GeoPosition position,
        double fuelKilograms,
        double verticalSpeed) => new()
        {
            CollectedAtUtc = collectedAtUtc,
            Position = position,
            AltitudeMsl = new Altitude(4_000),
            AltitudeAgl = new Altitude(1_500),
            IndicatedAirspeed = new Speed(160),
            GroundSpeed = new Speed(180),
            VerticalSpeed = new VerticalSpeed(verticalSpeed),
            OnGround = false,
            FuelRemaining = new FuelMass(fuelKilograms),
            Engines = ImmutableArray.Create(new EngineTelemetry(1, EngineOperatingState.Running)),
            Systems = new AircraftSystemsTelemetry { ParkingBrakeSet = false },
        };

    private static FlightPhaseEngineOptions CreateImmediatePhaseOptions() => new()
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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value;
    }
}

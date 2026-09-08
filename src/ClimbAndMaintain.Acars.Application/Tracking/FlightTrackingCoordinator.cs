using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Application.Tracking;

public sealed class FlightTrackingCoordinator : IFlightTrackingCoordinator, IDisposable
{
    private const double EarthRadiusNauticalMiles = 3440.065;

    private readonly IFlightStateStore stateStore;
    private readonly FlightTrackingOptions options;
    private readonly FlightPhaseEngineOptions phaseOptions;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly Lock stateLock = new();
    private FlightSessionState? currentSession;
    private FlightPhaseEngine? phaseEngine;
    private DateTimeOffset? lastPositionReportAtUtc;
    private bool disposed;

    public FlightTrackingCoordinator(
        IFlightStateStore stateStore,
        FlightTrackingOptions? options = null,
        FlightPhaseEngineOptions? phaseOptions = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        this.stateStore = stateStore;
        this.options = options ?? new FlightTrackingOptions();
        this.options.Validate();
        this.phaseOptions = phaseOptions ?? new FlightPhaseEngineOptions();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public FlightSessionState? CurrentSession
    {
        get
        {
            lock (stateLock)
            {
                return currentSession;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        operationLock.Dispose();
    }

    public async ValueTask<FlightSessionState?> RestoreAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState? restored = await stateStore
                .GetActiveAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (stateLock)
            {
                currentSession = restored;
                phaseEngine = restored is null
                    ? null
                    : new FlightPhaseEngine(restored.Phase, phaseOptions);
                lastPositionReportAtUtc = restored?.LastTelemetry?.CollectedAtUtc;
            }

            return restored;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> StartAsync(
        FlightPlan flightPlan,
        string? backendFlightId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flightPlan);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState? existing = CurrentSession
                ?? await stateStore.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            if (existing?.Status is FlightSessionStatus.Starting
                or FlightSessionStatus.Active
                or FlightSessionStatus.Paused)
            {
                throw new InvalidOperationException(
                    "A starting, active, or paused flight must be completed or cancelled first.");
            }

            DateTimeOffset now = GetUtcNow();
            FlightSessionState session = new(
                FlightSessionId.New(),
                flightPlan,
                FlightPhase.Ready,
                now,
                now)
            {
                BackendFlightId = NormalizeBackendFlightId(backendFlightId),
                Status = FlightSessionStatus.Active,
            };
            await stateStore.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            SetCurrent(session, new FlightPhaseEngine(FlightPhase.Ready, phaseOptions), null);
            return session;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> PrepareStartAsync(
        FlightPlan flightPlan,
        string requestMarker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flightPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestMarker);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState? existing = CurrentSession
                ?? await stateStore.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            if (existing?.Status is FlightSessionStatus.Starting
                or FlightSessionStatus.Active
                or FlightSessionStatus.Paused)
            {
                throw new InvalidOperationException(
                    "A starting, active, or paused flight must be completed or cancelled first.");
            }

            DateTimeOffset now = GetUtcNow();
            FlightSessionState session = new(
                FlightSessionId.New(),
                flightPlan,
                FlightPhase.Ready,
                now,
                now)
            {
                Status = FlightSessionStatus.Starting,
                StartIntent = new FlightStartIntent(
                    requestMarker,
                    FlightStartIntentState.Prepared,
                    now),
            };
            await stateStore.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            SetCurrent(session, new FlightPhaseEngine(FlightPhase.Ready, phaseOptions), null);
            return session;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> MarkPrefileRequestedAsync(
        CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireStartingSession();
            FlightStartIntent intent = RequireStartIntent(session);
            if (intent.State != FlightStartIntentState.Prepared)
            {
                return session;
            }

            DateTimeOffset now = GetMonotonicNow(session);
            FlightSessionState updated = CopySession(
                session,
                session.Phase,
                now,
                FlightSessionStatus.Starting,
                session.LastTelemetry,
                session.BackendFlightId) with
            {
                StartIntent = new FlightStartIntent(
                    intent.RequestMarker,
                    FlightStartIntentState.PrefileRequested,
                    intent.PreparedAtUtc,
                    now),
            };
            await stateStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            SetCurrent(updated);
            return updated;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> MarkStartReconciliationRequiredAsync(
        CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireStartingSession();
            FlightStartIntent intent = RequireStartIntent(session);
            if (intent.State == FlightStartIntentState.ReconciliationRequired)
            {
                return session;
            }

            if (intent.State != FlightStartIntentState.PrefileRequested)
            {
                throw new InvalidOperationException(
                    "Only a dispatched prefile request can require reconciliation.");
            }

            DateTimeOffset now = GetMonotonicNow(session);
            FlightSessionState updated = CopySession(
                session,
                session.Phase,
                now,
                FlightSessionStatus.Starting,
                session.LastTelemetry,
                session.BackendFlightId) with
            {
                StartIntent = new FlightStartIntent(
                    intent.RequestMarker,
                    FlightStartIntentState.ReconciliationRequired,
                    intent.PreparedAtUtc,
                    intent.LastAttemptAtUtc),
            };
            await stateStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            SetCurrent(updated);
            return updated;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> CompleteStartAsync(
        string backendFlightId,
        CancellationToken cancellationToken)
    {
        string normalizedId = NormalizeBackendFlightId(backendFlightId)
            ?? throw new ArgumentException("A backend flight ID is required.", nameof(backendFlightId));
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireStartingSession();
            FlightStartIntent intent = RequireStartIntent(session);
            DateTimeOffset now = GetMonotonicNow(session);
            FlightSessionState active = new(
                session.Id,
                session.FlightPlan,
                FlightPhase.Ready,
                now,
                now)
            {
                BackendFlightId = normalizedId,
                Status = FlightSessionStatus.Active,
                StartIntent = new FlightStartIntent(
                    intent.RequestMarker,
                    FlightStartIntentState.Completed,
                    intent.PreparedAtUtc,
                    intent.LastAttemptAtUtc),
            };
            await stateStore.SaveAsync(active, cancellationToken).ConfigureAwait(false);
            SetCurrent(active, new FlightPhaseEngine(FlightPhase.Ready, phaseOptions), null);
            return active;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> AttachBackendFlightAsync(
        string backendFlightId,
        CancellationToken cancellationToken)
    {
        string normalizedId = NormalizeBackendFlightId(backendFlightId)
            ?? throw new ArgumentException("A backend flight ID is required.", nameof(backendFlightId));
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireSession();
            if (session.BackendFlightId is not null
                && !string.Equals(session.BackendFlightId, normalizedId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The session is already attached to a different backend flight.");
            }

            DateTimeOffset now = GetMonotonicNow(session);
            FlightSessionMetrics metrics = AccountElapsedTime(session, now);
            FlightSessionState updated = CopySession(
                session,
                session.Phase,
                now,
                session.Status,
                session.LastTelemetry,
                normalizedId,
                metrics);
            await stateStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            SetCurrent(updated);
            return updated;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> ProcessTelemetryAsync(
        TelemetrySnapshot telemetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireSession();
            EnsureStatus(session, FlightSessionStatus.Active, "Telemetry can only be processed for an active flight.");
            if (telemetry.CollectedAtUtc < session.UpdatedAtUtc)
            {
                throw new ArgumentException("Telemetry cannot predate the current session state.", nameof(telemetry));
            }

            FlightPhaseEngine engine = phaseEngine
                ?? throw new InvalidOperationException("The phase engine has not been initialized.");
            if (UsesRecordedClock(session.LastTelemetry) != UsesRecordedClock(telemetry))
            {
                engine = new FlightPhaseEngine(session.Phase, phaseOptions);
                phaseEngine = engine;
            }

            TelemetrySnapshot phaseObservation = CreatePhaseObservation(telemetry);
            FlightPhaseTransition? transition = engine.Observe(phaseObservation);
            if (transition is not null && transition.OccurredAtUtc != telemetry.CollectedAtUtc)
            {
                transition = transition with { OccurredAtUtc = telemetry.CollectedAtUtc };
            }
            FlightPhase phase = transition?.To ?? session.Phase;
            FlightSessionMetrics metrics = AccumulateTelemetryMetrics(session, telemetry, transition);
            bool positionDue = lastPositionReportAtUtc is null
                || telemetry.CollectedAtUtc - lastPositionReportAtUtc >= options.PositionReportInterval
                || transition is not null;

            List<OutboxItem> outboxItems = [];
            TelemetrySnapshot? previousTelemetry = session.LastTelemetry;
            if (telemetry.IsPaused != (previousTelemetry?.IsPaused ?? false))
            {
                FlightEventType pauseEventType = telemetry.IsPaused
                    ? FlightEventType.Paused
                    : FlightEventType.Resumed;
                FlightEvent pauseEvent = FlightEvent.Create(
                    session.Id,
                    pauseEventType,
                    telemetry.CollectedAtUtc,
                    telemetry.IsPaused
                        ? "Simulator pause state entered."
                        : "Simulator pause state exited.");
                AddEventAndLog(
                    outboxItems,
                    pauseEvent,
                    telemetry.IsPaused
                        ? "Simulator paused."
                        : "Simulator resumed.");
            }

            if (telemetry.IsSlewActive && previousTelemetry?.IsSlewActive != true)
            {
                FlightEvent slewEvent = FlightEvent.Create(
                    session.Id,
                    FlightEventType.SlewDetected,
                    telemetry.CollectedAtUtc,
                    "Simulator slew mode became active; movement metrics and phase changes are suppressed.");
                AddEventAndLog(outboxItems, slewEvent, "Simulator slew mode detected.");
            }

            if (transition is not null)
            {
                FlightEvent flightEvent = transition.ToFlightEvent(session.Id);
                string logMessage = transition.LandingRate is { } landingRate
                    ? FormattableString.Invariant(
                        $"Phase changed from {transition.From} to {transition.To}; landing rate {landingRate.FeetPerMinute:0} ft/min.")
                    : $"Phase changed from {transition.From} to {transition.To}.";
                AddEventAndLog(outboxItems, flightEvent, logMessage);
            }

            if (positionDue)
            {
                PositionReport position = new(
                    PositionReportId.New(),
                    session.Id,
                    phase,
                    telemetry,
                    transition is null ? null : $"Phase changed to {phase}.");
                outboxItems.Add(new PositionOutboxItem(
                    OutboxItemId.New(),
                    session.Id,
                    telemetry.CollectedAtUtc,
                    position));
            }

            FlightSessionState updated = CopySession(
                session,
                phase,
                telemetry.CollectedAtUtc,
                FlightSessionStatus.Active,
                telemetry,
                session.BackendFlightId,
                metrics);
            try
            {
                await PersistAsync(updated, outboxItems, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                phaseEngine = new FlightPhaseEngine(session.Phase, phaseOptions);
                throw;
            }

            SetCurrent(updated, lastPositionAtUtc: positionDue ? telemetry.CollectedAtUtc : null);
            return updated;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public ValueTask<FlightSessionState> PauseAsync(CancellationToken cancellationToken) =>
        ChangePauseStateAsync(FlightSessionStatus.Paused, FlightEventType.Paused, cancellationToken);

    public ValueTask<FlightSessionState> ResumeAsync(CancellationToken cancellationToken) =>
        ChangePauseStateAsync(FlightSessionStatus.Active, FlightEventType.Resumed, cancellationToken);

    public async ValueTask<FlightSessionState> CancelAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireSession();
            if (session.Status == FlightSessionStatus.Cancelled)
            {
                return session;
            }

            if (session.Status == FlightSessionStatus.Completed)
            {
                throw new InvalidOperationException("A completed flight cannot be cancelled.");
            }

            DateTimeOffset now = GetMonotonicNow(session);
            if (session.Status == FlightSessionStatus.Starting)
            {
                FlightStartIntent intent = RequireStartIntent(session);
                FlightSessionState abandoned = CopySession(
                    session,
                    session.Phase,
                    now,
                    FlightSessionStatus.Cancelled,
                    session.LastTelemetry,
                    session.BackendFlightId) with
                {
                    StartIntent = new FlightStartIntent(
                        intent.RequestMarker,
                        FlightStartIntentState.Abandoned,
                        intent.PreparedAtUtc,
                        intent.LastAttemptAtUtc),
                };
                await stateStore.SaveAsync(abandoned, cancellationToken).ConfigureAwait(false);
                SetCurrent(abandoned);
                return abandoned;
            }

            FlightEvent cancelledEvent = FlightEvent.Create(session.Id, FlightEventType.Cancelled, now);
            FlightSessionMetrics metrics = AccountElapsedTime(session, now) with
            {
                LastTrackedPosition = null,
                LastObservedFuel = null,
            };
            FlightSessionState cancelled = CopySession(
                session,
                session.Phase,
                now,
                FlightSessionStatus.Cancelled,
                session.LastTelemetry,
                session.BackendFlightId,
                metrics);
            List<OutboxItem> outboxItems = [];
            AddEventAndLog(outboxItems, cancelledEvent, "Flight tracking cancelled.");
            outboxItems.Add(new CancellationOutboxItem(OutboxItemId.New(), session.Id, now));
            await PersistAsync(
                cancelled,
                outboxItems,
                cancellationToken).ConfigureAwait(false);
            SetCurrent(cancelled);
            return cancelled;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> CompleteAsync(
        CompletedFlightReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireSession();
            if (session.Status == FlightSessionStatus.Completed)
            {
                return session;
            }

            EnsureStatus(session, FlightSessionStatus.Active, "Only an active flight can be completed.");
            if (session.Phase != FlightPhase.OnBlock)
            {
                throw new InvalidOperationException("A flight can only be completed after reaching on-block.");
            }

            if (report.CompletedAtUtc < session.UpdatedAtUtc || report.CompletedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Completion time must use UTC and cannot predate the session.",
                    nameof(report));
            }

            FlightPhaseEngine engine = phaseEngine
                ?? throw new InvalidOperationException("The phase engine has not been initialized.");
            FlightPhaseTransition transition = engine.Complete(report.CompletedAtUtc);
            FlightSessionMetrics metrics = AccountElapsedTime(session, report.CompletedAtUtc) with
            {
                LastTrackedPosition = null,
                LastObservedFuel = null,
            };
            CompletedFlightReport completedReport = report with
            {
                BlockTime = report.BlockTime ?? metrics.ActiveTrackingTime,
                FuelUsed = report.FuelUsed ?? metrics.FuelUsed,
                LandingRate = report.LandingRate ?? metrics.LandingRate,
                FilingPlan = session.FlightPlan,
                TrackingStartedAtUtc = session.StartedAtUtc,
            };
            FlightSessionState completed = CopySession(
                session,
                FlightPhase.Completed,
                report.CompletedAtUtc,
                FlightSessionStatus.Completed,
                session.LastTelemetry,
                session.BackendFlightId,
                metrics);
            try
            {
                FlightEvent completedEvent = transition.ToFlightEvent(session.Id);
                List<OutboxItem> outboxItems = [];
                AddEventAndLog(outboxItems, completedEvent, "Flight completed and the PIREP was queued for filing.");
                outboxItems.Add(new CompletionOutboxItem(
                    OutboxItemId.New(),
                    session.Id,
                    report.CompletedAtUtc,
                    completedReport));
                await PersistAsync(
                    completed,
                    outboxItems,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                phaseEngine = new FlightPhaseEngine(session.Phase, phaseOptions);
                throw;
            }

            SetCurrent(completed);
            return completed;
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async ValueTask<FlightSessionState> ChangePauseStateAsync(
        FlightSessionStatus targetStatus,
        FlightEventType eventType,
        CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = RequireSession();
            FlightSessionStatus requiredStatus = targetStatus == FlightSessionStatus.Paused
                ? FlightSessionStatus.Active
                : FlightSessionStatus.Paused;
            if (session.Status == targetStatus)
            {
                return session;
            }

            EnsureStatus(
                session,
                requiredStatus,
                targetStatus == FlightSessionStatus.Paused
                    ? "Only an active flight can be paused."
                    : "Only a paused flight can be resumed.");

            DateTimeOffset now = GetMonotonicNow(session);
            FlightEvent flightEvent = FlightEvent.Create(session.Id, eventType, now);
            FlightSessionMetrics metrics = AccountElapsedTime(session, now) with
            {
                LastTrackedPosition = null,
                LastObservedFuel = null,
            };
            FlightSessionState updated = CopySession(
                session,
                session.Phase,
                now,
                targetStatus,
                session.LastTelemetry,
                session.BackendFlightId,
                metrics);
            List<OutboxItem> outboxItems = [];
            AddEventAndLog(
                outboxItems,
                flightEvent,
                targetStatus == FlightSessionStatus.Paused
                    ? "Flight tracking paused."
                    : "Flight tracking resumed.");
            await PersistAsync(
                updated,
                outboxItems,
                cancellationToken).ConfigureAwait(false);
            SetCurrent(updated);
            return updated;
        }
        finally
        {
            operationLock.Release();
        }
    }

    private static FlightSessionState CopySession(
        FlightSessionState source,
        FlightPhase phase,
        DateTimeOffset updatedAtUtc,
        FlightSessionStatus status,
        TelemetrySnapshot? lastTelemetry,
        string? backendFlightId,
        FlightSessionMetrics? metrics = null) => new(
            source.Id,
            source.FlightPlan,
            phase,
            source.StartedAtUtc,
            updatedAtUtc)
        {
            BackendFlightId = backendFlightId,
            LastTelemetry = lastTelemetry,
            Status = status,
            StartIntent = source.StartIntent,
            Metrics = metrics ?? source.Metrics,
        };

    private static FlightSessionMetrics AccumulateTelemetryMetrics(
        FlightSessionState session,
        TelemetrySnapshot telemetry,
        FlightPhaseTransition? transition)
    {
        FlightSessionMetrics metrics = AccountElapsedTime(session, telemetry.CollectedAtUtc);
        TelemetrySnapshot? previous = session.LastTelemetry;
        bool sourceMatches = previous is null
            || previous.Simulator.Kind == telemetry.Simulator.Kind;
        bool currentSampleUsable = !telemetry.IsPaused && !telemetry.IsSlewActive;
        bool previousSampleUsable = previous is null
            || (!previous.IsPaused && !previous.IsSlewActive);
        bool successiveSample = previous is not null
            && telemetry.CollectedAtUtc > previous.CollectedAtUtc
            && sourceMatches
            && previousSampleUsable;

        Distance distance = metrics.FlownDistance;
        if (successiveSample
            && currentSampleUsable
            && metrics.LastTrackedPosition is { } previousPosition)
        {
            double segmentDistance = GreatCircleDistance(previousPosition, telemetry.Position);
            if (double.IsFinite(segmentDistance) && segmentDistance >= 0)
            {
                distance = new Distance(distance.NauticalMiles + segmentDistance);
            }
        }

        FuelMass? fuelUsed = metrics.FuelUsed;
        if (telemetry.FuelRemaining is { } currentFuel)
        {
            fuelUsed ??= new FuelMass(0);
            if (successiveSample
                && !telemetry.IsSlewActive
                && metrics.LastObservedFuel is { } previousFuel)
            {
                double consumed = previousFuel.Kilograms - currentFuel.Kilograms;
                if (consumed > 0)
                {
                    fuelUsed = new FuelMass(fuelUsed.Value.Kilograms + consumed);
                }
            }
        }

        return metrics with
        {
            FlownDistance = distance,
            FuelUsed = fuelUsed,
            LandingRate = transition?.LandingRate ?? metrics.LandingRate,
            LastTrackedPosition = currentSampleUsable ? telemetry.Position : null,
            LastObservedFuel = currentSampleUsable ? telemetry.FuelRemaining : null,
        };
    }

    private static FlightSessionMetrics AccountElapsedTime(
        FlightSessionState session,
        DateTimeOffset observedAtUtc)
    {
        FlightSessionMetrics metrics = session.Metrics ?? new FlightSessionMetrics();
        if (session.Status != FlightSessionStatus.Active || observedAtUtc <= session.UpdatedAtUtc)
        {
            return metrics;
        }

        TimeSpan elapsed = observedAtUtc - session.UpdatedAtUtc;
        TelemetrySnapshot? previous = session.LastTelemetry;
        if (previous?.IsPaused == true)
        {
            return metrics;
        }

        TimeSpan activeTrackingTime = metrics.ActiveTrackingTime + elapsed;
        TimeSpan airborneFlightTime = metrics.AirborneFlightTime;
        if (previous is { OnGround: false, IsSlewActive: false })
        {
            airborneFlightTime += elapsed;
        }

        return metrics with
        {
            ActiveTrackingTime = activeTrackingTime,
            AirborneFlightTime = airborneFlightTime,
        };
    }

    private static double GreatCircleDistance(GeoPosition from, GeoPosition to)
    {
        double fromLatitude = DegreesToRadians(from.LatitudeDegrees);
        double toLatitude = DegreesToRadians(to.LatitudeDegrees);
        double latitudeDelta = toLatitude - fromLatitude;
        double longitudeDelta = DegreesToRadians(to.LongitudeDegrees - from.LongitudeDegrees);
        double sinLatitude = Math.Sin(latitudeDelta / 2);
        double sinLongitude = Math.Sin(longitudeDelta / 2);
        double haversine = (sinLatitude * sinLatitude)
            + (Math.Cos(fromLatitude) * Math.Cos(toLatitude) * sinLongitude * sinLongitude);
        double centralAngle = 2 * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1)));
        return EarthRadiusNauticalMiles * centralAngle;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static void AddEventAndLog(
        List<OutboxItem> outboxItems,
        FlightEvent flightEvent,
        string logMessage)
    {
        outboxItems.Add(new EventOutboxItem(
            OutboxItemId.New(),
            flightEvent.SessionId,
            flightEvent.OccurredAtUtc,
            flightEvent));
        outboxItems.Add(new LogOutboxItem(
            OutboxItemId.New(),
            flightEvent.SessionId,
            flightEvent.OccurredAtUtc,
            new FlightLogEntry(
                FlightEventId.New(),
                flightEvent.SessionId,
                flightEvent.OccurredAtUtc,
                logMessage)));
    }

    private async ValueTask PersistAsync(
        FlightSessionState session,
        IReadOnlyCollection<OutboxItem> outboxItems,
        CancellationToken cancellationToken)
    {
        await stateStore
            .SaveWithOutboxAsync(session, outboxItems, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureStatus(
        FlightSessionState session,
        FlightSessionStatus expected,
        string message)
    {
        if (session.Status != expected)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string? NormalizeBackendFlightId(string? backendFlightId) =>
        string.IsNullOrWhiteSpace(backendFlightId) ? null : backendFlightId.Trim();

    private FlightSessionState RequireSession() =>
        CurrentSession ?? throw new InvalidOperationException("No flight session has been started or restored.");

    private FlightSessionState RequireStartingSession()
    {
        FlightSessionState session = RequireSession();
        EnsureStatus(
            session,
            FlightSessionStatus.Starting,
            "No pending flight-start request is available for reconciliation.");
        return session;
    }

    private static FlightStartIntent RequireStartIntent(FlightSessionState session) =>
        session.StartIntent
        ?? throw new InvalidOperationException("The pending flight-start request has no durable intent metadata.");

    private DateTimeOffset GetMonotonicNow(FlightSessionState session)
    {
        DateTimeOffset now = GetUtcNow();
        if (now < session.UpdatedAtUtc)
        {
            throw new InvalidOperationException("System time moved backwards during the flight session.");
        }

        return now;
    }

    private DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow().ToUniversalTime();

    private static bool UsesRecordedClock(TelemetrySnapshot? telemetry) =>
        telemetry?.Simulator.Kind == SimulatorKind.RecordedTelemetry
        && telemetry.SimulatorTime is not null;

    private static TelemetrySnapshot CreatePhaseObservation(TelemetrySnapshot telemetry) =>
        UsesRecordedClock(telemetry)
            ? telemetry with { CollectedAtUtc = telemetry.SimulatorTime!.Value }
            : telemetry;

    private void SetCurrent(
        FlightSessionState session,
        FlightPhaseEngine? engine = null,
        DateTimeOffset? lastPositionAtUtc = null)
    {
        lock (stateLock)
        {
            currentSession = session;
            if (engine is not null)
            {
                phaseEngine = engine;
            }

            if (lastPositionAtUtc is not null || session.LastTelemetry is null)
            {
                lastPositionReportAtUtc = lastPositionAtUtc;
            }
        }
    }
}

using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Synchronization;
using ClimbAndMaintain.Acars.Application.Tracking;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class FlightOperationsService(
    IFlightTrackingCoordinator tracking,
    FlightStartCoordinator flightStarts,
    IOutboxStore outbox,
    IFlightSessionStore sessions,
    PhpVmsBackendLeaseFactory backendFactory,
    ILogger<FlightOperationsService> logger) : IDisposable
{
    private static readonly Action<ILogger, string, string, Exception?> LogSessionAvailable =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(3501, "FlightSessionAvailable"),
            "Flight session is available with lifecycle {SessionStatus} and phase {FlightPhase}.");

    private static readonly Action<ILogger, string, string, Exception?> LogSessionLifecycleChanged =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(3502, "FlightSessionLifecycleChanged"),
            "Flight session lifecycle changed from {PreviousStatus} to {CurrentStatus}.");

    private static readonly Action<ILogger, string, string, Exception?> LogFlightPhaseChanged =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(3503, "FlightPhaseChanged"),
            "Flight phase changed from {PreviousPhase} to {CurrentPhase}.");

    private static readonly Action<ILogger, Exception?> LogNoSessionRestored =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(3504, "NoFlightSessionRestored"),
            "No active flight session was available to restore.");

    private readonly IFlightTrackingCoordinator tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
    private readonly FlightStartCoordinator flightStarts = flightStarts ?? throw new ArgumentNullException(nameof(flightStarts));
    private readonly IOutboxStore outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    private readonly IFlightSessionStore sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    private readonly PhpVmsBackendLeaseFactory backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
    private readonly ILogger<FlightOperationsService> logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim synchronizationGate = new(1, 1);
    private readonly Lock logStateLock = new();
    private FlightSessionId? loggedSessionId;
    private FlightSessionStatus? loggedSessionStatus;
    private FlightPhase? loggedFlightPhase;
    private bool disposed;

    public FlightSessionState? CurrentSession => tracking.CurrentSession;

    public event EventHandler<FlightSessionChangedEventArgs>? SessionChanged;

    public async ValueTask<FlightSessionState?> RestoreAsync(CancellationToken cancellationToken)
    {
        FlightSessionState? session = await tracking.RestoreAsync(cancellationToken);
        if (session is null)
        {
            LogNoSessionRestored(logger, null);
        }

        OnSessionChanged(session, null);
        return session;
    }

    public async ValueTask<IReadOnlyList<BackendFlight>> GetAvailableFlightsAsync(CancellationToken cancellationToken)
    {
        using FlightBackendLease lease = await backendFactory.CreateAsync(cancellationToken);
        return await lease.Backend.GetAvailableFlightsAsync(cancellationToken);
    }

    public async ValueTask<FlightSessionState> StartAsync(
        BackendFlight flight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flight);
        using FlightBackendLease lease = await backendFactory.CreateAsync(cancellationToken);
        try
        {
            return await flightStarts.StartAsync(flight, lease.Backend, cancellationToken);
        }
        finally
        {
            OnSessionChanged(CurrentSession, CurrentSession?.LastTelemetry);
        }
    }

    public async ValueTask<FlightSessionState> ResumePendingStartAsync(
        CancellationToken cancellationToken)
    {
        using FlightBackendLease lease = await backendFactory.CreateAsync(cancellationToken);
        try
        {
            return await flightStarts
                .ResumePendingStartAsync(lease.Backend, cancellationToken);
        }
        finally
        {
            OnSessionChanged(CurrentSession, CurrentSession?.LastTelemetry);
        }
    }

    public async ValueTask<FlightSessionState> ProcessTelemetryAsync(
        TelemetrySnapshot telemetry,
        CancellationToken cancellationToken)
    {
        FlightSessionState session = await tracking.ProcessTelemetryAsync(telemetry, cancellationToken);
        OnSessionChanged(session, telemetry);
        return session;
    }

    public async ValueTask<FlightSessionState> PauseAsync(CancellationToken cancellationToken)
    {
        FlightSessionState session = await tracking.PauseAsync(cancellationToken);
        OnSessionChanged(session, session.LastTelemetry);
        return session;
    }

    public async ValueTask<FlightSessionState> ResumeAsync(CancellationToken cancellationToken)
    {
        FlightSessionState session = await tracking.ResumeAsync(cancellationToken);
        OnSessionChanged(session, session.LastTelemetry);
        return session;
    }

    public async ValueTask<FlightSessionState> CancelAsync(CancellationToken cancellationToken)
    {
        FlightSessionState session = await tracking.CancelAsync(cancellationToken);
        OnSessionChanged(session, session.LastTelemetry);
        return session;
    }

    public async ValueTask<FlightSessionState> CompleteAsync(
        CompletedFlightReport report,
        CancellationToken cancellationToken)
    {
        FlightSessionState session = await tracking.CompleteAsync(report, cancellationToken);
        OnSessionChanged(session, session.LastTelemetry);
        return session;
    }

    public async ValueTask<OutboxSynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            using FlightBackendLease lease = await backendFactory.CreateAsync(cancellationToken);
            using OutboxSynchronizer synchronizer = new(outbox, sessions, lease.Backend);
            return await synchronizer.SynchronizeAsync(cancellationToken);
        }
        finally
        {
            synchronizationGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        synchronizationGate.Dispose();
    }

    private void OnSessionChanged(FlightSessionState? session, TelemetrySnapshot? telemetry)
    {
        RecordSessionTransitions(session);
        SessionChanged?.Invoke(this, new FlightSessionChangedEventArgs(session, telemetry));
    }

    private void RecordSessionTransitions(FlightSessionState? session)
    {
        lock (logStateLock)
        {
            if (session is null)
            {
                loggedSessionId = null;
                loggedSessionStatus = null;
                loggedFlightPhase = null;
                return;
            }

            if (loggedSessionId != session.Id)
            {
                LogSessionAvailable(logger, session.Status.ToString(), session.Phase.ToString(), null);
                loggedSessionId = session.Id;
                loggedSessionStatus = session.Status;
                loggedFlightPhase = session.Phase;
                return;
            }

            if (loggedSessionStatus is { } previousStatus && previousStatus != session.Status)
            {
                LogSessionLifecycleChanged(logger, previousStatus.ToString(), session.Status.ToString(), null);
            }

            if (loggedFlightPhase is { } previousPhase && previousPhase != session.Phase)
            {
                LogFlightPhaseChanged(logger, previousPhase.ToString(), session.Phase.ToString(), null);
            }

            loggedSessionStatus = session.Status;
            loggedFlightPhase = session.Phase;
        }
    }
}

public sealed class FlightSessionChangedEventArgs(
    FlightSessionState? session,
    TelemetrySnapshot? telemetry) : EventArgs
{
    public FlightSessionState? Session { get; } = session;

    public TelemetrySnapshot? Telemetry { get; } = telemetry;
}

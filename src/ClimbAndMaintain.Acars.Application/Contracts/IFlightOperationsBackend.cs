using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Application.Contracts;

public interface IFlightOperationsBackend
{
    string BackendId { get; }

    ValueTask<BackendConnectionResult> TestConnectionAsync(CancellationToken cancellationToken);

    ValueTask<BackendPilot> GetCurrentPilotAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<BackendFlight>> GetAvailableFlightsAsync(CancellationToken cancellationToken);

    ValueTask<BackendPrefileResult> PrefileAsync(
        FlightPlan flightPlan,
        string requestMarker,
        CancellationToken cancellationToken);

    ValueTask<BackendPrefileResult?> FindPrefiledFlightAsync(
        FlightPlan flightPlan,
        string requestMarker,
        CancellationToken cancellationToken);

    ValueTask SendPositionsAsync(
        string backendFlightId,
        IReadOnlyList<PositionReport> positions,
        CancellationToken cancellationToken);

    ValueTask SendEventsAsync(
        string backendFlightId,
        IReadOnlyList<FlightEvent> events,
        CancellationToken cancellationToken);

    ValueTask SendLogsAsync(
        string backendFlightId,
        IReadOnlyList<FlightLogEntry> logs,
        CancellationToken cancellationToken);

    ValueTask FileFlightAsync(
        string backendFlightId,
        CompletedFlightReport report,
        CancellationToken cancellationToken);

    ValueTask CancelFlightAsync(string backendFlightId, CancellationToken cancellationToken);
}

public sealed record BackendConnectionResult(
    bool Succeeded,
    string? ServerVersion,
    string? FailureMessage);

public sealed record BackendPilot(string Id, string Callsign, string DisplayName);

public sealed record BackendFlight(string Id, FlightPlan FlightPlan, string? Briefing);

public sealed record BackendPrefileResult(string BackendFlightId, DateTimeOffset CreatedAtUtc);

public sealed record CompletedFlightReport(
    Distance Distance,
    TimeSpan FlightTime,
    TimeSpan? BlockTime,
    FuelMass? FuelUsed,
    VerticalSpeed? LandingRate,
    DateTimeOffset CompletedAtUtc)
{
    // Captured by the coordinator at completion so a later outbox retry uses
    // the same plan and timing values even after an application restart.
    public FlightPlan? FilingPlan { get; init; }

    public DateTimeOffset? TrackingStartedAtUtc { get; init; }
}

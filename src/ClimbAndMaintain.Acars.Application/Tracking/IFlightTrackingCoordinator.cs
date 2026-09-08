using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Application.Tracking;

public interface IFlightTrackingCoordinator
{
    FlightSessionState? CurrentSession { get; }

    ValueTask<FlightSessionState?> RestoreAsync(CancellationToken cancellationToken);

    ValueTask<FlightSessionState> StartAsync(
        FlightPlan flightPlan,
        string? backendFlightId,
        CancellationToken cancellationToken);

    ValueTask<FlightSessionState> PrepareStartAsync(
        FlightPlan flightPlan,
        string requestMarker,
        CancellationToken cancellationToken);

    ValueTask<FlightSessionState> MarkPrefileRequestedAsync(CancellationToken cancellationToken);

    ValueTask<FlightSessionState> MarkStartReconciliationRequiredAsync(CancellationToken cancellationToken);

    ValueTask<FlightSessionState> CompleteStartAsync(
        string backendFlightId,
        CancellationToken cancellationToken);

    ValueTask<FlightSessionState> AttachBackendFlightAsync(
        string backendFlightId,
        CancellationToken cancellationToken);

    ValueTask<FlightSessionState> ProcessTelemetryAsync(
        TelemetrySnapshot telemetry,
        CancellationToken cancellationToken);

    ValueTask<FlightSessionState> PauseAsync(CancellationToken cancellationToken);

    ValueTask<FlightSessionState> ResumeAsync(CancellationToken cancellationToken);

    ValueTask<FlightSessionState> CancelAsync(CancellationToken cancellationToken);

    ValueTask<FlightSessionState> CompleteAsync(
        CompletedFlightReport report,
        CancellationToken cancellationToken);
}

using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Core.Flights;

public sealed record FlightPhaseTransition(
    FlightPhase From,
    FlightPhase To,
    DateTimeOffset OccurredAtUtc,
    VerticalSpeed? LandingRate)
{
    public FlightEvent ToFlightEvent(FlightSessionId sessionId)
    {
        string? detail = LandingRate is null
            ? null
            : FormattableString.Invariant($"Landing rate: {LandingRate.Value.FeetPerMinute:0} ft/min");

        return FlightEvent.Create(sessionId, MapEventType(To), OccurredAtUtc, detail);
    }

    private static FlightEventType MapEventType(FlightPhase phase) => phase switch
    {
        FlightPhase.Ready => throw new InvalidOperationException("Ready is the initial state, not an event."),
        FlightPhase.Boarding => FlightEventType.Boarding,
        FlightPhase.Pushback => FlightEventType.Pushback,
        FlightPhase.TaxiOut => FlightEventType.TaxiOut,
        FlightPhase.Takeoff => FlightEventType.Takeoff,
        FlightPhase.Climb => FlightEventType.Climb,
        FlightPhase.Cruise => FlightEventType.Cruise,
        FlightPhase.Descent => FlightEventType.Descent,
        FlightPhase.Approach => FlightEventType.Approach,
        FlightPhase.Landing => FlightEventType.Landing,
        FlightPhase.TaxiIn => FlightEventType.TaxiIn,
        FlightPhase.OnBlock => FlightEventType.OnBlock,
        FlightPhase.Completed => FlightEventType.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown flight phase."),
    };
}

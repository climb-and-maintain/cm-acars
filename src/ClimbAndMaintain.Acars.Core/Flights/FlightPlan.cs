using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Core.Flights;

public sealed record FlightPlan
{
    public FlightPlan(
        string airlineId,
        string aircraftId,
        string flightNumber,
        string departureAirport,
        string arrivalAirport)
    {
        AirlineId = Required(airlineId, nameof(airlineId));
        AircraftId = Required(aircraftId, nameof(aircraftId));
        FlightNumber = Required(flightNumber, nameof(flightNumber));
        DepartureAirport = Required(departureAirport, nameof(departureAirport)).ToUpperInvariant();
        ArrivalAirport = Required(arrivalAirport, nameof(arrivalAirport)).ToUpperInvariant();
    }

    public string AirlineId { get; }

    public string AircraftId { get; }

    public string FlightNumber { get; }

    public string DepartureAirport { get; }

    public string ArrivalAirport { get; }

    public string? SourceFlightId { get; init; }

    public string? AlternateAirport { get; init; }

    public string? Route { get; init; }

    public Distance? PlannedDistance { get; init; }

    public TimeSpan? PlannedDuration { get; init; }

    public FuelMass? PlannedFuel { get; init; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

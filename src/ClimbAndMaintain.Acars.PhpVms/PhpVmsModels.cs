using System.Text.Json.Serialization;

namespace ClimbAndMaintain.Acars.PhpVms;

public sealed record PrefilePirepRequest
{
    [JsonPropertyName("airline_id")]
    public required string AirlineId { get; init; }

    [JsonPropertyName("aircraft_id")]
    public required string AircraftId { get; init; }

    [JsonPropertyName("flight_number")]
    public required string FlightNumber { get; init; }

    [JsonPropertyName("dpt_airport_id")]
    public required string DepartureAirportId { get; init; }

    [JsonPropertyName("arr_airport_id")]
    public required string ArrivalAirportId { get; init; }

    [JsonPropertyName("source_name")]
    public string SourceName { get; init; } = "C&M ACARS";

    [JsonPropertyName("flight_id")]
    public string? FlightId { get; init; }

    [JsonPropertyName("sim_type")]
    public int? SimulatorType { get; init; }

    [JsonPropertyName("alt_airport_id")]
    public string? AlternateAirportId { get; init; }

    [JsonPropertyName("route")]
    public string? Route { get; init; }

    [JsonPropertyName("planned_distance")]
    public double? PlannedDistance { get; init; }

    [JsonPropertyName("planned_flight_time")]
    public int? PlannedFlightTimeMinutes { get; init; }

    [JsonPropertyName("block_fuel")]
    public double? BlockFuel { get; init; }
}

public sealed record UpdatePirepRequest
{
    [JsonPropertyName("aircraft_id")]
    public string? AircraftId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("distance")]
    public double? Distance { get; init; }

    [JsonPropertyName("flight_time")]
    public int? FlightTimeMinutes { get; init; }

    [JsonPropertyName("block_time")]
    public int? BlockTimeMinutes { get; init; }

    [JsonPropertyName("fuel_used")]
    public double? FuelUsed { get; init; }

    [JsonPropertyName("block_fuel")]
    public double? BlockFuel { get; init; }

    [JsonPropertyName("route")]
    public string? Route { get; init; }

    [JsonPropertyName("landing_rate")]
    public double? LandingRate { get; init; }

    [JsonPropertyName("block_off_time")]
    public DateTimeOffset? BlockOffTimeUtc { get; init; }

    [JsonPropertyName("block_on_time")]
    public DateTimeOffset? BlockOnTimeUtc { get; init; }

    [JsonPropertyName("source_name")]
    public string? SourceName { get; init; }
}

public sealed record FlightSearchRequest
{
    public string? FlightId { get; init; }

    public string? AirlineId { get; init; }

    public string? FlightNumber { get; init; }

    public string? Callsign { get; init; }

    public string? DepartureAirportId { get; init; }

    public string? ArrivalAirportId { get; init; }

    public string? FlightType { get; init; }

    public string? RouteCode { get; init; }

    public string? IcaoType { get; init; }

    public string? SubfleetId { get; init; }

    public int? MinimumDistance { get; init; }

    public int? MaximumDistance { get; init; }

    public int? MinimumFlightTimeMinutes { get; init; }

    public int? MaximumFlightTimeMinutes { get; init; }

    public int? Page { get; init; }

    public int? Limit { get; init; }

    internal string ToQueryString()
    {
        ValidatePositive(Page, nameof(Page));
        ValidatePositive(Limit, nameof(Limit));
        ValidateNonNegative(MinimumDistance, nameof(MinimumDistance));
        ValidateNonNegative(MaximumDistance, nameof(MaximumDistance));
        ValidateNonNegative(MinimumFlightTimeMinutes, nameof(MinimumFlightTimeMinutes));
        ValidateNonNegative(MaximumFlightTimeMinutes, nameof(MaximumFlightTimeMinutes));

        List<string> parameters = [];
        Add(parameters, "flight_id", FlightId);
        Add(parameters, "airline_id", AirlineId);
        Add(parameters, "flight_number", FlightNumber);
        Add(parameters, "callsign", Callsign);
        Add(parameters, "dpt_airport_id", DepartureAirportId?.ToUpperInvariant());
        Add(parameters, "arr_airport_id", ArrivalAirportId?.ToUpperInvariant());
        Add(parameters, "flight_type", FlightType);
        Add(parameters, "route_code", RouteCode);
        Add(parameters, "icao_type", IcaoType?.ToUpperInvariant());
        Add(parameters, "subfleet_id", SubfleetId);
        Add(parameters, "dgt", MinimumDistance);
        Add(parameters, "dlt", MaximumDistance);
        Add(parameters, "tgt", MinimumFlightTimeMinutes);
        Add(parameters, "tlt", MaximumFlightTimeMinutes);
        Add(parameters, "page", Page);
        Add(parameters, "limit", Limit);
        return string.Join('&', parameters);
    }

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    private static void Add(List<string> parameters, string name, int? value)
    {
        if (value is { } number)
        {
            parameters.Add(FormattableString.Invariant($"{name}={number}"));
        }
    }

    private static void ValidatePositive(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be positive.");
        }
    }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value is < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value cannot be negative.");
        }
    }
}

public sealed record AcarsPosition
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("lat")]
    public required double Latitude { get; init; }

    [JsonPropertyName("lon")]
    public required double Longitude { get; init; }

    [JsonPropertyName("status")]
    public string? Phase { get; init; }

    [JsonPropertyName("altitude_agl")]
    public double? AltitudeAgl { get; init; }

    [JsonPropertyName("altitude_msl")]
    public double? AltitudeMsl { get; init; }

    [JsonPropertyName("heading")]
    public double? Heading { get; init; }

    [JsonPropertyName("vs")]
    public double? VerticalSpeed { get; init; }

    [JsonPropertyName("gs")]
    public double? GroundSpeed { get; init; }

    [JsonPropertyName("ias")]
    public double? IndicatedAirspeed { get; init; }

    [JsonPropertyName("transponder")]
    public string? Transponder { get; init; }

    [JsonPropertyName("autopilot")]
    public bool? Autopilot { get; init; }

    [JsonPropertyName("fuel")]
    public double? Fuel { get; init; }

    [JsonPropertyName("fuel_flow")]
    public double? FuelFlow { get; init; }

    [JsonPropertyName("log")]
    public string? Log { get; init; }

    [JsonPropertyName("sim_time")]
    public DateTimeOffset? SimulatorTime { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CollectedAtUtc { get; init; }
}

public sealed record AcarsEvent
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("event")]
    public required string Event { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("sim_time")]
    public DateTimeOffset? SimulatorTime { get; init; }
}

public sealed record AcarsLogEntry
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("log")]
    public required string Message { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("sim_time")]
    public DateTimeOffset? SimulatorTime { get; init; }
}

public sealed record FilePirepRequest
{
    [JsonPropertyName("distance")]
    public required double Distance { get; init; }

    [JsonPropertyName("flight_time")]
    public required int FlightTimeMinutes { get; init; }

    [JsonPropertyName("fuel_used")]
    public double? FuelUsed { get; init; }

    [JsonPropertyName("block_time")]
    public int? BlockTimeMinutes { get; init; }

    [JsonPropertyName("airline_id")]
    public string? AirlineId { get; init; }

    [JsonPropertyName("aircraft_id")]
    public string? AircraftId { get; init; }

    [JsonPropertyName("flight_number")]
    public string? FlightNumber { get; init; }

    [JsonPropertyName("dpt_airport_id")]
    public string? DepartureAirportId { get; init; }

    [JsonPropertyName("arr_airport_id")]
    public string? ArrivalAirportId { get; init; }

    [JsonPropertyName("planned_distance")]
    public double? PlannedDistance { get; init; }

    [JsonPropertyName("planned_flight_time")]
    public int? PlannedFlightTimeMinutes { get; init; }

    [JsonPropertyName("block_fuel")]
    public double? BlockFuel { get; init; }

    [JsonPropertyName("route")]
    public string? Route { get; init; }

    [JsonPropertyName("landing_rate")]
    public double? LandingRate { get; init; }

    [JsonPropertyName("block_off_time")]
    public DateTimeOffset? BlockOffTimeUtc { get; init; }

    [JsonPropertyName("block_on_time")]
    public DateTimeOffset? BlockOnTimeUtc { get; init; }

    [JsonPropertyName("source_name")]
    public string SourceName { get; init; } = "C&M ACARS";
}

public sealed record PhpVmsResponse(System.Net.HttpStatusCode StatusCode, System.Text.Json.JsonElement Data);

public sealed record PhpVmsDocumentResponse(
    System.Net.HttpStatusCode StatusCode,
    string? MediaType,
    string Content);

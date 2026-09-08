using System.Text.Json;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.PhpVms;

public sealed class PhpVmsBackend(PhpVmsClient client) : IFlightOperationsBackend
{
    private const int ReconciliationPageSize = 100;
    private const int MaximumReconciliationPages = 20;

    private readonly PhpVmsClient client = client ?? throw new ArgumentNullException(nameof(client));

    public string BackendId => "phpvms";

    public async ValueTask<BackendConnectionResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            PhpVmsResponse status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            _ = await client.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
            return new(true, OptionalString(status.Data, "version"), null);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or JsonException or InvalidDataException)
        {
            return new(false, null, exception.Message);
        }
    }

    public async ValueTask<BackendPilot> GetCurrentPilotAsync(CancellationToken cancellationToken)
    {
        PhpVmsResponse response = await client.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        string id = RequiredString(response.Data, "id");
        string callsign = FirstString(response.Data, "ident", "callsign") ?? id;
        string displayName = FirstString(response.Data, "name_private", "name", "display_name") ?? callsign;
        return new(id, callsign, displayName);
    }

    public async ValueTask<IReadOnlyList<BackendFlight>> GetAvailableFlightsAsync(CancellationToken cancellationToken)
    {
        PhpVmsResponse bids = await client.GetBidsAsync(cancellationToken).ConfigureAwait(false);
        PhpVmsResponse flights = await client.GetFlightsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, BackendFlight> result = new(StringComparer.Ordinal);
        AddAvailableFlights(bids.Data, "bid", result);
        AddAvailableFlights(flights.Data, "flight", result);
        return [.. result.Values];
    }

    public async ValueTask<BackendPrefileResult> PrefileAsync(
        FlightPlan flightPlan,
        string requestMarker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flightPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestMarker);
        PhpVmsResponse response = await client.PrefilePirepAsync(new PrefilePirepRequest
        {
            AirlineId = flightPlan.AirlineId,
            AircraftId = flightPlan.AircraftId,
            FlightNumber = flightPlan.FlightNumber,
            DepartureAirportId = flightPlan.DepartureAirport,
            ArrivalAirportId = flightPlan.ArrivalAirport,
            FlightId = flightPlan.SourceFlightId,
            AlternateAirportId = flightPlan.AlternateAirport,
            Route = flightPlan.Route,
            PlannedDistance = flightPlan.PlannedDistance?.NauticalMiles,
            PlannedFlightTimeMinutes = flightPlan.PlannedDuration is { } duration ? CheckedMinutes(duration) : null,
            BlockFuel = flightPlan.PlannedFuel?.Kilograms,
            SourceName = requestMarker.Trim(),
        }, cancellationToken).ConfigureAwait(false);

        string id = RequiredString(response.Data, "id");
        DateTimeOffset created = OptionalDateTime(response.Data, "created_at") ?? DateTimeOffset.UtcNow;
        return new(id, created.ToUniversalTime());
    }

    public async ValueTask<BackendPrefileResult?> FindPrefiledFlightAsync(
        FlightPlan flightPlan,
        string requestMarker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flightPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestMarker);
        string normalizedMarker = requestMarker.Trim();
        for (int page = 1; page <= MaximumReconciliationPages; page++)
        {
            PhpVmsResponse response = await client
                .GetPirepsAsync(0, page, ReconciliationPageSize, cancellationToken)
                .ConfigureAwait(false);
            if (response.Data.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("phpVMS returned a PIREP list that was not an array.");
            }

            int count = 0;
            foreach (JsonElement pirep in response.Data.EnumerateArray())
            {
                count++;
                if (!string.Equals(
                        OptionalString(pirep, "source_name"),
                        normalizedMarker,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string id = RequiredString(pirep, "id");
                DateTimeOffset created = OptionalDateTime(pirep, "created_at") ?? DateTimeOffset.UtcNow;
                return new BackendPrefileResult(id, created.ToUniversalTime());
            }

            if (count < ReconciliationPageSize)
            {
                break;
            }
        }

        return null;
    }

    public async ValueTask SendPositionsAsync(
        string backendFlightId,
        IReadOnlyList<PositionReport> positions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var payload = positions.Select(position => new AcarsPosition
        {
            Id = position.Id.Value,
            Latitude = position.Telemetry.Position.LatitudeDegrees,
            Longitude = position.Telemetry.Position.LongitudeDegrees,
            Phase = ToPhpVmsPhase(position.Phase),
            AltitudeAgl = position.Telemetry.AltitudeAgl?.Feet,
            AltitudeMsl = position.Telemetry.AltitudeMsl.Feet,
            Heading = position.Telemetry.MagneticHeading?.Degrees ?? position.Telemetry.TrueHeading?.Degrees,
            VerticalSpeed = position.Telemetry.VerticalSpeed.FeetPerMinute,
            GroundSpeed = position.Telemetry.GroundSpeed.Knots,
            IndicatedAirspeed = position.Telemetry.IndicatedAirspeed.Knots,
            Transponder = position.Telemetry.Systems.Transponder?.Code,
            Autopilot = position.Telemetry.Systems.AutopilotEngaged,
            Fuel = position.Telemetry.FuelRemaining?.Kilograms,
            FuelFlow = position.Telemetry.TotalFuelFlow?.KilogramsPerHour,
            Log = position.LogMessage,
            SimulatorTime = position.Telemetry.SimulatorTime,
            CollectedAtUtc = position.Telemetry.CollectedAtUtc,
        }).ToArray();
        _ = await client.SendPositionsAsync(backendFlightId, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendEventsAsync(
        string backendFlightId,
        IReadOnlyList<FlightEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        AcarsEvent[] payload = events.Select(item => new AcarsEvent
        {
            Id = item.Id.Value,
            Event = item.Detail is null ? item.Type.ToString() : $"{item.Type}: {item.Detail}",
            CreatedAtUtc = item.OccurredAtUtc,
        }).ToArray();
        _ = await client.SendEventsAsync(backendFlightId, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendLogsAsync(
        string backendFlightId,
        IReadOnlyList<FlightLogEntry> logs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logs);
        AcarsLogEntry[] payload = logs.Select(item => new AcarsLogEntry
        {
            Id = item.Id.Value,
            Message = item.Message,
            CreatedAtUtc = item.OccurredAtUtc,
        }).ToArray();
        _ = await client.SendLogsAsync(backendFlightId, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask FileFlightAsync(
        string backendFlightId,
        CompletedFlightReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        FlightPlan? plan = report.FilingPlan;
        PhpVmsResponse current = await client
            .GetPirepAsync(backendFlightId, cancellationToken)
            .ConfigureAwait(false);
        if (IsAlreadyFiled(current.Data))
        {
            return;
        }

        _ = await client.FilePirepAsync(backendFlightId, new FilePirepRequest
        {
            Distance = report.Distance.NauticalMiles,
            FlightTimeMinutes = CheckedMinutes(report.FlightTime),
            BlockTimeMinutes = report.BlockTime is { } block ? CheckedMinutes(block) : null,
            FuelUsed = report.FuelUsed?.Kilograms,
            AirlineId = plan?.AirlineId,
            AircraftId = plan?.AircraftId,
            FlightNumber = plan?.FlightNumber,
            DepartureAirportId = plan?.DepartureAirport,
            ArrivalAirportId = plan?.ArrivalAirport,
            PlannedDistance = plan?.PlannedDistance?.NauticalMiles,
            PlannedFlightTimeMinutes = plan?.PlannedDuration is { } plannedDuration
                ? CheckedMinutes(plannedDuration)
                : null,
            BlockFuel = plan?.PlannedFuel?.Kilograms,
            Route = plan?.Route,
            LandingRate = report.LandingRate?.FeetPerMinute,
            BlockOffTimeUtc = report.TrackingStartedAtUtc,
            BlockOnTimeUtc = report.CompletedAtUtc,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CancelFlightAsync(string backendFlightId, CancellationToken cancellationToken)
    {
        _ = await client.CancelPirepAsync(backendFlightId, cancellationToken).ConfigureAwait(false);
    }

    private static void AddAvailableFlights(
        JsonElement data,
        string listName,
        IDictionary<string, BackendFlight> destination)
    {
        if (data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"phpVMS returned a {listName} list that was not an array.");
        }

        foreach (JsonElement container in data.EnumerateArray())
        {
            JsonElement flight = container.TryGetProperty("flight", out JsonElement nestedFlight)
                && nestedFlight.ValueKind == JsonValueKind.Object
                ? nestedFlight
                : container;
            string id = RequiredString(flight, "id");
            string airlineId = NestedOrDirect(flight, "airline", "id", "airline_id");
            string aircraftId = ResolveAircraftId(flight, id);
            string flightNumber = FirstString(flight, "flight_number", "ident", "number")
                ?? throw new InvalidDataException($"phpVMS flight '{id}' omitted its flight number.");
            string departure = NestedOrDirect(flight, "dpt_airport", "id", "dpt_airport_id");
            string arrival = NestedOrDirect(flight, "arr_airport", "id", "arr_airport_id");
            FlightPlan plan = new(airlineId, aircraftId, flightNumber, departure, arrival)
            {
                SourceFlightId = id,
                AlternateAirport = FirstString(flight, "alt_airport_id"),
                Route = FirstString(flight, "route"),
                PlannedDistance = ReadDistance(flight),
                PlannedDuration = ReadDuration(flight),
                PlannedFuel = ReadFuel(flight),
            };
            destination.TryAdd(id, new(id, plan, FirstString(flight, "briefing")));
        }
    }

    private static bool IsAlreadyFiled(JsonElement pirep)
    {
        int? state = OptionalInt32(pirep, "state");
        return state is 1 or 2 or 6
            || pirep.TryGetProperty("submitted_at", out JsonElement submittedAt)
            && submittedAt.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static string ToPhpVmsPhase(FlightPhase phase) => phase switch
    {
        FlightPhase.Ready => "RDT",
        FlightPhase.Boarding => "BST",
        FlightPhase.Pushback => "PBT",
        FlightPhase.TaxiOut => "TXI",
        FlightPhase.Takeoff => "TOF",
        FlightPhase.Climb => "ICL",
        FlightPhase.Cruise => "ENR",
        FlightPhase.Descent => "ENR",
        FlightPhase.Approach => "APR",
        FlightPhase.Landing => "LDG",
        FlightPhase.TaxiIn => "TXI",
        FlightPhase.OnBlock => "ONB",
        FlightPhase.Completed => "ARR",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown flight phase."),
    };

    private static int CheckedMinutes(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero || duration.TotalMinutes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        return checked((int)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero));
    }

    private static string NestedOrDirect(JsonElement value, string nested, string nestedProperty, string direct)
    {
        if (value.TryGetProperty(nested, out JsonElement nestedValue) &&
            nestedValue.ValueKind == JsonValueKind.Object)
        {
            string? result = OptionalString(nestedValue, nestedProperty);
            if (result is not null)
            {
                return result;
            }
        }

        return RequiredString(value, direct);
    }

    private static string ResolveAircraftId(JsonElement flight, string flightId)
    {
        string? direct = FirstString(flight, "aircraft_id");
        if (direct is not null)
        {
            return direct;
        }

        if (flight.TryGetProperty("aircraft", out JsonElement aircraft)
            && aircraft.ValueKind == JsonValueKind.Object
            && OptionalString(aircraft, "id") is { } aircraftId)
        {
            return aircraftId;
        }

        if (flight.TryGetProperty("simbrief", out JsonElement simbrief)
            && simbrief.ValueKind == JsonValueKind.Object
            && simbrief.TryGetProperty("aircraft", out JsonElement simbriefAircraft)
            && simbriefAircraft.ValueKind == JsonValueKind.Object
            && OptionalString(simbriefAircraft, "id") is { } simbriefAircraftId)
        {
            return simbriefAircraftId;
        }

        if (flight.TryGetProperty("subfleets", out JsonElement subfleets)
            && subfleets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement subfleet in subfleets.EnumerateArray())
            {
                if (!subfleet.TryGetProperty("aircraft", out JsonElement fleetAircraft)
                    || fleetAircraft.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement item in fleetAircraft.EnumerateArray())
                {
                    if (OptionalString(item, "id") is { } fleetAircraftId)
                    {
                        return fleetAircraftId;
                    }
                }
            }
        }

        throw new InvalidDataException(
            $"phpVMS flight '{flightId}' did not include a selectable aircraft.");
    }

    private static string RequiredString(JsonElement value, string property) =>
        OptionalString(value, property)
        ?? throw new InvalidDataException($"phpVMS response omitted required property '{property}'.");

    private static string? FirstString(JsonElement value, params string[] properties)
    {
        foreach (string property in properties)
        {
            string? result = OptionalString(value, property);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static string? OptionalString(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement result))
        {
            return null;
        }

        return result.ValueKind switch
        {
            JsonValueKind.String => result.GetString(),
            JsonValueKind.Number => result.GetRawText(),
            _ => null,
        };
    }

    private static DateTimeOffset? OptionalDateTime(JsonElement value, string property) =>
        value.TryGetProperty(property, out JsonElement result) && result.TryGetDateTimeOffset(out DateTimeOffset parsed)
            ? parsed
            : null;

    private static int? OptionalInt32(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement result))
        {
            return null;
        }

        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out int number))
        {
            return number;
        }

        return result.ValueKind == JsonValueKind.String
            && int.TryParse(
                result.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out number)
            ? number
            : null;
    }

    private static Distance? ReadDistance(JsonElement flight)
    {
        double? nauticalMiles = OptionalDouble(flight, "planned_distance")
            ?? OptionalNestedDouble(flight, "distance", "nmi")
            ?? OptionalDouble(flight, "distance");
        return nauticalMiles is >= 0 and <= double.MaxValue
            ? new Distance(nauticalMiles.Value)
            : null;
    }

    private static TimeSpan? ReadDuration(JsonElement flight)
    {
        int? minutes = OptionalInt32(flight, "planned_flight_time")
            ?? OptionalInt32(flight, "flight_time");
        return minutes is >= 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
    }

    private static FuelMass? ReadFuel(JsonElement flight)
    {
        double? amount = OptionalDouble(flight, "block_fuel");
        return amount is >= 0 and <= double.MaxValue ? new FuelMass(amount.Value) : null;
    }

    private static double? OptionalNestedDouble(
        JsonElement value,
        string property,
        string nestedProperty) =>
        value.TryGetProperty(property, out JsonElement nested)
        && nested.ValueKind == JsonValueKind.Object
            ? OptionalDouble(nested, nestedProperty)
            : null;

    private static double? OptionalDouble(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement result))
        {
            return null;
        }

        if (result.ValueKind == JsonValueKind.Number && result.TryGetDouble(out double number))
        {
            return double.IsFinite(number) ? number : null;
        }

        return result.ValueKind == JsonValueKind.String
            && double.TryParse(
                result.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out number)
            && double.IsFinite(number)
            ? number
            : null;
    }
}

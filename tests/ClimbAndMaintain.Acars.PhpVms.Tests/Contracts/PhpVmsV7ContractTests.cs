using System.Net;
using System.Text;
using System.Text.Json;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;
using Xunit;

namespace ClimbAndMaintain.Acars.PhpVms.Tests.Contracts;

public sealed class PhpVmsV7ContractTests
{
    private static readonly DateTimeOffset CollectedAt =
        new(2026, 9, 8, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task BidsAircraftAndFleetUseOfficialAuthenticatedRoutes()
    {
        RecordingHandler handler = new((_, _) => Json(HttpStatusCode.OK, "{\"data\":[]}"));
        PhpVmsClient client = CreateClient(handler);

        _ = await client.GetBidsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await client
            .GetFlightAircraftAsync("flight/unsafe", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        _ = await client.GetFleetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(
            ["/api/bids", "/api/flights/flight%2Funsafe/aircraft", "/api/fleet"],
            handler.Requests.Select(request => request.Uri.AbsolutePath));
        Assert.All(
            handler.Requests,
            request => Assert.Equal("secret-value", Assert.Single(request.Headers["X-API-Key"])));
    }

    [Fact]
    public async Task SearchUsesV7FieldFiltersInsteadOfUnsupportedFreeTextParameter()
    {
        RecordingHandler handler = new((_, _) => Json(HttpStatusCode.OK, "{\"data\":[]}"));
        PhpVmsClient client = CreateClient(handler);

        _ = await client.SearchFlightsAsync(
            new FlightSearchRequest
            {
                FlightNumber = "CM 42",
                DepartureAirportId = "ksea",
                ArrivalAirportId = "klax",
                IcaoType = "b738",
                Page = 2,
                Limit = 25,
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await client
            .SearchFlightsAsync("CM100", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(
            "?flight_number=CM%2042&dpt_airport_id=KSEA&arr_airport_id=KLAX&icao_type=B738&page=2&limit=25",
            handler.Requests[0].Uri.Query);
        Assert.Equal("?flight_number=CM100", handler.Requests[1].Uri.Query);
        Assert.DoesNotContain("search=", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BriefingPreservesV7XmlDocument()
    {
        const string briefing = "<OFP><flight_number>CM100</flight_number></OFP>";
        RecordingHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(briefing, Encoding.UTF8, "application/xml"),
        });
        PhpVmsClient client = CreateClient(handler);

        PhpVmsDocumentResponse response = await client
            .GetFlightBriefingAsync("flight-1", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.MediaType);
        Assert.Equal(briefing, response.Content);
        Assert.Equal("/api/flights/flight-1/briefing", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    [Fact]
    public async Task PrefileUsesBlockFuelAndCurrentSimulatorFieldNames()
    {
        RecordingHandler handler = new((_, _) => Json(
            HttpStatusCode.Created,
            "{\"data\":{\"id\":\"pirep-1\"}}"));
        PhpVmsClient client = CreateClient(handler);
        PrefilePirepRequest prefile = new()
        {
            AirlineId = "7",
            AircraftId = "aircraft-9",
            FlightNumber = "100",
            DepartureAirportId = "KSEA",
            ArrivalAirportId = "KLAX",
            SimulatorType = 6,
            PlannedDistance = 834,
            PlannedFlightTimeMinutes = 155,
            BlockFuel = 7_500,
        };

        _ = await client
            .PrefilePirepAsync(prefile, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/pireps/prefile", request.Uri.AbsolutePath);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("C&M ACARS", body.RootElement.GetProperty("source_name").GetString());
        Assert.Equal(6, body.RootElement.GetProperty("sim_type").GetInt32());
        Assert.Equal(7_500, body.RootElement.GetProperty("block_fuel").GetDouble());
        Assert.False(body.RootElement.TryGetProperty("planned_fuel", out _));
        Assert.False(body.RootElement.TryGetProperty("simulator", out _));
    }

    [Fact]
    public async Task BackendSendsDurableStartMarkerAndFindsMatchingInProgressPirep()
    {
        const string requestMarker = "CM ACARS abcdefghijklmnop";
        RecordingHandler handler = new((request, _) => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Created,
                """
                {"data":{"id":"pirep-42","created_at":"2026-09-08T12:34:56Z"}}
                """)
            : Json(
                HttpStatusCode.OK,
                $$"""
                {"data":[
                  {"id":"unrelated","source_name":"C&M ACARS"},
                  {"id":"pirep-42","source_name":"{{requestMarker}}","created_at":"2026-09-08T12:34:56Z"}
                ]}
                """));
        PhpVmsBackend backend = new(CreateClient(handler));
        FlightPlan plan = new("7", "aircraft-9", "100", "KSEA", "KLAX")
        {
            SourceFlightId = "flight-1",
        };

        BackendPrefileResult prefile = await backend
            .PrefileAsync(plan, requestMarker, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        BackendPrefileResult? reconciled = await backend
            .FindPrefiledFlightAsync(plan, requestMarker, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal("pirep-42", prefile.BackendFlightId);
        Assert.Equal("pirep-42", reconciled?.BackendFlightId);
        Assert.Equal(2, handler.Requests.Count);
        using JsonDocument body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal(requestMarker, body.RootElement.GetProperty("source_name").GetString());
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/api/pireps", handler.Requests[1].Uri.AbsolutePath);
        Assert.Equal("?state=0&page=1&limit=100", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task UpdateUsesIdempotentV7PutRouteAndRetriesTransientFailure()
    {
        int call = 0;
        List<TimeSpan> delays = [];
        RecordingHandler handler = new((_, _) =>
        {
            call++;
            return Json(
                call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                call == 1 ? "{\"message\":\"busy\"}" : "{\"data\":{\"id\":\"pirep-1\"}}");
        });
        PhpVmsClient client = CreateClient(handler, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        UpdatePirepRequest update = new()
        {
            AircraftId = "aircraft-9",
            Status = "ENR",
            Distance = 410.5,
            FlightTimeMinutes = 72,
            FuelUsed = 1_250,
            Route = "SEA J5 LAX",
            SourceName = "C&M ACARS",
        };

        _ = await client
            .UpdatePirepAsync("pirep/unsafe", update, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Put, request.Method));
        Assert.All(
            handler.Requests,
            request => Assert.Equal("/api/pireps/pirep%2Funsafe/update", request.Uri.AbsolutePath));
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delays));
        using JsonDocument body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("ENR", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(410.5, body.RootElement.GetProperty("distance").GetDouble());
        Assert.Equal(72, body.RootElement.GetProperty("flight_time").GetInt32());
        Assert.Equal(1_250, body.RootElement.GetProperty("fuel_used").GetDouble());
    }

    [Fact]
    public async Task RetriedPositionBatchKeepsStableClientIdsAndBody()
    {
        int call = 0;
        RecordingHandler handler = new((_, _) =>
        {
            call++;
            return Json(
                call == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
                call == 1 ? "{\"message\":\"failed\"}" : "{\"data\":1}");
        });
        PhpVmsClient client = CreateClient(handler, (_, _) => Task.CompletedTask);
        Guid pointId = Guid.Parse("5e637b77-f7bf-4086-893c-fc9a80b3654f");

        _ = await client.SendPositionsAsync(
            "pirep-1",
            [new AcarsPosition
            {
                Id = pointId,
                Latitude = 47.4502,
                Longitude = -122.3088,
                CollectedAtUtc = CollectedAt,
            }],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        using JsonDocument body = JsonDocument.Parse(handler.Requests[1].Body!);
        JsonElement position = Assert.Single(body.RootElement.GetProperty("positions").EnumerateArray());
        Assert.Equal(PhpVmsClientId.Encode(pointId), position.GetProperty("id").GetString());
        Assert.Equal(16, position.GetProperty("id").GetString()!.Length);
    }

    [Fact]
    public async Task EventsAndLogsUseOfficialBatchContractsWithCompactStableIds()
    {
        RecordingHandler handler = new((_, _) => Json(HttpStatusCode.OK, "{\"data\":1}"));
        PhpVmsClient client = CreateClient(handler);
        Guid eventId = Guid.Parse("3e9fd289-d40d-411f-8119-e06f6e0be656");
        Guid logId = Guid.Parse("c9da1913-08aa-4bc1-860b-bf6714a1492e");

        _ = await client.SendEventsAsync(
            "pirep-1",
            [new AcarsEvent
            {
                Id = eventId,
                Event = "Takeoff: runway 16L",
                CreatedAtUtc = CollectedAt,
            }],
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await client.SendLogsAsync(
            "pirep-1",
            [new AcarsLogEntry
            {
                Id = logId,
                Message = "Positive rate",
                CreatedAtUtc = CollectedAt.AddSeconds(1),
            }],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(
            ["/api/pireps/pirep-1/acars/events", "/api/pireps/pirep-1/acars/logs"],
            handler.Requests.Select(request => request.Uri.AbsolutePath));
        using JsonDocument eventBody = JsonDocument.Parse(handler.Requests[0].Body!);
        JsonElement recordedEvent = Assert.Single(
            eventBody.RootElement.GetProperty("events").EnumerateArray());
        Assert.Equal(PhpVmsClientId.Encode(eventId), recordedEvent.GetProperty("id").GetString());
        Assert.Equal("Takeoff: runway 16L", recordedEvent.GetProperty("event").GetString());
        Assert.False(recordedEvent.TryGetProperty("sim_time", out _));
        using JsonDocument logBody = JsonDocument.Parse(handler.Requests[1].Body!);
        JsonElement recordedLog = Assert.Single(
            logBody.RootElement.GetProperty("logs").EnumerateArray());
        Assert.Equal(PhpVmsClientId.Encode(logId), recordedLog.GetProperty("id").GetString());
        Assert.Equal("Positive rate", recordedLog.GetProperty("log").GetString());
        Assert.False(recordedLog.TryGetProperty("sim_time", out _));
    }

    [Fact]
    public async Task CancelRetriesTransientFailureUsingOfficialPutRoute()
    {
        int call = 0;
        RecordingHandler handler = new((_, _) =>
        {
            call++;
            return Json(
                call == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.OK,
                call == 1 ? "{\"message\":\"upstream unavailable\"}" : "{\"data\":1}");
        });
        PhpVmsClient client = CreateClient(handler, (_, _) => Task.CompletedTask);

        _ = await client
            .CancelPirepAsync("pirep-1", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Put, request.Method));
        Assert.All(
            handler.Requests,
            request => Assert.Equal("/api/pireps/pirep-1/cancel", request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task RepeatedCancelTreatsOfficialAlreadyCancelledProblemAsConverged()
    {
        RecordingHandler handler = new((_, _) => Json(
            HttpStatusCode.BadRequest,
            """
            {"type":"https://va.example/api-errors/pirep-cancel-not-allowed","title":"This PIREP can't be cancelled","details":"This PIREP can't be cancelled","status":400,"pirep_id":"pirep-1","state":3}
            """));
        PhpVmsClient client = CreateClient(handler);

        PhpVmsResponse response = await client
            .CancelPirepAsync("pirep-1", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, response.Data.GetProperty("state").GetInt32());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancelStillFailsWhenPhpVmsReportsNonCancelledTerminalState()
    {
        RecordingHandler handler = new((_, _) => Json(
            HttpStatusCode.BadRequest,
            """
            {"type":"https://va.example/api-errors/pirep-cancel-not-allowed","title":"This PIREP can't be cancelled","status":400,"pirep_id":"pirep-1","state":2}
            """));
        PhpVmsClient client = CreateClient(handler);

        PhpVmsApiException exception = await Assert.ThrowsAsync<PhpVmsApiException>(
            () => client.CancelPirepAsync("pirep-1", TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        Assert.Equal(2, exception.PirepState);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NetworkInterruptionRetriesOnlySafeRequest()
    {
        int call = 0;
        RecordingHandler handler = new((_, _) =>
        {
            call++;
            if (call == 1)
            {
                throw new HttpRequestException("Connection reset.");
            }

            return Json(HttpStatusCode.OK, "{\"data\":[]}");
        });
        PhpVmsClient client = CreateClient(handler, (_, _) => Task.CompletedTask);

        _ = await client.GetBidsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count);

        RecordingHandler unsafeHandler = new((_, _) => throw new HttpRequestException("Connection reset."));
        PhpVmsClient unsafeClient = CreateClient(unsafeHandler, (_, _) => Task.CompletedTask);
        PrefilePirepRequest prefile = CreatePrefileRequest();

        _ = await Assert.ThrowsAsync<HttpRequestException>(
            () => unsafeClient.PrefilePirepAsync(prefile, TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        Assert.Single(unsafeHandler.Requests);
    }

    [Fact]
    public async Task RequestTimeoutRetriesSafeRequest()
    {
        int call = 0;
        RecordingHandler handler = new((_, _) =>
        {
            call++;
            return call == 1
                ? throw new OperationCanceledException("The transport timed out.")
                : Json(HttpStatusCode.OK, "{\"data\":[]}");
        });
        PhpVmsClient client = CreateClient(handler, (_, _) => Task.CompletedTask);

        _ = await client.GetFleetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ClientErrorsAreNotRetriedAndCredentialsAreRedacted(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new((_, _) => Json(
            statusCode,
            "{\"message\":\"Rejected secret-value\"}"));
        PhpVmsClient client = CreateClient(handler);

        PhpVmsApiException exception = await Assert.ThrowsAsync<PhpVmsApiException>(
            () => client.GetBidsAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("secret-value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedServerFailureStopsAtConfiguredAttemptLimit()
    {
        List<TimeSpan> delays = [];
        RecordingHandler handler = new((_, _) => Json(
            HttpStatusCode.InternalServerError,
            "{\"message\":\"failed\"}"));
        PhpVmsClient client = CreateClient(handler, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        PhpVmsApiException exception = await Assert.ThrowsAsync<PhpVmsApiException>(
            () => client.GetFleetAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task PrefileAndFileDoNotRetryAmbiguousServerFailure()
    {
        RecordingHandler handler = new((_, _) => Json(
            HttpStatusCode.InternalServerError,
            "{\"message\":\"failed\"}"));
        PhpVmsClient client = CreateClient(handler, (_, _) => Task.CompletedTask);
        PrefilePirepRequest prefile = new()
        {
            AirlineId = "7",
            AircraftId = "aircraft-9",
            FlightNumber = "100",
            DepartureAirportId = "KSEA",
            ArrivalAirportId = "KLAX",
        };

        _ = await Assert.ThrowsAsync<PhpVmsApiException>(
            () => client.PrefilePirepAsync(prefile, TestContext.Current.CancellationToken))
            .ConfigureAwait(true);
        _ = await Assert.ThrowsAsync<PhpVmsApiException>(
            () => client.FilePirepAsync(
                "pirep-1",
                new FilePirepRequest { Distance = 800, FlightTimeMinutes = 120 },
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SuccessfulFileUsesRequiredDistanceAndFlightTimeContract()
    {
        RecordingHandler handler = new((_, _) => Json(
            HttpStatusCode.OK,
            "{\"data\":{\"id\":\"pirep-1\"}}"));
        PhpVmsClient client = CreateClient(handler);

        _ = await client.FilePirepAsync(
            "pirep/unsafe",
            new FilePirepRequest
            {
                Distance = 834.25,
                FlightTimeMinutes = 155,
                BlockTimeMinutes = 177,
                FuelUsed = 5_250,
                AircraftId = "aircraft-9",
                Route = "SEA J5 LAX",
                LandingRate = -132,
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/pireps/pirep%2Funsafe/file", request.Uri.AbsolutePath);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(834.25, body.RootElement.GetProperty("distance").GetDouble());
        Assert.Equal(155, body.RootElement.GetProperty("flight_time").GetInt32());
        Assert.Equal(177, body.RootElement.GetProperty("block_time").GetInt32());
        Assert.Equal(5_250, body.RootElement.GetProperty("fuel_used").GetDouble());
        Assert.Equal("aircraft-9", body.RootElement.GetProperty("aircraft_id").GetString());
        Assert.Equal("C&M ACARS", body.RootElement.GetProperty("source_name").GetString());
    }

    [Fact]
    public async Task BackendFilesEveryReliablyKnownPlanAndTimingField()
    {
        RecordingHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith(
            "/file",
            StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, "{\"data\":{\"id\":\"pirep-1\"}}")
            : Json(HttpStatusCode.OK, "{\"data\":{\"id\":\"pirep-1\",\"state\":0}}"));
        PhpVmsBackend backend = new(CreateClient(handler));
        FlightPlan plan = new("7", "aircraft-9", "100", "KSEA", "KLAX")
        {
            Route = "SEA J5 LAX",
            PlannedDistance = new Distance(840),
            PlannedDuration = TimeSpan.FromMinutes(160),
            PlannedFuel = new FuelMass(7_500),
        };
        CompletedFlightReport report = new(
            new Distance(834.25),
            TimeSpan.FromMinutes(155),
            TimeSpan.FromMinutes(177),
            new FuelMass(5_250),
            new VerticalSpeed(-132),
            CollectedAt.AddMinutes(177))
        {
            FilingPlan = plan,
            TrackingStartedAtUtc = CollectedAt,
        };

        await backend
            .FileFlightAsync("pirep-1", report, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count);
        RecordedRequest request = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, request.Method);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(
            [
                "distance",
                "flight_time",
                "fuel_used",
                "block_time",
                "airline_id",
                "aircraft_id",
                "flight_number",
                "dpt_airport_id",
                "arr_airport_id",
                "planned_distance",
                "planned_flight_time",
                "block_fuel",
                "route",
                "landing_rate",
                "block_off_time",
                "block_on_time",
                "source_name",
            ],
            body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(834.25, body.RootElement.GetProperty("distance").GetDouble());
        Assert.Equal(155, body.RootElement.GetProperty("flight_time").GetInt32());
        Assert.Equal(177, body.RootElement.GetProperty("block_time").GetInt32());
        Assert.Equal(5_250, body.RootElement.GetProperty("fuel_used").GetDouble());
        Assert.Equal("7", body.RootElement.GetProperty("airline_id").GetString());
        Assert.Equal("aircraft-9", body.RootElement.GetProperty("aircraft_id").GetString());
        Assert.Equal("100", body.RootElement.GetProperty("flight_number").GetString());
        Assert.Equal("KSEA", body.RootElement.GetProperty("dpt_airport_id").GetString());
        Assert.Equal("KLAX", body.RootElement.GetProperty("arr_airport_id").GetString());
        Assert.Equal(840, body.RootElement.GetProperty("planned_distance").GetDouble());
        Assert.Equal(160, body.RootElement.GetProperty("planned_flight_time").GetInt32());
        Assert.Equal(7_500, body.RootElement.GetProperty("block_fuel").GetDouble());
        Assert.Equal("SEA J5 LAX", body.RootElement.GetProperty("route").GetString());
        Assert.Equal(-132, body.RootElement.GetProperty("landing_rate").GetDouble());
        Assert.Equal(
            CollectedAt,
            body.RootElement.GetProperty("block_off_time").GetDateTimeOffset());
        Assert.Equal(
            CollectedAt.AddMinutes(177),
            body.RootElement.GetProperty("block_on_time").GetDateTimeOffset());
        Assert.Equal("C&M ACARS", body.RootElement.GetProperty("source_name").GetString());
    }

    [Fact]
    public async Task AvailableFlightPreservesReliablePlanningValues()
    {
        RecordingHandler handler = new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/bids" => Json(HttpStatusCode.OK, "{\"data\":[]}"),
            "/api/flights" => Json(
                HttpStatusCode.OK,
                """
                {"data":[{"id":"flight-1","airline_id":7,"aircraft_id":"aircraft-9","flight_number":"100","dpt_airport_id":"KSEA","arr_airport_id":"KLAX","route":"SEA J5 LAX","distance":{"nmi":840.5},"flight_time":"160","block_fuel":"7500.25"}]}
                """),
            _ => throw new InvalidOperationException("Unexpected phpVMS request."),
        });
        PhpVmsBackend backend = new(CreateClient(handler));

        BackendFlight flight = Assert.Single(await backend
            .GetAvailableFlightsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true));

        Assert.Equal("SEA J5 LAX", flight.FlightPlan.Route);
        Assert.Equal(840.5, flight.FlightPlan.PlannedDistance?.NauticalMiles);
        Assert.Equal(TimeSpan.FromMinutes(160), flight.FlightPlan.PlannedDuration);
        Assert.Equal(7_500.25, flight.FlightPlan.PlannedFuel?.Kilograms);
    }

    [Fact]
    public async Task BackendMapsV7FlightAircraftAndOfficialPhaseCodes()
    {
        RecordingHandler handler = new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/bids" => Json(
                HttpStatusCode.OK,
                """
                {"data":[{"id":91,"flight_id":"bid-flight","flight":{"id":"bid-flight","airline_id":7,"flight_number":"200","dpt_airport_id":"KLAX","arr_airport_id":"KSFO","subfleets":[{"aircraft":[{"id":"aircraft-10"}]}]}}]}
                """),
            "/api/flights" => Json(
                HttpStatusCode.OK,
                """
                {"data":[{"id":"flight-1","airline_id":7,"flight_number":"100","dpt_airport_id":"KSEA","arr_airport_id":"KLAX","subfleets":[{"aircraft":[{"id":"aircraft-9"}]}]}]}
                """),
            _ => Json(HttpStatusCode.OK, "{\"data\":1}"),
        });
        PhpVmsClient client = CreateClient(handler);
        PhpVmsBackend backend = new(client);

        IReadOnlyList<BackendFlight> flights = await backend
            .GetAvailableFlightsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        FlightSessionId sessionId = FlightSessionId.New();
        await backend.SendPositionsAsync(
            "pirep-1",
            [
                CreatePosition(sessionId, FlightPhase.Ready, CollectedAt),
                CreatePosition(sessionId, FlightPhase.TaxiOut, CollectedAt.AddSeconds(1)),
                CreatePosition(sessionId, FlightPhase.Descent, CollectedAt.AddSeconds(2)),
            ],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, flights.Count);
        BackendFlight flight = Assert.Single(flights, item => item.Id == "flight-1");
        Assert.Equal("aircraft-9", flight.FlightPlan.AircraftId);
        Assert.Contains(flights, item => item.Id == "bid-flight" && item.FlightPlan.AircraftId == "aircraft-10");
        RecordedRequest positionRequest = handler.Requests[2];
        using JsonDocument body = JsonDocument.Parse(positionRequest.Body!);
        Assert.Equal(
            ["RDT", "TXI", "ENR"],
            body.RootElement
                .GetProperty("positions")
                .EnumerateArray()
                .Select(item => item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task BackendReconcilesAnAmbiguousFileResponseWithoutPostingTwice()
    {
        int state = 0;
        RecordingHandler handler = new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/pireps/pirep-1" => Json(
                HttpStatusCode.OK,
                $"{{\"data\":{{\"id\":\"pirep-1\",\"state\":{state}}}}}"),
            "/api/pireps/pirep-1/file" when state == 0 => ChangeStateAndLoseResponse(),
            _ => throw new InvalidOperationException("Unexpected phpVMS request."),
        });
        PhpVmsBackend backend = new(CreateClient(handler));
        CompletedFlightReport report = new(
            new Distance(834),
            TimeSpan.FromMinutes(155),
            TimeSpan.FromMinutes(177),
            new FuelMass(5_250),
            new VerticalSpeed(-132),
            CollectedAt);

        await Assert.ThrowsAsync<HttpRequestException>(() => backend
            .FileFlightAsync("pirep-1", report, TestContext.Current.CancellationToken)
            .AsTask()).ConfigureAwait(true);
        await backend
            .FileFlightAsync("pirep-1", report, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(1, handler.Requests.Count(request =>
            request.Uri.AbsolutePath == "/api/pireps/pirep-1/file"));

        HttpResponseMessage ChangeStateAndLoseResponse()
        {
            state = 1;
            throw new HttpRequestException("The response was lost after phpVMS committed the PIREP.");
        }
    }

    [Fact]
    public async Task ConnectionTestReportsMalformedServerResponseWithoutThrowing()
    {
        RecordingHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain"),
        });
        PhpVmsBackend backend = new(CreateClient(handler));

        BackendConnectionResult result = await backend
            .TestConnectionAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureMessage);
    }

    private static PositionReport CreatePosition(
        FlightSessionId sessionId,
        FlightPhase phase,
        DateTimeOffset collectedAtUtc) => new(
            PositionReportId.New(),
            sessionId,
            phase,
            new TelemetrySnapshot
            {
                CollectedAtUtc = collectedAtUtc,
                Position = new GeoPosition(47.4502, -122.3088),
                AltitudeMsl = new Altitude(12_000),
                AltitudeAgl = new Altitude(10_000),
                IndicatedAirspeed = new Speed(250),
                GroundSpeed = new Speed(310),
                VerticalSpeed = new VerticalSpeed(-1_200),
                OnGround = false,
            });

    private static PrefilePirepRequest CreatePrefileRequest() => new()
    {
        AirlineId = "7",
        AircraftId = "aircraft-9",
        FlightNumber = "100",
        DepartureAirportId = "KSEA",
        ArrivalAirportId = "KLAX",
    };

    private static PhpVmsClient CreateClient(
        RecordingHandler handler,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(
            new HttpClient(handler),
            new PhpVmsOptions(new("https://va.example/"), maximumAttempts: 3),
            new FixedCredentialProvider(new ApiKeyCredential("secret-value")),
            delay,
            () => 0);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(
                    item => item.Key,
                    item => item.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                body));
            return responseFactory(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string[]> Headers,
        string? Body);
}

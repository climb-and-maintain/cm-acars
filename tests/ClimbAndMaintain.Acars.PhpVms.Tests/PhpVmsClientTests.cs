using System.Net;
using System.Text;
using System.Text.Json;
using ClimbAndMaintain.Acars.Application.Contracts;
using Xunit;

namespace ClimbAndMaintain.Acars.PhpVms.Tests;

public sealed class PhpVmsClientTests
{
    [Fact]
    public async Task GetCurrentUserSendsApiKeyWithoutPuttingItInUri()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, "{\"data\":{\"id\":\"pilot-1\"}}"));
        var client = CreateClient(handler);

        var result = await client.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal("pilot-1", result.Data.GetProperty("id").GetString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("secret-value", Assert.Single(request.Headers["X-API-Key"]));
        Assert.DoesNotContain("secret-value", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("/api/user", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task SendPositionsUsesExactPhpVmsBatchContract()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, "{\"data\":1}"));
        var client = CreateClient(handler);
        var pointId = Guid.Parse("5e637b77-f7bf-4086-893c-fc9a80b3654f");
        var collected = new DateTimeOffset(2026, 9, 8, 12, 34, 56, TimeSpan.Zero);

        await client.SendPositionsAsync(
            "pirep/unsafe",
            [new AcarsPosition
            {
                Id = pointId,
                Latitude = 47.4502,
                Longitude = -122.3088,
                AltitudeMsl = 4321,
                GroundSpeed = 210,
                CollectedAtUtc = collected,
            }],
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/pireps/pirep%2Funsafe/acars/positions", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body!);
        var position = Assert.Single(body.RootElement.GetProperty("positions").EnumerateArray());
        Assert.Equal(PhpVmsClientId.Encode(pointId), position.GetProperty("id").GetString());
        Assert.Equal(16, position.GetProperty("id").GetString()!.Length);
        Assert.Equal(47.4502, position.GetProperty("lat").GetDouble());
        Assert.Equal(-122.3088, position.GetProperty("lon").GetDouble());
        Assert.Equal(4321, position.GetProperty("altitude_msl").GetDouble());
        Assert.Equal("2026-09-08T12:34:56+00:00", position.GetProperty("created_at").GetString());
        Assert.False(position.TryGetProperty("altitude_agl", out _));
    }

    [Fact]
    public async Task SendPositionsHonorsRetryAfterFor429()
    {
        var call = 0;
        var delays = new List<TimeSpan>();
        var telemetry = new List<PhpVmsRequestTelemetry>();
        var handler = new RecordingHandler((_, _) =>
        {
            call++;
            var response = Json(call == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, "{\"data\":1}");
            if (call == 1)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            }

            return response;
        });
        var client = CreateClient(handler, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        }, telemetry.Add);

        await client.SendPositionsAsync(
            "p1",
            [new AcarsPosition
            {
                Id = Guid.NewGuid(),
                Latitude = 1,
                Longitude = 2,
                CollectedAtUtc = DateTimeOffset.UtcNow,
            }],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(TimeSpan.FromSeconds(17), Assert.Single(delays));
        Assert.Collection(
            telemetry,
            first =>
            {
                Assert.Equal(PhpVmsRequestTelemetryKind.ResponseReceived, first.Kind);
                Assert.Equal("pireps.positions", first.Endpoint);
                Assert.Equal(429, first.StatusCode);
                Assert.Equal(1, first.Attempt);
            },
            retry =>
            {
                Assert.Equal(PhpVmsRequestTelemetryKind.RetryScheduled, retry.Kind);
                Assert.Equal("pireps.positions", retry.Endpoint);
                Assert.Equal(TimeSpan.FromSeconds(17), retry.RetryDelay);
            },
            last =>
            {
                Assert.Equal(PhpVmsRequestTelemetryKind.ResponseReceived, last.Kind);
                Assert.Equal(200, last.StatusCode);
                Assert.Equal(2, last.Attempt);
            });
        Assert.All(telemetry, item =>
        {
            Assert.DoesNotContain("secret-value", item.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("p1", item.Endpoint, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RequestTelemetryUsesFixedEndpointCategoryWithoutQueryValues()
    {
        var telemetry = new List<PhpVmsRequestTelemetry>();
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, "{\"data\":[]}"));
        var client = CreateClient(handler, observeRequest: telemetry.Add);

        await client.SearchFlightsAsync(
            new FlightSearchRequest { FlightNumber = "PRIVATE-QUERY-VALUE" },
            TestContext.Current.CancellationToken);

        PhpVmsRequestTelemetry observed = Assert.Single(telemetry);
        Assert.Equal("flights.search", observed.Endpoint);
        Assert.Equal("GET", observed.Method);
        Assert.DoesNotContain("PRIVATE-QUERY-VALUE", observed.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("va.example", observed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalTransportFailureEmitsSafeFailureType()
    {
        var telemetry = new List<PhpVmsRequestTelemetry>();
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("PRIVATE-DETAIL"));
        var client = CreateClient(handler, observeRequest: telemetry.Add);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PrefilePirepAsync(
                new PrefilePirepRequest
                {
                    AirlineId = "VA",
                    AircraftId = "N1",
                    FlightNumber = "100",
                    DepartureAirportId = "KSEA",
                    ArrivalAirportId = "KSFO",
                },
                TestContext.Current.CancellationToken));

        PhpVmsRequestTelemetry failure = Assert.Single(telemetry);
        Assert.Equal(PhpVmsRequestTelemetryKind.RequestFailed, failure.Kind);
        Assert.Equal("pireps.prefile", failure.Endpoint);
        Assert.Equal(nameof(HttpRequestException), failure.FailureType);
        Assert.DoesNotContain("PRIVATE-DETAIL", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrefileDoesNotRetryNonIdempotentRequest()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.InternalServerError, "{\"message\":\"failed\"}"));
        var client = CreateClient(handler);
        var request = new PrefilePirepRequest
        {
            AirlineId = "VA",
            AircraftId = "N1",
            FlightNumber = "100",
            DepartureAirportId = "KSEA",
            ArrivalAirportId = "KSFO",
        };

        var exception = await Assert.ThrowsAsync<PhpVmsApiException>(
            () => client.PrefilePirepAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("secret-value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsRejectsHttpUnlessExplicitlyEnabled()
    {
        Assert.Throws<ArgumentException>(() => new PhpVmsOptions(new("http://example.test")));
        Assert.Throws<ArgumentException>(() => new PhpVmsOptions(
            new("http://example.test"),
            allowInsecureHttp: true));
        var options = new PhpVmsOptions(new("http://localhost:8080"), allowInsecureHttp: true);
        Assert.True(options.AllowInsecureHttp);
    }

    private static PhpVmsClient CreateClient(
        RecordingHandler handler,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<PhpVmsRequestTelemetry>? observeRequest = null) =>
        new(
            new HttpClient(handler),
            new PhpVmsOptions(new("https://va.example/"), maximumAttempts: 3),
            new FixedCredentialProvider(new ApiKeyCredential("secret-value")),
            delay,
            () => 0,
            observeRequest);

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
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
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

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClimbAndMaintain.Acars.Application.Contracts;

namespace ClimbAndMaintain.Acars.PhpVms;

public sealed class PhpVmsClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly string ProductVersion = GetProductVersion();

    private readonly HttpClient httpClient;
    private readonly PhpVmsOptions options;
    private readonly IBackendCredentialProvider credentials;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Func<double> jitter;
    private readonly Action<PhpVmsRequestTelemetry>? observeRequest;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new PhpVmsClientIdJsonConverter());
        return options;
    }

    public PhpVmsClient(
        HttpClient httpClient,
        PhpVmsOptions options,
        IBackendCredentialProvider credentials,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<double>? jitter = null,
        Action<PhpVmsRequestTelemetry>? observeRequest = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.options.Validate();
        this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        this.delayAsync = delayAsync ?? Task.Delay;
        this.jitter = jitter ?? Random.Shared.NextDouble;
        this.observeRequest = observeRequest;
    }

    public Task<PhpVmsResponse> GetStatusAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, "api/status", null, false, true, cancellationToken);

    public Task<PhpVmsResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, "api/user", null, true, true, cancellationToken);

    public Task<PhpVmsResponse> GetBidsAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, "api/bids", null, true, true, cancellationToken);

    public Task<PhpVmsResponse> GetFlightsAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, "api/flights", null, true, true, cancellationToken);

    public Task<PhpVmsResponse> GetPirepsAsync(
        int state,
        int page,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        string path = FormattableString.Invariant(
            $"api/pireps?state={state}&page={page}&limit={limit}");
        return SendJsonAsync(HttpMethod.Get, path, null, true, true, cancellationToken);
    }

    public Task<PhpVmsResponse> SearchFlightsAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return SearchFlightsAsync(
            new FlightSearchRequest { FlightNumber = query.Trim() },
            cancellationToken);
    }

    public Task<PhpVmsResponse> SearchFlightsAsync(
        FlightSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string query = request.ToQueryString();
        string path = string.IsNullOrEmpty(query) ? "api/flights/search" : $"api/flights/search?{query}";
        return SendJsonAsync(HttpMethod.Get, path, null, true, true, cancellationToken);
    }

    public Task<PhpVmsDocumentResponse> GetFlightBriefingAsync(
        string flightId,
        CancellationToken cancellationToken = default) =>
        SendDocumentAsync(
            HttpMethod.Get,
            ResourcePath("api/flights", flightId, "briefing"),
            null,
            true,
            true,
            cancellationToken);

    public Task<PhpVmsResponse> GetFlightAircraftAsync(string flightId, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, ResourcePath("api/flights", flightId, "aircraft"), null, true, true, cancellationToken);

    public Task<PhpVmsResponse> GetFleetAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, "api/fleet", null, true, true, cancellationToken);

    public Task<PhpVmsResponse> PrefilePirepAsync(PrefilePirepRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, "api/pireps/prefile", request, true, false, cancellationToken);

    public Task<PhpVmsResponse> GetPirepAsync(
        string pirepId,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            HttpMethod.Get,
            ResourcePath("api/pireps", pirepId),
            null,
            true,
            true,
            cancellationToken);

    public Task<PhpVmsResponse> UpdatePirepAsync(
        string pirepId,
        UpdatePirepRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendJsonAsync(
            HttpMethod.Put,
            ResourcePath("api/pireps", pirepId, "update"),
            request,
            true,
            true,
            cancellationToken);
    }

    public Task<PhpVmsResponse> SendPositionsAsync(
        string pirepId,
        IReadOnlyList<AcarsPosition> positions,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(positions);
        return SendJsonAsync(HttpMethod.Post, ResourcePath("api/pireps", pirepId, "acars/positions"), new { positions }, true, true, cancellationToken);
    }

    public Task<PhpVmsResponse> SendEventsAsync(
        string pirepId,
        IReadOnlyList<AcarsEvent> events,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(events);
        return SendJsonAsync(HttpMethod.Post, ResourcePath("api/pireps", pirepId, "acars/events"), new { events }, true, true, cancellationToken);
    }

    public Task<PhpVmsResponse> SendLogsAsync(
        string pirepId,
        IReadOnlyList<AcarsLogEntry> logs,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(logs);
        return SendJsonAsync(HttpMethod.Post, ResourcePath("api/pireps", pirepId, "acars/logs"), new { logs }, true, true, cancellationToken);
    }

    public Task<PhpVmsResponse> FilePirepAsync(
        string pirepId,
        FilePirepRequest request,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, ResourcePath("api/pireps", pirepId, "file"), request, true, false, cancellationToken);

    public async Task<PhpVmsResponse> CancelPirepAsync(
        string pirepId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendJsonAsync(
                HttpMethod.Put,
                ResourcePath("api/pireps", pirepId, "cancel"),
                new { },
                true,
                true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (PhpVmsApiException exception) when (IsAlreadyCancelled(exception))
        {
            using JsonDocument document = JsonDocument.Parse("{\"state\":3}");
            return new PhpVmsResponse(HttpStatusCode.OK, document.RootElement.Clone());
        }
    }

    private Task<PhpVmsResponse> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        bool authenticated,
        bool retrySafe,
        CancellationToken cancellationToken) =>
        SendAsync(
            method,
            relativePath,
            body,
            authenticated,
            retrySafe,
            ReadJsonResponseAsync,
            cancellationToken);

    private Task<PhpVmsDocumentResponse> SendDocumentAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        bool authenticated,
        bool retrySafe,
        CancellationToken cancellationToken) =>
        SendAsync(
            method,
            relativePath,
            body,
            authenticated,
            retrySafe,
            ReadDocumentResponseAsync,
            cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? body,
        bool authenticated,
        bool retrySafe,
        Func<HttpResponseMessage, CancellationToken, Task<TResponse>> readSuccessAsync,
        CancellationToken cancellationToken)
    {
        string endpoint = ClassifyEndpoint(relativePath);
        var credential = authenticated
            ? await credentials.GetCredentialAsync(cancellationToken).ConfigureAwait(false)
            : null;
        if (authenticated && credential is null)
        {
            throw new InvalidOperationException("A phpVMS credential is required for this operation.");
        }

        string? serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        for (int attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout);
            try
            {
                using HttpRequestMessage request = CreateRequest(method, relativePath, serializedBody, credential);
                using HttpResponseMessage response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                ReportRequest(new(
                    PhpVmsRequestTelemetryKind.ResponseReceived,
                    method.Method,
                    endpoint,
                    attempt,
                    options.MaximumAttempts,
                    DateTimeOffset.UtcNow,
                    (int)response.StatusCode));

                if (response.IsSuccessStatusCode)
                {
                    return await readSuccessAsync(response, timeout.Token).ConfigureAwait(false);
                }

                TimeSpan? retryAfter = GetRetryAfter(response);
                if (retrySafe && IsTransient(response.StatusCode) && attempt < options.MaximumAttempts)
                {
                    TimeSpan retryDelay = GetRetryDelay(attempt, retryAfter);
                    ReportRetry(method, endpoint, attempt, (int)response.StatusCode, retryDelay, null);
                    await delayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                PhpVmsErrorDetail error = await ReadErrorDetailAsync(response, timeout.Token).ConfigureAwait(false);
                string detail = RedactCredential(error.Detail, credential);
                throw new PhpVmsApiException(
                    response.StatusCode,
                    $"phpVMS returned {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}",
                    retryAfter,
                    error.ProblemType,
                    error.PirepState);
            }
            catch (PhpVmsApiException)
            {
                throw;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                ReportRequestFailure(method, endpoint, attempt, exception);
                if (retrySafe && attempt < options.MaximumAttempts)
                {
                    TimeSpan retryDelay = GetRetryDelay(attempt, null);
                    ReportRetry(method, endpoint, attempt, null, retryDelay, exception.GetType().Name);
                    await delayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new HttpRequestException(
                    "The phpVMS request timed out before a complete response was received.",
                    exception);
            }
            catch (HttpRequestException exception) when (retrySafe && attempt < options.MaximumAttempts)
            {
                ReportRequestFailure(method, endpoint, attempt, exception);
                TimeSpan retryDelay = GetRetryDelay(attempt, null);
                ReportRetry(method, endpoint, attempt, null, retryDelay, exception.GetType().Name);
                await delayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                ReportRequestFailure(method, endpoint, attempt, exception);
                throw;
            }
        }

        throw new InvalidOperationException("The phpVMS request loop ended unexpectedly.");
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath,
        string? serializedBody,
        BackendCredential? credential)
    {
        var request = new HttpRequestMessage(method, new Uri(options.BaseUri, relativePath));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ClimbAndMaintain-ACARS", ProductVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        switch (credential)
        {
            case ApiKeyCredential apiKey:
                request.Headers.Add("X-API-Key", apiKey.ApiKey);
                break;
            case BearerCredential token:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                break;
            case not null:
                request.Dispose();
                throw new InvalidOperationException("The configured phpVMS credential is empty.");
        }

        if (serializedBody is not null)
        {
            request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static string GetProductVersion()
    {
        string? informationalVersion = typeof(PhpVmsClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        string version = string.IsNullOrWhiteSpace(informationalVersion)
            ? typeof(PhpVmsClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
            : informationalVersion.Split('+', 2)[0];
        return version;
    }

    private static async Task<PhpVmsResponse> ReadJsonResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            using JsonDocument empty = JsonDocument.Parse("{}");
            return new PhpVmsResponse(response.StatusCode, empty.RootElement.Clone());
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement data = document.RootElement.TryGetProperty("data", out JsonElement wrappedData)
            ? wrappedData
            : document.RootElement;
        return new PhpVmsResponse(response.StatusCode, data.Clone());
    }

    private static async Task<PhpVmsDocumentResponse> ReadDocumentResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new PhpVmsDocumentResponse(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            content);
    }

    private static async Task<PhpVmsErrorDetail> ReadErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new PhpVmsErrorDetail(string.Empty, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            string? message = FirstErrorString(root, "message", "details", "title");
            string? problemType = StringProperty(root, "type");
            int? pirepState = IntegerProperty(root, "state");
            string detail = message is null
                ? " See Diagnostics for the sanitized response metadata."
                : $" {message}";
            return new PhpVmsErrorDetail(detail, problemType, pirepState);
        }
        catch (JsonException)
        {
            // A non-JSON server/proxy error is intentionally not reflected because it may contain sensitive HTML.
        }

        return new PhpVmsErrorDetail(
            " See Diagnostics for the sanitized response metadata.",
            null,
            null);
    }

    private static string? FirstErrorString(JsonElement value, params string[] properties)
    {
        foreach (string property in properties)
        {
            if (StringProperty(value, property) is { } result)
            {
                return result;
            }
        }

        if (value.TryGetProperty("error", out JsonElement error)
            && error.ValueKind == JsonValueKind.Object)
        {
            return StringProperty(error, "message");
        }

        return null;
    }

    private static string? StringProperty(JsonElement value, string property) =>
        value.TryGetProperty(property, out JsonElement result) && result.ValueKind == JsonValueKind.String
            ? result.GetString()
            : null;

    private static int? IntegerProperty(JsonElement value, string property)
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
            && int.TryParse(result.GetString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out number)
                ? number
                : null;
    }

    private static string ResourcePath(string prefix, string id, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return $"{prefix}/{Uri.EscapeDataString(id)}/{suffix}";
    }

    private static string ResourcePath(string prefix, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return $"{prefix}/{Uri.EscapeDataString(id)}";
    }

    private static void ValidateBatch<T>(IReadOnlyCollection<T> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(batch), batch.Count, "A phpVMS batch must contain between 1 and 100 items.");
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static bool IsAlreadyCancelled(PhpVmsApiException exception) =>
        exception.StatusCode == HttpStatusCode.BadRequest
        && exception.PirepState == 3
        && exception.ProblemType?.EndsWith(
            "/pirep-cancel-not-allowed",
            StringComparison.OrdinalIgnoreCase) == true;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private TimeSpan Backoff(int attempt)
    {
        double seconds = Math.Pow(2, attempt - 1) + Math.Clamp(jitter(), 0, 1);
        return TimeSpan.FromSeconds(seconds);
    }

    private TimeSpan GetRetryDelay(int attempt, TimeSpan? retryAfter) =>
        Min(retryAfter ?? Backoff(attempt), options.MaximumRetryDelay);

    private void ReportRetry(
        HttpMethod method,
        string endpoint,
        int attempt,
        int? statusCode,
        TimeSpan retryDelay,
        string? failureType) =>
        ReportRequest(new(
            PhpVmsRequestTelemetryKind.RetryScheduled,
            method.Method,
            endpoint,
            attempt,
            options.MaximumAttempts,
            DateTimeOffset.UtcNow,
            statusCode,
            retryDelay,
            failureType));

    private void ReportRequestFailure(
        HttpMethod method,
        string endpoint,
        int attempt,
        Exception exception) =>
        ReportRequest(new(
            PhpVmsRequestTelemetryKind.RequestFailed,
            method.Method,
            endpoint,
            attempt,
            options.MaximumAttempts,
            DateTimeOffset.UtcNow,
            FailureType: exception.GetType().Name));

    private void ReportRequest(PhpVmsRequestTelemetry telemetry) => observeRequest?.Invoke(telemetry);

    private static string ClassifyEndpoint(string relativePath)
    {
        string path = relativePath.Split('?', 2)[0].Trim('/');
        if (path.EndsWith("/acars/positions", StringComparison.Ordinal))
        {
            return "pireps.positions";
        }

        if (path.EndsWith("/acars/events", StringComparison.Ordinal))
        {
            return "pireps.events";
        }

        if (path.EndsWith("/acars/logs", StringComparison.Ordinal))
        {
            return "pireps.logs";
        }

        if (path.EndsWith("/briefing", StringComparison.Ordinal))
        {
            return "flights.briefing";
        }

        if (path.EndsWith("/aircraft", StringComparison.Ordinal))
        {
            return "flights.aircraft";
        }

        if (path.EndsWith("/update", StringComparison.Ordinal))
        {
            return "pireps.update";
        }

        if (path.EndsWith("/file", StringComparison.Ordinal))
        {
            return "pireps.file";
        }

        if (path.EndsWith("/cancel", StringComparison.Ordinal))
        {
            return "pireps.cancel";
        }

        return path switch
        {
            "api/status" => "status",
            "api/user" => "user",
            "api/bids" => "bids",
            "api/flights" => "flights",
            "api/flights/search" => "flights.search",
            "api/fleet" => "fleet",
            "api/pireps" => "pireps.list",
            "api/pireps/prefile" => "pireps.prefile",
            _ when path.StartsWith("api/pireps/", StringComparison.Ordinal) => "pireps.detail",
            _ when path.StartsWith("api/flights/", StringComparison.Ordinal) => "flights.detail",
            _ => "other",
        };
    }

    private static string RedactCredential(string detail, BackendCredential? credential)
    {
        string? secret = credential switch
        {
            ApiKeyCredential apiKey => apiKey.ApiKey,
            BearerCredential bearer => bearer.Token,
            _ => null,
        };
        return string.IsNullOrEmpty(secret)
            ? detail
            : detail.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
    }

    private static TimeSpan Min(TimeSpan value, TimeSpan maximum) => value <= maximum ? value : maximum;

    private sealed record PhpVmsErrorDetail(
        string Detail,
        string? ProblemType,
        int? PirepState);
}

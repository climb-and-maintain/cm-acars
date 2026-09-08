using System.Net.Http;
using ClimbAndMaintain.Acars.App.Configuration;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.PhpVms;
using Microsoft.Extensions.Logging;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class PhpVmsBackendLeaseFactory(
    DesktopSettingsCoordinator settings,
    ISecretStore secretStore,
    IHttpClientFactory httpClientFactory,
    PhpVmsOperationalStatus operationalStatus,
    ILogger<PhpVmsBackendLeaseFactory> logger)
{
    private const string ApiKeySecretName = "phpvms-api-key";

    private static readonly Action<ILogger, string, string, int, int, Exception?> LogResponse =
        LoggerMessage.Define<string, string, int, int>(
            LogLevel.Information,
            new EventId(4001, "PhpVmsResponseReceived"),
            "phpVMS {Method} {Endpoint} returned HTTP {StatusCode} on attempt {Attempt}.");

    private static readonly Action<ILogger, string, string, int, int, double, Exception?> LogRetry =
        LoggerMessage.Define<string, string, int, int, double>(
            LogLevel.Warning,
            new EventId(4002, "PhpVmsRetryScheduled"),
            "phpVMS {Method} {Endpoint} retry {NextAttempt} of {MaximumAttempts} is scheduled after {DelayMilliseconds} ms.");

    private static readonly Action<ILogger, string, string, string, int, Exception?> LogRequestFailure =
        LoggerMessage.Define<string, string, string, int>(
            LogLevel.Warning,
            new EventId(4003, "PhpVmsRequestFailed"),
            "phpVMS {Method} {Endpoint} failed with {FailureType} on attempt {Attempt}.");

    private readonly DesktopSettingsCoordinator settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ISecretStore secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly PhpVmsOperationalStatus operationalStatus =
        operationalStatus ?? throw new ArgumentNullException(nameof(operationalStatus));
    private readonly ILogger<PhpVmsBackendLeaseFactory> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<FlightBackendLease> CreateAsync(CancellationToken cancellationToken)
    {
        DesktopSettings current = settings.Current;
        if (!Uri.TryCreate(current.PhpVmsBaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidOperationException("Configure an absolute phpVMS site URL before using flights.");
        }

        string? apiKey = await secretStore.GetSecretAsync(
            ApiKeySecretName,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Save a phpVMS API key before using flights.");
        }

        return Create(baseUri, apiKey, current.AllowInsecureLocalServer);
    }

    public FlightBackendLease Create(
        Uri baseUri,
        string apiKey,
        bool allowInsecureLocalServer)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ValidateBaseUri(baseUri, allowInsecureLocalServer);

        HttpClient httpClient = httpClientFactory.CreateClient("phpvms");
        try
        {
            PhpVmsClient client = new(
                httpClient,
                new PhpVmsOptions(baseUri, allowInsecureLocalServer),
                new FixedCredentialProvider(new ApiKeyCredential(apiKey)),
                observeRequest: ObserveRequest);
            return new FlightBackendLease(new PhpVmsBackend(client), httpClient);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    private void ObserveRequest(PhpVmsRequestTelemetry telemetry)
    {
        operationalStatus.Record(telemetry);
        switch (telemetry.Kind)
        {
            case PhpVmsRequestTelemetryKind.ResponseReceived when telemetry.StatusCode is { } statusCode:
                LogResponse(logger, telemetry.Method, telemetry.Endpoint, statusCode, telemetry.Attempt, null);
                break;
            case PhpVmsRequestTelemetryKind.RetryScheduled when telemetry.RetryDelay is { } retryDelay:
                LogRetry(
                    logger,
                    telemetry.Method,
                    telemetry.Endpoint,
                    telemetry.Attempt + 1,
                    telemetry.MaximumAttempts,
                    retryDelay.TotalMilliseconds,
                    null);
                break;
            case PhpVmsRequestTelemetryKind.RequestFailed:
                LogRequestFailure(
                    logger,
                    telemetry.Method,
                    telemetry.Endpoint,
                    telemetry.FailureType ?? "RequestFailure",
                    telemetry.Attempt,
                    null);
                break;
        }
    }

    private static void ValidateBaseUri(Uri baseUri, bool allowInsecureLocalServer)
    {
        if (baseUri.Scheme == Uri.UriSchemeHttp
            && (!allowInsecureLocalServer || !baseUri.IsLoopback))
        {
            throw new InvalidOperationException(
                "phpVMS requires HTTPS. The local-development HTTP override accepts only loopback hosts.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("The phpVMS site URL must use HTTPS or the explicit local HTTP override.");
        }
    }
}

public sealed class FlightBackendLease : IDisposable
{
    private readonly IDisposable ownedResource;
    private bool disposed;

    public FlightBackendLease(IFlightOperationsBackend backend, IDisposable ownedResource)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.ownedResource = ownedResource ?? throw new ArgumentNullException(nameof(ownedResource));
    }

    public IFlightOperationsBackend Backend { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ownedResource.Dispose();
    }
}

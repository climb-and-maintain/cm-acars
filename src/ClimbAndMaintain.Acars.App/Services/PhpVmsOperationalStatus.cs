using ClimbAndMaintain.Acars.PhpVms;

namespace ClimbAndMaintain.Acars.App.Services;

public enum PhpVmsRateLimitState
{
    Unknown,
    NotRateLimited,
    RateLimited,
}

public sealed record PhpVmsOperationalSnapshot(
    DateTimeOffset? LastRequestAtUtc,
    string? LastRequestMethod,
    string? LastRequestEndpoint,
    int? LastResponseStatusCode,
    string? LastFailureType,
    DateTimeOffset? LastSuccessfulRequestAtUtc,
    string? LastSuccessfulRequestEndpoint,
    PhpVmsRateLimitState RateLimitState,
    DateTimeOffset? LastRateLimitAtUtc,
    TimeSpan? LastRetryDelay,
    int? LastRetryAttempt,
    int? MaximumAttempts);

public sealed class PhpVmsOperationalStatus
{
    private readonly Lock stateLock = new();
    private DateTimeOffset? lastRequestAtUtc;
    private string? lastRequestMethod;
    private string? lastRequestEndpoint;
    private int? lastResponseStatusCode;
    private string? lastFailureType;
    private DateTimeOffset? lastSuccessfulRequestAtUtc;
    private string? lastSuccessfulRequestEndpoint;
    private PhpVmsRateLimitState rateLimitState;
    private DateTimeOffset? lastRateLimitAtUtc;
    private TimeSpan? lastRetryDelay;
    private int? lastRetryAttempt;
    private int? maximumAttempts;

    public void Record(PhpVmsRequestTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        lock (stateLock)
        {
            maximumAttempts = telemetry.MaximumAttempts;
            switch (telemetry.Kind)
            {
                case PhpVmsRequestTelemetryKind.ResponseReceived:
                    RecordRequest(telemetry);
                    lastResponseStatusCode = telemetry.StatusCode;
                    lastFailureType = null;
                    if (telemetry.StatusCode == 429)
                    {
                        rateLimitState = PhpVmsRateLimitState.RateLimited;
                        lastRateLimitAtUtc = telemetry.OccurredAtUtc;
                        lastRetryDelay = null;
                        lastRetryAttempt = null;
                    }
                    else
                    {
                        rateLimitState = PhpVmsRateLimitState.NotRateLimited;
                    }

                    if (telemetry.StatusCode is >= 200 and <= 299)
                    {
                        lastSuccessfulRequestAtUtc = telemetry.OccurredAtUtc;
                        lastSuccessfulRequestEndpoint = telemetry.Endpoint;
                    }

                    break;
                case PhpVmsRequestTelemetryKind.RequestFailed:
                    RecordRequest(telemetry);
                    lastResponseStatusCode = null;
                    lastFailureType = telemetry.FailureType;
                    break;
                case PhpVmsRequestTelemetryKind.RetryScheduled:
                    if (telemetry.StatusCode == 429)
                    {
                        rateLimitState = PhpVmsRateLimitState.RateLimited;
                        lastRateLimitAtUtc = telemetry.OccurredAtUtc;
                        lastRetryDelay = telemetry.RetryDelay;
                        lastRetryAttempt = telemetry.Attempt + 1;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(telemetry), telemetry.Kind, "Unknown phpVMS telemetry kind.");
            }
        }
    }

    public PhpVmsOperationalSnapshot GetSnapshot()
    {
        lock (stateLock)
        {
            return new(
                lastRequestAtUtc,
                lastRequestMethod,
                lastRequestEndpoint,
                lastResponseStatusCode,
                lastFailureType,
                lastSuccessfulRequestAtUtc,
                lastSuccessfulRequestEndpoint,
                rateLimitState,
                lastRateLimitAtUtc,
                lastRetryDelay,
                lastRetryAttempt,
                maximumAttempts);
        }
    }

    private void RecordRequest(PhpVmsRequestTelemetry telemetry)
    {
        lastRequestAtUtc = telemetry.OccurredAtUtc;
        lastRequestMethod = telemetry.Method;
        lastRequestEndpoint = telemetry.Endpoint;
    }
}

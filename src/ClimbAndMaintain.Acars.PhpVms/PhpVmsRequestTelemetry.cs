namespace ClimbAndMaintain.Acars.PhpVms;

/// <summary>
/// Identifies a sanitized operational event emitted by <see cref="PhpVmsClient"/>.
/// </summary>
public enum PhpVmsRequestTelemetryKind
{
    ResponseReceived,
    RetryScheduled,
    RequestFailed,
}

/// <summary>
/// Contains request metadata that is safe to record in application diagnostics.
/// </summary>
/// <remarks>
/// Endpoint names are fixed categories produced by the client. This type never contains a URL,
/// resource identifier, query string, credential, header, request body, or response body.
/// </remarks>
public sealed record PhpVmsRequestTelemetry(
    PhpVmsRequestTelemetryKind Kind,
    string Method,
    string Endpoint,
    int Attempt,
    int MaximumAttempts,
    DateTimeOffset OccurredAtUtc,
    int? StatusCode = null,
    TimeSpan? RetryDelay = null,
    string? FailureType = null);

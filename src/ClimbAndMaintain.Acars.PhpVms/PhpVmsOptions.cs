using ClimbAndMaintain.Acars.Application.Contracts;

namespace ClimbAndMaintain.Acars.PhpVms;

public sealed record PhpVmsOptions
{
    public PhpVmsOptions(Uri baseUri, bool allowInsecureHttp = false, int maximumAttempts = 5)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The phpVMS base URL must be absolute.", nameof(baseUri));
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps
            && !(allowInsecureHttp && baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback))
        {
            throw new ArgumentException(
                "The phpVMS base URL must use HTTPS. The developer-only HTTP override is restricted to loopback hosts.",
                nameof(baseUri));
        }

        if (maximumAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts), maximumAttempts, "Attempts must be between 1 and 10.");
        }

        BaseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        AllowInsecureHttp = allowInsecureHttp;
        MaximumAttempts = maximumAttempts;
    }

    public Uri BaseUri { get; }

    public bool AllowInsecureHttp { get; }

    public int MaximumAttempts { get; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    internal void Validate()
    {
        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestTimeout),
                RequestTimeout,
                "The request timeout must be positive.");
        }

        if (MaximumRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRetryDelay),
                MaximumRetryDelay,
                "The maximum retry delay cannot be negative.");
        }
    }
}

public sealed class FixedCredentialProvider(BackendCredential? credential) : IBackendCredentialProvider
{
    public ValueTask<BackendCredential?> GetCredentialAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(credential);
    }
}

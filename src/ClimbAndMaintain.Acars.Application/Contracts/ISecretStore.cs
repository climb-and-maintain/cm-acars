namespace ClimbAndMaintain.Acars.Application.Contracts;

public interface ISecretStore
{
    ValueTask<string?> GetSecretAsync(string name, CancellationToken cancellationToken);

    ValueTask SetSecretAsync(string name, string value, CancellationToken cancellationToken);

    ValueTask DeleteSecretAsync(string name, CancellationToken cancellationToken);
}

public interface IBackendCredentialProvider
{
    ValueTask<BackendCredential?> GetCredentialAsync(CancellationToken cancellationToken);
}

public abstract class BackendCredential
{
    private protected BackendCredential()
    {
    }
}

public sealed class ApiKeyCredential : BackendCredential
{
    public ApiKeyCredential(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ApiKey = apiKey;
    }

    public string ApiKey { get; }

    public override string ToString() => "API key [REDACTED]";
}

public sealed class BearerCredential : BackendCredential
{
    public BearerCredential(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Token = token;
    }

    public string Token { get; }

    public override string ToString() => "Bearer token [REDACTED]";
}

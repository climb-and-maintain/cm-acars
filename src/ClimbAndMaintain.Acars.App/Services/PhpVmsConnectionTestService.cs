using ClimbAndMaintain.Acars.Application.Contracts;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class PhpVmsConnectionTestService(PhpVmsBackendLeaseFactory backendFactory)
{
    private readonly PhpVmsBackendLeaseFactory backendFactory =
        backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));

    public async ValueTask<PhpVmsConnectionTestResult> TestAsync(
        Uri baseUri,
        string apiKey,
        bool allowInsecureLocalServer,
        CancellationToken cancellationToken)
    {
        using FlightBackendLease lease = backendFactory.Create(
            baseUri,
            apiKey,
            allowInsecureLocalServer);
        BackendConnectionResult connection = await lease.Backend
            .TestConnectionAsync(cancellationToken);
        if (!connection.Succeeded)
        {
            return new PhpVmsConnectionTestResult(connection, null);
        }

        BackendPilot pilot = await lease.Backend.GetCurrentPilotAsync(cancellationToken);
        return new PhpVmsConnectionTestResult(connection, pilot);
    }
}

public sealed record PhpVmsConnectionTestResult(
    BackendConnectionResult Connection,
    BackendPilot? Pilot);

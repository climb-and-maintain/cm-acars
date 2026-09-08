using ClimbAndMaintain.Acars.Core.Flights;

namespace ClimbAndMaintain.Acars.Application.Contracts;

public interface IFlightSessionStore
{
    ValueTask<FlightSessionState?> GetActiveAsync(CancellationToken cancellationToken);

    async ValueTask<FlightSessionState?> GetAsync(
        FlightSessionId sessionId,
        CancellationToken cancellationToken)
    {
        FlightSessionState? session = await GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return session?.Id == sessionId ? session : null;
    }

    ValueTask SaveAsync(FlightSessionState session, CancellationToken cancellationToken);

    ValueTask DeleteAsync(FlightSessionId sessionId, CancellationToken cancellationToken);
}

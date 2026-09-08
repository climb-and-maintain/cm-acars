using ClimbAndMaintain.Acars.Core.Flights;

namespace ClimbAndMaintain.Acars.Application.Contracts;

public interface IFlightStateStore : IFlightSessionStore, IOutboxStore
{
    ValueTask SaveWithOutboxAsync(
        FlightSessionState session,
        IReadOnlyCollection<OutboxItem> outboxItems,
        CancellationToken cancellationToken);
}

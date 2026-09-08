using ClimbAndMaintain.Acars.Core.Flights;

namespace ClimbAndMaintain.Acars.Core.Tests.Flights;

public sealed class FlightEventTests
{
    [Fact]
    public void CreateAssignsStableNonEmptyIdsForDurableRetries()
    {
        FlightSessionId sessionId = FlightSessionId.New();
        FlightEvent flightEvent = FlightEvent.Create(
            sessionId,
            FlightEventType.Takeoff,
            DateTimeOffset.UtcNow);

        FlightEvent retriedEvent = flightEvent with { };

        Assert.NotEqual(Guid.Empty, flightEvent.Id.Value);
        Assert.Equal(flightEvent.Id, retriedEvent.Id);
        Assert.Equal(sessionId, retriedEvent.SessionId);
    }

    [Fact]
    public void CreateAssignsDifferentIdsToDifferentEvents()
    {
        FlightSessionId sessionId = FlightSessionId.New();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        FlightEvent first = FlightEvent.Create(sessionId, FlightEventType.TaxiOut, timestamp);
        FlightEvent second = FlightEvent.Create(sessionId, FlightEventType.TaxiOut, timestamp);

        Assert.NotEqual(first.Id, second.Id);
    }
}

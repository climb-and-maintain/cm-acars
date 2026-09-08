namespace ClimbAndMaintain.Acars.Core.Flights;

public enum FlightPhase
{
    Ready,
    Boarding,
    Pushback,
    TaxiOut,
    Takeoff,
    Climb,
    Cruise,
    Descent,
    Approach,
    Landing,
    TaxiIn,
    OnBlock,
    Completed,
}

public enum FlightEventType
{
    Boarding,
    EnginesStarted,
    Pushback,
    TaxiOut,
    Takeoff,
    Climb,
    Cruise,
    Descent,
    Approach,
    Landing,
    TaxiIn,
    EnginesStopped,
    OnBlock,
    Completed,
    Cancelled,
    Paused,
    Resumed,
    SimulatorDisconnected,
    SimulatorReconnected,
    SlewDetected,
}

public enum FlightSessionStatus
{
    Active = 0,
    Paused = 1,
    Completed = 2,
    Cancelled = 3,
    Starting = 4,
}

public enum FlightStartIntentState
{
    Prepared = 0,
    PrefileRequested = 1,
    ReconciliationRequired = 2,
    Completed = 3,
    Abandoned = 4,
}

public sealed record FlightEvent
{
    public FlightEvent(
        FlightEventId id,
        FlightSessionId sessionId,
        FlightEventType type,
        DateTimeOffset occurredAtUtc,
        string? detail = null)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Event timestamps must use UTC.", nameof(occurredAtUtc));
        }

        Id = id;
        SessionId = sessionId;
        Type = type;
        OccurredAtUtc = occurredAtUtc;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
    }

    public FlightEventId Id { get; }

    public FlightSessionId SessionId { get; }

    public FlightEventType Type { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public string? Detail { get; }

    public static FlightEvent Create(
        FlightSessionId sessionId,
        FlightEventType type,
        DateTimeOffset occurredAtUtc,
        string? detail = null) =>
        new(FlightEventId.New(), sessionId, type, occurredAtUtc, detail);
}

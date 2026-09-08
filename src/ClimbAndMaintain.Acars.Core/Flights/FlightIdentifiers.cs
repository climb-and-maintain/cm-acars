namespace ClimbAndMaintain.Acars.Core.Flights;

public readonly record struct FlightSessionId
{
    public FlightSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A flight session ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static FlightSessionId New() => new(Guid.CreateVersion7());
}

public readonly record struct FlightEventId
{
    public FlightEventId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A flight event ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static FlightEventId New() => new(Guid.CreateVersion7());
}

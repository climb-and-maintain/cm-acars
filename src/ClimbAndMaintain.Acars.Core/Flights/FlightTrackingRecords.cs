using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Core.Flights;

public readonly record struct PositionReportId
{
    public PositionReportId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A position report ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static PositionReportId New() => new(Guid.CreateVersion7());
}

public sealed record PositionReport(
    PositionReportId Id,
    FlightSessionId SessionId,
    FlightPhase Phase,
    TelemetrySnapshot Telemetry,
    string? LogMessage = null);

public sealed record FlightLogEntry
{
    public FlightLogEntry(
        FlightEventId id,
        FlightSessionId sessionId,
        DateTimeOffset occurredAtUtc,
        string message)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Log-entry timestamps must use UTC.", nameof(occurredAtUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Id = id;
        SessionId = sessionId;
        OccurredAtUtc = occurredAtUtc;
        Message = message.Trim();
    }

    public FlightEventId Id { get; }

    public FlightSessionId SessionId { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public string Message { get; }
}

public sealed record FlightStartIntent
{
    public FlightStartIntent(
        string requestMarker,
        FlightStartIntentState state,
        DateTimeOffset preparedAtUtc,
        DateTimeOffset? lastAttemptAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestMarker);
        if (requestMarker.Length > 64)
        {
            throw new ArgumentException("A flight-start request marker cannot exceed 64 characters.", nameof(requestMarker));
        }

        if (preparedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Flight-start timestamps must use UTC.", nameof(preparedAtUtc));
        }

        if (lastAttemptAtUtc is { } attempted
            && (attempted.Offset != TimeSpan.Zero || attempted < preparedAtUtc))
        {
            throw new ArgumentException(
                "The prefile-attempt timestamp must use UTC and cannot precede the prepared timestamp.",
                nameof(lastAttemptAtUtc));
        }

        RequestMarker = requestMarker.Trim();
        State = state;
        PreparedAtUtc = preparedAtUtc;
        LastAttemptAtUtc = lastAttemptAtUtc;
    }

    public string RequestMarker { get; }

    public FlightStartIntentState State { get; }

    public DateTimeOffset PreparedAtUtc { get; }

    public DateTimeOffset? LastAttemptAtUtc { get; }
}

public sealed record FlightSessionMetrics
{
    public Distance FlownDistance { get; init; } = new(0);

    public TimeSpan ActiveTrackingTime { get; init; }

    public TimeSpan AirborneFlightTime { get; init; }

    public FuelMass? FuelUsed { get; init; }

    public VerticalSpeed? LandingRate { get; init; }

    // These anchors are persisted with the totals so a restart can continue
    // accounting without reconstructing or double-counting an earlier segment.
    public GeoPosition? LastTrackedPosition { get; init; }

    public FuelMass? LastObservedFuel { get; init; }
}

public sealed record FlightSessionState
{
    public FlightSessionState(
        FlightSessionId id,
        FlightPlan flightPlan,
        FlightPhase phase,
        DateTimeOffset startedAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(flightPlan);
        if (startedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Session timestamps must use UTC.", nameof(startedAtUtc));
        }

        if (updatedAtUtc.Offset != TimeSpan.Zero || updatedAtUtc < startedAtUtc)
        {
            throw new ArgumentException(
                "The update timestamp must use UTC and cannot precede the start timestamp.",
                nameof(updatedAtUtc));
        }

        Id = id;
        FlightPlan = flightPlan;
        Phase = phase;
        StartedAtUtc = startedAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public FlightSessionId Id { get; }

    public FlightPlan FlightPlan { get; }

    public FlightPhase Phase { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public string? BackendFlightId { get; init; }

    public TelemetrySnapshot? LastTelemetry { get; init; }

    public FlightSessionStatus Status { get; init; } = FlightSessionStatus.Active;

    public FlightStartIntent? StartIntent { get; init; }

    public FlightSessionMetrics Metrics { get; init; } = new();
}

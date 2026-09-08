using ClimbAndMaintain.Acars.Core.Flights;

namespace ClimbAndMaintain.Acars.Application.Contracts;

public readonly record struct OutboxItemId
{
    public OutboxItemId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An outbox item ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static OutboxItemId New() => new(Guid.CreateVersion7());
}

public abstract record OutboxItem(
    OutboxItemId Id,
    FlightSessionId SessionId,
    DateTimeOffset CreatedAtUtc);

public sealed record PositionOutboxItem(
    OutboxItemId Id,
    FlightSessionId SessionId,
    DateTimeOffset CreatedAtUtc,
    PositionReport Position)
    : OutboxItem(Id, SessionId, CreatedAtUtc);

public sealed record EventOutboxItem(
    OutboxItemId Id,
    FlightSessionId SessionId,
    DateTimeOffset CreatedAtUtc,
    FlightEvent Event)
    : OutboxItem(Id, SessionId, CreatedAtUtc);

public sealed record LogOutboxItem(
    OutboxItemId Id,
    FlightSessionId SessionId,
    DateTimeOffset CreatedAtUtc,
    FlightLogEntry LogEntry)
    : OutboxItem(Id, SessionId, CreatedAtUtc);

public sealed record CompletionOutboxItem(
    OutboxItemId Id,
    FlightSessionId SessionId,
    DateTimeOffset CreatedAtUtc,
    CompletedFlightReport Report)
    : OutboxItem(Id, SessionId, CreatedAtUtc);

public sealed record CancellationOutboxItem(
    OutboxItemId Id,
    FlightSessionId SessionId,
    DateTimeOffset CreatedAtUtc)
    : OutboxItem(Id, SessionId, CreatedAtUtc);

public sealed record PendingOutboxItem(
    OutboxItem Item,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc,
    string? LastError);

public sealed record OutboxCounts(int Positions, int Events, int Logs, int Operations = 0)
{
    public int Total => Positions + Events + Logs + Operations;
}

public interface IOutboxStore
{
    ValueTask EnqueueAsync(OutboxItem item, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<PendingOutboxItem>> GetPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    ValueTask RecordFailedAttemptAsync(
        OutboxItemId id,
        DateTimeOffset attemptedAtUtc,
        string failureMessage,
        CancellationToken cancellationToken);

    ValueTask MarkDeliveredAsync(
        OutboxItemId id,
        DateTimeOffset deliveredAtUtc,
        CancellationToken cancellationToken);

    ValueTask<OutboxCounts> GetCountsAsync(CancellationToken cancellationToken);
}

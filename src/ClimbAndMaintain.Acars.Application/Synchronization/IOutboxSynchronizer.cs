using ClimbAndMaintain.Acars.Application.Contracts;

namespace ClimbAndMaintain.Acars.Application.Synchronization;

public enum OutboxSynchronizationStatus
{
    NothingPending,
    Synchronized,
    PartiallySynchronized,
    DeferredForRetry,
    MissingFlightSession,
    MissingBackendFlightId,
    Failed,
}

public sealed record OutboxSynchronizationResult(
    OutboxSynchronizationStatus Status,
    int DeliveredCount,
    int FailedCount,
    int DeferredCount,
    OutboxCounts Remaining,
    string? FailureMessage = null);

public interface IOutboxSynchronizer
{
    ValueTask<OutboxSynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken);
}

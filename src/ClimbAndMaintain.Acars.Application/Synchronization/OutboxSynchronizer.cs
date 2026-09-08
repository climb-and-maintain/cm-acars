using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;

namespace ClimbAndMaintain.Acars.Application.Synchronization;

public sealed class OutboxSynchronizer : IOutboxSynchronizer, IDisposable
{
    private readonly IOutboxStore outboxStore;
    private readonly IFlightSessionStore sessionStore;
    private readonly IFlightOperationsBackend backend;
    private readonly OutboxSynchronizationOptions options;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim synchronizationLock = new(1, 1);
    private bool disposed;

    public OutboxSynchronizer(
        IOutboxStore outboxStore,
        IFlightSessionStore sessionStore,
        IFlightOperationsBackend backend,
        OutboxSynchronizationOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(backend);
        this.outboxStore = outboxStore;
        this.sessionStore = sessionStore;
        this.backend = backend;
        this.options = options ?? new OutboxSynchronizationOptions();
        this.options.Validate();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<OutboxSynchronizationResult> SynchronizeAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await synchronizationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<PendingOutboxItem> pending = await outboxStore
                .GetPendingAsync(options.MaximumItemsPerRun, cancellationToken)
                .ConfigureAwait(false);
            if (pending.Count == 0)
            {
                return await CreateResultAsync(
                    OutboxSynchronizationStatus.NothingPending,
                    0,
                    0,
                    0,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }

            DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
            int deliveredCount = 0;
            int deferredCount = 0;
            foreach (IGrouping<FlightSessionId, PendingOutboxItem> group in pending
                         .GroupBy(item => item.Item.SessionId)
                         .OrderBy(group => group.Min(item => item.Item.CreatedAtUtc)))
            {
                PendingOutboxItem[] sessionItems = group
                    .OrderBy(item => item.Item.CreatedAtUtc)
                    .ThenBy(item => item.Item.Id.Value)
                    .ToArray();
                FlightSessionState? session = await sessionStore
                    .GetAsync(group.Key, cancellationToken)
                    .ConfigureAwait(false);
                if (session is null)
                {
                    return await CreateResultAsync(
                        OutboxSynchronizationStatus.MissingFlightSession,
                        deliveredCount,
                        0,
                        deferredCount + sessionItems.Length,
                        FormattableString.Invariant(
                            $"Flight session '{group.Key.Value:D}' is unavailable; its queued reports were retained."),
                        cancellationToken).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(session.BackendFlightId))
                {
                    return await CreateResultAsync(
                        OutboxSynchronizationStatus.MissingBackendFlightId,
                        deliveredCount,
                        0,
                        deferredCount + sessionItems.Length,
                        FormattableString.Invariant(
                            $"Flight session '{group.Key.Value:D}' has not been attached to a backend flight."),
                        cancellationToken).ConfigureAwait(false);
                }

                if (sessionItems.Any(item => !IsRetryDue(item, now)))
                {
                    deferredCount += sessionItems.Length;
                    continue;
                }

                SessionSynchronizationResult sessionResult = await SynchronizeSessionAsync(
                    session.BackendFlightId,
                    sessionItems,
                    now,
                    cancellationToken).ConfigureAwait(false);
                deliveredCount += sessionResult.DeliveredCount;
                if (sessionResult.FailedCount > 0)
                {
                    return await CreateResultAsync(
                        OutboxSynchronizationStatus.Failed,
                        deliveredCount,
                        sessionResult.FailedCount,
                        deferredCount,
                        sessionResult.FailureMessage,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            OutboxSynchronizationStatus status = deliveredCount switch
            {
                > 0 when deferredCount > 0 => OutboxSynchronizationStatus.PartiallySynchronized,
                > 0 => OutboxSynchronizationStatus.Synchronized,
                _ => OutboxSynchronizationStatus.DeferredForRetry,
            };
            return await CreateResultAsync(
                status,
                deliveredCount,
                0,
                deferredCount,
                null,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            synchronizationLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        synchronizationLock.Dispose();
    }

    private async ValueTask<SessionSynchronizationResult> SynchronizeSessionAsync(
        string backendFlightId,
        IReadOnlyList<PendingOutboxItem> items,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken)
    {
        SessionSynchronizationResult result = await SendBatchesAsync<PositionOutboxItem, PositionReport>(
            backendFlightId,
            items,
            item => item.Position,
            backend.SendPositionsAsync,
            attemptedAtUtc,
            cancellationToken).ConfigureAwait(false);
        if (result.FailedCount > 0)
        {
            return result;
        }

        SessionSynchronizationResult eventsResult = await SendBatchesAsync<EventOutboxItem, FlightEvent>(
            backendFlightId,
            items,
            item => item.Event,
            backend.SendEventsAsync,
            attemptedAtUtc,
            cancellationToken).ConfigureAwait(false);
        result = result.Add(eventsResult);
        if (eventsResult.FailedCount > 0)
        {
            return result;
        }

        SessionSynchronizationResult logsResult = await SendBatchesAsync<LogOutboxItem, FlightLogEntry>(
            backendFlightId,
            items,
            item => item.LogEntry,
            backend.SendLogsAsync,
            attemptedAtUtc,
            cancellationToken).ConfigureAwait(false);
        result = result.Add(logsResult);
        if (logsResult.FailedCount > 0)
        {
            return result;
        }

        foreach (PendingOutboxItem pendingOperation in items.Where(
                     item => item.Item is CompletionOutboxItem or CancellationOutboxItem))
        {
            SessionSynchronizationResult operationResult = await SendOperationAsync(
                backendFlightId,
                pendingOperation,
                attemptedAtUtc,
                cancellationToken).ConfigureAwait(false);
            result = result.Add(operationResult);
            if (operationResult.FailedCount > 0)
            {
                return result;
            }
        }

        return result;
    }

    private async ValueTask<SessionSynchronizationResult> SendBatchesAsync<TItem, TPayload>(
        string backendFlightId,
        IReadOnlyList<PendingOutboxItem> items,
        Func<TItem, TPayload> payloadSelector,
        Func<string, IReadOnlyList<TPayload>, CancellationToken, ValueTask> sender,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken)
        where TItem : OutboxItem
    {
        int deliveredCount = 0;
        IEnumerable<PendingOutboxItem[]> batches = items
            .Where(item => item.Item is TItem)
            .Chunk(options.BatchSize);
        foreach (PendingOutboxItem[] batch in batches)
        {
            TPayload[] payloads = batch
                .Select(item => payloadSelector((TItem)item.Item))
                .ToArray();
            try
            {
                await sender(backendFlightId, payloads, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                string failure = GetFailureMessage(exception);
                await RecordBatchFailureAsync(batch, attemptedAtUtc, failure, cancellationToken)
                    .ConfigureAwait(false);
                return new SessionSynchronizationResult(deliveredCount, batch.Length, failure);
            }

            await MarkBatchDeliveredAsync(batch, attemptedAtUtc, cancellationToken).ConfigureAwait(false);
            deliveredCount += batch.Length;
        }

        return new SessionSynchronizationResult(deliveredCount, 0, null);
    }

    private async ValueTask<SessionSynchronizationResult> SendOperationAsync(
        string backendFlightId,
        PendingOutboxItem pendingOperation,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (pendingOperation.Item)
            {
                case CompletionOutboxItem completion:
                    await backend.FileFlightAsync(backendFlightId, completion.Report, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case CancellationOutboxItem:
                    await backend.CancelFlightAsync(backendFlightId, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("The queued item is not a terminal flight operation.");
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            string failure = GetFailureMessage(exception);
            await RecordBatchFailureAsync(
                [pendingOperation],
                attemptedAtUtc,
                failure,
                cancellationToken).ConfigureAwait(false);
            return new SessionSynchronizationResult(0, 1, failure);
        }

        await outboxStore
            .MarkDeliveredAsync(pendingOperation.Item.Id, attemptedAtUtc, cancellationToken)
            .ConfigureAwait(false);
        return new SessionSynchronizationResult(1, 0, null);
    }

    private async ValueTask MarkBatchDeliveredAsync(
        IEnumerable<PendingOutboxItem> items,
        DateTimeOffset deliveredAtUtc,
        CancellationToken cancellationToken)
    {
        foreach (PendingOutboxItem item in items)
        {
            await outboxStore
                .MarkDeliveredAsync(item.Item.Id, deliveredAtUtc, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask RecordBatchFailureAsync(
        IEnumerable<PendingOutboxItem> items,
        DateTimeOffset attemptedAtUtc,
        string failure,
        CancellationToken cancellationToken)
    {
        foreach (PendingOutboxItem item in items)
        {
            await outboxStore
                .RecordFailedAttemptAsync(item.Item.Id, attemptedAtUtc, failure, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<OutboxSynchronizationResult> CreateResultAsync(
        OutboxSynchronizationStatus status,
        int deliveredCount,
        int failedCount,
        int deferredCount,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        OutboxCounts remaining = await outboxStore.GetCountsAsync(cancellationToken).ConfigureAwait(false);
        return new OutboxSynchronizationResult(
            status,
            deliveredCount,
            failedCount,
            deferredCount,
            remaining,
            failureMessage);
    }

    private bool IsRetryDue(PendingOutboxItem item, DateTimeOffset now)
    {
        if (item.LastAttemptAtUtc is not { } lastAttempt)
        {
            return true;
        }

        return now >= lastAttempt
            && now - lastAttempt >= options.GetRetryDelay(item.AttemptCount);
    }

    private static string GetFailureMessage(Exception exception)
    {
        Exception rootCause = exception.GetBaseException();
        string message = string.IsNullOrWhiteSpace(rootCause.Message)
            ? rootCause.GetType().Name
            : rootCause.Message.Trim();
        return message.Length <= 2_048 ? message : message[..2_048];
    }

    private sealed record SessionSynchronizationResult(
        int DeliveredCount,
        int FailedCount,
        string? FailureMessage)
    {
        public SessionSynchronizationResult Add(SessionSynchronizationResult other) => new(
            DeliveredCount + other.DeliveredCount,
            FailedCount + other.FailedCount,
            FailureMessage ?? other.FailureMessage);
    }
}

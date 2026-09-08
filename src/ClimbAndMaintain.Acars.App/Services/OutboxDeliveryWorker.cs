using System.Diagnostics.CodeAnalysis;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Synchronization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class OutboxDeliveryWorker(
    IOutboxStore outbox,
    FlightOperationsService operations,
    ILogger<OutboxDeliveryWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, int, Exception?> LogDelivery = LoggerMessage.Define<int, int>(
        LogLevel.Information,
        new EventId(2001, "OutboxDelivered"),
        "Delivered {DeliveredCount} queued ACARS item(s); {RemainingCount} remain.");

    private static readonly Action<ILogger, string, Exception?> LogDeliveryDeferred = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(2002, "OutboxDeliveryDeferred"),
        "Queued ACARS delivery was deferred ({FailureType}); no items were discarded.");

    private static readonly Action<ILogger, string, int, int, int, Exception?> LogSynchronizationOutcome =
        LoggerMessage.Define<string, int, int, int>(
            LogLevel.Information,
            new EventId(2003, "OutboxSynchronizationOutcome"),
            "Queued ACARS synchronization ended with {Status}: {FailedCount} failed, {DeferredCount} deferred, {RemainingCount} remain.");

    private readonly IOutboxStore outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    private readonly FlightOperationsService operations = operations ?? throw new ArgumentNullException(nameof(operations));
    private readonly ILogger<OutboxDeliveryWorker> logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private OutboxSynchronizationStatus? lastLoggedStatus;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A background delivery failure must retain the durable queue and allow future retries.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                OutboxCounts counts = await outbox.GetCountsAsync(stoppingToken);
                if (counts.Total == 0)
                {
                    continue;
                }

                OutboxSynchronizationResult result = await operations.SynchronizeAsync(stoppingToken);
                if (result.DeliveredCount > 0)
                {
                    LogDelivery(logger, result.DeliveredCount, result.Remaining.Total, null);
                }

                if (result.FailedCount > 0 || result.Status != lastLoggedStatus)
                {
                    LogSynchronizationOutcome(
                        logger,
                        result.Status.ToString(),
                        result.FailedCount,
                        result.DeferredCount,
                        result.Remaining.Total,
                        null);
                    lastLoggedStatus = result.Status;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDeliveryDeferred(logger, exception.GetType().Name, null);
            }
        }
    }
}

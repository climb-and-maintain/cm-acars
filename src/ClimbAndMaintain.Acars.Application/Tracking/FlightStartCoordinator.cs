using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;

namespace ClimbAndMaintain.Acars.Application.Tracking;

public sealed class FlightStartCoordinator : IDisposable
{
    private const string RequestMarkerPrefix = "CM ACARS ";

    private readonly IFlightTrackingCoordinator tracking;
    private readonly Func<string> requestMarkerFactory;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private bool disposed;

    public FlightStartCoordinator(
        IFlightTrackingCoordinator tracking,
        Func<string>? requestMarkerFactory = null)
    {
        this.tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
        this.requestMarkerFactory = requestMarkerFactory ?? CreateRequestMarker;
    }

    public async ValueTask<FlightSessionState> StartAsync(
        BackendFlight flight,
        IFlightOperationsBackend backend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(backend);
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState? current = tracking.CurrentSession
                ?? await tracking.RestoreAsync(cancellationToken).ConfigureAwait(false);
            if (current?.Status == FlightSessionStatus.Starting)
            {
                if (current.FlightPlan != flight.FlightPlan)
                {
                    throw new InvalidOperationException(
                        "Resolve or discard the pending flight start before selecting another flight.");
                }

                return await ContinueStartAsync(current, backend, cancellationToken).ConfigureAwait(false);
            }

            if (current?.Status is FlightSessionStatus.Active or FlightSessionStatus.Paused)
            {
                throw new InvalidOperationException(
                    "Complete or cancel the current flight before starting another.");
            }

            FlightSessionState prepared = await tracking
                .PrepareStartAsync(flight.FlightPlan, requestMarkerFactory(), cancellationToken)
                .ConfigureAwait(false);
            return await ContinueStartAsync(prepared, backend, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<FlightSessionState> ResumePendingStartAsync(
        IFlightOperationsBackend backend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSessionState session = tracking.CurrentSession
                ?? await tracking.RestoreAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No pending flight start was found.");
            if (session.Status != FlightSessionStatus.Starting)
            {
                throw new InvalidOperationException("The recovered flight does not have a pending start request.");
            }

            return await ContinueStartAsync(session, backend, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        operationLock.Dispose();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any failure after dispatch can represent a committed phpVMS prefile and must leave a durable reconciliation state.")]
    private async ValueTask<FlightSessionState> ContinueStartAsync(
        FlightSessionState session,
        IFlightOperationsBackend backend,
        CancellationToken cancellationToken)
    {
        FlightStartIntent intent = session.StartIntent
            ?? throw new InvalidOperationException("The pending flight start has no durable request marker.");
        BackendPrefileResult? existing = await backend
            .FindPrefiledFlightAsync(session.FlightPlan, intent.RequestMarker, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return await tracking
                .CompleteStartAsync(existing.BackendFlightId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (intent.State != FlightStartIntentState.Prepared)
        {
            if (intent.State == FlightStartIntentState.PrefileRequested)
            {
                _ = await tracking
                    .MarkStartReconciliationRequiredAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                "phpVMS has not confirmed the pending prefile yet. No duplicate prefile was sent; choose Resume to check again later, or Discard and review phpVMS manually.");
        }

        _ = await tracking.MarkPrefileRequestedAsync(cancellationToken).ConfigureAwait(false);
        BackendPrefileResult prefile;
        try
        {
            prefile = await backend
                .PrefileAsync(session.FlightPlan, intent.RequestMarker, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _ = await tracking
                .MarkStartReconciliationRequiredAsync(CancellationToken.None)
                .ConfigureAwait(false);
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new InvalidOperationException(
                "phpVMS did not return a complete prefile response. The start request was preserved and will be reconciled before another prefile can be sent.",
                exception);
        }

        return await tracking
            .CompleteStartAsync(prefile.BackendFlightId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string CreateRequestMarker()
    {
        Span<byte> random = stackalloc byte[12];
        RandomNumberGenerator.Fill(random);
        string token = Convert
            .ToBase64String(random)
            .Replace('+', '-')
            .Replace('/', '_');
        return RequestMarkerPrefix + token;
    }
}

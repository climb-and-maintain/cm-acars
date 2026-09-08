using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Application.Contracts;

public interface ISimulatorTelemetryProvider : IAsyncDisposable
{
    string ProviderId { get; }

    SimulatorConnectionState ConnectionState { get; }

    SimulatorIdentity? ConnectedSimulator { get; }

    event EventHandler<SimulatorConnectionStateChangedEventArgs>? ConnectionStateChanged;

    ValueTask<SimulatorConnectionResult> ConnectAsync(CancellationToken cancellationToken);

    ValueTask DisconnectAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<TelemetrySnapshot> ReadTelemetryAsync(CancellationToken cancellationToken);
}

public sealed class SimulatorConnectionStateChangedEventArgs : EventArgs
{
    public SimulatorConnectionStateChangedEventArgs(
        SimulatorConnectionState previous,
        SimulatorConnectionState current,
        DateTimeOffset occurredAtUtc,
        string? reason = null)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("State-change timestamps must use UTC.", nameof(occurredAtUtc));
        }

        Previous = previous;
        Current = current;
        OccurredAtUtc = occurredAtUtc;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public SimulatorConnectionState Previous { get; }

    public SimulatorConnectionState Current { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public string? Reason { get; }
}

public enum SimulatorConnectionFailureKind
{
    None,
    PrerequisiteMissing,
    IncompatibleLibrary,
    SimulatorNotRunning,
    TimedOut,
    AccessDenied,
    ProviderFault,
}

public sealed record SimulatorConnectionResult
{
    private SimulatorConnectionResult(
        bool succeeded,
        SimulatorIdentity? simulator,
        SimulatorConnectionFailureKind failureKind,
        string? message)
    {
        Succeeded = succeeded;
        Simulator = simulator;
        FailureKind = failureKind;
        Message = message;
    }

    public bool Succeeded { get; }

    public SimulatorIdentity? Simulator { get; }

    public SimulatorConnectionFailureKind FailureKind { get; }

    public string? Message { get; }

    public static SimulatorConnectionResult Success(SimulatorIdentity simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        return new(true, simulator, SimulatorConnectionFailureKind.None, null);
    }

    public static SimulatorConnectionResult Failure(
        SimulatorConnectionFailureKind failureKind,
        string message)
    {
        if (failureKind == SimulatorConnectionFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind), "A failure must have a failure kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(false, null, failureKind, message.Trim());
    }
}

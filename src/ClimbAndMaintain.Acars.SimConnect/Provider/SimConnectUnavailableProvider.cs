using System.Threading.Channels;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.SimConnect.Provider;

public sealed class SimConnectUnavailableProvider : ISimulatorTelemetryProvider
{
    private readonly string reason;
    private readonly Channel<TelemetrySnapshot> telemetry;

    public SimConnectUnavailableProvider(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        this.reason = reason.Trim();
        telemetry = Channel.CreateUnbounded<TelemetrySnapshot>();
        telemetry.Writer.TryComplete();
    }

    public string ProviderId => "simconnect-unavailable";

    public SimulatorConnectionState ConnectionState => SimulatorConnectionState.Unavailable;

    public SimulatorIdentity? ConnectedSimulator => null;

    public event EventHandler<SimulatorConnectionStateChangedEventArgs>? ConnectionStateChanged
    {
        add { }
        remove { }
    }

    public ValueTask<SimulatorConnectionResult> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SimulatorConnectionResult.Failure(
            SimulatorConnectionFailureKind.PrerequisiteMissing,
            reason));
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<TelemetrySnapshot> ReadTelemetryAsync(CancellationToken cancellationToken) =>
        telemetry.Reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

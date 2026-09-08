using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Infrastructure.Replay;
using ClimbAndMaintain.Acars.SimConnect;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class TelemetrySourceSwitcher(
    RecordedTelemetryProvider replay,
    LiveSimulatorService liveSimulator) : IDisposable
{
    private readonly RecordedTelemetryProvider replay = replay ?? throw new ArgumentNullException(nameof(replay));
    private readonly LiveSimulatorService liveSimulator = liveSimulator ?? throw new ArgumentNullException(nameof(liveSimulator));
    private readonly SemaphoreSlim sourceGate = new(1, 1);
    private bool disposed;

    public async ValueTask<SimulatorConnectionResult> SwitchToLiveAsync(
        SimConnectTarget target,
        string libraryPath,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await sourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await replay.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await liveSimulator.ConnectAsync(target, libraryPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sourceGate.Release();
        }
    }

    public async ValueTask<SimulatorConnectionResult> SwitchToReplayAsync(
        double playbackRate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await sourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await liveSimulator.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await replay.SetPlaybackRateAsync(playbackRate, cancellationToken).ConfigureAwait(false);
            return await replay.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sourceGate.Release();
        }
    }

    public async ValueTask DisconnectLiveAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await sourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await liveSimulator.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sourceGate.Release();
        }
    }

    public async ValueTask DisconnectReplayAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await sourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await replay.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sourceGate.Release();
        }
    }

    public async ValueTask ReloadLiveProfilesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await sourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await replay.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await liveSimulator.ReloadProfilesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sourceGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        sourceGate.Dispose();
    }
}

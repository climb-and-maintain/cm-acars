namespace ClimbAndMaintain.Acars.Application.Contracts;

public enum ReplayState
{
    Empty,
    Loaded,
    Playing,
    Paused,
    Completed,
    Faulted,
}

public interface IRecordedTelemetryProvider : ISimulatorTelemetryProvider
{
    ReplayState ReplayState { get; }

    double PlaybackRate { get; }

    ValueTask LoadJsonLinesAsync(Stream recording, CancellationToken cancellationToken);

    ValueTask SetPlaybackRateAsync(double playbackRate, CancellationToken cancellationToken);
}

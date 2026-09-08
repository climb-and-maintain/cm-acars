using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.Infrastructure.Serialization;

namespace ClimbAndMaintain.Acars.Infrastructure.Replay;

public sealed class RecordedTelemetryProvider : IRecordedTelemetryProvider
{
    public const double InstantPlaybackRate = double.PositiveInfinity;

    private static readonly SimulatorIdentity ReplaySimulator =
        new(SimulatorKind.RecordedTelemetry, "Recorded telemetry", null);

    private static readonly JsonSerializerOptions JsonOptions = AcarsJsonSerializerOptions.Create();

    private readonly Lock stateLock = new();
    private readonly TimeProvider timeProvider;
    private ImmutableArray<ReplayFrame> frames = [];
    private CancellationTokenSource? activePlayback;
    private TaskCompletionSource<bool>? activePlaybackCompletion;
    private SimulatorConnectionState connectionState = SimulatorConnectionState.Disconnected;
    private SimulatorIdentity? connectedSimulator;
    private ReplayState replayState = ReplayState.Empty;
    private double playbackRate = 1;
    private bool disposed;

    public RecordedTelemetryProvider(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string ProviderId => "recorded-telemetry";

    public SimulatorConnectionState ConnectionState
    {
        get
        {
            lock (stateLock)
            {
                return connectionState;
            }
        }
    }

    public SimulatorIdentity? ConnectedSimulator
    {
        get
        {
            lock (stateLock)
            {
                return connectedSimulator;
            }
        }
    }

    public ReplayState ReplayState
    {
        get
        {
            lock (stateLock)
            {
                return replayState;
            }
        }
    }

    public double PlaybackRate
    {
        get
        {
            lock (stateLock)
            {
                return playbackRate;
            }
        }
    }

    public event EventHandler<SimulatorConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public async ValueTask LoadJsonLinesAsync(Stream recording, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(recording);
        if (!recording.CanRead)
        {
            throw new ArgumentException("The recording stream must be readable.", nameof(recording));
        }

        lock (stateLock)
        {
            if (activePlayback is not null)
            {
                throw new InvalidOperationException("A recording cannot be replaced while it is playing.");
            }
        }

        List<ReplayFrame> loadedFrames = [];
        using StreamReader reader = new(recording, leaveOpen: true);
        int lineNumber = 0;
        DateTimeOffset? previousTimestamp = null;

        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                ReplayFrame frame = ParseFrame(line, lineNumber);
                if (previousTimestamp is { } previous && previous > frame.TimestampUtc)
                {
                    throw new InvalidDataException(
                        FormattableString.Invariant(
                            $"Replay timestamp moved backwards at JSONL line {lineNumber}."));
                }

                previousTimestamp = frame.TimestampUtc;
                loadedFrames.Add(frame);
            }

            if (loadedFrames.Count == 0)
            {
                throw new InvalidDataException("The recording contains no telemetry or control frames.");
            }

            lock (stateLock)
            {
                frames = [.. loadedFrames];
                replayState = ReplayState.Loaded;
            }
        }
        catch
        {
            lock (stateLock)
            {
                frames = [];
                replayState = ReplayState.Faulted;
            }

            SetConnectionState(SimulatorConnectionState.Faulted, "The recording could not be loaded.");
            throw;
        }
    }

    public ValueTask SetPlaybackRateAsync(double playbackRate, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupportedRate(playbackRate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(playbackRate),
                playbackRate,
                "Playback rate must be 1x, 2x, 10x, or positive infinity for instant playback.");
        }

        lock (stateLock)
        {
            this.playbackRate = playbackRate;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<SimulatorConnectionResult> ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (stateLock)
        {
            if (frames.IsDefaultOrEmpty)
            {
                return ValueTask.FromResult(
                    SimulatorConnectionResult.Failure(
                        SimulatorConnectionFailureKind.PrerequisiteMissing,
                        "Load a recorded telemetry JSONL file before connecting replay mode."));
            }
        }

        SetConnectionState(SimulatorConnectionState.Connecting, null);
        lock (stateLock)
        {
            connectedSimulator = ReplaySimulator;
        }

        SetConnectionState(SimulatorConnectionState.Connected, null);
        SetConnectionState(SimulatorConnectionState.Ready, null);
        return ValueTask.FromResult(SimulatorConnectionResult.Success(ReplaySimulator));
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await StopPlaybackAsync(dispose: false).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TelemetrySnapshot> ReadTelemetryAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ImmutableArray<ReplayFrame> playbackFrames;
        double rate;
        CancellationTokenSource playbackCancellation;

        lock (stateLock)
        {
            if (connectionState != SimulatorConnectionState.Ready)
            {
                throw new InvalidOperationException("Replay must be connected and ready before telemetry is read.");
            }

            if (activePlayback is not null)
            {
                throw new InvalidOperationException("Only one replay reader can run at a time.");
            }

            playbackFrames = frames;
            rate = playbackRate;
            playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activePlayback = playbackCancellation;
            activePlaybackCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            replayState = ReplayState.Playing;
        }

        SetConnectionState(SimulatorConnectionState.Tracking, null);
        bool completed = false;
        try
        {
            DateTimeOffset? previousTimestamp = null;
            foreach (ReplayFrame frame in playbackFrames)
            {
                playbackCancellation.Token.ThrowIfCancellationRequested();
                if (previousTimestamp is { } previous)
                {
                    await DelayAsync(frame.TimestampUtc - previous, rate, playbackCancellation.Token)
                        .ConfigureAwait(false);
                }

                previousTimestamp = frame.TimestampUtc;
                if (frame is DisconnectFrame disconnect)
                {
                    SetConnectionState(SimulatorConnectionState.Recovering, disconnect.Reason);
                    await DelayAsync(disconnect.ReconnectAfter, rate, playbackCancellation.Token)
                        .ConfigureAwait(false);
                    SetConnectionState(SimulatorConnectionState.Tracking, "Replay connection restored.");
                    continue;
                }

                yield return ((TelemetryFrame)frame).Snapshot;
            }

            completed = true;
        }
        finally
        {
            TaskCompletionSource<bool>? playbackCompletion = null;
            lock (stateLock)
            {
                if (ReferenceEquals(activePlayback, playbackCancellation))
                {
                    activePlayback = null;
                    playbackCompletion = activePlaybackCompletion;
                    activePlaybackCompletion = null;
                    if (connectionState != SimulatorConnectionState.Disconnected)
                    {
                        replayState = completed ? ReplayState.Completed : ReplayState.Loaded;
                    }
                }
            }

            playbackCancellation.Dispose();
            playbackCompletion?.TrySetResult(true);
            if (ConnectionState != SimulatorConnectionState.Disconnected)
            {
                SetConnectionState(SimulatorConnectionState.Ready, completed ? "Replay completed." : "Replay stopped.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await StopPlaybackAsync(dispose: true).ConfigureAwait(false);
    }

    private async ValueTask StopPlaybackAsync(bool dispose)
    {
        CancellationTokenSource? playback;
        Task? playbackCompletion;
        SimulatorConnectionState previousState;
        lock (stateLock)
        {
            previousState = connectionState;
            connectionState = SimulatorConnectionState.Disconnected;
            connectedSimulator = null;
            playback = activePlayback;
            playbackCompletion = activePlaybackCompletion?.Task;
            if (replayState is ReplayState.Playing or ReplayState.Paused)
            {
                replayState = ReplayState.Loaded;
            }

            disposed = dispose;
        }

        if (previousState != SimulatorConnectionState.Disconnected)
        {
            ConnectionStateChanged?.Invoke(
                this,
                new SimulatorConnectionStateChangedEventArgs(
                    previousState,
                    SimulatorConnectionState.Disconnected,
                    timeProvider.GetUtcNow().ToUniversalTime(),
                    dispose ? "Replay disposed." : "Replay disconnected."));
        }

        try
        {
            playback?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The playback iterator won the completion race and already disposed its linked token source.
        }

        if (playbackCompletion is not null)
        {
            await playbackCompletion.ConfigureAwait(false);
        }
    }

    private static ReplayFrame ParseFrame(string json, int lineNumber)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("kind", out JsonElement kindElement))
            {
                TelemetrySnapshot snapshot = DeserializeSnapshot(root.GetRawText(), lineNumber);
                return new TelemetryFrame(snapshot);
            }

            string? kind = kindElement.GetString();
            return kind switch
            {
                "telemetry" => new TelemetryFrame(
                    DeserializeSnapshot(root.GetProperty("snapshot").GetRawText(), lineNumber)),
                "disconnect" => ParseDisconnect(root),
                _ => throw new InvalidDataException(
                    FormattableString.Invariant($"Unknown replay frame kind '{kind}' at JSONL line {lineNumber}.")),
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                FormattableString.Invariant($"Invalid JSON at replay line {lineNumber}."),
                exception);
        }
    }

    private static TelemetrySnapshot DeserializeSnapshot(string json, int lineNumber)
    {
        TelemetrySnapshot snapshot = JsonSerializer.Deserialize<TelemetrySnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException(
                FormattableString.Invariant($"Missing telemetry snapshot at JSONL line {lineNumber}."));
        if (snapshot.CollectedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                FormattableString.Invariant($"Telemetry timestamp at JSONL line {lineNumber} must use UTC."));
        }

        return snapshot;
    }

    private static DisconnectFrame ParseDisconnect(JsonElement element)
    {
        DateTimeOffset timestamp = element.GetProperty("timestampUtc").GetDateTimeOffset();
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("Replay disconnect timestamps must use UTC.");
        }

        double reconnectAfterSeconds = element.TryGetProperty("reconnectAfterSeconds", out JsonElement duration)
            ? duration.GetDouble()
            : 0;
        if (!double.IsFinite(reconnectAfterSeconds) || reconnectAfterSeconds < 0)
        {
            throw new InvalidDataException("Replay reconnect duration must be finite and non-negative.");
        }

        string reason = element.TryGetProperty("reason", out JsonElement reasonElement)
            ? reasonElement.GetString() ?? "Recorded simulator disconnect."
            : "Recorded simulator disconnect.";
        return new DisconnectFrame(timestamp, TimeSpan.FromSeconds(reconnectAfterSeconds), reason);
    }

    private static bool IsSupportedRate(double rate) =>
        rate is 1 or 2 or 10 || double.IsPositiveInfinity(rate);

    private static Task DelayAsync(TimeSpan recordingDelay, double rate, CancellationToken cancellationToken)
    {
        if (recordingDelay <= TimeSpan.Zero || double.IsPositiveInfinity(rate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        TimeSpan delay = TimeSpan.FromTicks((long)(recordingDelay.Ticks / rate));
        return Task.Delay(delay, cancellationToken);
    }

    private void SetConnectionState(SimulatorConnectionState newState, string? reason)
    {
        SimulatorConnectionState previous;
        lock (stateLock)
        {
            previous = connectionState;
            if (previous == newState)
            {
                return;
            }

            connectionState = newState;
        }

        ConnectionStateChanged?.Invoke(
            this,
            new SimulatorConnectionStateChangedEventArgs(
                previous,
                newState,
                timeProvider.GetUtcNow().ToUniversalTime(),
                reason));
    }

    private abstract record ReplayFrame(DateTimeOffset TimestampUtc);

    private sealed record TelemetryFrame(TelemetrySnapshot Snapshot)
        : ReplayFrame(Snapshot.CollectedAtUtc);

    private sealed record DisconnectFrame(
        DateTimeOffset TimestampUtc,
        TimeSpan ReconnectAfter,
        string Reason)
        : ReplayFrame(TimestampUtc);
}

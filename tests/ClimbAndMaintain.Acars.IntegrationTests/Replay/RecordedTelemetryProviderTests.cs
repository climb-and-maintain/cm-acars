using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.Infrastructure.Replay;

namespace ClimbAndMaintain.Acars.IntegrationTests.Replay;

public sealed class RecordedTelemetryProviderTests
{
    [Fact]
    public async Task NormalFlightReplaysInstantlyWithoutSimulatorDependencies()
    {
        await using RecordedTelemetryProvider provider = new();
        await using FileStream recording = File.OpenRead(FixturePath("normal-flight.jsonl"));

        await provider.LoadJsonLinesAsync(recording, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await provider.SetPlaybackRateAsync(
            RecordedTelemetryProvider.InstantPlaybackRate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        SimulatorConnectionResult result = await provider
            .ConnectAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        List<TelemetrySnapshot> snapshots = [];
        await foreach (TelemetrySnapshot snapshot in provider
            .ReadTelemetryAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true))
        {
            snapshots.Add(snapshot);
        }

        Assert.True(result.Succeeded);
        Assert.Equal(SimulatorKind.RecordedTelemetry, result.Simulator?.Kind);
        Assert.Equal(11, snapshots.Count);
        Assert.True(snapshots[0].OnGround);
        Assert.True(snapshots[^1].OnGround);
        Assert.Equal(0, snapshots[^1].RunningEngineCount);
        Assert.Equal(ReplayState.Completed, provider.ReplayState);
        Assert.Equal(SimulatorConnectionState.Ready, provider.ConnectionState);
    }

    [Fact]
    public async Task DisconnectFixtureTransitionsThroughRecoveringAndContinues()
    {
        await using RecordedTelemetryProvider provider = new();
        List<SimulatorConnectionState> states = [];
        provider.ConnectionStateChanged += (_, args) => states.Add(args.Current);
        await using FileStream recording = File.OpenRead(FixturePath("simulator-disconnect.jsonl"));
        await provider.LoadJsonLinesAsync(recording, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await provider.SetPlaybackRateAsync(
            RecordedTelemetryProvider.InstantPlaybackRate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await provider.ConnectAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        List<TelemetrySnapshot> snapshots = [];
        await foreach (TelemetrySnapshot snapshot in provider
            .ReadTelemetryAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true))
        {
            snapshots.Add(snapshot);
        }

        Assert.Equal(2, snapshots.Count);
        Assert.Contains(SimulatorConnectionState.Recovering, states);
        Assert.True(states.LastIndexOf(SimulatorConnectionState.Tracking)
            > states.IndexOf(SimulatorConnectionState.Recovering));
        Assert.Equal(SimulatorConnectionState.Ready, provider.ConnectionState);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(double.PositiveInfinity)]
    public async Task SupportedPlaybackRatesAreAccepted(double rate)
    {
        await using RecordedTelemetryProvider provider = new();

        await provider.SetPlaybackRateAsync(rate, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(rate, provider.PlaybackRate);
    }

    [Fact]
    public async Task MissingRecordingReturnsActionableConnectionFailure()
    {
        await using RecordedTelemetryProvider provider = new();

        SimulatorConnectionResult result = await provider
            .ConnectAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal(SimulatorConnectionFailureKind.PrerequisiteMissing, result.FailureKind);
        Assert.Equal(SimulatorConnectionState.Disconnected, provider.ConnectionState);
    }

    [Fact]
    public async Task DisconnectWaitsUntilTheActiveReplayReaderHasDrained()
    {
        await using RecordedTelemetryProvider provider = new();
        await using FileStream recording = File.OpenRead(FixturePath("normal-flight.jsonl"));
        await provider.LoadJsonLinesAsync(recording, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await provider.SetPlaybackRateAsync(
            RecordedTelemetryProvider.InstantPlaybackRate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await provider.ConnectAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        TaskCompletionSource<bool> firstFrameReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task reader = ConsumeUntilCancelledAsync(provider, firstFrameReached, releaseReader);
        await firstFrameReached.Task.ConfigureAwait(true);

        Task disconnect = provider.DisconnectAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(disconnect.IsCompleted);
        releaseReader.SetResult(true);
        await disconnect.ConfigureAwait(true);
        await reader.ConfigureAwait(true);

        Assert.Equal(SimulatorConnectionState.Disconnected, provider.ConnectionState);
        Assert.Equal(ReplayState.Loaded, provider.ReplayState);
    }

    [Theory]
    [InlineData("network-failure.jsonl")]
    [InlineData("touch-and-go.jsonl")]
    [InlineData("paused-flight.jsonl")]
    public async Task RequiredScenarioFixturesReplayWithoutSimulatorDependencies(string fileName)
    {
        await using RecordedTelemetryProvider provider = new();
        await using FileStream recording = File.OpenRead(FixturePath(fileName));
        await provider.LoadJsonLinesAsync(recording, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await provider.SetPlaybackRateAsync(
            RecordedTelemetryProvider.InstantPlaybackRate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        SimulatorConnectionResult result = await provider
            .ConnectAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        List<TelemetrySnapshot> snapshots = [];
        await foreach (TelemetrySnapshot snapshot in provider
            .ReadTelemetryAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true))
        {
            snapshots.Add(snapshot);
        }

        Assert.True(result.Succeeded);
        Assert.Equal(3, snapshots.Count);
        Assert.Equal(ReplayState.Completed, provider.ReplayState);

        if (fileName == "touch-and-go.jsonl")
        {
            Assert.Equal([false, true, false], snapshots.Select(snapshot => snapshot.OnGround));
        }
        else if (fileName == "paused-flight.jsonl")
        {
            Assert.Equal([false, true, false], snapshots.Select(snapshot => snapshot.IsPaused));
        }
    }

    private static async Task ConsumeUntilCancelledAsync(
        RecordedTelemetryProvider provider,
        TaskCompletionSource<bool> firstFrameReached,
        TaskCompletionSource<bool> releaseReader)
    {
        try
        {
            await foreach (TelemetrySnapshot _ in provider.ReadTelemetryAsync(CancellationToken.None).ConfigureAwait(true))
            {
                firstFrameReached.TrySetResult(true);
                await releaseReader.Task.ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when DisconnectAsync cancels the provider-owned playback token.
        }
    }

    private static string FixturePath(string fileName) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "replay", fileName);
}

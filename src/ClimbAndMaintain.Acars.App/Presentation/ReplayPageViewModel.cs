using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Input;
using ClimbAndMaintain.Acars.App.Services;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.Infrastructure.Replay;
using System.Waf.Applications;
using System.Waf.Foundation;

namespace ClimbAndMaintain.Acars.App.Presentation;

public sealed record PlaybackRateOption(string Name, double Rate);

public sealed class ReplayPageViewModel : Model, IAsyncDisposable
{
    private readonly RecordedTelemetryProvider provider;
    private readonly IDesktopFileDialogService fileDialog;
    private readonly FlightOperationsService flightOperations;
    private readonly TelemetrySourceSwitcher sourceSwitcher;
    private readonly AsyncDelegateCommand playCommand;
    private readonly AsyncDelegateCommand stopCommand;
    private string recordingPath = "No recording loaded.";
    private string status = "Replay mode is ready. It does not require SimConnect.";
    private string lastTelemetry = "No telemetry received.";
    private string phase = FlightPhase.Ready.ToString();
    private PlaybackRateOption selectedRate;
    private bool isPlaying;
    private int frameCount;
    private CancellationTokenSource? playbackCancellation;

    public ReplayPageViewModel(
        RecordedTelemetryProvider provider,
        IDesktopFileDialogService fileDialog,
        FlightOperationsService flightOperations,
        TelemetrySourceSwitcher sourceSwitcher)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        this.flightOperations = flightOperations ?? throw new ArgumentNullException(nameof(flightOperations));
        this.sourceSwitcher = sourceSwitcher ?? throw new ArgumentNullException(nameof(sourceSwitcher));
        PlaybackRates =
        [
            new("Real time (1×)", 1),
            new("Twice speed (2×)", 2),
            new("Ten times speed (10×)", 10),
            new("Instant test mode", RecordedTelemetryProvider.InstantPlaybackRate),
        ];
        selectedRate = PlaybackRates[0];

        LoadCommand = new AsyncDelegateCommand(LoadAsync, () => !IsPlaying);
        playCommand = new AsyncDelegateCommand(PlayAsync, () => !IsPlaying && provider.ReplayState is not ReplayState.Empty);
        stopCommand = new AsyncDelegateCommand(StopAsync, () => IsPlaying);
    }

    public IReadOnlyList<PlaybackRateOption> PlaybackRates { get; }

    public PlaybackRateOption SelectedRate
    {
        get => selectedRate;
        set => SetProperty(ref selectedRate, value);
    }

    public string RecordingPath
    {
        get => recordingPath;
        private set => SetProperty(ref recordingPath, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string LastTelemetry
    {
        get => lastTelemetry;
        private set => SetProperty(ref lastTelemetry, value);
    }

    public string Phase
    {
        get => phase;
        private set => SetProperty(ref phase, value);
    }

    public int FrameCount
    {
        get => frameCount;
        private set => SetProperty(ref frameCount, value);
    }

    public bool IsPlaying
    {
        get => isPlaying;
        private set
        {
            if (SetProperty(ref isPlaying, value))
            {
                playCommand.RaiseCanExecuteChanged();
                stopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand LoadCommand { get; }

    public ICommand PlayCommand => playCommand;

    public ICommand StopCommand => stopCommand;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Replay file errors are isolated and shown without closing the application.")]
    private async Task LoadAsync()
    {
        string? path = fileDialog.SelectTelemetryRecording();
        if (path is null)
        {
            return;
        }

        try
        {
            await RecordedTelemetryFileService.LoadAsync(provider, path, CancellationToken.None);
            RecordingPath = path;
            Status = "Recording loaded. Choose a speed and start replay.";
            playCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            Status = $"Recording could not be loaded. {exception.Message}";
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Replay failures are isolated and shown without closing the application.")]
    private async Task PlayAsync()
    {
        playbackCancellation?.Dispose();
        playbackCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = playbackCancellation.Token;
        IsPlaying = true;
        FrameCount = 0;
        Phase = FlightPhase.Ready.ToString();
        FlightPhaseEngine phaseEngine = new();
        DateTimeOffset lastCollectedAtUtc = flightOperations.CurrentSession?.UpdatedAtUtc
            ?? DateTimeOffset.MinValue;

        try
        {
            SimulatorConnectionResult connection = await sourceSwitcher.SwitchToReplayAsync(
                SelectedRate.Rate,
                cancellationToken);
            if (!connection.Succeeded)
            {
                throw new InvalidOperationException(connection.Message ?? "Replay could not start.");
            }

            Status = $"Playing {SelectedRate.Name}.";
            await foreach (TelemetrySnapshot snapshot in provider.ReadTelemetryAsync(cancellationToken))
            {
                DateTimeOffset collectedAtUtc = DateTimeOffset.UtcNow;
                if (collectedAtUtc < lastCollectedAtUtc)
                {
                    collectedAtUtc = lastCollectedAtUtc;
                }

                lastCollectedAtUtc = collectedAtUtc;
                TelemetrySnapshot playbackSnapshot = snapshot with
                {
                    CollectedAtUtc = collectedAtUtc,
                    SimulatorTime = snapshot.CollectedAtUtc,
                    Simulator = connection.Simulator ?? snapshot.Simulator,
                };
                FrameCount++;
                _ = phaseEngine.Observe(snapshot);
                Phase = phaseEngine.CurrentPhase.ToString();
                LastTelemetry = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0:N0} ft MSL, {1:N0} kt ground speed, {2:N0} ft/min vertical speed",
                    playbackSnapshot.AltitudeMsl.Feet,
                    playbackSnapshot.GroundSpeed.Knots,
                    playbackSnapshot.VerticalSpeed.FeetPerMinute);
                if (flightOperations.CurrentSession?.Status == FlightSessionStatus.Active)
                {
                    _ = await flightOperations.ProcessTelemetryAsync(playbackSnapshot, cancellationToken);
                }
            }

            Status = $"Replay completed. {FrameCount:N0} telemetry frames processed.";
        }
        catch (OperationCanceledException)
        {
            Status = $"Replay stopped after {FrameCount:N0} telemetry frames.";
        }
        catch (Exception exception)
        {
            Status = $"Replay failed. {exception.Message}";
        }
        finally
        {
            IsPlaying = false;
        }
    }

    private async Task StopAsync()
    {
        playbackCancellation?.Cancel();
        await sourceSwitcher.DisconnectReplayAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        playbackCancellation?.Cancel();
        playbackCancellation?.Dispose();
        await provider.DisposeAsync();
    }
}

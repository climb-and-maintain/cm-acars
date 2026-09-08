using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Input;
using ClimbAndMaintain.Acars.App.Services;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Synchronization;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;
using System.Waf.Applications;
using System.Waf.Foundation;

namespace ClimbAndMaintain.Acars.App.Presentation;

public sealed record AvailableFlightItem(BackendFlight Flight)
{
    public string DisplayName => string.Format(
        CultureInfo.CurrentCulture,
        "{0}  {1} → {2}",
        Flight.FlightPlan.FlightNumber,
        Flight.FlightPlan.DepartureAirport,
        Flight.FlightPlan.ArrivalAirport);
}

public sealed class FlightOperationsViewModel : Model
{
    private const int MaximumVisibleLogEntries = 500;

    private readonly FlightOperationsService operations;
    private readonly AsyncDelegateCommand refreshFlightsCommand;
    private readonly AsyncDelegateCommand startCommand;
    private readonly AsyncDelegateCommand pauseCommand;
    private readonly AsyncDelegateCommand resumeCommand;
    private readonly AsyncDelegateCommand cancelCommand;
    private readonly AsyncDelegateCommand fileCommand;
    private readonly AsyncDelegateCommand synchronizeCommand;
    private readonly AsyncDelegateCommand resumeRecoveredCommand;
    private readonly AsyncDelegateCommand discardRecoveredCommand;
    private AvailableFlightItem? selectedFlight;
    private FlightSessionState? currentSession;
    private string status = "No flight selected. Simulator setup and replay remain available.";
    private string telemetry = "No live flight telemetry received.";
    private string distanceNauticalMiles = "0";
    private string flightTimeMinutes = "0";
    private bool distanceWasEdited;
    private bool flightTimeWasEdited;
    private bool isBusy;
    private bool hasRecoveryPrompt;

    public FlightOperationsViewModel(FlightOperationsService operations)
    {
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        operations.SessionChanged += OnSessionChanged;

        refreshFlightsCommand = new AsyncDelegateCommand(RefreshFlightsAsync, () => !IsBusy);
        startCommand = new AsyncDelegateCommand(StartAsync, () => !IsBusy && SelectedFlight is not null && !HasActiveFlight);
        pauseCommand = new AsyncDelegateCommand(PauseAsync, () => !IsBusy && CurrentSession?.Status == FlightSessionStatus.Active);
        resumeCommand = new AsyncDelegateCommand(ResumeAsync, () => !IsBusy && CurrentSession?.Status == FlightSessionStatus.Paused);
        cancelCommand = new AsyncDelegateCommand(CancelAsync, () => !IsBusy && HasActiveFlight);
        fileCommand = new AsyncDelegateCommand(FileAsync, () => !IsBusy && CanFileFlight);
        synchronizeCommand = new AsyncDelegateCommand(SynchronizeAsync, () => !IsBusy);
        resumeRecoveredCommand = new AsyncDelegateCommand(
            ResumeRecoveredAsync,
            () => !IsBusy && HasRecoveryPrompt);
        discardRecoveredCommand = new AsyncDelegateCommand(DiscardRecoveredAsync, () => !IsBusy && HasRecoveryPrompt);
        InspectRecoveredCommand = new DelegateCommand(InspectRecovered, () => HasRecoveryPrompt);
    }

    public ObservableCollection<AvailableFlightItem> AvailableFlights { get; } = [];

    public ObservableCollection<string> LogEntries { get; } = [];

    public AvailableFlightItem? SelectedFlight
    {
        get => selectedFlight;
        set
        {
            if (SetProperty(ref selectedFlight, value))
            {
                startCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public FlightSessionState? CurrentSession
    {
        get => currentSession;
        private set
        {
            if (!SetProperty(ref currentSession, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(HasActiveFlight));
            RaisePropertyChanged(nameof(CanFileFlight));
            RaisePropertyChanged(nameof(CurrentFlight));
            RaisePropertyChanged(nameof(CurrentPhase));
            RaisePropertyChanged(nameof(ElapsedTime));
            RaisePropertyChanged(nameof(BlockTime));
            RaisePropertyChanged(nameof(AirborneTime));
            RaiseCommandStates();
        }
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string Telemetry
    {
        get => telemetry;
        private set => SetProperty(ref telemetry, value);
    }

    public string DistanceNauticalMiles
    {
        get => distanceNauticalMiles;
        set
        {
            if (SetProperty(ref distanceNauticalMiles, value))
            {
                distanceWasEdited = true;
            }
        }
    }

    public string FlightTimeMinutes
    {
        get => flightTimeMinutes;
        set
        {
            if (SetProperty(ref flightTimeMinutes, value))
            {
                flightTimeWasEdited = true;
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool HasRecoveryPrompt
    {
        get => hasRecoveryPrompt;
        private set
        {
            if (SetProperty(ref hasRecoveryPrompt, value))
            {
                discardRecoveredCommand.RaiseCanExecuteChanged();
                resumeRecoveredCommand.RaiseCanExecuteChanged();
                ((DelegateCommand)InspectRecoveredCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasActiveFlight => CurrentSession?.Status is FlightSessionStatus.Starting
        or FlightSessionStatus.Active
        or FlightSessionStatus.Paused;

    public bool CanFileFlight => CurrentSession is { Status: FlightSessionStatus.Active, Phase: FlightPhase.OnBlock };

    public string CurrentFlight => CurrentSession is null
        ? "No active flight"
        : $"{CurrentSession.FlightPlan.FlightNumber}  {CurrentSession.FlightPlan.DepartureAirport} → {CurrentSession.FlightPlan.ArrivalAirport}";

    public string CurrentPhase => CurrentSession?.Phase.ToString() ?? FlightPhase.Ready.ToString();

    public string ElapsedTime => CurrentSession is null
        ? "00:00:00"
        : (CurrentSession.UpdatedAtUtc - CurrentSession.StartedAtUtc).ToString("c", CultureInfo.CurrentCulture);

    public string BlockTime => (CurrentSession?.Metrics.ActiveTrackingTime ?? TimeSpan.Zero)
        .ToString("c", CultureInfo.CurrentCulture);

    public string AirborneTime => (CurrentSession?.Metrics.AirborneFlightTime ?? TimeSpan.Zero)
        .ToString("c", CultureInfo.CurrentCulture);

    public ICommand RefreshFlightsCommand => refreshFlightsCommand;

    public ICommand StartCommand => startCommand;

    public ICommand PauseCommand => pauseCommand;

    public ICommand ResumeCommand => resumeCommand;

    public ICommand CancelCommand => cancelCommand;

    public ICommand FileCommand => fileCommand;

    public ICommand SynchronizeCommand => synchronizeCommand;

    public ICommand ResumeRecoveredCommand => resumeRecoveredCommand;

    public ICommand DiscardRecoveredCommand => discardRecoveredCommand;

    public ICommand InspectRecoveredCommand { get; }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A damaged optional flight store must not prevent the shell, configuration, diagnostics, or replay from opening.")]
    public async Task InitializeAsync()
    {
        try
        {
            FlightSessionState? restored = await operations.RestoreAsync(CancellationToken.None);
            if (restored is null)
            {
                return;
            }

            HasRecoveryPrompt = true;
            Status = restored.Status == FlightSessionStatus.Starting
                ? $"An unfinished phpVMS prefile was found for {CurrentFlight}. Choose Resume to reconcile it, Discard, or Inspect."
                : $"An unfinished flight was found: {CurrentFlight}. Choose Resume, Discard, or Inspect.";
            AppendLog($"Recovered flight state from {restored.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC.");
        }
        catch (Exception exception)
        {
            Status = $"Saved flight recovery is unavailable: {exception.Message} Configuration, diagnostics, and replay remain available.";
            AppendLog("Saved flight recovery failed; other application modes remain available.");
        }
    }

    private async Task RefreshFlightsAsync()
    {
        await RunAsync(async () =>
        {
            IReadOnlyList<BackendFlight> flights = await operations.GetAvailableFlightsAsync(CancellationToken.None);
            AvailableFlights.Clear();
            foreach (BackendFlight flight in flights)
            {
                AvailableFlights.Add(new AvailableFlightItem(flight));
            }

            SelectedFlight = AvailableFlights.FirstOrDefault();
            Status = flights.Count == 0
                ? "phpVMS returned no available flights or bids."
                : $"Loaded {flights.Count:N0} available phpVMS flight(s).";
        });
    }

    private async Task StartAsync()
    {
        AvailableFlightItem flight = SelectedFlight
            ?? throw new InvalidOperationException("Select a flight before starting.");
        await RunAsync(async () =>
        {
            FlightSessionState session = await operations.StartAsync(flight.Flight, CancellationToken.None);
            HasRecoveryPrompt = false;
            ApplyMeasuredFilingDefaults(session, resetOverrides: true);
            Status = $"Flight {session.FlightPlan.FlightNumber} started and prefiled with phpVMS.";
            AppendLog($"Started {CurrentFlight}.");
        });
    }

    private Task PauseAsync() => RunAsync(async () =>
    {
        _ = await operations.PauseAsync(CancellationToken.None);
        Status = "Flight tracking paused; the durable session and queue were preserved.";
        AppendLog("Flight tracking paused.");
    });

    private Task ResumeAsync() => RunAsync(async () =>
    {
        _ = await operations.ResumeAsync(CancellationToken.None);
        Status = "Flight tracking resumed.";
        AppendLog("Flight tracking resumed.");
    });

    private Task CancelAsync() => RunAsync(async () =>
    {
        bool wasPendingStart = CurrentSession?.Status == FlightSessionStatus.Starting;
        _ = await operations.CancelAsync(CancellationToken.None);
        HasRecoveryPrompt = false;
        Status = wasPendingStart
            ? "Pending start discarded locally. If the phpVMS response was lost, review the phpVMS PIREP list and cancel any matching entry manually."
            : "Flight cancelled locally. The cancellation is queued for phpVMS until synchronization succeeds.";
        AppendLog(wasPendingStart
            ? "Pending phpVMS prefile discarded locally."
            : "Flight cancelled; backend cancellation queued.");
    });

    private Task SynchronizeAsync() => RunAsync(async () =>
    {
        OutboxSynchronizationResult result = await operations.SynchronizeAsync(CancellationToken.None);
        Status = result.FailureMessage is null
            ? $"Queue synchronization: {result.Status}; {result.DeliveredCount:N0} delivered, {result.Remaining.Total:N0} remain."
            : $"Queue synchronization: {result.Status}. {result.FailureMessage} {result.Remaining.Total:N0} item(s) remain safely queued.";
        AppendLog(Status);
    });

    private Task FileAsync() => RunAsync(async () =>
    {
        FlightSessionState session = CurrentSession
            ?? throw new InvalidOperationException("There is no active flight to file.");
        if (!double.TryParse(
                DistanceNauticalMiles,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double distance)
            || !double.IsFinite(distance)
            || distance < 0)
        {
            throw new InvalidOperationException("Enter a non-negative distance in nautical miles.");
        }

        if (!double.TryParse(
                FlightTimeMinutes,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double minutes)
            || !double.IsFinite(minutes)
            || minutes < 0)
        {
            throw new InvalidOperationException("Enter a non-negative flight time in minutes.");
        }

        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        if (completedAtUtc < session.UpdatedAtUtc)
        {
            completedAtUtc = session.UpdatedAtUtc;
        }

        FlightSessionMetrics metrics = session.Metrics ?? new FlightSessionMetrics();
        CompletedFlightReport report = new(
            new Distance(distanceWasEdited ? distance : metrics.FlownDistance.NauticalMiles),
            flightTimeWasEdited ? TimeSpan.FromMinutes(minutes) : metrics.AirborneFlightTime,
            metrics.ActiveTrackingTime,
            metrics.FuelUsed,
            metrics.LandingRate,
            completedAtUtc);
        _ = await operations.CompleteAsync(report, CancellationToken.None);
        Status = "PIREP completion is saved locally. Synchronize now or later; network loss cannot discard it.";
        AppendLog("PIREP completion queued.");
    });

    private Task ResumeRecoveredAsync() => RunAsync(async () =>
    {
        bool wasPendingStart = CurrentSession?.Status == FlightSessionStatus.Starting;
        if (wasPendingStart)
        {
            _ = await operations.ResumePendingStartAsync(CancellationToken.None);
        }
        else if (CurrentSession?.Status == FlightSessionStatus.Paused)
        {
            _ = await operations.ResumeAsync(CancellationToken.None);
        }

        HasRecoveryPrompt = false;
        Status = wasPendingStart
            ? $"Reconciled the phpVMS prefile and started {CurrentFlight}."
            : $"Resumed recovered flight {CurrentFlight}.";
        AppendLog(wasPendingStart
            ? "Recovered phpVMS prefile reconciled; flight tracking started."
            : "Recovered flight resumed.");
    });

    private Task DiscardRecoveredAsync() => RunAsync(async () =>
    {
        bool wasPendingStart = CurrentSession?.Status == FlightSessionStatus.Starting;
        _ = await operations.CancelAsync(CancellationToken.None);
        HasRecoveryPrompt = false;
        Status = wasPendingStart
            ? "Pending start discarded locally. If phpVMS committed it, cancel that PIREP manually."
            : "Recovered flight discarded locally; any phpVMS cancellation remains safely queued.";
        AppendLog(wasPendingStart
            ? "Recovered pending prefile discarded locally."
            : "Recovered flight discarded and cancellation queued.");
    });

    private void InspectRecovered()
    {
        bool pendingStart = CurrentSession?.Status == FlightSessionStatus.Starting;
        HasRecoveryPrompt = pendingStart;
        Status = pendingStart
            ? $"Pending prefile for {CurrentFlight}; state {CurrentSession!.StartIntent?.State}; last update {CurrentSession.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC. Resume will reconcile without sending a duplicate."
            : $"Recovered {CurrentFlight}; phase {CurrentPhase}; last update {CurrentSession!.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC.";
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "User-command failures are converted into non-fatal status text.")]
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            AppendLog($"Action failed: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnSessionChanged(object? sender, FlightSessionChangedEventArgs eventArgs)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => ApplySession(eventArgs.Session, eventArgs.Telemetry));
            return;
        }

        ApplySession(eventArgs.Session, eventArgs.Telemetry);
    }

    private void ApplySession(FlightSessionState? session, TelemetrySnapshot? snapshot)
    {
        FlightSessionId? previousSessionId = CurrentSession?.Id;
        FlightPhase? previousPhase = CurrentSession?.Phase;
        CurrentSession = session;
        if (session is not null)
        {
            ApplyMeasuredFilingDefaults(session, previousSessionId != session.Id);
        }

        if (session?.Status == FlightSessionStatus.Starting)
        {
            HasRecoveryPrompt = true;
        }

        if (snapshot is not null)
        {
            Telemetry = string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} ft MSL · {1:N0} kt GS · {2:N0} kt IAS · {3:N0} ft/min VS · fuel {4}",
                snapshot.AltitudeMsl.Feet,
                snapshot.GroundSpeed.Knots,
                snapshot.IndicatedAirspeed.Knots,
                snapshot.VerticalSpeed.FeetPerMinute,
                snapshot.FuelRemaining is { } fuel
                    ? $"{fuel.Kilograms:N0} kg"
                    : "not reported");
        }

        if (session is not null && previousPhase is not null && previousPhase != session.Phase)
        {
            AppendLog($"Phase changed from {previousPhase} to {session.Phase}.");
        }
    }

    private void ApplyMeasuredFilingDefaults(FlightSessionState session, bool resetOverrides)
    {
        if (resetOverrides)
        {
            distanceWasEdited = false;
            flightTimeWasEdited = false;
        }

        FlightSessionMetrics metrics = session.Metrics ?? new FlightSessionMetrics();
        if (!distanceWasEdited)
        {
            _ = SetProperty(
                ref distanceNauticalMiles,
                metrics.FlownDistance.NauticalMiles.ToString("0.###", CultureInfo.CurrentCulture),
                nameof(DistanceNauticalMiles));
        }

        if (!flightTimeWasEdited)
        {
            _ = SetProperty(
                ref flightTimeMinutes,
                metrics.AirborneFlightTime.TotalMinutes.ToString("0.##", CultureInfo.CurrentCulture),
                nameof(FlightTimeMinutes));
        }
    }

    private void AppendLog(string entry)
    {
        LogEntries.Add($"{DateTimeOffset.Now:T}  {entry}");
        while (LogEntries.Count > MaximumVisibleLogEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    private void RaiseCommandStates()
    {
        refreshFlightsCommand.RaiseCanExecuteChanged();
        startCommand.RaiseCanExecuteChanged();
        pauseCommand.RaiseCanExecuteChanged();
        resumeCommand.RaiseCanExecuteChanged();
        cancelCommand.RaiseCanExecuteChanged();
        fileCommand.RaiseCanExecuteChanged();
        synchronizeCommand.RaiseCanExecuteChanged();
        resumeRecoveredCommand.RaiseCanExecuteChanged();
        discardRecoveredCommand.RaiseCanExecuteChanged();
    }
}

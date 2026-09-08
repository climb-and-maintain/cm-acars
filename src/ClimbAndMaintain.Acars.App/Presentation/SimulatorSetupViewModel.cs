using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using ClimbAndMaintain.Acars.App.Configuration;
using ClimbAndMaintain.Acars.App.Services;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.SimConnect;
using ClimbAndMaintain.Acars.SimConnect.Configuration;
using ClimbAndMaintain.Acars.SimConnect.Connection;
using ClimbAndMaintain.Acars.SimConnect.Diagnostics;
using ClimbAndMaintain.Acars.SimConnect.Discovery;
using ClimbAndMaintain.Acars.SimConnect.Validation;
using System.Waf.Applications;
using System.Waf.Foundation;

namespace ClimbAndMaintain.Acars.App.Presentation;

public sealed record SimulatorLibraryCandidateItem(
    string Path,
    string Source,
    SimConnectTarget? TargetHint,
    bool StaticValidationPassed)
{
    public string DisplayName => TargetHint is null
        ? $"{Path} — {Source}; target must be tested"
        : $"{Path} — {Source}; {TargetHint}";
}

public sealed class SimulatorSetupViewModel : Model, IDisposable
{
    private const string NotConfiguredMessage = "Not configured. Diagnostics and replay remain available.";

    private readonly DesktopSettingsCoordinator settings;
    private readonly IDesktopFileDialogService fileDialog;
    private readonly SimConnectLibraryValidator validator;
    private readonly SimConnectLibraryDiscovery discovery;
    private readonly SimConnectConnectionTester connectionTester;
    private readonly LiveSimulatorService liveSimulator;
    private readonly TelemetrySourceSwitcher sourceSwitcher;
    private readonly AsyncDelegateCommand browse2020Command;
    private readonly AsyncDelegateCommand browse2024Command;
    private readonly AsyncDelegateCommand validate2020Command;
    private readonly AsyncDelegateCommand validate2024Command;
    private readonly AsyncDelegateCommand discoverCommand;
    private readonly AsyncDelegateCommand test2020Command;
    private readonly AsyncDelegateCommand test2024Command;
    private readonly AsyncDelegateCommand saveSelectionsCommand;
    private readonly AsyncDelegateCommand useCandidateFor2020Command;
    private readonly AsyncDelegateCommand useCandidateFor2024Command;
    private readonly AsyncDelegateCommand connect2020Command;
    private readonly AsyncDelegateCommand connect2024Command;
    private readonly AsyncDelegateCommand disconnectCommand;
    private readonly AsyncDelegateCommand reloadProfilesCommand;
    private string? msfs2020LibraryPath;
    private string? msfs2024LibraryPath;
    private string msfs2020Status = NotConfiguredMessage;
    private string msfs2024Status = NotConfiguredMessage;
    private SimulatorLibraryCandidateItem? selectedCandidate;
    private bool isBusy;
    private bool disposed;
    private string liveConnectionStatus = "Live simulator is disconnected. Replay and diagnostics remain available.";
    private string liveProfileStatus = "Aircraft profiles will load when a live connection is started.";
    private string liveTelemetryStatus = "No live telemetry received.";

    public SimulatorSetupViewModel(
        DesktopSettingsCoordinator settings,
        IDesktopFileDialogService fileDialog,
        SimConnectLibraryValidator validator,
        SimConnectLibraryDiscovery discovery,
        SimConnectConnectionTester connectionTester,
        LiveSimulatorService liveSimulator,
        TelemetrySourceSwitcher sourceSwitcher)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        this.connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
        this.liveSimulator = liveSimulator ?? throw new ArgumentNullException(nameof(liveSimulator));
        this.sourceSwitcher = sourceSwitcher ?? throw new ArgumentNullException(nameof(sourceSwitcher));
        liveSimulator.StatusChanged += OnLiveStatusChanged;
        liveSimulator.TelemetryReceived += OnLiveTelemetryReceived;

        msfs2020LibraryPath = settings.Current.SimConnect.Msfs2020.Path;
        msfs2024LibraryPath = settings.Current.SimConnect.Msfs2024.Path;
        RefreshStatusFromAttestations();

        browse2020Command = new AsyncDelegateCommand(
            () => BrowseAsync(SimConnectTarget.Msfs2020),
            () => !IsBusy);
        browse2024Command = new AsyncDelegateCommand(
            () => BrowseAsync(SimConnectTarget.Msfs2024),
            () => !IsBusy);
        validate2020Command = new AsyncDelegateCommand(
            () => ValidateAsync(SimConnectTarget.Msfs2020),
            () => CanTest(Msfs2020LibraryPath));
        validate2024Command = new AsyncDelegateCommand(
            () => ValidateAsync(SimConnectTarget.Msfs2024),
            () => CanTest(Msfs2024LibraryPath));
        discoverCommand = new AsyncDelegateCommand(DiscoverAsync, () => !IsBusy);
        test2020Command = new AsyncDelegateCommand(
            () => TestConnectionAsync(SimConnectTarget.Msfs2020),
            () => CanTest(Msfs2020LibraryPath));
        test2024Command = new AsyncDelegateCommand(
            () => TestConnectionAsync(SimConnectTarget.Msfs2024),
            () => CanTest(Msfs2024LibraryPath));
        saveSelectionsCommand = new AsyncDelegateCommand(SaveSelectionsAsync, () => !IsBusy);
        useCandidateFor2020Command = new AsyncDelegateCommand(
            () => UseSelectedCandidateAsync(SimConnectTarget.Msfs2020),
            () => SelectedCandidate is not null && !IsBusy);
        useCandidateFor2024Command = new AsyncDelegateCommand(
            () => UseSelectedCandidateAsync(SimConnectTarget.Msfs2024),
            () => SelectedCandidate is not null && !IsBusy);
        connect2020Command = new AsyncDelegateCommand(
            () => ConnectLiveAsync(SimConnectTarget.Msfs2020),
            () => CanTest(Msfs2020LibraryPath));
        connect2024Command = new AsyncDelegateCommand(
            () => ConnectLiveAsync(SimConnectTarget.Msfs2024),
            () => CanTest(Msfs2024LibraryPath));
        disconnectCommand = new AsyncDelegateCommand(
            DisconnectLiveAsync,
            () => !IsBusy && liveSimulator.ConnectionState is not SimulatorConnectionState.Disconnected
                and not SimulatorConnectionState.Unavailable);
        reloadProfilesCommand = new AsyncDelegateCommand(ReloadProfilesAsync, () => !IsBusy);
    }

    public string? Msfs2020LibraryPath
    {
        get => msfs2020LibraryPath;
        set
        {
            if (SetProperty(ref msfs2020LibraryPath, value))
            {
                Msfs2020Status = NotConfiguredMessage;
                RaiseCommandStates();
            }
        }
    }

    public string? Msfs2024LibraryPath
    {
        get => msfs2024LibraryPath;
        set
        {
            if (SetProperty(ref msfs2024LibraryPath, value))
            {
                Msfs2024Status = NotConfiguredMessage;
                RaiseCommandStates();
            }
        }
    }

    public string Msfs2020Status
    {
        get => msfs2020Status;
        private set => SetProperty(ref msfs2020Status, value);
    }

    public string Msfs2024Status
    {
        get => msfs2024Status;
        private set => SetProperty(ref msfs2024Status, value);
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

    public ObservableCollection<SimulatorLibraryCandidateItem> Candidates { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    public ObservableCollection<string> ProfileDiagnostics { get; } = [];

    public string LiveConnectionStatus
    {
        get => liveConnectionStatus;
        private set => SetProperty(ref liveConnectionStatus, value);
    }

    public string LiveProfileStatus
    {
        get => liveProfileStatus;
        private set => SetProperty(ref liveProfileStatus, value);
    }

    public string LiveTelemetryStatus
    {
        get => liveTelemetryStatus;
        private set => SetProperty(ref liveTelemetryStatus, value);
    }

    public SimulatorLibraryCandidateItem? SelectedCandidate
    {
        get => selectedCandidate;
        set
        {
            if (SetProperty(ref selectedCandidate, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public ICommand Browse2020Command => browse2020Command;

    public ICommand Browse2024Command => browse2024Command;

    public ICommand Validate2020Command => validate2020Command;

    public ICommand Validate2024Command => validate2024Command;

    public ICommand DiscoverCommand => discoverCommand;

    public ICommand Test2020Command => test2020Command;

    public ICommand Test2024Command => test2024Command;

    public ICommand SaveSelectionsCommand => saveSelectionsCommand;

    public ICommand UseCandidateFor2020Command => useCandidateFor2020Command;

    public ICommand UseCandidateFor2024Command => useCandidateFor2024Command;

    public ICommand Connect2020Command => connect2020Command;

    public ICommand Connect2024Command => connect2024Command;

    public ICommand DisconnectCommand => disconnectCommand;

    public ICommand ReloadProfilesCommand => reloadProfilesCommand;

    private bool CanTest(string? path) => !IsBusy && !string.IsNullOrWhiteSpace(path);

    private async Task BrowseAsync(SimConnectTarget target)
    {
        string? selected = fileDialog.SelectNativeLibrary(GetPath(target));
        if (selected is null)
        {
            return;
        }

        SetPath(target, selected);
        await ValidateAsync(target);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Discovery failures are isolated and shown without closing the application.")]
    private async Task DiscoverAsync()
    {
        IsBusy = true;
        try
        {
            SimConnectLibrarySettings selections = CreateSelections();
            string[] roots = GetStandardSearchRoots().ToArray();
            IReadOnlyList<SimConnectLibraryCandidate> found = await discovery
                .DiscoverAsync(selections, roots, CancellationToken.None);
            Candidates.Clear();
            foreach (SimConnectLibraryCandidate candidate in found)
            {
                Candidates.Add(new(
                    candidate.Path,
                    candidate.Source,
                    candidate.TargetHint,
                    candidate.Validation.IsCompatible));
            }

            SelectedCandidate = Candidates.FirstOrDefault(static candidate => candidate.StaticValidationPassed);
            ReplaceDiagnostics(found.SelectMany(static candidate => candidate.Validation.Diagnostics));
            if (found.Count == 0)
            {
                Diagnostics.Add("No candidates were found. Use Browse to select a native library you obtained with your simulator or SDK installation.");
            }
        }
        catch (Exception exception)
        {
            Diagnostics.Clear();
            Diagnostics.Add($"Discovery failed without changing your selections. {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UseSelectedCandidateAsync(SimConnectTarget target)
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        SetPath(target, SelectedCandidate.Path);
        await ValidateAsync(target);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Validation failures are isolated and shown without closing the application.")]
    private async Task ValidateAsync(SimConnectTarget target)
    {
        IsBusy = true;
        try
        {
            SimConnectLibraryValidationResult result = await validator
                .ValidateAsync(GetPath(target), CancellationToken.None);
            ReplaceDiagnostics(result.Diagnostics);
            SimConnectLibrarySettings selections = CreateSelections();
            SimConnectLibrarySelection selection = selections.Get(target);
            bool hasCurrentAttestation = result.IsCompatible
                && result.Metadata is not null
                && selection.HasCurrentAttestation(result.Metadata);
            if (!hasCurrentAttestation)
            {
                await settings.UpdateAsync(current => current with
                {
                    SimConnect = selections.WithSelection(target, GetPath(target)),
                });
            }

            string status = result.IsCompatible
                ? hasCurrentAttestation
                    ? $"Static checks passed and the recorded {target} live test matches this unchanged file."
                    : "Static checks passed. Run the live connection test for this simulator; the file is not yet target-verified."
                : FirstError(result.Diagnostics) ?? "Static validation failed.";
            SetStatus(target, status);
        }
        catch (Exception exception)
        {
            SetStatus(target, $"Static validation failed: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Connection-test failures are isolated and shown without closing the application.")]
    private async Task TestConnectionAsync(SimConnectTarget target)
    {
        IsBusy = true;
        try
        {
            string? path = GetPath(target);
            SimConnectConnectionTestResult result = await connectionTester.TestAsync(
                target,
                path,
                TimeSpan.FromSeconds(15));
            ReplaceDiagnostics(result.Diagnostics);
            SetStatus(target, Describe(result));

            SimConnectLibrarySettings selections = CreateSelections()
                .WithSelection(target, path);
            if (result.Attestation is not null)
            {
                selections = selections.WithAttestation(result.Attestation);
            }

            await settings.UpdateAsync(current => current with { SimConnect = selections });
        }
        catch (Exception exception)
        {
            SetStatus(target, $"Connection test failed: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Settings-write failures are isolated and shown without closing the application.")]
    private async Task SaveSelectionsAsync()
    {
        IsBusy = true;
        try
        {
            await settings.UpdateAsync(current => current with { SimConnect = CreateSelections() });
            RefreshStatusFromAttestations();
        }
        catch (Exception exception)
        {
            Diagnostics.Clear();
            Diagnostics.Add($"Selections were not saved. {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Live connection errors are isolated and shown without closing the application.")]
    private async Task ConnectLiveAsync(SimConnectTarget target)
    {
        IsBusy = true;
        try
        {
            string path = GetPath(target)
                ?? throw new InvalidOperationException($"Select a native library for {target} before connecting.");
            SimConnectLibraryValidationResult validation = await validator
                .ValidateAsync(path, CancellationToken.None);
            ReplaceDiagnostics(validation.Diagnostics);
            if (!validation.IsCompatible || validation.Metadata is null)
            {
                throw new InvalidOperationException(FirstError(validation.Diagnostics)
                    ?? "The selected native library did not pass structural validation.");
            }

            SimConnectLibrarySettings selections = CreateSelections();
            if (!selections.Get(target).HasCurrentAttestation(validation.Metadata))
            {
                selections = selections.WithSelection(target, path);
            }

            await settings.UpdateAsync(current => current with
            {
                SimConnect = selections,
            });
            SimulatorConnectionResult result = await sourceSwitcher.SwitchToLiveAsync(
                target,
                path,
                CancellationToken.None);
            LiveConnectionStatus = result.Succeeded
                ? $"Live connection ready: {result.Simulator!.DisplayName} {result.Simulator.Version ?? string.Empty}."
                : $"Live connection failed: {result.Message}";
        }
        catch (Exception exception)
        {
            LiveConnectionStatus = $"Live connection failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Live disconnect errors are isolated and shown without closing the application.")]
    private async Task DisconnectLiveAsync()
    {
        IsBusy = true;
        try
        {
            await sourceSwitcher.DisconnectLiveAsync(CancellationToken.None);
            LiveConnectionStatus = "Live simulator disconnected. Configuration, diagnostics, and replay remain available.";
        }
        catch (Exception exception)
        {
            LiveConnectionStatus = $"The live simulator could not disconnect cleanly: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Profile reload errors are isolated and shown without closing the application.")]
    private async Task ReloadProfilesAsync()
    {
        IsBusy = true;
        try
        {
            await sourceSwitcher.ReloadLiveProfilesAsync(CancellationToken.None);
            LiveProfileStatus = liveSimulator.ProfileStatus;
        }
        catch (Exception exception)
        {
            LiveProfileStatus = $"Profiles could not be reloaded: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshStatusFromAttestations()
    {
        RefreshStatusFromAttestation(SimConnectTarget.Msfs2020, settings.Current.SimConnect.Msfs2020);
        RefreshStatusFromAttestation(SimConnectTarget.Msfs2024, settings.Current.SimConnect.Msfs2024);
    }

    private void RefreshStatusFromAttestation(SimConnectTarget target, SimConnectLibrarySelection selection)
    {
        if (string.IsNullOrWhiteSpace(selection.Path))
        {
            SetStatus(target, NotConfiguredMessage);
            return;
        }

        string status = selection.Attestation is null
            ? "Library selected; validate it and run a live target-specific connection test."
            : $"A prior {target} test was recorded on {selection.Attestation.TestedAtUtc:yyyy-MM-dd HH:mm} UTC; validate or connect to confirm the file is unchanged.";
        SetStatus(target, status);
    }

    private SimConnectLibrarySettings CreateSelections() => new()
    {
        Msfs2020 = CreateSelection(settings.Current.SimConnect.Msfs2020, Msfs2020LibraryPath),
        Msfs2024 = CreateSelection(settings.Current.SimConnect.Msfs2024, Msfs2024LibraryPath),
    };

    private string? GetPath(SimConnectTarget target) => target switch
    {
        SimConnectTarget.Msfs2020 => Msfs2020LibraryPath,
        SimConnectTarget.Msfs2024 => Msfs2024LibraryPath,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported simulator target."),
    };

    private void SetPath(SimConnectTarget target, string path)
    {
        if (target == SimConnectTarget.Msfs2020)
        {
            Msfs2020LibraryPath = path;
        }
        else if (target == SimConnectTarget.Msfs2024)
        {
            Msfs2024LibraryPath = path;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported simulator target.");
        }
    }

    private void SetStatus(SimConnectTarget target, string status)
    {
        if (target == SimConnectTarget.Msfs2020)
        {
            Msfs2020Status = status;
        }
        else if (target == SimConnectTarget.Msfs2024)
        {
            Msfs2024Status = status;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported simulator target.");
        }
    }

    private void ReplaceDiagnostics(IEnumerable<SimConnectDiagnostic> diagnostics)
    {
        Diagnostics.Clear();
        foreach (SimConnectDiagnostic diagnostic in diagnostics)
        {
            string detail = string.IsNullOrWhiteSpace(diagnostic.Detail) ? string.Empty : $" {diagnostic.Detail}";
            Diagnostics.Add($"{diagnostic.Severity}: {diagnostic.Code} — {diagnostic.Message}{detail}");
        }
    }

    private static string? FirstError(IEnumerable<SimConnectDiagnostic> diagnostics) => diagnostics
        .FirstOrDefault(static diagnostic => diagnostic.Severity == SimConnectDiagnosticSeverity.Error)?.Message;

    private static string Describe(SimConnectConnectionTestResult result) => result.Outcome switch
    {
        SimConnectConnectionTestOutcome.Succeeded =>
            $"Verified with {result.Target}. Simulator {result.Server!.ApplicationVersion}; SimConnect {result.Server.SimConnectVersion}.",
        SimConnectConnectionTestOutcome.WrongSimulator =>
            $"Wrong simulator answered. This selection was not verified for {result.Target}.",
        SimConnectConnectionTestOutcome.PlatformUnsupported =>
            "Static checks passed. Run the live test on Windows with the selected simulator running.",
        _ => FirstError(result.Diagnostics) ?? $"Connection test ended with {result.Outcome}.",
    };

    private static IEnumerable<string> GetStandardSearchRoots()
    {
        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Microsoft Flight Simulator SDK");
            yield return Path.Combine(programFiles, "MSFS 2024 SDK");
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Microsoft Flight Simulator SDK");
            yield return Path.Combine(programFilesX86, "MSFS 2024 SDK");
        }

        string? documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            yield return Path.Combine(documents, "MyFSProjects");
        }
    }

    private void RaiseCommandStates()
    {
        browse2020Command.RaiseCanExecuteChanged();
        browse2024Command.RaiseCanExecuteChanged();
        validate2020Command.RaiseCanExecuteChanged();
        validate2024Command.RaiseCanExecuteChanged();
        discoverCommand.RaiseCanExecuteChanged();
        test2020Command.RaiseCanExecuteChanged();
        test2024Command.RaiseCanExecuteChanged();
        saveSelectionsCommand.RaiseCanExecuteChanged();
        useCandidateFor2020Command.RaiseCanExecuteChanged();
        useCandidateFor2024Command.RaiseCanExecuteChanged();
        connect2020Command.RaiseCanExecuteChanged();
        connect2024Command.RaiseCanExecuteChanged();
        disconnectCommand.RaiseCanExecuteChanged();
        reloadProfilesCommand.RaiseCanExecuteChanged();
    }

    private void OnLiveStatusChanged(object? sender, LiveSimulatorStatusEventArgs eventArgs) => DispatchToUi(() =>
    {
        LiveConnectionStatus = eventArgs.Message;
        LiveProfileStatus = eventArgs.ProfileStatus;
        ReplaceProfileDiagnostics();
        RaiseCommandStates();
    });

    private void OnLiveTelemetryReceived(object? sender, LiveTelemetryEventArgs eventArgs) => DispatchToUi(() =>
    {
        TelemetrySnapshot snapshot = eventArgs.Snapshot;
        LiveTelemetryStatus = string.Format(
            CultureInfo.CurrentCulture,
            "{0} · {1} · {2:N0} ft MSL · {3:N0} kt GS · {4:N0} ft/min VS",
            snapshot.Aircraft.Title,
            snapshot.Aircraft.IcaoType ?? "ICAO unknown",
            snapshot.AltitudeMsl.Feet,
            snapshot.GroundSpeed.Knots,
            snapshot.VerticalSpeed.FeetPerMinute);
        LiveProfileStatus = liveSimulator.ProfileStatus;
        ReplaceProfileDiagnostics();
    });

    private static void DispatchToUi(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(action);
            return;
        }

        action();
    }

    private void ReplaceProfileDiagnostics()
    {
        ProfileDiagnostics.Clear();
        foreach (string item in liveSimulator.ProfileInspection)
        {
            ProfileDiagnostics.Add(item);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        liveSimulator.StatusChanged -= OnLiveStatusChanged;
        liveSimulator.TelemetryReceived -= OnLiveTelemetryReceived;
    }

    private static SimConnectLibrarySelection CreateSelection(
        SimConnectLibrarySelection current,
        string? path)
    {
        string? normalized = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(current.Path, normalized, comparison)
            ? current with { Path = normalized }
            : new SimConnectLibrarySelection { Path = normalized };
    }
}

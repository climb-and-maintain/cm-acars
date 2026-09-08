using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;
using ClimbAndMaintain.Acars.App.Configuration;
using ClimbAndMaintain.Acars.App.Services;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using System.Waf.Applications;
using System.Waf.Foundation;

namespace ClimbAndMaintain.Acars.App.Presentation;

public abstract class PageViewModel(string name, string title, string description) : Model
{
    public string Name { get; } = name;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public string AutomationId => $"Page.{Name.Replace(" ", string.Empty, StringComparison.Ordinal)}";
}

public sealed class DashboardPageViewModel(
    SimulatorSetupViewModel simulator,
    PhpVmsSetupViewModel phpVms,
    ReplayPageViewModel replay,
    FlightOperationsViewModel flightOperations)
    : PageViewModel("Dashboard", "Dashboard", "Simulator, server, flight, and telemetry status at a glance.")
{
    public SimulatorSetupViewModel Simulator { get; } = simulator;

    public PhpVmsSetupViewModel PhpVms { get; } = phpVms;

    public ReplayPageViewModel Replay { get; } = replay;

    public FlightOperationsViewModel FlightOperations { get; } = flightOperations;
}

public sealed class FlightsPageViewModel(ReplayPageViewModel replay, FlightOperationsViewModel flightOperations)
    : PageViewModel("Flights", "Flights and replay", "Select a phpVMS flight or exercise the complete telemetry path with a recording.")
{
    public ReplayPageViewModel Replay { get; } = replay;

    public FlightOperationsViewModel FlightOperations { get; } = flightOperations;
}

public sealed class FlightLogPageViewModel(FlightOperationsViewModel flightOperations)
    : PageViewModel("Flight Log", "Flight log", "Durable ACARS events and messages for the active flight.")
{
    public ObservableCollection<string> Entries => flightOperations.LogEntries;
}

public sealed class SettingsPageViewModel : PageViewModel
{
    private readonly DesktopSettingsCoordinator settings;
    private readonly ThemeService themeService;
    private string appearanceStatus;

    public SettingsPageViewModel(
        SimulatorSetupViewModel simulator,
        PhpVmsSetupViewModel phpVms,
        DesktopSettingsCoordinator settings,
        ThemeService themeService)
        : base("Settings", "Settings", "Configure phpVMS, simulator libraries, appearance, and accessibility defaults.")
    {
        Simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        PhpVms = phpVms ?? throw new ArgumentNullException(nameof(phpVms));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        appearanceStatus = $"{settings.Current.Appearance} appearance selected.";
        UseSystemThemeCommand = new AsyncDelegateCommand(() => ApplyThemeAsync(AppearanceMode.System));
        UseLightThemeCommand = new AsyncDelegateCommand(() => ApplyThemeAsync(AppearanceMode.Light));
        UseDarkThemeCommand = new AsyncDelegateCommand(() => ApplyThemeAsync(AppearanceMode.Dark));
    }

    public SimulatorSetupViewModel Simulator { get; }

    public PhpVmsSetupViewModel PhpVms { get; }

    public string AppearanceStatus
    {
        get => appearanceStatus;
        private set => SetProperty(ref appearanceStatus, value);
    }

    public ICommand UseSystemThemeCommand { get; }

    public ICommand UseLightThemeCommand { get; }

    public ICommand UseDarkThemeCommand { get; }

    private async Task ApplyThemeAsync(AppearanceMode appearance)
    {
        themeService.Apply(appearance);
        await settings.UpdateAsync(current => current with { Appearance = appearance });
        AppearanceStatus = $"{appearance} appearance applied without restarting.";
    }
}

public sealed class AboutPageViewModel(BrandingOptions branding)
    : PageViewModel("About", $"About {branding.ProductName}", "Project identity, licensing, authorship, and version information.")
{
    public BrandingOptions Branding { get; } = branding;

    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "development";

    public string License { get; } = "Apache License 2.0";

    public string Attribution { get; } = $"Original project by {branding.OriginalAuthor}. Third-party notices are included with every release.";
}

public sealed class DiagnosticsPageViewModel : PageViewModel
{
    private const int MaximumQueueItemsInspected = 500;

    private static readonly Action<ILogger, string, string, Exception?> LogDiagnosticsActionFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(5001, "DiagnosticsActionFailed"),
            "Diagnostics action {Action} failed with {FailureType}.");

    private readonly SimulatorSetupViewModel simulator;
    private readonly PhpVmsSetupViewModel phpVms;
    private readonly IOutboxStore outbox;
    private readonly SqliteAcarsStore database;
    private readonly PhpVmsOperationalStatus phpVmsOperationalStatus;
    private readonly SupportBundleService supportBundle;
    private readonly ILogger<DiagnosticsPageViewModel> logger;
    private readonly AsyncDelegateCommand refreshCommand;
    private readonly string runtime = RuntimeInformation.FrameworkDescription;
    private readonly string operatingSystem = RuntimeInformation.OSDescription;
    private readonly string applicationVersion = ApplicationBuildInfo.Version;
    private readonly string applicationBuild = ApplicationBuildInfo.Build;
    private string databaseStatus = "Database schema not loaded.";
    private string queueStatus = "Queue counts not loaded.";
    private string queueRetryStatus = "Queue retry state not loaded.";
    private string phpVmsRequestStatus = "No phpVMS request observed this run.";
    private string phpVmsLastSuccessfulRequest = "No successful phpVMS request observed this run.";
    private string phpVmsRateLimitStatus = "phpVMS rate-limit state is unknown.";
    private string actionStatus = "Diagnostics contain no credentials.";

    public DiagnosticsPageViewModel(
        SimulatorSetupViewModel simulator,
        PhpVmsSetupViewModel phpVms,
        IOutboxStore outbox,
        SqliteAcarsStore database,
        PhpVmsOperationalStatus phpVmsOperationalStatus,
        SupportBundleService supportBundle,
        ILogger<DiagnosticsPageViewModel> logger)
        : base("Diagnostics", "Diagnostics", "Connection, compatibility, queue, runtime, and support information.")
    {
        this.simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        this.phpVms = phpVms ?? throw new ArgumentNullException(nameof(phpVms));
        this.outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.phpVmsOperationalStatus = phpVmsOperationalStatus
            ?? throw new ArgumentNullException(nameof(phpVmsOperationalStatus));
        this.supportBundle = supportBundle ?? throw new ArgumentNullException(nameof(supportBundle));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        refreshCommand = new AsyncDelegateCommand(RefreshAsync);
        CopyCommand = new DelegateCommand(Copy);
        ExportCommand = new AsyncDelegateCommand(ExportAsync);
        OpenLogFolderCommand = new DelegateCommand(OpenLogFolder);
    }

    public SimulatorSetupViewModel Simulator => simulator;

    public PhpVmsSetupViewModel PhpVms => phpVms;

    public string Runtime => runtime;

    public string OperatingSystem => operatingSystem;

    public string ApplicationVersion => applicationVersion;

    public string ApplicationBuild => applicationBuild;

    public string DatabaseStatus
    {
        get => databaseStatus;
        private set => SetProperty(ref databaseStatus, value);
    }

    public string QueueStatus
    {
        get => queueStatus;
        private set => SetProperty(ref queueStatus, value);
    }

    public string QueueRetryStatus
    {
        get => queueRetryStatus;
        private set => SetProperty(ref queueRetryStatus, value);
    }

    public string PhpVmsRequestStatus
    {
        get => phpVmsRequestStatus;
        private set => SetProperty(ref phpVmsRequestStatus, value);
    }

    public string PhpVmsLastSuccessfulRequest
    {
        get => phpVmsLastSuccessfulRequest;
        private set => SetProperty(ref phpVmsLastSuccessfulRequest, value);
    }

    public string PhpVmsRateLimitStatus
    {
        get => phpVmsRateLimitStatus;
        private set => SetProperty(ref phpVmsRateLimitStatus, value);
    }

    public string ActionStatus
    {
        get => actionStatus;
        private set => SetProperty(ref actionStatus, value);
    }

    public ICommand RefreshCommand => refreshCommand;

    public ICommand CopyCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    private async Task RefreshAsync()
    {
        try
        {
            OutboxCounts counts = await outbox.GetCountsAsync(CancellationToken.None);
            QueueStatus = string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} positions, {1:N0} events, {2:N0} log messages, and {3:N0} completion/cancellation operations waiting.",
                counts.Positions,
                counts.Events,
                counts.Logs,
                counts.Operations);
            IReadOnlyList<PendingOutboxItem> pending = counts.Total == 0
                ? []
                : await outbox.GetPendingAsync(
                    Math.Min(counts.Total, MaximumQueueItemsInspected),
                    CancellationToken.None);
            UpdateQueueRetryStatus(pending, counts.Total);

            int schemaVersion = await database.ReadSchemaVersionAsync(CancellationToken.None);
            DatabaseStatus = string.Format(
                CultureInfo.CurrentCulture,
                "Database schema: {0:N0}; supported schema: {1:N0}.",
                schemaVersion,
                SqliteAcarsStore.SupportedSchemaVersion);
            UpdatePhpVmsStatus();
            ActionStatus = "Diagnostics refreshed.";
        }
        catch (Exception exception)
        {
            LogDiagnosticsActionFailed(logger, "Refresh", exception.GetType().Name, null);
            ActionStatus = $"Diagnostics could not be refreshed. {exception.Message}";
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Clipboard errors are reported as non-fatal diagnostics.")]
    private void Copy()
    {
        try
        {
            SupportBundleService.CopyDiagnostics(CreateSanitizedDiagnostics());
            ActionStatus = "Sanitized diagnostics copied to the clipboard.";
        }
        catch (Exception exception)
        {
            LogDiagnosticsActionFailed(logger, "Copy", exception.GetType().Name, null);
            ActionStatus = $"Diagnostics could not be copied. {exception.Message}";
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Support-export errors are reported without closing the application.")]
    private async Task ExportAsync()
    {
        try
        {
            string? destination = await supportBundle.ExportAsync(CreateSanitizedDiagnostics());
            ActionStatus = destination is null
                ? "Support bundle export cancelled."
                : $"Sanitized support bundle exported to {destination}.";
        }
        catch (Exception exception)
        {
            LogDiagnosticsActionFailed(logger, "Export", exception.GetType().Name, null);
            ActionStatus = $"Support bundle could not be exported. {exception.Message}";
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Shell-launch failures are reported without closing the application.")]
    private void OpenLogFolder()
    {
        try
        {
            supportBundle.OpenLogFolder();
            ActionStatus = "Log folder opened.";
        }
        catch (Exception exception)
        {
            LogDiagnosticsActionFailed(logger, "OpenLogFolder", exception.GetType().Name, null);
            ActionStatus = $"Log folder could not be opened. {exception.Message}";
        }
    }

    private string CreateSanitizedDiagnostics()
    {
        UpdatePhpVmsStatus();
        string path2020 = FileNameOnly(simulator.Msfs2020LibraryPath);
        string path2024 = FileNameOnly(simulator.Msfs2024LibraryPath);
        Uri.TryCreate(phpVms.BaseUrl, UriKind.Absolute, out Uri? baseUri);
        string server = baseUri?.GetLeftPart(UriPartial.Authority) ?? "not configured";
        List<string> lines =
        [
            "Climb and Maintain ACARS diagnostics (sanitized)",
            $"Version: {ApplicationVersion}",
            $"Build: {ApplicationBuild}",
            $"Runtime: {Runtime}",
            $"OS: {OperatingSystem}",
            DatabaseStatus,
            $"MSFS 2020 library file: {path2020}",
            $"MSFS 2020 status: {simulator.Msfs2020Status}",
            $"MSFS 2024 library file: {path2024}",
            $"MSFS 2024 status: {simulator.Msfs2024Status}",
            $"Live simulator: {simulator.LiveConnectionStatus}",
            $"Live telemetry: {simulator.LiveTelemetryStatus}",
            $"Profile: {simulator.LiveProfileStatus}",
            $"phpVMS server: {server}",
            $"phpVMS status: {phpVms.Status}",
            PhpVmsRequestStatus,
            PhpVmsLastSuccessfulRequest,
            PhpVmsRateLimitStatus,
            $"Queue: {QueueStatus}",
            $"Queue retry: {QueueRetryStatus}",
        ];
        lines.AddRange(simulator.ProfileDiagnostics.Select(SanitizeProfileDiagnostic));
        return string.Join(Environment.NewLine, lines);
    }

    private void UpdateQueueRetryStatus(IReadOnlyList<PendingOutboxItem> pending, int totalCount)
    {
        PendingOutboxItem[] attempted = pending.Where(static item => item.AttemptCount > 0).ToArray();
        if (attempted.Length == 0)
        {
            QueueRetryStatus = totalCount == 0
                ? "No queued items are waiting for retry."
                : "No failed delivery attempt is recorded for the inspected queued items.";
            return;
        }

        int maximumAttemptCount = attempted.Max(static item => item.AttemptCount);
        DateTimeOffset? latestAttempt = attempted.Max(static item => item.LastAttemptAtUtc);
        string inspection = pending.Count < totalCount
            ? $" First {pending.Count:N0} of {totalCount:N0} queued items inspected."
            : string.Empty;
        QueueRetryStatus = string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} queued item(s) have failed attempts; highest attempt count {1:N0}; latest attempt {2}.{3}",
            attempted.Length,
            maximumAttemptCount,
            latestAttempt?.ToString("u", CultureInfo.CurrentCulture) ?? "not recorded",
            inspection);
    }

    private void UpdatePhpVmsStatus()
    {
        PhpVmsOperationalSnapshot snapshot = phpVmsOperationalStatus.GetSnapshot();
        PhpVmsRequestStatus = snapshot.LastRequestAtUtc is not { } lastRequest
            ? "No phpVMS request observed this run."
            : snapshot.LastResponseStatusCode is { } statusCode
                ? $"Last phpVMS request: {lastRequest:u}; {snapshot.LastRequestMethod} {snapshot.LastRequestEndpoint}; HTTP {statusCode}."
                : $"Last phpVMS request: {lastRequest:u}; {snapshot.LastRequestMethod} {snapshot.LastRequestEndpoint}; {snapshot.LastFailureType ?? "request failure"}.";
        PhpVmsLastSuccessfulRequest = snapshot.LastSuccessfulRequestAtUtc is not { } lastSuccess
            ? "No successful phpVMS request observed this run."
            : $"Last successful phpVMS request: {lastSuccess:u}; {snapshot.LastSuccessfulRequestEndpoint}.";
        PhpVmsRateLimitStatus = snapshot.RateLimitState switch
        {
            PhpVmsRateLimitState.Unknown => "phpVMS rate-limit state is unknown; no response observed this run.",
            PhpVmsRateLimitState.NotRateLimited => "The latest phpVMS response was not rate-limited.",
            PhpVmsRateLimitState.RateLimited => FormatRateLimitStatus(snapshot),
            _ => "phpVMS rate-limit state is unavailable.",
        };
    }

    private static string FormatRateLimitStatus(PhpVmsOperationalSnapshot snapshot)
    {
        string observed = snapshot.LastRateLimitAtUtc is { } throttledAt
            ? throttledAt.ToString("u", CultureInfo.CurrentCulture)
            : "time not recorded";
        string retry = snapshot.LastRetryDelay is { } delay
            ? string.Format(
                CultureInfo.CurrentCulture,
                " Retry {0:N0} of {1:N0} was scheduled after {2:N0} ms.",
                snapshot.LastRetryAttempt,
                snapshot.MaximumAttempts,
                delay.TotalMilliseconds)
            : " The server did not schedule a client retry.";
        return $"phpVMS rate limit observed at {observed}.{retry}";
    }

    private static string FileNameOnly(string? path) => string.IsNullOrWhiteSpace(path)
        ? "not configured"
        : Path.GetFileName(path);

    private static string SanitizeProfileDiagnostic(string line)
    {
        const string configurationPrefix = "Configuration: ";
        return line.StartsWith(configurationPrefix, StringComparison.Ordinal)
            ? configurationPrefix + FileNameOnly(line[configurationPrefix.Length..])
            : line;
    }
}

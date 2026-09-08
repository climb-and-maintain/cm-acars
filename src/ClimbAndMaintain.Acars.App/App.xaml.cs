using System.Windows;
using System.IO;
using ClimbAndMaintain.Acars.App.Configuration;
using ClimbAndMaintain.Acars.App.Presentation;
using ClimbAndMaintain.Acars.App.Services;
using ClimbAndMaintain.Acars.App.Views;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Application.Tracking;
using ClimbAndMaintain.Acars.Infrastructure.Persistence;
using ClimbAndMaintain.Acars.Infrastructure.Replay;
using ClimbAndMaintain.Acars.Infrastructure.Security;
using ClimbAndMaintain.Acars.SimConnect.Connection;
using ClimbAndMaintain.Acars.SimConnect.Discovery;
using ClimbAndMaintain.Acars.SimConnect.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Targets;

namespace ClimbAndMaintain.Acars.App;

public partial class App : System.Windows.Application
{
    private static readonly Action<Microsoft.Extensions.Logging.ILogger, string, string, string, string, Exception?> LogApplicationStarted =
        LoggerMessage.Define<string, string, string, string>(
            Microsoft.Extensions.Logging.LogLevel.Information,
            new EventId(1000, "ApplicationStarted"),
            "Climb and Maintain ACARS started. Version {Version}; build {Build}; runtime {Runtime}; OS {OperatingSystem}.");

    private static readonly Action<Microsoft.Extensions.Logging.ILogger, string, Exception?> LogSettingsWarning =
        LoggerMessage.Define<string>(
            Microsoft.Extensions.Logging.LogLevel.Warning,
            new EventId(1001, "SettingsLoadFailed"),
            "Settings could not be read; safe defaults are active ({FailureType}).");

    private static readonly Action<Microsoft.Extensions.Logging.ILogger, string, Exception?> LogDatabaseFailure =
        LoggerMessage.Define<string>(
            Microsoft.Extensions.Logging.LogLevel.Error,
            new EventId(1002, "DatabaseInitializationFailed"),
            "The local ACARS database could not be initialized ({FailureType}). The shell will remain available for repair and diagnostics.");

    private static readonly Action<Microsoft.Extensions.Logging.ILogger, int, Exception?> LogDatabaseReady =
        LoggerMessage.Define<int>(
            Microsoft.Extensions.Logging.LogLevel.Information,
            new EventId(1003, "DatabaseInitialized"),
            "The local ACARS database is ready at schema version {SchemaVersion}.");

    private IHost? host;
    private Microsoft.Extensions.Logging.ILogger<App>? logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string applicationData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClimbAndMaintain",
            "ACARS");
        string logDirectory = Path.Combine(applicationData, "logs");
        Directory.CreateDirectory(logDirectory);
        ConfigureLogging(Path.Combine(logDirectory, "acars.log"));

        BrandingOptions branding = BrandingOptions.Load(Path.Combine(AppContext.BaseDirectory, "branding.json"));
        DesktopSettingsStore settingsStore = new(Path.Combine(applicationData, "settings.json"));
        DesktopSettings initialSettings = await settingsStore.LoadAsync();
        if (initialSettings.PhpVmsBaseUrl == "https://" && branding.DefaultBackendUrl != "https://")
        {
            initialSettings = initialSettings with { PhpVmsBaseUrl = branding.DefaultBackendUrl };
        }
        DesktopSettingsCoordinator settings = new(settingsStore, initialSettings);
        ThemeService themeService = new(this);
        themeService.Apply(initialSettings.Appearance);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddNLog();
        builder.Logging.AddFilter("System.Net.Http.HttpClient.phpvms", Microsoft.Extensions.Logging.LogLevel.None);

        builder.Services.AddSingleton(settingsStore);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(branding);
        builder.Services.AddSingleton(themeService);
        builder.Services.AddSingleton<IDesktopFileDialogService, DesktopFileDialogService>();
        builder.Services.AddSingleton<SimConnectLibraryValidator>();
        builder.Services.AddSingleton<SimConnectLibraryDiscovery>();
        builder.Services.AddSingleton<SimConnectConnectionTester>();
        builder.Services.AddSingleton<ISecretStore>(_ => new DpapiSecretStore(Path.Combine(applicationData, "secrets")));
        builder.Services.AddSingleton(_ => new SqliteAcarsStore(Path.Combine(applicationData, "acars.db")));
        builder.Services.AddSingleton<IFlightStateStore>(services => services.GetRequiredService<SqliteAcarsStore>());
        builder.Services.AddSingleton<IOutboxStore>(services => services.GetRequiredService<SqliteAcarsStore>());
        builder.Services.AddSingleton<IFlightSessionStore>(services => services.GetRequiredService<SqliteAcarsStore>());
        builder.Services.AddSingleton<RecordedTelemetryProvider>();
        builder.Services.AddHttpClient("phpvms");
        builder.Services.AddSingleton<PhpVmsOperationalStatus>();
        builder.Services.AddSingleton<IFlightTrackingCoordinator, FlightTrackingCoordinator>();
        builder.Services.AddSingleton(services => new FlightStartCoordinator(
            services.GetRequiredService<IFlightTrackingCoordinator>()));
        builder.Services.AddSingleton<PhpVmsBackendLeaseFactory>();
        builder.Services.AddSingleton<PhpVmsConnectionTestService>();
        builder.Services.AddSingleton<FlightOperationsService>();
        builder.Services.AddSingleton(services => new LiveSimulatorService(
            services.GetRequiredService<FlightOperationsService>(),
            Path.Combine(applicationData, "profiles"),
            services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiveSimulatorService>>()));
        builder.Services.AddSingleton<TelemetrySourceSwitcher>();
        builder.Services.AddHostedService<OutboxDeliveryWorker>();
        builder.Services.AddSingleton(_ => new SupportBundleService(
            _.GetRequiredService<IDesktopFileDialogService>(),
            logDirectory));

        builder.Services.AddSingleton<SimulatorSetupViewModel>();
        builder.Services.AddSingleton<PhpVmsSetupViewModel>();
        builder.Services.AddSingleton<ReplayPageViewModel>();
        builder.Services.AddSingleton<FlightOperationsViewModel>();
        builder.Services.AddSingleton<DashboardPageViewModel>();
        builder.Services.AddSingleton<FlightsPageViewModel>();
        builder.Services.AddSingleton<FlightLogPageViewModel>();
        builder.Services.AddSingleton<DiagnosticsPageViewModel>();
        builder.Services.AddSingleton<SettingsPageViewModel>();
        builder.Services.AddSingleton<AboutPageViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<FirstRunWizardViewModel>();

        host = builder.Build();
        await host.StartAsync();
        logger = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<App>>();
        LogApplicationStarted(
            logger,
            ApplicationBuildInfo.Version,
            ApplicationBuildInfo.Build,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            null);
        if (settingsStore.LastLoadWarning is not null)
        {
            LogSettingsWarning(logger, settingsStore.LastLoadFailureType ?? "SettingsReadFailure", null);
        }

        await InitializeDatabaseAsync(host.Services.GetRequiredService<SqliteAcarsStore>());
        await host.Services.GetRequiredService<FlightOperationsViewModel>().InitializeAsync();

        ShellWindow shell = new()
        {
            DataContext = host.Services.GetRequiredService<ShellViewModel>(),
        };
        MainWindow = shell;
        shell.Show();

        if (!initialSettings.SetupCompleted)
        {
            ShowFirstRun(shell);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (host is not null)
            {
                host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                if (host is IAsyncDisposable asyncDisposable)
                {
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                else
                {
                    host.Dispose();
                }
            }
        }
        finally
        {
            LogManager.Shutdown();
            base.OnExit(e);
        }
    }

    private static void ConfigureLogging(string logPath)
    {
        NLog.Config.LoggingConfiguration configuration = new();
        FileTarget fileTarget = new("application-log")
        {
            FileName = logPath,
            ArchiveEvery = FileArchivePeriod.Day,
            ArchiveAboveSize = 10 * 1024 * 1024,
            MaxArchiveFiles = 14,
            Layout = "${longdate}|${level:uppercase=true}|${logger}|${message:withexception=true}",
        };
        configuration.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, fileTarget);
        LogManager.Configuration = configuration;
    }

    private async Task InitializeDatabaseAsync(SqliteAcarsStore database)
    {
        try
        {
            await database.InitializeAsync();
            if (logger is not null)
            {
                LogDatabaseReady(logger, SqliteAcarsStore.SupportedSchemaVersion, null);
            }
        }
        catch (Exception exception)
        {
            if (logger is not null)
            {
                LogDatabaseFailure(logger, exception.GetType().Name, null);
            }
        }
    }

    private void ShowFirstRun(Window owner)
    {
        FirstRunWizardViewModel viewModel = host!.Services.GetRequiredService<FirstRunWizardViewModel>();
        FirstRunWindow wizard = new()
        {
            Owner = owner,
            DataContext = viewModel,
        };
        viewModel.RequestClose += (_, _) => wizard.Close();
        _ = wizard.ShowDialog();
    }
}

using System.Diagnostics.CodeAnalysis;
using System.IO;
using ClimbAndMaintain.Acars.AircraftProfiles;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.SimConnect;
using ClimbAndMaintain.Acars.SimConnect.Provider;
using ClimbAndMaintain.Acars.SimConnect.Variables;
using Microsoft.Extensions.Logging;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class LiveSimulatorService : IAsyncDisposable
{
    private static readonly Action<ILogger, string, Exception?> LogConnectRequested =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3001, "SimulatorConnectRequested"),
            "Simulator connection requested for {Target}.");

    private static readonly Action<ILogger, string, string, string, Exception?> LogConnected =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(3002, "SimulatorConnected"),
            "Simulator connection for {Target} succeeded: {Simulator}; version {SimulatorVersion}.");

    private static readonly Action<ILogger, string, string, Exception?> LogConnectFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3003, "SimulatorConnectFailed"),
            "Simulator connection for {Target} failed with {FailureKind}.");

    private static readonly Action<ILogger, string, string, Exception?> LogConnectionStateChanged =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(3004, "SimulatorStateChanged"),
            "Simulator state changed from {PreviousState} to {CurrentState}.");

    private static readonly Action<ILogger, Exception?> LogDisconnected =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(3005, "SimulatorDisconnected"),
            "Simulator disconnected; configuration, diagnostics, and replay remain available.");

    private static readonly Action<ILogger, int, int, Exception?> LogProfilesLoaded =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(3006, "AircraftProfilesLoaded"),
            "Loaded {ProfileCount} aircraft profiles; {InvalidProfileCount} invalid user profiles were ignored.");

    private static readonly Action<ILogger, string, string, string, Exception?> LogAircraftDetected =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(3007, "AircraftDetected"),
            "Aircraft detected in {Simulator}: title {AircraftTitle}; ICAO {IcaoType}.");

    private static readonly Action<ILogger, string, string, string, Exception?> LogProfileSelected =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(3008, "AircraftProfileSelected"),
            "Aircraft profile selected: {ProfileId}; name {ProfileName}; source {ProfileSource}.");

    private static readonly Action<ILogger, string, string, Exception?> LogConsumerFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3009, "SimulatorConsumerFailed"),
            "The {Consumer} simulator consumer stopped with {FailureType}.");

    private readonly FlightOperationsService flightOperations;
    private readonly string userProfilesDirectory;
    private readonly ILogger<LiveSimulatorService> logger;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Lock profileStateLock = new();
    private readonly Dictionary<string, RawVariableValue> rawVariables = new(StringComparer.OrdinalIgnoreCase);
    private ProfileCatalog profiles = new();
    private MsfsSimConnectProvider? provider;
    private CancellationTokenSource? consumerCancellation;
    private Task? telemetryConsumer;
    private Task? variableConsumer;
    private SimConnectTarget? target;
    private string? libraryPath;
    private string profileStatus = "Profiles have not been loaded.";
    private IReadOnlyList<string> profileInspection = ["No aircraft profile has been evaluated."];
    private TelemetrySnapshot? lastSnapshot;
    private string? lastLoggedAircraftIdentity;
    private string? lastLoggedProfileIdentity;
    private bool disposed;

    public LiveSimulatorService(
        FlightOperationsService flightOperations,
        string userProfilesDirectory,
        ILogger<LiveSimulatorService> logger)
    {
        this.flightOperations = flightOperations ?? throw new ArgumentNullException(nameof(flightOperations));
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfilesDirectory);
        this.userProfilesDirectory = Path.GetFullPath(userProfilesDirectory);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SimulatorConnectionState ConnectionState => provider?.ConnectionState
        ?? (string.IsNullOrWhiteSpace(libraryPath)
            ? SimulatorConnectionState.Unavailable
            : SimulatorConnectionState.Disconnected);

    public SimulatorIdentity? ConnectedSimulator => provider?.ConnectedSimulator;

    public TelemetrySnapshot? LastSnapshot
    {
        get
        {
            lock (profileStateLock)
            {
                return lastSnapshot;
            }
        }
    }

    public string ProfileStatus
    {
        get
        {
            lock (profileStateLock)
            {
                return profileStatus;
            }
        }
    }

    public IReadOnlyList<string> ProfileInspection
    {
        get
        {
            lock (profileStateLock)
            {
                return profileInspection;
            }
        }
    }

    public event EventHandler<LiveSimulatorStatusEventArgs>? StatusChanged;

    public event EventHandler<LiveTelemetryEventArgs>? TelemetryReceived;

    public async ValueTask<SimulatorConnectionResult> ConnectAsync(
        SimConnectTarget requestedTarget,
        string library,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(library);
        LogConnectRequested(logger, requestedTarget.ToString(), null);
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await DisconnectCoreAsync(CancellationToken.None);
            await ReloadProfilesCoreAsync(cancellationToken);

            target = requestedTarget;
            libraryPath = Path.GetFullPath(library);
            IReadOnlyList<SimConnectReadOnlyVariableDefinition> definitions = CreateReadOnlyDefinitions(profiles.Profiles);
            MsfsSimConnectProvider newProvider = new(new SimConnectProviderOptions
            {
                Target = requestedTarget,
                LibraryPath = libraryPath,
                ReadOnlyVariables = definitions,
            });
            provider = newProvider;
            newProvider.ConnectionStateChanged += OnConnectionStateChanged;
            consumerCancellation = new CancellationTokenSource();
            telemetryConsumer = ConsumeTelemetryAsync(newProvider, consumerCancellation.Token);
            variableConsumer = ConsumeVariablesAsync(newProvider, consumerCancellation.Token);

            SimulatorConnectionResult result = await newProvider.ConnectAsync(cancellationToken);
            if (result.Succeeded)
            {
                LogConnected(
                    logger,
                    requestedTarget.ToString(),
                    SanitizeLogValue(result.Simulator!.DisplayName),
                    SanitizeLogValue(result.Simulator.Version),
                    null);
            }
            else
            {
                LogConnectFailed(logger, requestedTarget.ToString(), result.FailureKind.ToString(), null);
            }

            RaiseStatus(
                result.Succeeded
                    ? $"Connected to {result.Simulator!.DisplayName} {result.Simulator.Version ?? string.Empty}."
                    : result.Message ?? $"Simulator connection failed: {result.FailureKind}.");
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogConnectFailed(logger, requestedTarget.ToString(), exception.GetType().Name, null);
            throw;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await DisconnectCoreAsync(cancellationToken);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask ReloadProfilesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            SimConnectTarget? reconnectTarget = target;
            string? reconnectPath = libraryPath;
            bool reconnect = provider is not null;
            await DisconnectCoreAsync(CancellationToken.None);
            await ReloadProfilesCoreAsync(cancellationToken);
            if (reconnect && reconnectTarget is not null && reconnectPath is not null)
            {
                target = reconnectTarget;
                libraryPath = reconnectPath;
                IReadOnlyList<SimConnectReadOnlyVariableDefinition> definitions = CreateReadOnlyDefinitions(profiles.Profiles);
                MsfsSimConnectProvider newProvider = new(new SimConnectProviderOptions
                {
                    Target = reconnectTarget.Value,
                    LibraryPath = reconnectPath,
                    ReadOnlyVariables = definitions,
                });
                provider = newProvider;
                newProvider.ConnectionStateChanged += OnConnectionStateChanged;
                consumerCancellation = new CancellationTokenSource();
                telemetryConsumer = ConsumeTelemetryAsync(newProvider, consumerCancellation.Token);
                variableConsumer = ConsumeVariablesAsync(newProvider, consumerCancellation.Token);
                SimulatorConnectionResult result = await newProvider.ConnectAsync(cancellationToken);
                if (result.Succeeded)
                {
                    LogConnected(
                        logger,
                        reconnectTarget.Value.ToString(),
                        SanitizeLogValue(result.Simulator!.DisplayName),
                        SanitizeLogValue(result.Simulator.Version),
                        null);
                }
                else
                {
                    LogConnectFailed(
                        logger,
                        reconnectTarget.Value.ToString(),
                        result.FailureKind.ToString(),
                        null);
                }

                RaiseStatus(result.Succeeded
                    ? $"Profiles reloaded and {result.Simulator!.DisplayName} reconnected."
                    : $"Profiles reloaded. Reconnection failed: {result.Message}");
            }
            else
            {
                RaiseStatus("Aircraft profiles reloaded. Connect a simulator to apply them.");
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await lifecycleGate.WaitAsync();
        try
        {
            await DisconnectCoreAsync(CancellationToken.None);
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
        }
    }

    private async Task ReloadProfilesCoreAsync(CancellationToken cancellationToken)
    {
        ProfileCatalog replacement = new();
        replacement.LoadBundled();
        List<string> warnings = [];
        Directory.CreateDirectory(userProfilesDirectory);
        if (Directory.Exists(userProfilesDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(
                         userProfilesDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (new FileInfo(path).Length > 1024 * 1024)
                    {
                        warnings.Add($"{Path.GetFileName(path)}: file exceeds the 1 MiB profile limit.");
                        continue;
                    }

                    string json = await File.ReadAllTextAsync(path, cancellationToken);
                    replacement.LoadJson(json, ProfileSource.User, path);
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Text.Json.JsonException
                                                   or InvalidDataException)
                {
                    warnings.Add($"{Path.GetFileName(path)}: {exception.Message}");
                }
            }
        }

        lock (profileStateLock)
        {
            profiles = replacement;
            rawVariables.Clear();
            profileStatus = warnings.Count == 0
                ? $"Loaded {replacement.Profiles.Count:N0} aircraft profile(s); no user-profile errors."
                : $"Loaded {replacement.Profiles.Count:N0} profile(s). Ignored {warnings.Count:N0} invalid user profile(s): {string.Join(" ", warnings)}";
            profileInspection = [profileStatus];
        }

        LogProfilesLoaded(logger, replacement.Profiles.Count, warnings.Count, null);
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        MsfsSimConnectProvider? oldProvider = provider;
        CancellationTokenSource? oldCancellation = consumerCancellation;
        Task? oldTelemetryConsumer = telemetryConsumer;
        Task? oldVariableConsumer = variableConsumer;

        provider = null;
        consumerCancellation = null;
        telemetryConsumer = null;
        variableConsumer = null;
        oldCancellation?.Cancel();

        if (oldProvider is not null)
        {
            oldProvider.ConnectionStateChanged -= OnConnectionStateChanged;
            await oldProvider.DisconnectAsync(cancellationToken);
        }

        await IgnoreExpectedCancellationAsync(oldTelemetryConsumer);
        await IgnoreExpectedCancellationAsync(oldVariableConsumer);
        oldCancellation?.Dispose();
        if (oldProvider is not null)
        {
            await oldProvider.DisposeAsync();
            LogDisconnected(logger, null);
            RaiseStatus("Simulator disconnected. Configuration, diagnostics, and replay remain available.");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The long-running telemetry consumer reports a provider boundary failure without terminating the desktop application.")]
    private async Task ConsumeTelemetryAsync(
        MsfsSimConnectProvider source,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TelemetrySnapshot snapshot in source.ReadTelemetryAsync(cancellationToken))
            {
                TelemetrySnapshot enriched = ApplyProfile(snapshot);
                lock (profileStateLock)
                {
                    lastSnapshot = enriched;
                }

                TelemetryReceived?.Invoke(this, new LiveTelemetryEventArgs(enriched));
                if (flightOperations.CurrentSession?.Status == FlightSessionStatus.Active)
                {
                    try
                    {
                        _ = await flightOperations.ProcessTelemetryAsync(enriched, cancellationToken);
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        LogConsumerFailed(logger, "flight-persistence", exception.GetType().Name, null);
                        RaiseStatus($"Live telemetry is visible, but the active flight could not persist a sample: {exception.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogConsumerFailed(logger, "telemetry", exception.GetType().Name, null);
            RaiseStatus($"The live telemetry consumer stopped: {exception.Message}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The long-running variable consumer reports a provider boundary failure without terminating the desktop application.")]
    private async Task ConsumeVariablesAsync(
        MsfsSimConnectProvider source,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (SimConnectReadOnlyVariableSample sample in source.ReadVariableSamplesAsync(cancellationToken))
            {
                VariableKind kind = sample.Definition.Kind == SimConnectVariableKind.LocalVariable
                    ? VariableKind.LVar
                    : VariableKind.SimVar;
                RawVariableValue value = new(kind, sample.Definition.Name, sample.NumericValue);
                lock (profileStateLock)
                {
                    rawVariables[VariableKey(kind, sample.Definition.Name)] = value;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogConsumerFailed(logger, "profile-variable", exception.GetType().Name, null);
            RaiseStatus($"The read-only profile-variable consumer stopped: {exception.Message}");
        }
    }

    private TelemetrySnapshot ApplyProfile(TelemetrySnapshot snapshot)
    {
        LoadedAircraftProfile[] loadedProfiles;
        RawVariableValue[] values;
        lock (profileStateLock)
        {
            loadedProfiles = [.. profiles.Profiles];
            values = [.. rawVariables.Values];
        }

        ProfileApplicationResult result = TelemetryProfileApplicator.Apply(loadedProfiles, snapshot, values);
        LoadedAircraftProfile selectedProfile = result.Selection.Match ?? result.Selection.Universal;
        string aircraftIdentity = string.Join(
            '\u001f',
            snapshot.Simulator.Kind,
            snapshot.Aircraft.Title,
            snapshot.Aircraft.IcaoType,
            snapshot.Aircraft.ConfigurationPath);
        string selectedProfileIdentity = string.Join(
            '\u001f',
            selectedProfile.Profile.Meta.Id,
            selectedProfile.Profile.Meta.Name,
            selectedProfile.Source);
        bool aircraftChanged;
        bool profileChanged;
        lock (profileStateLock)
        {
            profileStatus = result.Selection.Match is null
                ? $"Universal profile: {result.Selection.Universal.Profile.Meta.Name}."
                : $"Profile: {result.Selection.Match.Profile.Meta.Name}; source {result.Selection.Match.Source}; priority {result.Selection.Match.Profile.Meta.Priority}.";
            profileInspection = CreateProfileInspection(snapshot, result);
            aircraftChanged = !string.Equals(lastLoggedAircraftIdentity, aircraftIdentity, StringComparison.Ordinal);
            profileChanged = !string.Equals(lastLoggedProfileIdentity, selectedProfileIdentity, StringComparison.Ordinal);
            lastLoggedAircraftIdentity = aircraftIdentity;
            lastLoggedProfileIdentity = selectedProfileIdentity;
        }

        if (aircraftChanged)
        {
            LogAircraftDetected(
                logger,
                snapshot.Simulator.Kind.ToString(),
                SanitizeLogValue(snapshot.Aircraft.Title),
                SanitizeLogValue(snapshot.Aircraft.IcaoType),
                null);
        }

        if (profileChanged)
        {
            LogProfileSelected(
                logger,
                SanitizeLogValue(selectedProfile.Profile.Meta.Id),
                SanitizeLogValue(selectedProfile.Profile.Meta.Name),
                selectedProfile.Source.ToString(),
                null);
        }

        return result.Snapshot;
    }

    private void OnConnectionStateChanged(object? sender, SimulatorConnectionStateChangedEventArgs eventArgs)
    {
        LogConnectionStateChanged(logger, eventArgs.Previous.ToString(), eventArgs.Current.ToString(), null);
        RaiseStatus(eventArgs.Reason is null
            ? $"Simulator state: {eventArgs.Current}."
            : $"Simulator state: {eventArgs.Current}. {eventArgs.Reason}");
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(
        this,
        new LiveSimulatorStatusEventArgs(ConnectionState, message, ProfileStatus));

    private static IReadOnlyList<SimConnectReadOnlyVariableDefinition> CreateReadOnlyDefinitions(
        IEnumerable<LoadedAircraftProfile> loadedProfiles)
    {
        Dictionary<string, SimConnectReadOnlyVariableDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (TelemetryRule rule in loadedProfiles
                     .SelectMany(static profile => ProfileEngine.EnumerateVariables(profile.Profile))
                     .OrderBy(static rule => rule.Kind)
                     .ThenBy(static rule => rule.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static rule => rule.Unit, StringComparer.OrdinalIgnoreCase))
        {
            string identity = $"{rule.Kind}:{rule.Name}:{rule.Unit}";
            if (definitions.ContainsKey(identity))
            {
                continue;
            }

            if (definitions.Count == 256)
            {
                break;
            }

            definitions.Add(identity, new(
                $"profile-{index++:D3}",
                rule.Kind == VariableKind.LVar
                    ? SimConnectVariableKind.LocalVariable
                    : SimConnectVariableKind.SimVar,
                rule.Name,
                rule.Unit,
                IsBooleanRule(rule) ? SimConnectVariableValueKind.Logical : SimConnectVariableValueKind.Numeric));
        }

        return [.. definitions.Values];
    }

    private static bool IsBooleanRule(TelemetryRule rule) => rule.Operator is RuleOperator.Equals
        or RuleOperator.NotEquals
        or RuleOperator.GreaterThan
        or RuleOperator.GreaterThanOrEqual
        or RuleOperator.LessThan
        or RuleOperator.LessThanOrEqual;

    private static string VariableKey(VariableKind kind, string name) => $"{kind}:{name}";

    private static string SanitizeLogValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "not reported";
        }

        return string.Concat(value.Trim().Take(128).Select(static character =>
            char.IsControl(character) ? ' ' : character));
    }

    private static List<string> CreateProfileInspection(
        TelemetrySnapshot source,
        ProfileApplicationResult result)
    {
        List<string> inspection =
        [
            $"Simulator: {source.Simulator.DisplayName} {source.Simulator.Version ?? "version not reported"}",
            $"Aircraft: {source.Aircraft.Title}; ICAO {source.Aircraft.IcaoType ?? "not reported"}",
            $"Configuration: {source.Aircraft.ConfigurationPath ?? "not reported"}",
            result.Selection.Match is null
                ? $"Profile: {result.Selection.Universal.Profile.Meta.Name} (universal)"
                : $"Profile: {result.Selection.Match.Profile.Meta.Name}; {result.Selection.Match.Source}; priority {result.Selection.Match.Profile.Meta.Priority}",
        ];
        foreach ((string feature, InterpretedFeature value) in result.InterpretedFeatures
                     .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string interpreted = value.IsAvailable
                ? value.BooleanValue is { } boolean
                    ? boolean.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : value.NumericValue?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable"
                : $"unavailable ({value.Error})";
            string raw = value.RawValue?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "not received";
            inspection.Add($"{feature}: {value.SourceVariable}; raw {raw}; interpreted {interpreted}");
        }

        return inspection;
    }

    private static async Task IgnoreExpectedCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed class LiveSimulatorStatusEventArgs(
    SimulatorConnectionState state,
    string message,
    string profileStatus) : EventArgs
{
    public SimulatorConnectionState State { get; } = state;

    public string Message { get; } = message;

    public string ProfileStatus { get; } = profileStatus;
}

public sealed class LiveTelemetryEventArgs(TelemetrySnapshot snapshot) : EventArgs
{
    public TelemetrySnapshot Snapshot { get; } = snapshot;
}

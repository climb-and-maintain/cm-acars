using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.SimConnect.Connection;
using ClimbAndMaintain.Acars.SimConnect.Diagnostics;
using ClimbAndMaintain.Acars.SimConnect.Interop;
using ClimbAndMaintain.Acars.SimConnect.Validation;
using ClimbAndMaintain.Acars.SimConnect.Variables;

namespace ClimbAndMaintain.Acars.SimConnect.Provider;

public sealed class MsfsSimConnectProvider : ISimulatorTelemetryProvider, ISimConnectReadOnlyVariableSource
{
    private const uint CoreDefinitionId = 0x434D1001;
    private const uint CoreRequestId = 0x434D1002;
    private const uint StandardSystemsDefinitionId = 0x434D1003;
    private const uint StandardSystemsRequestId = 0x434D1004;
    private const uint AircraftIdentityDefinitionId = 0x434D1005;
    private const uint AircraftIdentityRequestId = 0x434D1006;
    private const uint DynamicVariablesDefinitionIdBase = 0x434D2000;
    private const uint DynamicVariablesRequestIdBase = 0x434D3000;
    private const uint FastControlsDefinitionId = 0x434D1009;
    private const uint FastControlsRequestId = 0x434D100A;
    private const uint SimulatorTimeDefinitionId = 0x434D1101;
    private const uint SimulatorTimeRequestId = 0x434D1102;
    private const uint SlewStateDefinitionId = 0x434D1103;
    private const uint SlewStateRequestId = 0x434D1104;
    private const uint TransponderCodeDefinitionId = 0x434D1105;
    private const uint TransponderCodeRequestId = 0x434D1106;
    private const uint PauseStateEventId = 0x434D1110;
    private const uint AircraftLoadedEventId = 0x434D1010;
    private const uint FlightLoadedEventId = 0x434D1011;
    private const int MaximumDispatchBytes = 1024 * 1024;
    private static readonly TimeSpan FastTelemetryInterval = TimeSpan.FromMilliseconds(500);

    private static readonly Definition[] CoreDefinitions =
    [
        new("PLANE LATITUDE", "degrees"),
        new("PLANE LONGITUDE", "degrees"),
        new("PLANE ALTITUDE", "feet"),
        new("PLANE ALT ABOVE GROUND", "feet"),
        new("AIRSPEED INDICATED", "knots"),
        new("GROUND VELOCITY", "knots"),
        new("VERTICAL SPEED", "feet per minute"),
        new("PLANE HEADING DEGREES TRUE", "degrees"),
        new("PLANE HEADING DEGREES MAGNETIC", "degrees"),
        new("SIM ON GROUND", "Bool"),
    ];

    private static readonly Definition[] StandardSystemsDefinitions =
    [
        new("FUEL TOTAL QUANTITY WEIGHT", "pounds"),
        new("NUMBER OF ENGINES", "number"),
        new("GENERAL ENG COMBUSTION:1", "Bool"),
        new("GENERAL ENG STARTER:1", "Bool"),
        new("TURB ENG N1:1", "Percent Over 100"),
        new("ENG FUEL FLOW PPH:1", "pounds per hour"),
        new("GENERAL ENG COMBUSTION:2", "Bool"),
        new("GENERAL ENG STARTER:2", "Bool"),
        new("TURB ENG N1:2", "Percent Over 100"),
        new("ENG FUEL FLOW PPH:2", "pounds per hour"),
        new("GENERAL ENG COMBUSTION:3", "Bool"),
        new("GENERAL ENG STARTER:3", "Bool"),
        new("TURB ENG N1:3", "Percent Over 100"),
        new("ENG FUEL FLOW PPH:3", "pounds per hour"),
        new("GENERAL ENG COMBUSTION:4", "Bool"),
        new("GENERAL ENG STARTER:4", "Bool"),
        new("TURB ENG N1:4", "Percent Over 100"),
        new("ENG FUEL FLOW PPH:4", "pounds per hour"),
        new("LIGHT BEACON", "Bool"),
        new("LIGHT NAV", "Bool"),
        new("LIGHT STROBE", "Bool"),
        new("LIGHT LANDING", "Bool"),
        new("LIGHT TAXI", "Bool"),
        new("LIGHT WING", "Bool"),
        new("LIGHT LOGO", "Bool"),
        new("AUTOPILOT MASTER", "Bool"),
        new("BRAKE PARKING POSITION", "Bool"),
        new("TRANSPONDER STATE:1", "enum"),
        new("ELECTRICAL MASTER BATTERY:1", "Bool"),
        new("EXTERNAL POWER ON", "Bool"),
        new("APU PCT RPM", "Percent Over 100"),
        new("STRUCTURAL DEICE SWITCH", "Bool"),
        new("CABIN SEATBELTS ALERT SWITCH", "Bool"),
    ];

    private static readonly Definition[] AircraftIdentityDefinitions =
    [
        new("TITLE", null, SimConnectDataType.String256),
        new("ATC MODEL", null, SimConnectDataType.String32),
    ];

    private static readonly Definition[] FastControlsDefinitions =
    [
        new("GEAR TOTAL PCT EXTENDED", "Percent Over 100"),
        new("FLAPS HANDLE INDEX", "number"),
        new("TRAILING EDGE FLAPS LEFT PERCENT", "Percent Over 100"),
    ];

    private static readonly Definition[] SimulatorTimeDefinitions =
    [
        new("ZULU YEAR", "number"),
        new("ZULU MONTH OF YEAR", "number"),
        new("ZULU DAY OF MONTH", "number"),
        new("ZULU TIME", "seconds"),
    ];

    private static readonly Definition[] SlewStateDefinitions =
    [
        new("IS SLEW ACTIVE", "Bool"),
    ];

    private static readonly Definition[] TransponderCodeDefinitions =
    [
        new("TRANSPONDER CODE:1", "BCO16"),
    ];

    private readonly SimConnectProviderOptions options;
    private readonly SimConnectLibraryValidator validator;
    private readonly ISimConnectNativeApiFactory? nativeApiFactory;
    private readonly bool platformSupported;
    private readonly ImmutableArray<SimConnectReadOnlyVariableDefinition> dynamicDefinitions;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Channel<TelemetrySnapshot> telemetry = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(32)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = false,
        SingleWriter = true,
    });
    private readonly Channel<SimConnectReadOnlyVariableSample> dynamicSamples =
        Channel.CreateBounded<SimConnectReadOnlyVariableSample>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });
    private readonly Dictionary<uint, int> optionalDynamicSendIds = [];
    private readonly HashSet<uint> optionalSendIds = [];
    private readonly HashSet<int> configuredDynamicDefinitionIndexes = [];

    private CancellationTokenSource? runCancellation;
    private TaskCompletionSource<SimulatorConnectionResult>? connectCompletion;
    private TaskCompletionSource? workerCompletion;
    private int connectionState;
    private SimulatorIdentity? connectedSimulator;
    private SimConnectStandardSystemsTelemetry? latestSystems;
    private SimConnectFlightControlsTelemetry latestFlightControls = SimConnectFlightControlsTelemetry.Unknown;
    private AircraftIdentity latestAircraft = AircraftIdentity.Unknown;
    private DateTimeOffset? latestSimulatorTime;
    private bool latestIsPaused;
    private bool latestIsSlewActive;
    private string? latestTransponderCode;
    private int disposed;

    public MsfsSimConnectProvider(SimConnectProviderOptions options)
        : this(
            options,
            new SimConnectLibraryValidator(),
            OperatingSystem.IsWindows() ? new Win32SimConnectNativeApiFactory() : null,
            OperatingSystem.IsWindows())
    {
    }

    internal MsfsSimConnectProvider(
        SimConnectProviderOptions options,
        SimConnectLibraryValidator validator,
        ISimConnectNativeApiFactory? nativeApiFactory,
        bool platformSupported)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ConnectionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ConnectionTimeout, "The connection timeout must be positive.");
        }

        if (options.RecoveryInitialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.RecoveryInitialDelay, "The initial recovery delay must be positive.");
        }

        if (options.RecoveryMaximumDelay < options.RecoveryInitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.RecoveryMaximumDelay, "The maximum recovery delay cannot be shorter than the initial delay.");
        }

        ArgumentNullException.ThrowIfNull(options.ReadOnlyVariables);
        dynamicDefinitions = [.. options.ReadOnlyVariables];
        if (dynamicDefinitions.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(options), dynamicDefinitions.Length, "At most 256 read-only profile variables may be requested per provider.");
        }

        if (dynamicDefinitions.Any(static item => item is null))
        {
            throw new ArgumentException("Read-only SimConnect variable definitions cannot be null.", nameof(options));
        }

        if (dynamicDefinitions.Select(static item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != dynamicDefinitions.Length)
        {
            throw new ArgumentException("Read-only SimConnect variable keys must be unique.", nameof(options));
        }

        _ = SimConnectCompatibility.ExpectedApplicationMajor(options.Target);
        this.options = options;
        this.validator = validator;
        this.nativeApiFactory = nativeApiFactory;
        this.platformSupported = platformSupported;
        connectionState = string.IsNullOrWhiteSpace(options.LibraryPath)
            ? (int)SimulatorConnectionState.Unavailable
            : (int)SimulatorConnectionState.Disconnected;
    }

    public string ProviderId => options.Target == SimConnectTarget.Msfs2020
        ? "simconnect-msfs2020"
        : "simconnect-msfs2024";

    public SimulatorConnectionState ConnectionState =>
        (SimulatorConnectionState)Volatile.Read(ref connectionState);

    public SimulatorIdentity? ConnectedSimulator => Volatile.Read(ref connectedSimulator);

    public event EventHandler<SimulatorConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public async ValueTask<SimulatorConnectionResult> ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task<SimulatorConnectionResult> pendingConnection;
        CancellationTokenSource activeCancellation;

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            SimulatorIdentity? activeSimulator = ConnectedSimulator;
            if (activeSimulator is not null && ConnectionState is SimulatorConnectionState.Connected
                or SimulatorConnectionState.Ready
                or SimulatorConnectionState.Tracking)
            {
                return SimulatorConnectionResult.Success(activeSimulator);
            }

            if (connectCompletion is not null && workerCompletion is not null && !workerCompletion.Task.IsCompleted)
            {
                pendingConnection = connectCompletion.Task;
                activeCancellation = runCancellation!;
            }
            else
            {
                ResetCompletedWorker();

                SimConnectLibraryValidationResult validation = await validator
                    .ValidateAsync(options.LibraryPath, cancellationToken)
                    .ConfigureAwait(false);
                if (!validation.IsCompatible || validation.Metadata is null)
                {
                    TransitionTo(SimulatorConnectionState.Unavailable, FirstError(validation));
                    return SimulatorConnectionResult.Failure(
                        SimulatorConnectionFailureKind.IncompatibleLibrary,
                        FirstError(validation));
                }

                if (!platformSupported || nativeApiFactory is null)
                {
                    const string reason = "Native SimConnect connectivity is available only on Windows; diagnostics and replay remain available.";
                    TransitionTo(SimulatorConnectionState.Unavailable, reason);
                    return SimulatorConnectionResult.Failure(
                        SimulatorConnectionFailureKind.PrerequisiteMissing,
                        reason);
                }

                CancellationTokenSource newRunCancellation = new();
                TaskCompletionSource<SimulatorConnectionResult> newConnectCompletion = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource newWorkerCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                runCancellation = newRunCancellation;
                activeCancellation = newRunCancellation;
                connectCompletion = newConnectCompletion;
                workerCompletion = newWorkerCompletion;
                pendingConnection = newConnectCompletion.Task;
                TransitionTo(SimulatorConnectionState.Connecting);

                Thread worker = new(() => RunProviderWorker(
                    newConnectCompletion,
                    newWorkerCompletion,
                    newRunCancellation.Token))
                {
                    IsBackground = true,
                    Name = $"SimConnect provider ({options.Target})",
                };
                worker.Start();
            }
        }
        finally
        {
            lifecycleGate.Release();
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            activeCancellation);
        return await pendingConnection.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        Task? pendingWorker;

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            runCancellation?.Cancel();
            pendingWorker = workerCompletion?.Task;
        }
        finally
        {
            lifecycleGate.Release();
        }

        if (pendingWorker is not null)
        {
            await pendingWorker.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResetCompletedWorker();
            Volatile.Write(ref connectedSimulator, null);
            if (Volatile.Read(ref disposed) == 0 && ConnectionState != SimulatorConnectionState.Unavailable)
            {
                TransitionTo(SimulatorConnectionState.Disconnected);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public IAsyncEnumerable<TelemetrySnapshot> ReadTelemetryAsync(CancellationToken cancellationToken) =>
        telemetry.Reader.ReadAllAsync(cancellationToken);

    public IAsyncEnumerable<SimConnectReadOnlyVariableSample> ReadVariableSamplesAsync(CancellationToken cancellationToken) =>
        dynamicSamples.Reader.ReadAllAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        runCancellation?.Cancel();
        Task? pendingWorker = workerCompletion?.Task;
        if (pendingWorker is not null)
        {
            await pendingWorker.ConfigureAwait(false);
        }

        ResetCompletedWorker();
        Volatile.Write(ref connectedSimulator, null);
        telemetry.Writer.TryComplete();
        dynamicSamples.Writer.TryComplete();
        TransitionTo(SimulatorConnectionState.Disconnected);
        lifecycleGate.Dispose();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The native worker boundary must convert failures into provider diagnostics.")]
    private void RunProviderWorker(
        TaskCompletionSource<SimulatorConnectionResult> connectionResult,
        TaskCompletionSource exited,
        CancellationToken cancellationToken)
    {
        int recoveryAttempt = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                latestSystems = null;
                latestFlightControls = SimConnectFlightControlsTelemetry.Unknown;
                latestAircraft = AircraftIdentity.Unknown;
                latestSimulatorTime = null;
                latestIsPaused = false;
                latestIsSlewActive = false;
                latestTransponderCode = null;
                optionalDynamicSendIds.Clear();
                optionalSendIds.Clear();
                configuredDynamicDefinitionIndexes.Clear();
                bool reconnectRequested = false;
                bool terminalFailure = false;
                bool telemetryConfigured = false;
                ISimConnectNativeApi? api = null;
                SimConnectLibraryLoadLease? loadLease = null;
                AutoResetEvent? dispatchEvent = null;
                nint connection = 0;

                try
                {
                    SimConnectLibraryValidationResult loadValidation = validator.Validate(options.LibraryPath);
                    if (!loadValidation.IsCompatible || loadValidation.Metadata is null)
                    {
                        terminalFailure = true;
                        FailConnection(
                            connectionResult,
                            SimulatorConnectionFailureKind.IncompatibleLibrary,
                            FirstError(loadValidation));
                        break;
                    }

                    loadLease = validator.AcquireLoadLease(loadValidation);
                    loadValidation = loadLease.Validation;
                    if (!loadValidation.IsCompatible || loadValidation.Metadata is null)
                    {
                        terminalFailure = true;
                        FailConnection(
                            connectionResult,
                            SimulatorConnectionFailureKind.IncompatibleLibrary,
                            FirstError(loadValidation));
                        break;
                    }

                    api = nativeApiFactory!.Load(loadValidation.Metadata.CanonicalPath);
                    dispatchEvent = new(initialState: false);
                    int openResult = api.Open(
                        out connection,
                        "Climb and Maintain ACARS",
                        dispatchEvent,
                        SimConnectConstants.OpenConfigIndexLocal);

                    if (SimConnectConstants.Failed(openResult) || connection == 0)
                    {
                        const string reason = "The SimConnect library loaded, but the simulator is not accepting a local connection.";
                        connectionResult.TrySetResult(SimulatorConnectionResult.Failure(
                            SimulatorConnectionFailureKind.SimulatorNotRunning,
                            reason));
                        reconnectRequested = true;
                    }
                    else
                    {
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        TimeSpan nextFastTelemetryRequest = TimeSpan.MaxValue;
                        while (!cancellationToken.IsCancellationRequested && !reconnectRequested && !terminalFailure)
                        {
                            if (ConnectionState == SimulatorConnectionState.Connecting
                                && stopwatch.Elapsed >= options.ConnectionTimeout)
                            {
                                const string reason = "The simulator did not complete the SimConnect handshake before the timeout.";
                                connectionResult.TrySetResult(SimulatorConnectionResult.Failure(
                                    SimulatorConnectionFailureKind.TimedOut,
                                    reason));
                                reconnectRequested = true;
                                break;
                            }

                            if (telemetryConfigured && stopwatch.Elapsed >= nextFastTelemetryRequest)
                            {
                                RequestFastTelemetry(api, connection);
                                do
                                {
                                    nextFastTelemetryRequest += FastTelemetryInterval;
                                }
                                while (nextFastTelemetryRequest <= stopwatch.Elapsed);
                            }

                            if (!dispatchEvent.WaitOne(250))
                            {
                                continue;
                            }

                            bool wasTelemetryConfigured = telemetryConfigured;
                            while (DrainOneMessage(
                                api,
                                connection,
                                connectionResult,
                                ref reconnectRequested,
                                ref terminalFailure,
                                ref telemetryConfigured))
                            {
                            }

                            if (!wasTelemetryConfigured && telemetryConfigured)
                            {
                                nextFastTelemetryRequest = stopwatch.Elapsed + FastTelemetryInterval;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (SimConnectNativeCallException exception)
                {
                    if (exception.HResultCode == SimConnectConstants.StatusRemoteDisconnect)
                    {
                        reconnectRequested = true;
                    }
                    else
                    {
                        terminalFailure = true;
                        FailConnection(connectionResult, SimulatorConnectionFailureKind.ProviderFault, exception.Message);
                    }
                }
                catch (Win32Exception exception)
                {
                    terminalFailure = true;
                    FailConnection(connectionResult, SimulatorConnectionFailureKind.IncompatibleLibrary, exception.Message);
                }
                catch (Exception exception)
                {
                    terminalFailure = true;
                    FailConnection(connectionResult, SimulatorConnectionFailureKind.ProviderFault, exception.Message);
                }
                finally
                {
                    bool wasConnected = ConnectedSimulator is not null;
                    if (api is not null && connection != 0)
                    {
                        _ = api.Close(connection);
                    }

                    api?.Dispose();
                    loadLease?.Dispose();
                    if (dispatchEvent is not null)
                    {
                        GC.KeepAlive(dispatchEvent);
                        dispatchEvent.Dispose();
                    }

                    if (wasConnected)
                    {
                        recoveryAttempt = 0;
                    }
                }

                if (terminalFailure || cancellationToken.IsCancellationRequested)
                {
                    if (terminalFailure)
                    {
                        Volatile.Write(ref connectedSimulator, null);
                    }

                    break;
                }

                if (reconnectRequested)
                {
                    Volatile.Write(ref connectedSimulator, null);
                    TransitionTo(SimulatorConnectionState.Recovering, "The SimConnect session ended; reconnection will be attempted automatically.");
                    TimeSpan delay = RecoveryDelay(recoveryAttempt);
                    recoveryAttempt = Math.Min(recoveryAttempt + 1, 30);
                    if (cancellationToken.WaitHandle.WaitOne(delay))
                    {
                        break;
                    }

                    TransitionTo(SimulatorConnectionState.Connecting);
                }
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested && ConnectionState != SimulatorConnectionState.Unavailable)
            {
                connectionResult.TrySetCanceled(cancellationToken);
                TransitionTo(SimulatorConnectionState.Disconnected);
            }

            exited.TrySetResult();
        }
    }

    private bool DrainOneMessage(
        ISimConnectNativeApi api,
        nint connection,
        TaskCompletionSource<SimulatorConnectionResult> connectionResult,
        ref bool reconnectRequested,
        ref bool terminalFailure,
        ref bool telemetryConfigured)
    {
        int dispatchResult = api.GetNextDispatch(connection, out nint data, out uint dataSize);
        EnsureSucceeded(dispatchResult, "SimConnect_GetNextDispatch");
        if (data == 0 && dataSize == 0)
        {
            return false;
        }

        if (data == 0 || dataSize < SimConnectReceiveParser.HeaderSize || dataSize > MaximumDispatchBytes)
        {
            throw new InvalidOperationException($"The dispatch buffer pointer or size ({dataSize}) is invalid.");
        }

        int packetSize = checked((int)dataSize);
        byte[] packet = new byte[packetSize];
        Marshal.Copy(data, packet, 0, packetSize);
        if (!SimConnectReceiveParser.TryParseHeader(packet, out SimConnectReceiveHeader? header, out string? error))
        {
            throw new InvalidOperationException(error);
        }

        switch (header!.Id)
        {
            case SimConnectReceiveId.Null:
                return false;

            case SimConnectReceiveId.Open:
                if (!HandleOpen(api, connection, packet, connectionResult, ref telemetryConfigured))
                {
                    terminalFailure = true;
                    return false;
                }

                break;

            case SimConnectReceiveId.Exception:
                if (HandleException(packet, connectionResult))
                {
                    terminalFailure = true;
                    return false;
                }

                break;

            case SimConnectReceiveId.SimObjectData:
                HandleTelemetry(packet);
                break;

            case SimConnectReceiveId.Event:
                HandleSystemEvent(packet);
                break;

            case SimConnectReceiveId.EventFilename:
                HandleFilenameEvent(api, connection, packet);
                break;

            case SimConnectReceiveId.Quit:
                reconnectRequested = true;
                return false;
        }

        return true;
    }

    private bool HandleOpen(
        ISimConnectNativeApi api,
        nint connection,
        ReadOnlySpan<byte> packet,
        TaskCompletionSource<SimulatorConnectionResult> connectionResult,
        ref bool telemetryConfigured)
    {
        if (!SimConnectReceiveParser.TryParseOpen(packet, out SimConnectOpenMessage? open, out string? error))
        {
            throw new InvalidOperationException(error);
        }

        if (!SimConnectCompatibility.IsExpectedSimulator(options.Target, open!.Information))
        {
            string reason = $"Connected simulator major {open.Information.ApplicationVersionMajor} does not match {options.Target}.";
            connectionResult.TrySetResult(SimulatorConnectionResult.Failure(
                SimulatorConnectionFailureKind.IncompatibleLibrary,
                reason));
            TransitionTo(SimulatorConnectionState.Faulted, reason);
            return false;
        }

        if (!telemetryConfigured)
        {
            ConfigureRecurringTelemetry(api, connection);
            telemetryConfigured = true;
        }

        SimulatorIdentity identity = new(
            options.Target == SimConnectTarget.Msfs2020 ? SimulatorKind.Msfs2020 : SimulatorKind.Msfs2024,
            options.Target == SimConnectTarget.Msfs2020
                ? "Microsoft Flight Simulator 2020"
                : "Microsoft Flight Simulator 2024",
            open.Information.ApplicationVersion);
        Volatile.Write(ref connectedSimulator, identity);
        TransitionTo(SimulatorConnectionState.Connected);
        connectionResult.TrySetResult(SimulatorConnectionResult.Success(identity));
        return true;
    }

    private bool HandleException(
        ReadOnlySpan<byte> packet,
        TaskCompletionSource<SimulatorConnectionResult> connectionResult)
    {
        if (!SimConnectReceiveParser.TryParseException(packet, out SimConnectExceptionMessage? exception, out string? error))
        {
            throw new InvalidOperationException(error);
        }

        if (optionalDynamicSendIds.TryGetValue(exception!.SendId, out int dynamicDefinitionIndex))
        {
            configuredDynamicDefinitionIndexes.Remove(dynamicDefinitionIndex);
            return false;
        }

        if (optionalSendIds.Contains(exception.SendId))
        {
            return false;
        }

        string reason = exception.Exception == SimConnectExceptionCode.VersionMismatch
            ? "The SimConnect client library and simulator server reported a version mismatch."
            : $"The simulator reported SimConnect exception {(uint)exception.Exception}.";
        connectionResult.TrySetResult(SimulatorConnectionResult.Failure(
            SimulatorConnectionFailureKind.IncompatibleLibrary,
            reason));
        TransitionTo(SimulatorConnectionState.Faulted, reason);
        return true;
    }

    private void HandleTelemetry(ReadOnlySpan<byte> packet)
    {
        if (!SimConnectReceiveParser.TryParseObjectData(packet, out SimConnectObjectDataMessage? objectData, out string? objectError))
        {
            throw new InvalidOperationException(objectError);
        }

        switch (objectData!.RequestId, objectData.DefinitionId)
        {
            case (CoreRequestId, CoreDefinitionId):
                HandleCoreTelemetry(objectData);
                break;

            case (StandardSystemsRequestId, StandardSystemsDefinitionId):
                if (!SimConnectReceiveParser.TryParseStandardSystemsTelemetry(
                        objectData,
                        out SimConnectStandardSystemsTelemetry? systems,
                        out string? systemsError))
                {
                    throw new InvalidOperationException(systemsError);
                }

                latestSystems = systems;
                break;

            case (AircraftIdentityRequestId, AircraftIdentityDefinitionId):
                if (!SimConnectReceiveParser.TryParseAircraftIdentity(
                        objectData,
                        out SimConnectAircraftIdentityTelemetry? identity,
                        out string? identityError))
                {
                    throw new InvalidOperationException(identityError);
                }

                latestAircraft = new(
                    identity!.Title,
                    identity.IcaoType,
                    latestAircraft.ConfigurationPath,
                    profileId: null);
                if (ConnectionState == SimulatorConnectionState.AircraftLoading)
                {
                    TransitionTo(SimulatorConnectionState.Connected);
                }

                break;

            case (FastControlsRequestId, FastControlsDefinitionId):
                if (!SimConnectReceiveParser.TryParseFlightControlsTelemetry(
                        objectData,
                        out SimConnectFlightControlsTelemetry? flightControls,
                        out string? flightControlsError))
                {
                    throw new InvalidOperationException(flightControlsError);
                }

                latestFlightControls = flightControls!;
                break;

            case (SimulatorTimeRequestId, SimulatorTimeDefinitionId):
                latestSimulatorTime = SimConnectReceiveParser.TryParseSimulatorTime(
                    objectData,
                    out DateTimeOffset? simulatorTime,
                    out _)
                    ? simulatorTime
                    : null;
                break;

            case (SlewStateRequestId, SlewStateDefinitionId):
                latestIsSlewActive = SimConnectReceiveParser.TryParseBooleanValue(
                    objectData,
                    out bool isSlewActive,
                    out _)
                    && isSlewActive;
                break;

            case (TransponderCodeRequestId, TransponderCodeDefinitionId):
                latestTransponderCode = SimConnectReceiveParser.TryParseTransponderCode(
                    objectData,
                    out string? transponderCode,
                    out _)
                    ? transponderCode
                    : null;
                break;

            default:
                if (TryGetDynamicDefinitionIndex(objectData, out int dynamicDefinitionIndex))
                {
                    HandleDynamicVariableValue(objectData, dynamicDefinitionIndex);
                }

                break;
        }
    }

    private void HandleCoreTelemetry(SimConnectObjectDataMessage objectData)
    {
        if (!SimConnectReceiveParser.TryParseCoreTelemetry(
                objectData,
                out SimConnectCoreTelemetry? sample,
                out string? telemetryError))
        {
            throw new InvalidOperationException(telemetryError);
        }

        SimulatorIdentity simulator = ConnectedSimulator ?? SimulatorIdentity.Unknown;
        SimConnectCoreTelemetry values = sample!;
        SimConnectStandardSystemsTelemetry? systems = latestSystems;
        ImmutableArray<EngineTelemetry> engines = systems is null
            ? []
            : BuildEngineTelemetry(systems);
        telemetry.Writer.TryWrite(new TelemetrySnapshot
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            SimulatorTime = latestSimulatorTime,
            Position = new(values.LatitudeDegrees, values.LongitudeDegrees),
            AltitudeMsl = new Altitude(values.AltitudeMslFeet),
            AltitudeAgl = new Altitude(values.AltitudeAglFeet),
            IndicatedAirspeed = new Speed(values.IndicatedAirspeedKnots),
            GroundSpeed = new Speed(values.GroundSpeedKnots),
            VerticalSpeed = new VerticalSpeed(values.VerticalSpeedFeetPerMinute),
            TrueHeading = new Heading(values.TrueHeadingDegrees),
            MagneticHeading = new Heading(values.MagneticHeadingDegrees),
            OnGround = values.OnGround,
            FuelRemaining = systems is null
                ? null
                : new FuelMass(Math.Max(0, systems.FuelWeightPounds) * SimConnectConstants.PoundsToKilograms),
            TotalFuelFlow = systems is null
                ? null
                : new FuelFlow(engines.Sum(static engine => engine.FuelFlow?.KilogramsPerHour ?? 0)),
            Engines = engines,
            Systems = BuildSystemsTelemetry(latestFlightControls, systems, latestTransponderCode),
            Aircraft = latestAircraft,
            Simulator = simulator,
            IsPaused = latestIsPaused,
            IsSlewActive = latestIsSlewActive,
        });

        if (ConnectionState == SimulatorConnectionState.Connected)
        {
            TransitionTo(SimulatorConnectionState.Ready);
        }
        else if (ConnectionState == SimulatorConnectionState.Ready)
        {
            TransitionTo(SimulatorConnectionState.Tracking);
        }
    }

    private void HandleFilenameEvent(ISimConnectNativeApi api, nint connection, ReadOnlySpan<byte> packet)
    {
        if (!SimConnectReceiveParser.TryParseEventFilename(
                packet,
                out SimConnectEventFilenameMessage? filenameEvent,
                out string? error))
        {
            throw new InvalidOperationException(error);
        }

        if (filenameEvent!.EventId != AircraftLoadedEventId)
        {
            return;
        }

        latestAircraft = new(
            latestAircraft.Title,
            latestAircraft.IcaoType,
            filenameEvent.FileName,
            profileId: null);
        TransitionTo(SimulatorConnectionState.AircraftLoading, "The simulator loaded a different aircraft.");
        EnsureSucceeded(
            api.RequestDataOnSimObject(
                connection,
                AircraftIdentityRequestId,
                AircraftIdentityDefinitionId,
                SimConnectConstants.ObjectIdUser,
                SimConnectPeriod.Once,
                SimConnectConstants.DataRequestFlagDefault,
                0,
                0,
                0),
            "SimConnect_RequestDataOnSimObject(aircraft identity)");
    }

    private void HandleSystemEvent(ReadOnlySpan<byte> packet)
    {
        if (SimConnectReceiveParser.TryParseEvent(packet, out SimConnectEventMessage? message, out _)
            && message!.EventId == PauseStateEventId)
        {
            latestIsPaused = message.Data != 0;
        }
    }

    private void HandleDynamicVariableValue(
        SimConnectObjectDataMessage objectData,
        int dynamicDefinitionIndex)
    {
        if (!SimConnectReceiveParser.TryParseFloat64Values(
                objectData,
                1,
                out ImmutableArray<double> values,
                out string? error))
        {
            throw new InvalidOperationException(error);
        }

        dynamicSamples.Writer.TryWrite(new(
            dynamicDefinitions[dynamicDefinitionIndex],
            values[0],
            DateTimeOffset.UtcNow));
    }

    private void ConfigureRecurringTelemetry(ISimConnectNativeApi api, nint connection)
    {
        AddDefinitions(api, connection, CoreDefinitionId, CoreDefinitions);
        AddDefinitions(api, connection, StandardSystemsDefinitionId, StandardSystemsDefinitions);
        AddDefinitions(api, connection, AircraftIdentityDefinitionId, AircraftIdentityDefinitions);
        AddDefinitions(api, connection, FastControlsDefinitionId, FastControlsDefinitions);
        TryConfigureOptionalTelemetry(
            api,
            connection,
            SimulatorTimeDefinitionId,
            SimulatorTimeRequestId,
            SimulatorTimeDefinitions);
        TryConfigureOptionalTelemetry(
            api,
            connection,
            SlewStateDefinitionId,
            SlewStateRequestId,
            SlewStateDefinitions);
        TryConfigureOptionalTelemetry(
            api,
            connection,
            TransponderCodeDefinitionId,
            TransponderCodeRequestId,
            TransponderCodeDefinitions);
        for (int index = 0; index < dynamicDefinitions.Length; index++)
        {
            SimConnectReadOnlyVariableDefinition definition = dynamicDefinitions[index];
            uint definitionId = DynamicVariablesDefinitionIdBase + checked((uint)index);
            int addResult = api.AddToDataDefinition(
                connection,
                definitionId,
                definition.Name,
                definition.Unit,
                SimConnectDataType.Float64,
                0,
                SimConnectConstants.Unused);
            if (SimConnectConstants.Failed(addResult))
            {
                continue;
            }

            TrackOptionalSendId(api, connection, index);
            int requestResult = api.RequestDataOnSimObject(
                connection,
                DynamicVariablesRequestIdBase + checked((uint)index),
                definitionId,
                SimConnectConstants.ObjectIdUser,
                SimConnectPeriod.Second,
                SimConnectConstants.DataRequestFlagDefault,
                0,
                0,
                0);
            if (SimConnectConstants.Failed(requestResult))
            {
                continue;
            }

            TrackOptionalSendId(api, connection, index);
            configuredDynamicDefinitionIndexes.Add(index);
        }

        EnsureSucceeded(
            api.SubscribeToSystemEvent(connection, AircraftLoadedEventId, "AircraftLoaded"),
            "SimConnect_SubscribeToSystemEvent(AircraftLoaded)");
        EnsureSucceeded(
            api.SubscribeToSystemEvent(connection, FlightLoadedEventId, "FlightLoaded"),
            "SimConnect_SubscribeToSystemEvent(FlightLoaded)");
        int pauseResult = api.SubscribeToSystemEvent(connection, PauseStateEventId, "Pause");
        if (!SimConnectConstants.Failed(pauseResult))
        {
            TrackOptionalSendId(api, connection);
        }
        EnsureSucceeded(
            api.RequestDataOnSimObject(
                connection,
                StandardSystemsRequestId,
                StandardSystemsDefinitionId,
                SimConnectConstants.ObjectIdUser,
                SimConnectPeriod.Second,
                SimConnectConstants.DataRequestFlagDefault,
                0,
                0,
                0),
            "SimConnect_RequestDataOnSimObject(standard systems)");
        EnsureSucceeded(
            api.RequestDataOnSimObject(
                connection,
                AircraftIdentityRequestId,
                AircraftIdentityDefinitionId,
                SimConnectConstants.ObjectIdUser,
                SimConnectPeriod.Once,
                SimConnectConstants.DataRequestFlagDefault,
                0,
                0,
                0),
            "SimConnect_RequestDataOnSimObject(aircraft identity)");

        RequestFastTelemetry(api, connection);
    }

    private bool TryGetDynamicDefinitionIndex(
        SimConnectObjectDataMessage objectData,
        out int dynamicDefinitionIndex)
    {
        uint definitionOffset = objectData.DefinitionId - DynamicVariablesDefinitionIdBase;
        uint requestOffset = objectData.RequestId - DynamicVariablesRequestIdBase;
        if (definitionOffset == requestOffset
            && definitionOffset < checked((uint)dynamicDefinitions.Length)
            && configuredDynamicDefinitionIndexes.Contains(checked((int)definitionOffset)))
        {
            dynamicDefinitionIndex = checked((int)definitionOffset);
            return true;
        }

        dynamicDefinitionIndex = -1;
        return false;
    }

    private void TrackOptionalSendId(ISimConnectNativeApi api, nint connection, int dynamicDefinitionIndex)
    {
        if (!SimConnectConstants.Failed(api.GetLastSentPacketId(connection, out uint packetId)))
        {
            optionalDynamicSendIds[packetId] = dynamicDefinitionIndex;
        }
    }

    private void TrackOptionalSendId(ISimConnectNativeApi api, nint connection)
    {
        if (!SimConnectConstants.Failed(api.GetLastSentPacketId(connection, out uint packetId)))
        {
            optionalSendIds.Add(packetId);
        }
    }

    private void TryConfigureOptionalTelemetry(
        ISimConnectNativeApi api,
        nint connection,
        uint definitionId,
        uint requestId,
        IEnumerable<Definition> definitions)
    {
        foreach (Definition definition in definitions)
        {
            int addResult = api.AddToDataDefinition(
                connection,
                definitionId,
                definition.Name,
                definition.Unit,
                definition.DataType,
                0,
                SimConnectConstants.Unused);
            if (SimConnectConstants.Failed(addResult))
            {
                return;
            }

            TrackOptionalSendId(api, connection);
        }

        int requestResult = api.RequestDataOnSimObject(
            connection,
            requestId,
            definitionId,
            SimConnectConstants.ObjectIdUser,
            SimConnectPeriod.Second,
            SimConnectConstants.DataRequestFlagDefault,
            0,
            0,
            0);
        if (!SimConnectConstants.Failed(requestResult))
        {
            TrackOptionalSendId(api, connection);
        }
    }

    private static void RequestFastTelemetry(ISimConnectNativeApi api, nint connection)
    {
        EnsureSucceeded(
            api.RequestDataOnSimObject(
                connection,
                FastControlsRequestId,
                FastControlsDefinitionId,
                SimConnectConstants.ObjectIdUser,
                SimConnectPeriod.Once,
                SimConnectConstants.DataRequestFlagDefault,
                0,
                0,
                0),
            "SimConnect_RequestDataOnSimObject(fast controls)");
        EnsureSucceeded(
            api.RequestDataOnSimObject(
                connection,
                CoreRequestId,
                CoreDefinitionId,
                SimConnectConstants.ObjectIdUser,
                SimConnectPeriod.Once,
                SimConnectConstants.DataRequestFlagDefault,
                0,
                0,
                0),
            "SimConnect_RequestDataOnSimObject(core telemetry)");
    }

    private static void AddDefinitions(
        ISimConnectNativeApi api,
        nint connection,
        uint definitionId,
        IEnumerable<Definition> definitions)
    {
        foreach (Definition definition in definitions)
        {
            EnsureSucceeded(
                api.AddToDataDefinition(
                    connection,
                    definitionId,
                    definition.Name,
                    definition.Unit,
                    definition.DataType,
                    0,
                    SimConnectConstants.Unused),
                $"SimConnect_AddToDataDefinition({definition.Name})");
        }
    }

    private static ImmutableArray<EngineTelemetry> BuildEngineTelemetry(SimConnectStandardSystemsTelemetry systems)
    {
        ImmutableArray<EngineTelemetry>.Builder engines = ImmutableArray.CreateBuilder<EngineTelemetry>(systems.EngineCount);
        for (int index = 0; index < systems.EngineCount; index++)
        {
            SimConnectEngineTelemetry engine = systems.Engines[index];
            EngineOperatingState state = engine.Combustion
                ? EngineOperatingState.Running
                : engine.StarterActive
                    ? EngineOperatingState.Starting
                    : EngineOperatingState.Off;
            engines.Add(new(
                index + 1,
                state,
                new Ratio(Math.Clamp(engine.N1Ratio, 0, 1)),
                new FuelFlow(Math.Max(0, engine.FuelFlowPoundsPerHour) * SimConnectConstants.PoundsToKilograms)));
        }

        return engines.MoveToImmutable();
    }

    private static AircraftSystemsTelemetry BuildSystemsTelemetry(
        SimConnectFlightControlsTelemetry flightControls,
        SimConnectStandardSystemsTelemetry? systems,
        string? transponderCode)
    {
        return new()
        {
            ParkingBrakeSet = systems?.ParkingBrakeSet ?? false,
            Gear = flightControls.GearExtensionRatio switch
            {
                <= 0.01 => GearPosition.Up,
                >= 0.99 => GearPosition.Down,
                _ => GearPosition.InTransit,
            },
            Flaps = new(
                flightControls.FlapHandleIndex,
                new Ratio(Math.Clamp(flightControls.FlapExtensionRatio, 0, 1))),
            AutopilotEngaged = systems?.AutopilotEngaged ?? false,
            Transponder = new(transponderCode, systems?.TransponderState switch
            {
                0 => TransponderMode.Off,
                1 or 2 => TransponderMode.Standby,
                3 => TransponderMode.On,
                4 or 5 => TransponderMode.Altitude,
                _ => TransponderMode.Unknown,
            }),
            Lights = new()
            {
                Beacon = systems?.BeaconLight ?? false,
                Navigation = systems?.NavigationLight ?? false,
                Strobe = systems?.StrobeLight ?? false,
                Landing = systems?.LandingLight ?? false,
                Taxi = systems?.TaxiLight ?? false,
                Wing = systems?.WingLight ?? false,
                Logo = systems?.LogoLight ?? false,
            },
            BatteryOn = systems?.BatteryOn ?? false,
            ExternalPowerOn = systems?.ExternalPowerOn ?? false,
            ApuRunning = systems?.ApuRpmRatio >= 0.05,
            AntiIceOn = systems?.AntiIceOn ?? false,
            SeatBeltSignOn = systems?.SeatBeltSignOn ?? false,
        };
    }

    private TimeSpan RecoveryDelay(int attempt)
    {
        double multiplier = Math.Pow(2, Math.Min(attempt, 30));
        double milliseconds = Math.Min(
            options.RecoveryMaximumDelay.TotalMilliseconds,
            options.RecoveryInitialDelay.TotalMilliseconds * multiplier);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private void FailConnection(
        TaskCompletionSource<SimulatorConnectionResult> connectionResult,
        SimulatorConnectionFailureKind failureKind,
        string reason)
    {
        connectionResult.TrySetResult(SimulatorConnectionResult.Failure(failureKind, reason));
        TransitionTo(SimulatorConnectionState.Faulted, reason);
    }

    private void TransitionTo(SimulatorConnectionState next, string? reason = null)
    {
        SimulatorConnectionState previous = (SimulatorConnectionState)Interlocked.Exchange(
            ref connectionState,
            (int)next);
        if (previous == next)
        {
            return;
        }

        ConnectionStateChanged?.Invoke(this, new(previous, next, DateTimeOffset.UtcNow, reason));
    }

    private void ResetCompletedWorker()
    {
        if (workerCompletion is not null && !workerCompletion.Task.IsCompleted)
        {
            return;
        }

        runCancellation?.Dispose();
        runCancellation = null;
        connectCompletion = null;
        workerCompletion = null;
    }

    private static string FirstError(SimConnectLibraryValidationResult validation) =>
        validation.Diagnostics.FirstOrDefault(static item => item.Severity == SimConnectDiagnosticSeverity.Error)?.Message
        ?? "The selected SimConnect library is unavailable.";

    private static void EnsureSucceeded(int hResult, string operation)
    {
        if (SimConnectConstants.Failed(hResult))
        {
            throw new SimConnectNativeCallException(operation, hResult);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private sealed record Definition(
        string Name,
        string? Unit,
        SimConnectDataType DataType = SimConnectDataType.Float64);
}

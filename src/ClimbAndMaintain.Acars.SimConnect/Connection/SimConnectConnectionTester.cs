using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ClimbAndMaintain.Acars.SimConnect.Configuration;
using ClimbAndMaintain.Acars.SimConnect.Diagnostics;
using ClimbAndMaintain.Acars.SimConnect.Interop;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Connection;

public sealed class SimConnectConnectionTester
{
    private const uint CoreDefinitionId = 0x434D0001;
    private const uint CoreRequestId = 0x434D0002;
    private const uint AircraftLoadedEventId = 0x434D0010;
    private const uint FlightLoadedEventId = 0x434D0011;
    private const int MaximumDispatchBytes = 1024 * 1024;

    private static readonly CoreDefinition[] CoreDefinitions =
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

    private readonly SimConnectLibraryValidator validator;
    private readonly ISimConnectNativeApiFactory? nativeApiFactory;
    private readonly bool platformSupported;

    public SimConnectConnectionTester()
        : this(
            new SimConnectLibraryValidator(),
            OperatingSystem.IsWindows() ? new Win32SimConnectNativeApiFactory() : null,
            OperatingSystem.IsWindows())
    {
    }

    internal SimConnectConnectionTester(
        SimConnectLibraryValidator validator,
        ISimConnectNativeApiFactory? nativeApiFactory,
        bool platformSupported)
    {
        this.validator = validator;
        this.nativeApiFactory = nativeApiFactory;
        this.platformSupported = platformSupported;
    }

    public async Task<SimConnectConnectionTestResult> TestAsync(
        SimConnectTarget target,
        string? libraryPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The connection timeout must be positive.");
        }

        _ = SimConnectCompatibility.ExpectedApplicationMajor(target);
        SimConnectLibraryValidationResult validation = await validator
            .ValidateAsync(libraryPath, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsCompatible || validation.Metadata is null)
        {
            return CreateResult(
                target,
                SimConnectConnectionTestOutcome.StaticValidationFailed,
                validation,
                validation.Diagnostics);
        }

        if (!platformSupported || nativeApiFactory is null)
        {
            SimConnectDiagnostic diagnostic = Error(
                SimConnectDiagnosticCodes.PlatformUnsupported,
                "A live SimConnect connection test can only run on Windows. Static library validation passed.");
            return CreateResult(
                target,
                SimConnectConnectionTestOutcome.PlatformUnsupported,
                validation,
                [.. validation.Diagnostics, diagnostic]);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        TaskCompletionSource<SimConnectConnectionTestResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(() => RunWorker(
            target,
            validation,
            timeout,
            completion,
            cancellationToken))
        {
            IsBackground = true,
            Name = $"SimConnect test ({target})",
        };
        worker.Start();
        return await completion.Task.ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The native worker boundary must always complete its task with diagnostics.")]
    private void RunWorker(
        SimConnectTarget target,
        SimConnectLibraryValidationResult validation,
        TimeSpan timeout,
        TaskCompletionSource<SimConnectConnectionTestResult> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            completion.TrySetResult(TestOnOwningThread(target, validation, timeout, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.Cancelled,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.ConnectionCancelled, "The connection test was cancelled.")]));
        }
        catch (Win32Exception exception)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.LibraryLoadFailed,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.LoadFailed, "Windows could not load the library or one of its dependencies.", exception.Message)]));
        }
        catch (EntryPointNotFoundException exception)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.LibraryLoadFailed,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.ExportBindingFailed, "A required export could not be bound.", exception.Message)]));
        }
        catch (BadImageFormatException exception)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.LibraryLoadFailed,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.LoadFailed, "Windows rejected the native library.", exception.Message)]));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.LibraryLoadFailed,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.LoadFailed, "The selected library could not be locked and revalidated for loading.", exception.Message)]));
        }
        catch (SimConnectNativeCallException exception)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.NativeCallFailed,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.NativeCallFailed, exception.Message, exception.Operation)]));
        }
        catch (InvalidOperationException exception)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.InvalidResponse,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.InvalidMessage, "The SimConnect response was invalid.", exception.Message)]));
        }
        catch (Exception exception)
        {
            completion.TrySetResult(CreateResult(
                target,
                SimConnectConnectionTestOutcome.NativeCallFailed,
                validation,
                [.. validation.Diagnostics, Error(SimConnectDiagnosticCodes.NativeCallFailed, "The native connection-test worker failed.", exception.Message)]));
        }
    }

    private SimConnectConnectionTestResult TestOnOwningThread(
        SimConnectTarget target,
        SimConnectLibraryValidationResult validation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using SimConnectLibraryLoadLease loadLease = validator.AcquireLoadLease(validation);
        validation = loadLease.Validation;
        if (!validation.IsCompatible || validation.Metadata is null)
        {
            return CreateResult(
                target,
                SimConnectConnectionTestOutcome.StaticValidationFailed,
                validation,
                validation.Diagnostics);
        }

        ISimConnectNativeApi api = nativeApiFactory!.Load(validation.Metadata!.CanonicalPath);
        using AutoResetEvent dispatchEvent = new(initialState: false);
        nint connection = 0;

        try
        {
            int openResult = api.Open(
                out connection,
                "Climb and Maintain ACARS connection test",
                dispatchEvent,
                SimConnectConstants.OpenConfigIndexLocal);
            if (SimConnectConstants.Failed(openResult) || connection == 0)
            {
                return CreateResult(
                    target,
                    SimConnectConnectionTestOutcome.SimulatorNotRunning,
                    validation,
                    [.. validation.Diagnostics, Error(
                        SimConnectDiagnosticCodes.SimulatorUnavailable,
                        "The library loaded, but a local simulator connection could not be opened.",
                        FormattableString.Invariant($"HRESULT 0x{openResult:X8}."))]);
            }

            return WaitForValidatedTelemetry(
                api,
                connection,
                dispatchEvent,
                target,
                validation,
                timeout,
                cancellationToken);
        }
        finally
        {
            if (connection != 0)
            {
                _ = api.Close(connection);
            }

            api.Dispose();
            GC.KeepAlive(dispatchEvent);
        }
    }

    private static SimConnectConnectionTestResult WaitForValidatedTelemetry(
        ISimConnectNativeApi api,
        nint connection,
        AutoResetEvent dispatchEvent,
        SimConnectTarget target,
        SimConnectLibraryValidationResult validation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        SimConnectOpenInfo? server = null;
        bool definitionsConfigured = false;

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining = timeout - stopwatch.Elapsed;
            int waitMilliseconds = (int)Math.Clamp(remaining.TotalMilliseconds, 1, 250);
            if (!dispatchEvent.WaitOne(waitMilliseconds))
            {
                continue;
            }

            while (true)
            {
                int dispatchResult = api.GetNextDispatch(connection, out nint data, out uint dataSize);
                EnsureSucceeded(dispatchResult, "SimConnect_GetNextDispatch");

                if (data == 0 && dataSize == 0)
                {
                    break;
                }

                if (data == 0 || dataSize < SimConnectReceiveParser.HeaderSize || dataSize > MaximumDispatchBytes)
                {
                    throw new InvalidOperationException($"The dispatch buffer pointer or size ({dataSize}) is invalid.");
                }

                int packetSize = checked((int)dataSize);
                byte[] packet = new byte[packetSize];
                Marshal.Copy(data, packet, 0, packetSize);
                if (!SimConnectReceiveParser.TryParseHeader(packet, out SimConnectReceiveHeader? header, out string? headerError))
                {
                    throw new InvalidOperationException(headerError);
                }

                if (header!.Id == SimConnectReceiveId.Null)
                {
                    break;
                }

                switch (header.Id)
                {
                    case SimConnectReceiveId.Open:
                        if (!SimConnectReceiveParser.TryParseOpen(packet, out SimConnectOpenMessage? open, out string? openError))
                        {
                            throw new InvalidOperationException(openError);
                        }

                        server = open!.Information;
                        if (!SimConnectCompatibility.IsExpectedSimulator(target, server))
                        {
                            return CreateResult(
                                target,
                                SimConnectConnectionTestOutcome.WrongSimulator,
                                validation,
                                [.. validation.Diagnostics, Error(
                                    SimConnectDiagnosticCodes.WrongSimulator,
                                    $"The selected library connected to application major {server.ApplicationVersionMajor}, not {SimConnectCompatibility.ExpectedApplicationMajor(target)}.",
                                    server.ApplicationName)],
                                server);
                        }

                        if (!definitionsConfigured)
                        {
                            ConfigureTelemetryTest(api, connection);
                            definitionsConfigured = true;
                        }

                        break;

                    case SimConnectReceiveId.Exception:
                        if (!SimConnectReceiveParser.TryParseException(packet, out SimConnectExceptionMessage? exception, out string? exceptionError))
                        {
                            throw new InvalidOperationException(exceptionError);
                        }

                        if (exception!.Exception == SimConnectExceptionCode.VersionMismatch)
                        {
                            return CreateResult(
                                target,
                                SimConnectConnectionTestOutcome.VersionMismatch,
                                validation,
                                [.. validation.Diagnostics, Error(
                                    SimConnectDiagnosticCodes.VersionMismatch,
                                    "The SimConnect client library and simulator server reported a version mismatch.",
                                    FormattableString.Invariant($"Send ID {exception.SendId}; parameter {exception.ParameterIndex}."))],
                                server);
                        }

                        return CreateResult(
                            target,
                            SimConnectConnectionTestOutcome.NativeCallFailed,
                            validation,
                            [.. validation.Diagnostics, Error(
                                SimConnectDiagnosticCodes.NativeCallFailed,
                                $"The simulator reported SimConnect exception {(uint)exception.Exception}.",
                                FormattableString.Invariant($"Send ID {exception.SendId}; parameter {exception.ParameterIndex}."))],
                            server);

                    case SimConnectReceiveId.SimObjectData:
                        if (server is null || !definitionsConfigured)
                        {
                            throw new InvalidOperationException("Telemetry arrived before the open handshake completed.");
                        }

                        if (!SimConnectReceiveParser.TryParseObjectData(packet, out SimConnectObjectDataMessage? objectData, out string? objectError))
                        {
                            throw new InvalidOperationException(objectError);
                        }

                        if (objectData!.RequestId != CoreRequestId || objectData.DefinitionId != CoreDefinitionId)
                        {
                            break;
                        }

                        if (!SimConnectReceiveParser.TryParseCoreTelemetry(objectData, out SimConnectCoreTelemetry? telemetry, out string? telemetryError))
                        {
                            return CreateResult(
                                target,
                                SimConnectConnectionTestOutcome.TelemetryFailed,
                                validation,
                                [.. validation.Diagnostics, Error(
                                    SimConnectDiagnosticCodes.TelemetryTestFailed,
                                    "The simulator returned an invalid core telemetry sample.",
                                    telemetryError)],
                                server);
                        }

                        SimConnectLibraryAttestation attestation = new()
                        {
                            Target = target,
                            CanonicalPath = validation.Metadata!.CanonicalPath,
                            Sha256 = validation.Metadata.Sha256,
                            FileLength = validation.Metadata.FileLength,
                            LastWriteTimeUtc = validation.Metadata.LastWriteTimeUtc,
                            Server = server,
                            Capabilities = SimConnectCapabilities.NativeCoreApi
                                | SimConnectCapabilities.CoreTelemetry
                                | SimConnectCapabilities.SystemEvents,
                            TestedAtUtc = DateTimeOffset.UtcNow,
                        };

                        return CreateResult(
                            target,
                            SimConnectConnectionTestOutcome.Succeeded,
                            validation,
                            [.. validation.Diagnostics, new(
                                SimConnectDiagnosticSeverity.Information,
                                SimConnectDiagnosticCodes.ConnectionSucceeded,
                                $"The library connected to {target} and returned core telemetry successfully.")],
                            server,
                            telemetry,
                            attestation);

                    case SimConnectReceiveId.Quit:
                        return CreateResult(
                            target,
                            SimConnectConnectionTestOutcome.SimulatorNotRunning,
                            validation,
                            [.. validation.Diagnostics, Error(
                                SimConnectDiagnosticCodes.SimulatorUnavailable,
                                "The simulator closed before the connection test completed.")],
                            server);
                }
            }
        }

        return CreateResult(
            target,
            SimConnectConnectionTestOutcome.TimedOut,
            validation,
            [.. validation.Diagnostics, Error(
                SimConnectDiagnosticCodes.ConnectionTimedOut,
                "The simulator did not complete the open and telemetry handshake before the timeout.")],
            server);
    }

    private static void ConfigureTelemetryTest(ISimConnectNativeApi api, nint connection)
    {
        foreach (CoreDefinition definition in CoreDefinitions)
        {
            EnsureSucceeded(
                api.AddToDataDefinition(
                    connection,
                    CoreDefinitionId,
                    definition.Name,
                    definition.Unit,
                    SimConnectDataType.Float64,
                    0,
                    SimConnectConstants.Unused),
                $"SimConnect_AddToDataDefinition({definition.Name})");
        }

        EnsureSucceeded(
            api.SubscribeToSystemEvent(connection, AircraftLoadedEventId, "AircraftLoaded"),
            "SimConnect_SubscribeToSystemEvent(AircraftLoaded)");
        EnsureSucceeded(
            api.SubscribeToSystemEvent(connection, FlightLoadedEventId, "FlightLoaded"),
            "SimConnect_SubscribeToSystemEvent(FlightLoaded)");
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
            "SimConnect_RequestDataOnSimObject");
    }

    private static void EnsureSucceeded(int hResult, string operation)
    {
        if (SimConnectConstants.Failed(hResult))
        {
            throw new SimConnectNativeCallException(operation, hResult);
        }
    }

    private static SimConnectConnectionTestResult CreateResult(
        SimConnectTarget target,
        SimConnectConnectionTestOutcome outcome,
        SimConnectLibraryValidationResult validation,
        IReadOnlyList<SimConnectDiagnostic> diagnostics,
        SimConnectOpenInfo? server = null,
        SimConnectCoreTelemetry? telemetry = null,
        SimConnectLibraryAttestation? attestation = null) => new()
        {
            Target = target,
            Outcome = outcome,
            LibraryValidation = validation,
            Diagnostics = diagnostics,
            Server = server,
            TelemetrySample = telemetry,
            Attestation = attestation,
        };

    private static SimConnectDiagnostic Error(string code, string message, string? detail = null) => new(
        SimConnectDiagnosticSeverity.Error,
        code,
        message,
        detail);

    private sealed record CoreDefinition(string Name, string Unit);
}

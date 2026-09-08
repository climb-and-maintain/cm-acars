using ClimbAndMaintain.Acars.SimConnect.Configuration;
using ClimbAndMaintain.Acars.SimConnect.Diagnostics;
using ClimbAndMaintain.Acars.SimConnect.Interop;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Connection;

public enum SimConnectConnectionTestOutcome
{
    Succeeded,
    StaticValidationFailed,
    PlatformUnsupported,
    LibraryLoadFailed,
    SimulatorNotRunning,
    TimedOut,
    WrongSimulator,
    VersionMismatch,
    InvalidResponse,
    TelemetryFailed,
    Cancelled,
    NativeCallFailed,
}

public sealed record SimConnectConnectionTestResult
{
    public required SimConnectTarget Target { get; init; }

    public required SimConnectConnectionTestOutcome Outcome { get; init; }

    public required SimConnectLibraryValidationResult LibraryValidation { get; init; }

    public SimConnectOpenInfo? Server { get; init; }

    public SimConnectCoreTelemetry? TelemetrySample { get; init; }

    public SimConnectLibraryAttestation? Attestation { get; init; }

    public required IReadOnlyList<SimConnectDiagnostic> Diagnostics { get; init; }

    public bool Succeeded => Outcome == SimConnectConnectionTestOutcome.Succeeded;
}

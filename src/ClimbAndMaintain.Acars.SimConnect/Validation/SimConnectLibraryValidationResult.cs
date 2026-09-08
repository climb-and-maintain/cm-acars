using ClimbAndMaintain.Acars.SimConnect.Diagnostics;

namespace ClimbAndMaintain.Acars.SimConnect.Validation;

public sealed record SimConnectLibraryValidationResult
{
    public required string? RequestedPath { get; init; }

    public SimConnectPeMetadata? Metadata { get; init; }

    public required IReadOnlyList<SimConnectDiagnostic> Diagnostics { get; init; }

    public bool IsCompatible => Metadata is not null
        && !Diagnostics.Any(static diagnostic => diagnostic.Severity == SimConnectDiagnosticSeverity.Error);
}

public static class SimConnectDiagnosticCodes
{
    public const string PathRequired = "SC-LIB-001";
    public const string PathMustBeAbsolute = "SC-LIB-002";
    public const string FileNotFound = "SC-LIB-003";
    public const string FileReadFailed = "SC-LIB-004";
    public const string InvalidPortableExecutable = "SC-LIB-005";
    public const string WrongArchitecture = "SC-LIB-006";
    public const string ManagedWrapperSelected = "SC-LIB-007";
    public const string MissingExport = "SC-LIB-008";
    public const string StaticValidationPassed = "SC-LIB-009";
    public const string PlatformUnsupported = "SC-RUNTIME-001";
    public const string LoadFailed = "SC-RUNTIME-002";
    public const string ExportBindingFailed = "SC-RUNTIME-003";
    public const string SimulatorUnavailable = "SC-CONNECTION-001";
    public const string ConnectionTimedOut = "SC-CONNECTION-002";
    public const string WrongSimulator = "SC-CONNECTION-003";
    public const string VersionMismatch = "SC-CONNECTION-004";
    public const string InvalidMessage = "SC-CONNECTION-005";
    public const string TelemetryTestFailed = "SC-CONNECTION-006";
    public const string ConnectionSucceeded = "SC-CONNECTION-007";
    public const string ConnectionCancelled = "SC-CONNECTION-008";
    public const string NativeCallFailed = "SC-CONNECTION-009";
}

namespace ClimbAndMaintain.Acars.SimConnect.Diagnostics;

public enum SimConnectDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record SimConnectDiagnostic(
    SimConnectDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Detail = null);

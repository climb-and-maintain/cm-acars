using System.ComponentModel;
using ClimbAndMaintain.Acars.SimConnect.Diagnostics;
using ClimbAndMaintain.Acars.SimConnect.Interop;

namespace ClimbAndMaintain.Acars.SimConnect.Validation;

public sealed record SimConnectRuntimeProbeResult
{
    public required SimConnectLibraryValidationResult StaticValidation { get; init; }

    public required IReadOnlyList<SimConnectDiagnostic> Diagnostics { get; init; }

    public bool Succeeded => StaticValidation.IsCompatible
        && !Diagnostics.Any(static diagnostic => diagnostic.Severity == SimConnectDiagnosticSeverity.Error);
}

public sealed class SimConnectRuntimeProbe(SimConnectLibraryValidator validator)
{
    public SimConnectRuntimeProbeResult Probe(string? path)
    {
        SimConnectLibraryValidationResult validation = validator.Validate(path);
        List<SimConnectDiagnostic> diagnostics = [.. validation.Diagnostics];
        if (!validation.IsCompatible || validation.Metadata is null)
        {
            return Result(validation, diagnostics);
        }

        if (!OperatingSystem.IsWindows())
        {
            diagnostics.Add(new(
                SimConnectDiagnosticSeverity.Error,
                SimConnectDiagnosticCodes.PlatformUnsupported,
                "Native SimConnect libraries can only be loaded on Windows. Static validation remains available."));
            return Result(validation, diagnostics);
        }

        try
        {
            using ISimConnectNativeApi api = Win32SimConnectNativeApi.Load(validation.Metadata.CanonicalPath);
            diagnostics.Add(new(
                SimConnectDiagnosticSeverity.Information,
                SimConnectDiagnosticCodes.StaticValidationPassed,
                "Windows loaded the native library and bound the required exports successfully."));
        }
        catch (Win32Exception exception)
        {
            diagnostics.Add(new(
                SimConnectDiagnosticSeverity.Error,
                SimConnectDiagnosticCodes.LoadFailed,
                "Windows could not load the native SimConnect library or one of its dependencies.",
                exception.Message));
        }
        catch (EntryPointNotFoundException exception)
        {
            diagnostics.Add(new(
                SimConnectDiagnosticSeverity.Error,
                SimConnectDiagnosticCodes.ExportBindingFailed,
                "The loaded library is missing a required SimConnect export.",
                exception.Message));
        }
        catch (BadImageFormatException exception)
        {
            diagnostics.Add(new(
                SimConnectDiagnosticSeverity.Error,
                SimConnectDiagnosticCodes.LoadFailed,
                "Windows rejected the selected native library.",
                exception.Message));
        }

        return Result(validation, diagnostics);
    }

    private static SimConnectRuntimeProbeResult Result(
        SimConnectLibraryValidationResult validation,
        IReadOnlyList<SimConnectDiagnostic> diagnostics) => new()
        {
            StaticValidation = validation,
            Diagnostics = diagnostics,
        };
}

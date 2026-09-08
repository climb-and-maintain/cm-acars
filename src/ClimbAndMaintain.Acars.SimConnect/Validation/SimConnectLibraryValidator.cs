using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using ClimbAndMaintain.Acars.SimConnect.Diagnostics;

namespace ClimbAndMaintain.Acars.SimConnect.Validation;

public sealed class SimConnectLibraryValidator
{
    private const ushort Amd64Machine = 0x8664;
    private const long MaximumLibraryBytes = 64L * 1024 * 1024;

    public Task<SimConnectLibraryValidationResult> ValidateAsync(
        string? path,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Validate(path);
        }, cancellationToken);

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The validator is an injectable service boundary and may gain policy dependencies without changing consumers.")]
    public SimConnectLibraryValidationResult Validate(string? path)
    {
        List<SimConnectDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(path))
        {
            diagnostics.Add(Error(SimConnectDiagnosticCodes.PathRequired, "Select a native SimConnect library."));
            return Result(path, null, diagnostics);
        }

        string trimmedPath = path.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            diagnostics.Add(Error(
                SimConnectDiagnosticCodes.PathMustBeAbsolute,
                "The SimConnect library path must be absolute.",
                trimmedPath));
            return Result(path, null, diagnostics);
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(trimmedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error(SimConnectDiagnosticCodes.PathMustBeAbsolute, "The library path is invalid.", exception.Message));
            return Result(path, null, diagnostics);
        }

        if (!File.Exists(canonicalPath))
        {
            diagnostics.Add(Error(SimConnectDiagnosticCodes.FileNotFound, "The selected SimConnect library does not exist.", canonicalPath));
            return Result(path, null, diagnostics);
        }

        byte[] image;
        FileInfo fileInfo = new(canonicalPath);
        try
        {
            if (fileInfo.Length <= 0 || fileInfo.Length > MaximumLibraryBytes)
            {
                diagnostics.Add(Error(
                    SimConnectDiagnosticCodes.FileReadFailed,
                    "The selected library size is outside the supported range of 1 byte through 64 MiB.",
                    canonicalPath));
                return Result(path, null, diagnostics);
            }

            image = File.ReadAllBytes(canonicalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(SimConnectDiagnosticCodes.FileReadFailed, "The selected library could not be read.", exception.Message));
            return Result(path, null, diagnostics);
        }

        try
        {
            using MemoryStream stream = new(image, writable: false);
            using PEReader reader = new(stream, PEStreamOptions.LeaveOpen);
            PEHeaders headers = reader.PEHeaders;
            PEHeader peHeader = headers.PEHeader
                ?? throw new BadImageFormatException("The file has no PE optional header.");

            ushort machine = (ushort)headers.CoffHeader.Machine;
            bool isPe32Plus = peHeader.Magic == PEMagic.PE32Plus;
            bool hasClrHeader = peHeader.CorHeaderTableDirectory.RelativeVirtualAddress != 0
                && peHeader.CorHeaderTableDirectory.Size != 0;
            IReadOnlyList<string> exports = PortableExecutableExports.Read(image, headers);
            string sha256 = Convert.ToHexString(SHA256.HashData(image));

            SimConnectPeMetadata metadata = new(
                canonicalPath,
                sha256,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                machine,
                isPe32Plus,
                hasClrHeader,
                exports);

            if (machine != Amd64Machine || !isPe32Plus)
            {
                diagnostics.Add(Error(
                    SimConnectDiagnosticCodes.WrongArchitecture,
                    "The selected library is not a native Windows x64 library.",
                    FormattableString.Invariant($"PE machine 0x{machine:X4}; PE32+={isPe32Plus}.")));
            }

            if (hasClrHeader)
            {
                string message = string.Equals(
                    Path.GetFileName(canonicalPath),
                    "Microsoft.FlightSimulator.SimConnect.dll",
                    StringComparison.OrdinalIgnoreCase)
                    ? "The selected file is Microsoft's managed wrapper. Select the native SimConnect library instead."
                    : "The selected file is a managed assembly, not a native SimConnect library.";
                diagnostics.Add(Error(SimConnectDiagnosticCodes.ManagedWrapperSelected, message, canonicalPath));
            }

            HashSet<string> actualExports = new(exports, StringComparer.Ordinal);
            foreach (string requiredExport in SimConnectExportSurface.RequiredExports)
            {
                if (!actualExports.Contains(requiredExport))
                {
                    diagnostics.Add(Error(
                        SimConnectDiagnosticCodes.MissingExport,
                        $"The native library does not export {requiredExport}.",
                        canonicalPath));
                }
            }

            if (diagnostics.Count == 0)
            {
                diagnostics.Add(new(
                    SimConnectDiagnosticSeverity.Information,
                    SimConnectDiagnosticCodes.StaticValidationPassed,
                    "The file is a native Windows x64 library with the required SimConnect exports."));
            }

            return Result(path, metadata, diagnostics);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(Error(
                SimConnectDiagnosticCodes.InvalidPortableExecutable,
                "The selected file is not a valid supported native library.",
                exception.Message));
            return Result(path, null, diagnostics);
        }
    }

    internal SimConnectLibraryLoadLease AcquireLoadLease(
        SimConnectLibraryValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        string canonicalPath = validation.Metadata?.CanonicalPath
            ?? throw new InvalidOperationException("A compatible static validation is required before acquiring a load lease.");
        FileStream stream = new(
            canonicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.SequentialScan);
        try
        {
            // Validate again after denying writes and deletes. The lease remains
            // open until the native module is unloaded by the caller.
            return new(stream, Validate(canonicalPath));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static SimConnectLibraryValidationResult Result(
        string? requestedPath,
        SimConnectPeMetadata? metadata,
        IReadOnlyList<SimConnectDiagnostic> diagnostics) => new()
        {
            RequestedPath = requestedPath,
            Metadata = metadata,
            Diagnostics = diagnostics,
        };

    private static SimConnectDiagnostic Error(string code, string message, string? detail = null) => new(
        SimConnectDiagnosticSeverity.Error,
        code,
        message,
        detail);
}

internal sealed class SimConnectLibraryLoadLease(
    FileStream stream,
    SimConnectLibraryValidationResult validation) : IDisposable
{
    private readonly FileStream stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public SimConnectLibraryValidationResult Validation { get; } =
        validation ?? throw new ArgumentNullException(nameof(validation));

    public void Dispose() => stream.Dispose();
}

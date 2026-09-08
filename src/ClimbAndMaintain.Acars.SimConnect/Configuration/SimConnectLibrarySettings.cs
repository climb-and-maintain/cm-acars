using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Configuration;

public sealed record SimConnectLibrarySettings
{
    public SimConnectLibrarySelection Msfs2020 { get; init; } = new();

    public SimConnectLibrarySelection Msfs2024 { get; init; } = new();

    public SimConnectLibrarySelection Get(SimConnectTarget target) => target switch
    {
        SimConnectTarget.Msfs2020 => Msfs2020,
        SimConnectTarget.Msfs2024 => Msfs2024,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported simulator target."),
    };

    public SimConnectLibrarySettings WithSelection(SimConnectTarget target, string? path)
    {
        SimConnectLibrarySelection selection = new()
        {
            Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
        };

        return target switch
        {
            SimConnectTarget.Msfs2020 => this with { Msfs2020 = selection },
            SimConnectTarget.Msfs2024 => this with { Msfs2024 = selection },
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported simulator target."),
        };
    }

    public SimConnectLibrarySettings WithAttestation(SimConnectLibraryAttestation attestation)
    {
        ArgumentNullException.ThrowIfNull(attestation);

        SimConnectLibrarySelection current = Get(attestation.Target);
        SimConnectLibrarySelection updated = current with
        {
            Path = attestation.CanonicalPath,
            Attestation = attestation,
        };

        return attestation.Target switch
        {
            SimConnectTarget.Msfs2020 => this with { Msfs2020 = updated },
            SimConnectTarget.Msfs2024 => this with { Msfs2024 = updated },
            _ => throw new ArgumentOutOfRangeException(nameof(attestation), attestation.Target, "Unsupported simulator target."),
        };
    }
}

public sealed record SimConnectLibrarySelection
{
    public string? Path { get; init; }

    public SimConnectLibraryAttestation? Attestation { get; init; }

    public bool HasCurrentAttestation(SimConnectPeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Attestation is not null && Attestation.Matches(metadata);
    }
}

public sealed record SimConnectLibraryAttestation
{
    public required SimConnectTarget Target { get; init; }

    public required string CanonicalPath { get; init; }

    public required string Sha256 { get; init; }

    public required long FileLength { get; init; }

    public required DateTimeOffset LastWriteTimeUtc { get; init; }

    public required SimConnectOpenInfo Server { get; init; }

    public required SimConnectCapabilities Capabilities { get; init; }

    public required DateTimeOffset TestedAtUtc { get; init; }

    public bool Matches(SimConnectPeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(CanonicalPath, metadata.CanonicalPath, pathComparison)
            && string.Equals(Sha256, metadata.Sha256, StringComparison.OrdinalIgnoreCase)
            && FileLength == metadata.FileLength
            && LastWriteTimeUtc == metadata.LastWriteTimeUtc;
    }
}

public sealed record SimConnectOpenInfo(
    string ApplicationName,
    uint ApplicationVersionMajor,
    uint ApplicationVersionMinor,
    uint ApplicationBuildMajor,
    uint ApplicationBuildMinor,
    uint SimConnectVersionMajor,
    uint SimConnectVersionMinor,
    uint SimConnectBuildMajor,
    uint SimConnectBuildMinor)
{
    public string ApplicationVersion => FormattableString.Invariant(
        $"{ApplicationVersionMajor}.{ApplicationVersionMinor}.{ApplicationBuildMajor}.{ApplicationBuildMinor}");

    public string SimConnectVersion => FormattableString.Invariant(
        $"{SimConnectVersionMajor}.{SimConnectVersionMinor}.{SimConnectBuildMajor}.{SimConnectBuildMinor}");
}

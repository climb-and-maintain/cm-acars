namespace ClimbAndMaintain.Acars.SimConnect.Validation;

public sealed record SimConnectPeMetadata(
    string CanonicalPath,
    string Sha256,
    long FileLength,
    DateTimeOffset LastWriteTimeUtc,
    ushort Machine,
    bool IsPe32Plus,
    bool HasClrHeader,
    IReadOnlyList<string> ExportNames);

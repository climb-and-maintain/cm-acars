using ClimbAndMaintain.Acars.Application.Contracts;
using System.IO;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class RecordedTelemetryFileService
{
    public static async ValueTask LoadAsync(
        IRecordedTelemetryProvider provider,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = new(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        await provider.LoadJsonLinesAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}

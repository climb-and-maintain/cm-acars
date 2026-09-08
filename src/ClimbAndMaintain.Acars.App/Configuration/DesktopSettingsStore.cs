using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClimbAndMaintain.Acars.App.Configuration;

public sealed class DesktopSettingsStore : IDisposable
{
    private const long MaximumSettingsBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;
    private readonly SemaphoreSlim access = new(1, 1);
    private bool disposed;

    public DesktopSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public string? LastLoadWarning { get; private set; }

    public string? LastLoadFailureType { get; private set; }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A damaged optional settings file must never prevent application startup.")]
    public async ValueTask<DesktopSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await access.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LastLoadWarning = null;
            LastLoadFailureType = null;
            if (!File.Exists(path))
            {
                return new();
            }

            try
            {
                if (new FileInfo(path).Length > MaximumSettingsBytes)
                {
                    throw new InvalidDataException("The settings file exceeds the 1 MiB safety limit.");
                }

                await using FileStream stream = File.OpenRead(path);
                DesktopSettings loaded = await JsonSerializer
                    .DeserializeAsync<DesktopSettings>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false) ?? new();
                return Normalize(loaded);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LastLoadFailureType = exception.GetType().Name;
                LastLoadWarning = $"Settings could not be read; safe defaults are active. {exception.Message}";
                return new();
            }
        }
        finally
        {
            access.Release();
        }
    }

    private static DesktopSettings Normalize(DesktopSettings loaded)
    {
        ClimbAndMaintain.Acars.SimConnect.Configuration.SimConnectLibrarySettings simConnect =
            loaded.SimConnect ?? new();
        return loaded with
        {
            PhpVmsBaseUrl = string.IsNullOrWhiteSpace(loaded.PhpVmsBaseUrl)
                ? "https://"
                : loaded.PhpVmsBaseUrl.Trim(),
            SimConnect = simConnect with
            {
                Msfs2020 = simConnect.Msfs2020 ?? new(),
                Msfs2024 = simConnect.Msfs2024 ?? new(),
            },
        };
    }

    public async ValueTask SaveAsync(DesktopSettings settings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        await access.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            access.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        access.Dispose();
    }
}

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed partial class SupportBundleService
{
    private const int MaximumLogFiles = 5;
    private const long MaximumLogBytes = 2 * 1024 * 1024;

    private readonly IDesktopFileDialogService fileDialog;
    private readonly string logDirectory;

    public SupportBundleService(IDesktopFileDialogService fileDialog, string logDirectory)
    {
        this.fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        this.logDirectory = Path.GetFullPath(logDirectory);
    }

    public static void CopyDiagnostics(string sanitizedDiagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sanitizedDiagnostics);
        Clipboard.SetText(sanitizedDiagnostics);
    }

    public async ValueTask<string?> ExportAsync(
        string sanitizedDiagnostics,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sanitizedDiagnostics);
        string? destination = fileDialog.SelectSupportBundleDestination();
        if (destination is null)
        {
            return null;
        }

        string fullDestination = Path.GetFullPath(destination);
        await using FileStream stream = new(
            fullDestination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true);
        ZipArchiveEntry diagnosticsEntry = archive.CreateEntry("diagnostics.txt", CompressionLevel.Optimal);
        await WriteEntryAsync(diagnosticsEntry, Sanitize(sanitizedDiagnostics), cancellationToken).ConfigureAwait(false);

        List<string> omittedLogs = [];
        if (Directory.Exists(logDirectory))
        {
            foreach (string logPath in Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(MaximumLogFiles))
            {
                try
                {
                    string log = await ReadTailAsync(logPath, cancellationToken).ConfigureAwait(false);
                    ZipArchiveEntry logEntry = archive.CreateEntry(
                        $"logs/{Path.GetFileName(logPath)}",
                        CompressionLevel.Optimal);
                    await WriteEntryAsync(logEntry, Sanitize(log), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    omittedLogs.Add(Path.GetFileName(logPath));
                }
            }
        }

        if (omittedLogs.Count > 0)
        {
            ZipArchiveEntry warningEntry = archive.CreateEntry("logs/OMITTED.txt", CompressionLevel.Optimal);
            await WriteEntryAsync(
                warningEntry,
                $"The following locked or inaccessible logs were omitted: {string.Join(", ", omittedLogs)}",
                cancellationToken).ConfigureAwait(false);
        }

        return fullDestination;
    }

    public void OpenLogFolder()
    {
        Directory.CreateDirectory(logDirectory);
        ProcessStartInfo startInfo = new()
        {
            FileName = logDirectory,
            UseShellExecute = true,
        };
        _ = Process.Start(startInfo);
    }

    private static async Task<string> ReadTailAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        if (stream.Length > MaximumLogBytes)
        {
            stream.Seek(-MaximumLogBytes, SeekOrigin.End);
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEntryAsync(
        ZipArchiveEntry entry,
        string content,
        CancellationToken cancellationToken)
    {
        await using Stream entryStream = entry.Open();
        await using StreamWriter writer = new(entryStream, new UTF8Encoding(false), leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string Sanitize(string value)
    {
        string sanitized = ApiKeyHeader().Replace(value, "$1[REDACTED]");
        sanitized = BearerHeader().Replace(sanitized, "$1[REDACTED]");
        sanitized = SecretAssignment().Replace(sanitized, "$1[REDACTED]");
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? sanitized
            : sanitized.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("(?i)(X-API-Key\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyHeader();

    [GeneratedRegex("(?i)(Authorization\\s*[:=]\\s*Bearer\\s+)[^\\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerHeader();

    [GeneratedRegex("(?i)((?:api[_-]?key|access[_-]?token|password)\\s*[:=]\\s*)[^\\s&,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignment();
}

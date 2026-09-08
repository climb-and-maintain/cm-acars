using Microsoft.Win32;

namespace ClimbAndMaintain.Acars.App.Services;

public interface IDesktopFileDialogService
{
    string? SelectNativeLibrary(string? currentPath);

    string? SelectTelemetryRecording();

    string? SelectSupportBundleDestination();
}

public sealed class DesktopFileDialogService : IDesktopFileDialogService
{
    public string? SelectNativeLibrary(string? currentPath)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select your native SimConnect library",
            Filter = "Native SimConnect library (*.dll)|*.dll|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            FileName = string.IsNullOrWhiteSpace(currentPath) ? string.Empty : currentPath,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectTelemetryRecording()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Open a recorded telemetry flight",
            Filter = "Telemetry recordings (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectSupportBundleDestination()
    {
        SaveFileDialog dialog = new()
        {
            Title = "Export sanitized support bundle",
            Filter = "ZIP archive (*.zip)|*.zip",
            AddExtension = true,
            DefaultExt = ".zip",
            FileName = $"cm-acars-support-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip",
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

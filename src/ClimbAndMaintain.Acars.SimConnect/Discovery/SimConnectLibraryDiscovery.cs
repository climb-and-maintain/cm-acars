using ClimbAndMaintain.Acars.SimConnect.Configuration;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Discovery;

public sealed record SimConnectLibraryCandidate(
    string Path,
    SimConnectTarget? TargetHint,
    string Source,
    SimConnectLibraryValidationResult Validation);

public sealed class SimConnectLibraryDiscovery(SimConnectLibraryValidator validator)
{
    private static readonly string[] RelativeNativeLibraryPaths =
    [
        "SimConnect.dll",
        Path.Combine("SimConnect SDK", "lib", "SimConnect.dll"),
    ];

    public Task<IReadOnlyList<SimConnectLibraryCandidate>> DiscoverAsync(
        SimConnectLibrarySettings settings,
        IEnumerable<string>? additionalSearchRoots = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string[]? roots = additionalSearchRoots?.ToArray();
        return Task.Run(
            () => Discover(settings, roots),
            cancellationToken);
    }

    public IReadOnlyList<SimConnectLibraryCandidate> Discover(
        SimConnectLibrarySettings settings,
        IEnumerable<string>? additionalSearchRoots = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        HashSet<string> seenPaths = new(pathComparer);
        List<SimConnectLibraryCandidate> candidates = [];

        AddConfigured(settings.Msfs2020, SimConnectTarget.Msfs2020, "MSFS 2020 settings", candidates, seenPaths);
        AddConfigured(settings.Msfs2024, SimConnectTarget.Msfs2024, "MSFS 2024 settings", candidates, seenPaths);
        AddEnvironmentRoot("MSFS2020_SDK_PATH", SimConnectTarget.Msfs2020, candidates, seenPaths);
        AddEnvironmentRoot("MSFS2024_SDK_PATH", SimConnectTarget.Msfs2024, candidates, seenPaths);
        AddEnvironmentRoot("MSFS_SDK_PATH", null, candidates, seenPaths);

        if (additionalSearchRoots is not null)
        {
            foreach (string root in additionalSearchRoots)
            {
                AddRoot(root, null, "Additional search location", candidates, seenPaths);
            }
        }

        candidates.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return candidates;
    }

    private void AddConfigured(
        SimConnectLibrarySelection selection,
        SimConnectTarget target,
        string source,
        ICollection<SimConnectLibraryCandidate> candidates,
        ISet<string> seenPaths)
    {
        if (!string.IsNullOrWhiteSpace(selection.Path))
        {
            AddFile(selection.Path, target, source, includeMissing: true, candidates, seenPaths);
        }
    }

    private void AddEnvironmentRoot(
        string variableName,
        SimConnectTarget? target,
        ICollection<SimConnectLibraryCandidate> candidates,
        ISet<string> seenPaths)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            AddRoot(value, target, $"Environment variable {variableName}", candidates, seenPaths);
        }
    }

    private void AddRoot(
        string root,
        SimConnectTarget? target,
        string source,
        ICollection<SimConnectLibraryCandidate> candidates,
        ISet<string> seenPaths)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        string trimmedRoot = root.Trim();
        if (Path.GetExtension(trimmedRoot).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            AddFile(trimmedRoot, target, source, includeMissing: false, candidates, seenPaths);
            return;
        }

        foreach (string relativePath in RelativeNativeLibraryPaths)
        {
            AddFile(Path.Combine(trimmedRoot, relativePath), target, source, includeMissing: false, candidates, seenPaths);
        }
    }

    private void AddFile(
        string path,
        SimConnectTarget? target,
        string source,
        bool includeMissing,
        ICollection<SimConnectLibraryCandidate> candidates,
        ISet<string> seenPaths)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if ((!includeMissing && !File.Exists(normalizedPath)) || !seenPaths.Add(normalizedPath))
        {
            return;
        }

        candidates.Add(new(
            normalizedPath,
            target,
            source,
            validator.Validate(normalizedPath)));
    }
}

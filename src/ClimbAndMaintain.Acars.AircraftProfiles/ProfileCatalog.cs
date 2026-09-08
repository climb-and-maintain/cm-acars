using System.Reflection;
using System.Text.Json;

namespace ClimbAndMaintain.Acars.AircraftProfiles;

public sealed class ProfileCatalog
{
    private readonly List<LoadedAircraftProfile> profiles = [];

    public IReadOnlyList<LoadedAircraftProfile> Profiles => profiles;

    public void Add(AircraftProfile profile, ProfileSource source, string origin)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var validation = ProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new InvalidDataException($"Invalid aircraft profile '{origin}': {string.Join(" ", validation.Errors)}");
        }

        if (profiles.Any(x => string.Equals(x.Profile.Meta.Id, profile.Meta.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Aircraft profile ID '{profile.Meta.Id}' is duplicated.");
        }

        profiles.Add(new(profile, source, origin));
    }

    public void LoadJson(string json, ProfileSource source, string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var profile = JsonSerializer.Deserialize(json, ProfileJsonContext.Default.AircraftProfile)
            ?? throw new InvalidDataException($"Aircraft profile '{origin}' was empty.");
        Add(profile, source, origin);
    }

    public async Task LoadDirectoryAsync(string directory, ProfileSource source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            LoadJson(json, source, path);
        }
    }

    public void LoadBundled(Assembly? assembly = null)
    {
        assembly ??= typeof(ProfileCatalog).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(x => x.Contains(".Profiles.", StringComparison.Ordinal) && x.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded profile '{name}' could not be opened.");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var source = name.Contains(".upstream.", StringComparison.OrdinalIgnoreCase)
                ? ProfileSource.UpstreamPhpVms
                : name.Contains(".bundled.universal", StringComparison.OrdinalIgnoreCase)
                    ? ProfileSource.BundledUniversal
                    : ProfileSource.ClimbAndMaintain;
            LoadJson(json, source, name);
        }
    }
}

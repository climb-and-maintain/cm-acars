using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace ClimbAndMaintain.Acars.App.Configuration;

public sealed record BrandingOptions
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string ProductName { get; init; } = "Climb and Maintain ACARS";

    public string ShortProductName { get; init; } = "C&M ACARS";

    public string Publisher { get; init; } = "Climb and Maintain, SPC";

    public string OriginalAuthor { get; init; } = "Climb and Maintain, SPC";

    public string ProductWebsite { get; init; } = "https://climbandmaintain.com";

    public string SourceRepository { get; init; } = "https://github.com/climb-and-maintain/cm-acars";

    public string SupportUrl { get; init; } = "https://github.com/climb-and-maintain/cm-acars/issues";

    public string DefaultAccent { get; init; } = "#005EA8";

    public string DefaultBackendUrl { get; init; } = "https://";

    public string AppId { get; init; } = "ClimbAndMaintain.Acars";

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Branding damage must not prevent access to diagnostics and configuration.")]
    public static BrandingOptions Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!File.Exists(path))
            {
                return new();
            }

            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<BrandingOptions>(stream, SerializerOptions)
                ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }
}

using System.Reflection;

namespace ClimbAndMaintain.Acars.App.Services;

public static class ApplicationBuildInfo
{
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationBuildInfo).Assembly;

    public static string Version { get; } =
        ApplicationAssembly.GetName().Version?.ToString() ?? "development";

    public static string Build { get; } = GetBuild();

    private static string GetBuild()
    {
        string? informationalVersion = ApplicationAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informationalVersion)
            ? Version
            : informationalVersion;
    }
}

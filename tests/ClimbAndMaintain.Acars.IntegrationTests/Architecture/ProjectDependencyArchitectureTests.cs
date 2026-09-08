using System.Xml.Linq;

namespace ClimbAndMaintain.Acars.IntegrationTests.Architecture;

public sealed class ProjectDependencyArchitectureTests
{
    [Fact]
    public void ProjectReferencesRespectLayerBoundaries()
    {
        string repositoryRoot = FindRepositoryRoot();
        Dictionary<string, string[]> allowedProjectReferences = new(StringComparer.Ordinal)
        {
            ["ClimbAndMaintain.Acars.Core"] = [],
            ["ClimbAndMaintain.Acars.Application"] = ["ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.AircraftProfiles"] = ["ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.Infrastructure"] =
                ["ClimbAndMaintain.Acars.Application", "ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.PhpVms"] =
                ["ClimbAndMaintain.Acars.Application", "ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.SimConnect"] =
                ["ClimbAndMaintain.Acars.Application", "ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.SimConnect.Probe"] = ["ClimbAndMaintain.Acars.SimConnect"],
        };

        foreach ((string project, string[] allowed) in allowedProjectReferences)
        {
            string projectFile = Path.Combine(repositoryRoot, "src", project, $"{project}.csproj");
            XDocument document = XDocument.Load(projectFile);
            string[] references = document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(static include => !string.IsNullOrWhiteSpace(include))
                .Select(static include => Path.GetFileNameWithoutExtension(
                    include!.Replace('\\', '/')))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(allowed.Order(StringComparer.Ordinal), references);
        }
    }

    [Fact]
    public void PresentationLayerDoesNotConstructTransportSimulatorOrDatabaseAdapters()
    {
        string presentationDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ClimbAndMaintain.Acars.App",
            "Presentation");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(presentationDirectory, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("System.Net.Http", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClimbAndMaintain.Acars.PhpVms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClimbAndMaintain.Acars.SimConnect.Provider", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}

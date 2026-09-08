using System.Xml.Linq;

namespace ClimbAndMaintain.Acars.UITests;

public sealed class ProjectDependencyBoundaryTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ApprovedReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ClimbAndMaintain.Acars.Core"] = [],
            ["ClimbAndMaintain.Acars.Application"] = ["ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.AircraftProfiles"] =
                ["ClimbAndMaintain.Acars.Application", "ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.Infrastructure"] =
                ["ClimbAndMaintain.Acars.Application", "ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.PhpVms"] =
                ["ClimbAndMaintain.Acars.Application", "ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.SimConnect"] =
                ["ClimbAndMaintain.Acars.Application", "ClimbAndMaintain.Acars.Core"],
            ["ClimbAndMaintain.Acars.SimConnect.Probe"] = ["ClimbAndMaintain.Acars.SimConnect"],
            ["ClimbAndMaintain.Acars.App"] =
            [
                "ClimbAndMaintain.Acars.Application",
                "ClimbAndMaintain.Acars.AircraftProfiles",
                "ClimbAndMaintain.Acars.Core",
                "ClimbAndMaintain.Acars.Infrastructure",
                "ClimbAndMaintain.Acars.PhpVms",
                "ClimbAndMaintain.Acars.SimConnect",
            ],
        };

    private static readonly string[] TestOnlyPackagePrefixes =
        ["coverlet.", "FlaUI.", "Microsoft.NET.Test.Sdk", "NSubstitute", "xunit"];

    private static readonly string[] AuthoredProjectDirectories = ["src", "tests"];

    [Fact]
    public void EveryProductionProjectHasAnExplicitDependencyPolicy()
    {
        IReadOnlyDictionary<string, ProjectModel> projects = LoadProductionProjects();
        string[] expected = ApprovedReferences.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        string[] actual = projects.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProductionProjectReferencesRespectApprovedDirectionAndAreUnconditional()
    {
        IReadOnlyDictionary<string, ProjectModel> projects = LoadProductionProjects();

        foreach ((string projectName, ProjectModel project) in projects)
        {
            string[] approved = ApprovedReferences[projectName];
            foreach (ProjectReferenceModel reference in project.ProjectReferences)
            {
                Assert.True(
                    File.Exists(reference.AbsolutePath),
                    $"{projectName} references a missing project: {reference.AbsolutePath}");
                Assert.Contains(reference.ProjectName, approved);
                Assert.False(
                    IsConditional(reference.Element),
                    $"{projectName} conditionally references {reference.ProjectName}; architecture boundaries must not vary by build environment.");
            }
        }
    }

    [Fact]
    public void CoreIsBclOnlyAndProductionProjectsDoNotReferenceTestPackages()
    {
        IReadOnlyDictionary<string, ProjectModel> projects = LoadProductionProjects();
        ProjectModel core = projects["ClimbAndMaintain.Acars.Core"];

        Assert.Empty(core.ProjectReferences);
        Assert.Empty(core.PackageReferences);

        foreach ((string projectName, ProjectModel project) in projects)
        {
            foreach (string package in project.PackageReferences)
            {
                bool isTestPackage = TestOnlyPackagePrefixes.Any(prefix =>
                    package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                Assert.False(isTestPackage, $"Production project {projectName} references test-only package {package}.");
            }
        }
    }

    [Fact]
    public void SimConnectAdapterIsAlwaysComposedWithoutSdkBuildInputs()
    {
        IReadOnlyDictionary<string, ProjectModel> projects = LoadProductionProjects();
        ProjectModel app = projects["ClimbAndMaintain.Acars.App"];
        ProjectReferenceModel simulatorReference = Assert.Single(
            app.ProjectReferences,
            static reference => reference.ProjectName == "ClimbAndMaintain.Acars.SimConnect");

        Assert.False(IsConditional(simulatorReference.Element));

        IEnumerable<string> centralBuildFiles = Directory
            .EnumerateFiles(RepositoryLayout.Root, "*", SearchOption.TopDirectoryOnly)
            .Where(static path =>
                path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
        string buildMetadata = string.Join(
            Environment.NewLine,
            projects.Values.Select(static project => project.Path).Concat(centralBuildFiles).Select(File.ReadAllText));
        string[] prohibitedTerms =
        [
            "EnableMsfsSdk",
            "Microsoft.FlightSimulator.SimConnect",
            "MSFS2020_SDK_PATH",
            "MSFS2024_SDK_PATH",
            "MSFS_SDK_PATH",
        ];

        foreach (string prohibitedTerm in prohibitedTerms)
        {
            Assert.False(
                buildMetadata.Contains(prohibitedTerm, StringComparison.OrdinalIgnoreCase),
                $"Production build metadata contains prohibited simulator SDK input: {prohibitedTerm}");
        }
    }

    [Fact]
    public void SolutionIncludesEverySourceAndTestProject()
    {
        string solutionPath = RepositoryLayout.FromRoot("ClimbAndMaintain.Acars.slnx");
        XDocument solution = XDocument.Load(solutionPath);
        HashSet<string> included = solution
            .Descendants()
            .Where(static element => element.Name.LocalName == "Project")
            .Select(static element => (string?)element.Attribute("Path"))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => NormalizeRelativePath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] expected = AuthoredProjectDirectories
            .SelectMany(directory => EnumerateAuthoredProjects(RepositoryLayout.FromRoot(directory)))
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(RepositoryLayout.Root, path)))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] missing = expected.Where(path => !included.Contains(path)).ToArray();

        Assert.True(missing.Length == 0, $"Projects missing from ClimbAndMaintain.Acars.slnx: {string.Join(", ", missing)}");
    }

    private static Dictionary<string, ProjectModel> LoadProductionProjects()
    {
        return EnumerateAuthoredProjects(RepositoryLayout.FromRoot("src"))
            .Select(LoadProject)
            .ToDictionary(static project => project.Name, StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateAuthoredProjects(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !Path.GetFileNameWithoutExtension(path)
                .EndsWith("_wpftmp", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetRelativePath(directory, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(static segment =>
                    string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)));
    }

    private static ProjectModel LoadProject(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        string projectDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Project path has no parent directory: {fullPath}");
        XDocument document = XDocument.Load(fullPath);
        ProjectReferenceModel[] projectReferences = document
            .Descendants()
            .Where(static element => element.Name.LocalName == "ProjectReference")
            .Select(element =>
            {
                string include = (string?)element.Attribute("Include")
                    ?? throw new InvalidOperationException($"ProjectReference in {fullPath} has no Include attribute.");
                string portableInclude = include.Replace('\\', Path.DirectorySeparatorChar);
                string absolutePath = Path.GetFullPath(portableInclude, projectDirectory);
                return new ProjectReferenceModel(
                    Path.GetFileNameWithoutExtension(absolutePath),
                    absolutePath,
                    element);
            })
            .ToArray();
        string[] packageReferences = document
            .Descendants()
            .Where(static element => element.Name.LocalName == "PackageReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!)
            .ToArray();

        return new ProjectModel(
            Path.GetFileNameWithoutExtension(fullPath),
            fullPath,
            projectReferences,
            packageReferences);
    }

    private static bool IsConditional(XElement item)
    {
        return item
            .AncestorsAndSelf()
            .TakeWhile(static element => element.Name.LocalName != "Project")
            .Any(static element => element.Attribute("Condition") is not null);
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private sealed record ProjectModel(
        string Name,
        string Path,
        IReadOnlyList<ProjectReferenceModel> ProjectReferences,
        IReadOnlyList<string> PackageReferences);

    private sealed record ProjectReferenceModel(string ProjectName, string AbsolutePath, XElement Element);
}

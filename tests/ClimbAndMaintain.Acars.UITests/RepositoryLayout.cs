namespace ClimbAndMaintain.Acars.UITests;

internal static class RepositoryLayout
{
    private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);

    public static string Root => RepositoryRoot.Value;

    public static string FromRoot(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments.Aggregate(Root, Path.Combine);
    }

    private static string FindRepositoryRoot()
    {
        foreach (string seed in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? directory = new(seed);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ClimbAndMaintain.Acars.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate ClimbAndMaintain.Acars.slnx from the test output or current directory.");
    }
}

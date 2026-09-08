using Microsoft.Data.Sqlite;

namespace ClimbAndMaintain.Acars.IntegrationTests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cm-acars-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}

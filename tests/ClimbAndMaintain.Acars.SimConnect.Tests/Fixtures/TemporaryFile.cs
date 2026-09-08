namespace ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;

internal sealed class TemporaryFile : IDisposable
{
    public TemporaryFile(byte[] content, string fileName = "SimConnect.dll")
    {
        DirectoryPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"cm-acars-simconnect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        Path = System.IO.Path.Combine(DirectoryPath, fileName);
        File.WriteAllBytes(Path, content);
    }

    public string DirectoryPath { get; }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

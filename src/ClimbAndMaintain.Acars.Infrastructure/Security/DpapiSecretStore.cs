using System.Security.Cryptography;
using System.Text;
using ClimbAndMaintain.Acars.Application.Contracts;

namespace ClimbAndMaintain.Acars.Infrastructure.Security;

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string directoryPath;
    private readonly ISecretProtector protector;

    public DpapiSecretStore(string directoryPath)
        : this(directoryPath, new WindowsDpapiProtector())
    {
    }

    internal DpapiSecretStore(string directoryPath, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(protector);
        this.directoryPath = Path.GetFullPath(directoryPath);
        this.protector = protector;
    }

    public static bool IsSupported => OperatingSystem.IsWindows();

    public async ValueTask<string?> GetSecretAsync(string name, CancellationToken cancellationToken)
    {
        protector.EnsureSupported();
        string path = GetSecretPath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        byte[]? plaintextBytes = null;
        try
        {
            plaintextBytes = protector.Unprotect(protectedBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plaintextBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }
        }
    }

    public async ValueTask SetSecretAsync(
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        protector.EnsureSupported();
        ArgumentException.ThrowIfNullOrEmpty(value);
        string path = GetSecretPath(name);
        Directory.CreateDirectory(directoryPath);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(value);
        byte[]? protectedBytes = null;
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            protectedBytes = protector.Protect(plaintextBytes);
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public ValueTask DeleteSecretAsync(string name, CancellationToken cancellationToken)
    {
        protector.EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetSecretPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return ValueTask.CompletedTask;
    }

    private string GetSecretPath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name.Trim());
        try
        {
            string fileName = Convert.ToHexStringLower(SHA256.HashData(nameBytes)) + ".secret";
            return Path.Combine(directoryPath, fileName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nameBytes);
        }
    }
}

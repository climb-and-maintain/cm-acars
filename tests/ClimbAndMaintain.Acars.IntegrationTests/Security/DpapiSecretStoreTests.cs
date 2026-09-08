using System.Text;
using ClimbAndMaintain.Acars.Infrastructure.Security;

namespace ClimbAndMaintain.Acars.IntegrationTests.Security;

public sealed class DpapiSecretStoreTests
{
    [Fact]
    public async Task SecretStoreNeverPersistsPlaintext()
    {
        using TemporaryDirectory temporaryDirectory = new();
        const string secret = "phpvms-super-secret-api-key";
        DpapiSecretStore store = new(temporaryDirectory.Path, new TestSecretProtector());

        await store.SetSecretAsync("phpvms-api-key", secret, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        string secretFile = Assert.Single(Directory.GetFiles(temporaryDirectory.Path, "*.secret"));
        byte[] persistedBytes = await File
            .ReadAllBytesAsync(secretFile, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(persistedBytes), StringComparison.Ordinal);

        DpapiSecretStore reopenedStore = new(temporaryDirectory.Path, new TestSecretProtector());
        Assert.Equal(
            secret,
            await reopenedStore.GetSecretAsync("phpvms-api-key", TestContext.Current.CancellationToken)
                .ConfigureAwait(true));

        await reopenedStore.DeleteSecretAsync("phpvms-api-key", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Null(await reopenedStore
            .GetSecretAsync("phpvms-api-key", TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
    }

    [Fact]
    public async Task ProductionStoreFailsExplicitlyOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporaryDirectory = new();
        DpapiSecretStore store = new(temporaryDirectory.Path);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await store.SetSecretAsync("key", "value", TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ProductionStoreUsesCurrentUserDpapiOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporaryDirectory = new();
        const string secret = "windows-dpapi-integration-secret";
        DpapiSecretStore store = new(temporaryDirectory.Path);

        await store.SetSecretAsync("credential", secret, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(
            secret,
            await store.GetSecretAsync("credential", TestContext.Current.CancellationToken)
                .ConfigureAwait(true));

        string secretFile = Assert.Single(Directory.GetFiles(temporaryDirectory.Path, "*.secret"));
        string persisted = Encoding.UTF8.GetString(await File
            .ReadAllBytesAsync(secretFile, TestContext.Current.CancellationToken)
            .ConfigureAwait(true));
        Assert.DoesNotContain(secret, persisted, StringComparison.Ordinal);
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        private const byte Mask = 0xA5;

        public void EnsureSupported()
        {
        }

        public byte[] Protect(ReadOnlySpan<byte> plaintext) => Transform(plaintext);

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => Transform(protectedData);

        private static byte[] Transform(ReadOnlySpan<byte> input)
        {
            byte[] result = input.ToArray();
            for (int index = 0; index < result.Length; index++)
            {
                result[index] ^= Mask;
            }

            return result;
        }
    }
}

namespace ClimbAndMaintain.Acars.Infrastructure.Security;

internal interface ISecretProtector
{
    void EnsureSupported();

    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

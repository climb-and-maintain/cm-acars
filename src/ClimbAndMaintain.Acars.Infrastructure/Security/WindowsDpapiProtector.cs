using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClimbAndMaintain.Acars.Infrastructure.Security;

internal sealed partial class WindowsDpapiProtector : ISecretProtector
{
    private const uint CryptProtectUiForbidden = 0x1;

    public void EnsureSupported()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Data Protection API secret storage is only available on Windows.");
        }
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        EnsureSupported();
        return Transform(plaintext, protect: true);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        EnsureSupported();
        return Transform(protectedData, protect: false);
    }

    private static byte[] Transform(ReadOnlySpan<byte> input, bool protect)
    {
        IntPtr inputBuffer = Marshal.AllocHGlobal(input.Length);
        DataBlob output = default;
        IntPtr description = IntPtr.Zero;
        try
        {
            byte[] inputArray = input.ToArray();
            try
            {
                Marshal.Copy(inputArray, 0, inputBuffer, inputArray.Length);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(inputArray);
            }

            DataBlob inputBlob = new(input.Length, inputBuffer);
            bool succeeded = protect
                ? NativeMethods.CryptProtectData(
                    ref inputBlob,
                    "Climb and Maintain ACARS secret",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : NativeMethods.CryptUnprotectData(
                    ref inputBlob,
                    out description,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);

            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            byte[] result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (inputBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(inputBuffer);
            }

            if (output.Data != IntPtr.Zero)
            {
                _ = NativeMethods.LocalFree(output.Data);
            }

            if (description != IntPtr.Zero)
            {
                _ = NativeMethods.LocalFree(description);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob
    {
        public DataBlob(int size, IntPtr data)
        {
            Size = size;
            Data = data;
        }

        public int Size { get; }

        public IntPtr Data { get; }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("crypt32.dll", EntryPoint = "CryptProtectData", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [LibraryImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CryptUnprotectData(
            ref DataBlob dataIn,
            out IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [LibraryImport("kernel32.dll", EntryPoint = "LocalFree")]
        internal static partial IntPtr LocalFree(IntPtr memory);
    }
}

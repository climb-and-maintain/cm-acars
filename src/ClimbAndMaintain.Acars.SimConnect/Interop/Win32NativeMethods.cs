using System.Runtime.InteropServices;

namespace ClimbAndMaintain.Acars.SimConnect.Interop;

internal static partial class Win32NativeMethods
{
    internal const uint LoadLibrarySearchDllLoadDirectory = 0x00000100;
    internal const uint LoadLibrarySearchSystem32 = 0x00000800;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint LoadLibraryEx(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(nint module, string procedureName);

    [LibraryImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FreeLibrary(nint module);
}

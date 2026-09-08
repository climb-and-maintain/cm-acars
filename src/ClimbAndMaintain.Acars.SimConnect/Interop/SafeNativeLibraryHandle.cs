using Microsoft.Win32.SafeHandles;

namespace ClimbAndMaintain.Acars.SimConnect.Interop;

internal sealed class SafeNativeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeNativeLibraryHandle()
        : base(ownsHandle: true)
    {
    }

    public SafeNativeLibraryHandle(nint handle)
        : this()
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => Win32NativeMethods.FreeLibrary(handle);
}

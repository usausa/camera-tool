namespace CameraTool.Capture;

using System.Runtime.InteropServices;

using Windows.Foundation;

using WinRT;

internal static unsafe class MemoryBufferAccessor
{
    private static readonly Guid IidMemoryBufferByteAccess = new("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d");

    // IMemoryBufferByteAccess::GetBuffer without RCW
    public static bool TryGetBuffer(IMemoryBufferReference reference, out IntPtr buffer, out int capacity)
    {
        buffer = IntPtr.Zero;
        capacity = 0;

        var unknown = MarshalInspectable<IMemoryBufferReference>.FromManaged(reference);
        try
        {
            if (Marshal.QueryInterface(unknown, in IidMemoryBufferByteAccess, out var access) < 0)
            {
                return false;
            }

            try
            {
                byte* data;
                uint size;
                if (((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, int>)(*(void***)access)[3])(access, &data, &size) < 0)
                {
                    return false;
                }

                buffer = (IntPtr)data;
                capacity = (int)size;
                return true;
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}

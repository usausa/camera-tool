namespace CameraTool.Capture;

using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Windows.Media.Capture.Frames;

using WinRT;

// Receives MediaFrameReader.FrameArrived through the COM vtable.
// The projection creates wrapper objects for the event arguments on every frame (about 440 bytes).
internal sealed unsafe class FrameArrivedSubscription : IDisposable
{
    // IInspectable based interface starts at slot 6
    private const int AddFrameArrivedSlot = 6;
    private const int RemoveFrameArrivedSlot = 7;

    private static readonly Guid IidMediaFrameReader = new("e4c94395-2028-48ed-90b0-d1c1b162e24c");

    // TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs>
    private static readonly Guid IidHandler = new("d3dd49cb-8d25-591a-80f7-8363d5c03ec9");

    private readonly Action<IntPtr> callback;

    private IntPtr reader;

    private long token;

    private bool subscribed;

    private FrameArrivedSubscription(Action<IntPtr> callback)
    {
        this.callback = callback;
    }

    // The callback receives IMediaFrameReader on the event thread
    public static FrameArrivedSubscription Subscribe(MediaFrameReader reader, Action<IntPtr> callback)
    {
        var subscription = new FrameArrivedSubscription(callback);
        try
        {
            subscription.Add(((IWinRTObject)reader).NativeObject.ThisPtr);
            return subscription;
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (reader == IntPtr.Zero)
        {
            return;
        }

        if (subscribed)
        {
            // IMediaFrameReader.remove_FrameArrived
            ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(reader)[RemoveFrameArrivedSlot])(reader, token);
            subscribed = false;
        }

        Marshal.Release(reader);
        reader = IntPtr.Zero;
    }

    private void Add(IntPtr inspectable)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in IidMediaFrameReader, out reader));

        var unknown = HandlerWrappers.Instance.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in IidHandler, out var handler));
            try
            {
                // IMediaFrameReader.add_FrameArrived (the event source keeps a reference to the handler)
                long value;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)Vtbl(reader)[AddFrameArrivedSlot])(reader, handler, &value));
                token = value;
                subscribed = true;
            }
            finally
            {
                Marshal.Release(handler);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private static void** Vtbl(IntPtr pointer) => *(void***)pointer;

    // TypedEventHandler.Invoke
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Invoke(IntPtr self, IntPtr sender, IntPtr args)
    {
        // MediaFrameArrivedEventArgs has no members
        _ = args;
        try
        {
            ComWrappers.ComInterfaceDispatch.GetInstance<FrameArrivedSubscription>((ComWrappers.ComInterfaceDispatch*)self).callback(sender);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Exceptions must not reach the native caller (same as the projection)
            return ex.HResult;
        }
    }

    // CCW of the handler (the handler interface and IAgileObject)
    private sealed class HandlerWrappers : ComWrappers
    {
        public static readonly HandlerWrappers Instance = new();

        private static readonly Guid IidAgileObject = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");

        private static readonly ComInterfaceEntry* Entries = CreateEntries();

        private const int EntryCount = 2;

        private static ComInterfaceEntry* CreateEntries()
        {
            GetIUnknownImpl(out var queryInterface, out var addRef, out var release);

            var handler = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(HandlerWrappers), IntPtr.Size * 4);
            handler[0] = queryInterface;
            handler[1] = addRef;
            handler[2] = release;
            handler[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)&Invoke;

            var agile = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(HandlerWrappers), IntPtr.Size * 3);
            agile[0] = queryInterface;
            agile[1] = addRef;
            agile[2] = release;

            var entries = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(HandlerWrappers), sizeof(ComInterfaceEntry) * EntryCount);
            entries[0].IID = IidHandler;
            entries[0].Vtable = (IntPtr)handler;
            entries[1].IID = IidAgileObject;
            entries[1].Vtable = (IntPtr)agile;
            return entries;
        }

        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            count = EntryCount;
            return Entries;
        }

        protected override object CreateObject(IntPtr externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();

        protected override void ReleaseObjects(IEnumerable objects) => throw new NotSupportedException();
    }
}

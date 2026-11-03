namespace CameraTool.Vision;

using System.Globalization;
using System.Runtime.InteropServices;

// Media Foundation called through the COM vtable (only what the H.264 encoder uses)
internal static unsafe partial class MediaFoundation
{
    public const uint Version = 0x00020070;

    public const uint StartupLite = 1;

    public const uint CoInitMultithreaded = 0;

    // HRESULT
    public const int TransformNeedMoreInput = unchecked((int)0xC00D6D72);

    public const int TransformStreamChange = unchecked((int)0xC00D6D61);

    public const int NotAccepting = unchecked((int)0xC00D36B5);

    public const int NoEventsAvailable = unchecked((int)0xC00D3E80);

    public const int NoInterface = unchecked((int)0x80004002);

    // MFT_ENUM_FLAG
    public const uint EnumSync = 0x1;

    public const uint EnumAsync = 0x2;

    public const uint EnumHardware = 0x4;

    public const uint EnumLocal = 0x10;

    public const uint EnumSortAndFilter = 0x40;

    // MFT_MESSAGE_TYPE
    public const uint CommandDrain = 1;

    public const uint NotifyBeginStreaming = 0x10000000;

    public const uint NotifyEndStreaming = 0x10000001;

    public const uint NotifyEndOfStream = 0x10000002;

    public const uint NotifyStartOfStream = 0x10000003;

    // MFT_OUTPUT_STREAM_INFO flags
    public const uint OutputProvidesSamples = 0x100;

    public const uint OutputCanProvideSamples = 0x200;

    // MediaEventType
    public const uint EventError = 1;

    public const uint EventNeedInput = 601;

    public const uint EventHaveOutput = 602;

    public const uint EventDrainComplete = 603;

    // MFVideoInterlace_Progressive
    public const uint Progressive = 2;

    // MFNominalRange
    public const uint NominalRangeFull = 1;

    public const uint NominalRangeVideo = 2;

    // MFVideoTransferMatrix_BT601
    public const uint MatrixBt601 = 2;

    // MFVideoPrimaries_BT709
    public const uint PrimariesBt709 = 2;

    // MFVideoTransFunc_709 (the encoder writes no transfer for MFVideoTransFunc_sRGB)
    public const uint TransferBt709 = 5;

    // eAVEncH264VProfile_Main
    public const uint ProfileMain = 77;

    // IMFAttributes
    private const int GetUInt32Slot = 7;
    private const int GetGuidSlot = 10;
    private const int GetAllocatedStringSlot = 13;
    private const int SetUInt32Slot = 21;
    private const int SetUInt64Slot = 22;
    private const int SetGuidSlot = 24;
    // IMFActivate
    private const int ActivateObjectSlot = 33;
    private const int ShutdownObjectSlot = 34;
    // IMFTransform
    private const int GetOutputStreamInfoSlot = 7;
    private const int GetAttributesSlot = 8;
    private const int GetInputAvailableTypeSlot = 13;
    private const int GetOutputAvailableTypeSlot = 14;
    private const int SetInputTypeSlot = 15;
    private const int SetOutputTypeSlot = 16;
    private const int ProcessMessageSlot = 23;
    private const int ProcessInputSlot = 24;
    private const int ProcessOutputSlot = 25;
    // IMFSample
    private const int GetSampleTimeSlot = 35;
    private const int SetSampleTimeSlot = 36;
    private const int SetSampleDurationSlot = 38;
    private const int ConvertToContiguousBufferSlot = 41;
    private const int AddBufferSlot = 42;
    // IMFMediaBuffer
    private const int LockSlot = 3;
    private const int UnlockSlot = 4;
    private const int SetCurrentLengthSlot = 6;
    // IMFMediaEventGenerator
    private const int GetEventSlot = 3;
    // IMFMediaEvent
    private const int GetTypeSlot = 33;
    private const int GetStatusSlot = 35;

    // MF_EVENT_FLAG_NO_WAIT
    private const uint EventNoWait = 1;
    // ICodecAPI
    private const int SetValueSlot = 9;
    // IMFShutdown
    private const int ShutdownSlot = 3;

    private const ushort VariantUInt32 = 19;
    private const ushort VariantBool = 11;

    public static readonly Guid CategoryVideoEncoder = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid FormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid FormatNv12 = new("3231564e-0000-0010-8000-00aa00389b71");

    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid AverageBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid NominalRange = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    public static readonly Guid YuvMatrix = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static readonly Guid VideoPrimaries = new("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
    public static readonly Guid TransferFunction = new("5fb0fce9-be5c-4935-a811-ec838f8eed93");

    public static readonly Guid FriendlyName = new("314ffbae-5b41-4c95-9c19-4e7d586face3");
    public static readonly Guid TransformAsync = new("f81a699a-649a-497d-8c73-29f8fed6ad7a");
    public static readonly Guid TransformAsyncUnlock = new("e5666d6b-3422-4eb6-a421-da7db1f8e207");
    public static readonly Guid CleanPoint = new("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");

    // ICodecAPI properties
    public static readonly Guid LowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    public static readonly Guid RateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    public static readonly Guid MeanBitrate = new("f7222374-2144-4815-b550-a37f8e12ee52");
    public static readonly Guid GopSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    public static readonly Guid BPictureCount = new("8d390aac-dc5c-4200-b57f-814d04babab2");
    public static readonly Guid ForceKeyFrame = new("398c1b98-8353-475a-9ef2-8f265d260345");

    public static readonly Guid IidTransform = new("bf94c121-5b05-4e6f-8000-ba598961414d");
    public static readonly Guid IidCodecApi = new("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");
    public static readonly Guid IidEventGenerator = new("2cd0bd52-bcd5-4b89-b62c-eadc0c031e7d");
    public static readonly Guid IidShutdown = new("97ec2ea4-0e42-4937-97ac-9d6d328824e1");

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static void** Vtbl(IntPtr pointer) => *(void***)pointer;

    public static void Release(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.Release(pointer);
        }
    }

    // Zero when the interface is not supported
    public static IntPtr QueryInterface(IntPtr pointer, in Guid iid) =>
        Marshal.QueryInterface(pointer, in iid, out var result) < 0 ? IntPtr.Zero : result;

    // Every failure is an InvalidOperationException with the HRESULT
    // (Marshal.ThrowExceptionForHR maps some HRESULTs to other exception types, such as OutOfMemoryException)
    public static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException(String.Create(CultureInfo.InvariantCulture, $"Media Foundation failed. code=[0x{hr:X8}]")) { HResult = hr };
        }
    }

    //--------------------------------------------------------------------------------
    // Functions
    //--------------------------------------------------------------------------------

    // H.264 encoders as (IMFActivate, name), the caller releases the activates
    public static List<(IntPtr Activate, string Name)> EnumerateEncoders(uint flags)
    {
        var input = new RegisterTypeInfo { MajorType = MediaTypeVideo, Subtype = FormatNv12 };
        var output = new RegisterTypeInfo { MajorType = MediaTypeVideo, Subtype = FormatH264 };
        IntPtr array;
        uint count;
        ThrowIfFailed(MFTEnumEx(CategoryVideoEncoder, flags, &input, &output, &array, &count));
        var result = new List<(IntPtr, string)>((int)count);
        for (var i = 0; i < count; i++)
        {
            var activate = ((IntPtr*)array)[i];
            result.Add((activate, GetString(activate, FriendlyName) ?? string.Empty));
        }

        CoTaskMemFree(array);
        return result;
    }

    public static IntPtr CreateVideoType(Guid subtype, int width, int height, int frameRate)
    {
        IntPtr type;
        ThrowIfFailed(MFCreateMediaType(&type));
        SetGuid(type, MajorType, MediaTypeVideo);
        SetGuid(type, Subtype, subtype);
        SetUInt64(type, FrameSize, ((ulong)(uint)width << 32) | (uint)height);
        SetUInt64(type, FrameRate, ((ulong)(uint)frameRate << 32) | 1);
        SetUInt64(type, PixelAspectRatio, (1UL << 32) | 1);
        SetUInt32(type, InterlaceMode, Progressive);
        return type;
    }

    // Sample with one memory buffer, the buffer is returned without an extra reference
    public static IntPtr CreateSample(int size, out IntPtr buffer)
    {
        IntPtr sample;
        ThrowIfFailed(MFCreateSample(&sample));
        IntPtr created;
        var hr = MFCreateMemoryBuffer((uint)size, &created);
        if (hr < 0)
        {
            Release(sample);
            ThrowIfFailed(hr);
        }

        hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(sample)[AddBufferSlot])(sample, created);
        Release(created);
        if (hr < 0)
        {
            Release(sample);
            ThrowIfFailed(hr);
        }

        buffer = created;
        return sample;
    }

    //--------------------------------------------------------------------------------
    // IMFAttributes
    //--------------------------------------------------------------------------------

    public static bool TryGetUInt32(IntPtr attributes, Guid key, out uint value)
    {
        uint result;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Vtbl(attributes)[GetUInt32Slot])(attributes, &key, &result);
        value = result;
        return hr >= 0;
    }

    public static bool TryGetGuid(IntPtr attributes, Guid key, out Guid value)
    {
        Guid result;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(attributes)[GetGuidSlot])(attributes, &key, &result);
        value = result;
        return hr >= 0;
    }

    public static string? GetString(IntPtr attributes, Guid key)
    {
        char* text;
        uint length;
        if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, char**, uint*, int>)Vtbl(attributes)[GetAllocatedStringSlot])(attributes, &key, &text, &length) < 0)
        {
            return null;
        }

        var result = new string(text, 0, (int)length);
        CoTaskMemFree((IntPtr)text);
        return result;
    }

    public static void SetUInt32(IntPtr attributes, Guid key, uint value) =>
        ThrowIfFailed(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Vtbl(attributes)[SetUInt32Slot])(attributes, &key, value));

    public static void SetUInt64(IntPtr attributes, Guid key, ulong value) =>
        ThrowIfFailed(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Vtbl(attributes)[SetUInt64Slot])(attributes, &key, value));

    public static void SetGuid(IntPtr attributes, Guid key, Guid value) =>
        ThrowIfFailed(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(attributes)[SetGuidSlot])(attributes, &key, &value));

    //--------------------------------------------------------------------------------
    // IMFActivate
    //--------------------------------------------------------------------------------

    public static int ActivateObject(IntPtr activate, Guid iid, out IntPtr result)
    {
        IntPtr pointer;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vtbl(activate)[ActivateObjectSlot])(activate, &iid, &pointer);
        result = hr < 0 ? IntPtr.Zero : pointer;
        return hr;
    }

    public static void ShutdownObject(IntPtr activate) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(activate)[ShutdownObjectSlot])(activate);

    //--------------------------------------------------------------------------------
    // IMFTransform
    //--------------------------------------------------------------------------------

    public static OutputStreamInfo GetOutputStreamInfo(IntPtr transform)
    {
        OutputStreamInfo info;
        ThrowIfFailed(((delegate* unmanaged[Stdcall]<IntPtr, uint, OutputStreamInfo*, int>)Vtbl(transform)[GetOutputStreamInfoSlot])(transform, 0, &info));
        return info;
    }

    // Zero when the transform has no attributes
    public static IntPtr GetAttributes(IntPtr transform)
    {
        IntPtr attributes;
        return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(transform)[GetAttributesSlot])(transform, &attributes) < 0 ? IntPtr.Zero : attributes;
    }

    // Zero at the end of the list
    public static IntPtr GetInputAvailableType(IntPtr transform, uint index)
    {
        IntPtr type;
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)Vtbl(transform)[GetInputAvailableTypeSlot])(transform, 0, index, &type) < 0 ? IntPtr.Zero : type;
    }

    public static IntPtr GetOutputAvailableType(IntPtr transform, uint index)
    {
        IntPtr type;
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)Vtbl(transform)[GetOutputAvailableTypeSlot])(transform, 0, index, &type) < 0 ? IntPtr.Zero : type;
    }

    public static int SetInputType(IntPtr transform, IntPtr type) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, uint, int>)Vtbl(transform)[SetInputTypeSlot])(transform, 0, type, 0);

    public static int SetOutputType(IntPtr transform, IntPtr type) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, uint, int>)Vtbl(transform)[SetOutputTypeSlot])(transform, 0, type, 0);

    public static int ProcessMessage(IntPtr transform, uint message) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, nuint, int>)Vtbl(transform)[ProcessMessageSlot])(transform, message, 0);

    public static int ProcessInput(IntPtr transform, IntPtr sample) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, uint, int>)Vtbl(transform)[ProcessInputSlot])(transform, 0, sample, 0);

    public static int ProcessOutput(IntPtr transform, OutputDataBuffer* buffer)
    {
        uint status;
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, OutputDataBuffer*, uint*, int>)Vtbl(transform)[ProcessOutputSlot])(transform, 0, 1, buffer, &status);
    }

    //--------------------------------------------------------------------------------
    // IMFSample / IMFMediaBuffer
    //--------------------------------------------------------------------------------

    public static long GetSampleTime(IntPtr sample)
    {
        long time;
        return ((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)Vtbl(sample)[GetSampleTimeSlot])(sample, &time) < 0 ? 0 : time;
    }

    public static void SetSampleTime(IntPtr sample, long time, long duration)
    {
        ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[SetSampleTimeSlot])(sample, time);
        ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[SetSampleDurationSlot])(sample, duration);
    }

    // The buffer has a reference
    public static int ConvertToContiguousBuffer(IntPtr sample, out IntPtr buffer)
    {
        IntPtr pointer;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(sample)[ConvertToContiguousBufferSlot])(sample, &pointer);
        buffer = hr < 0 ? IntPtr.Zero : pointer;
        return hr;
    }

    public static int Lock(IntPtr buffer, out byte* data, out int length)
    {
        byte* pointer;
        uint max;
        uint current;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Vtbl(buffer)[LockSlot])(buffer, &pointer, &max, &current);
        data = pointer;
        length = (int)current;
        return hr;
    }

    public static void Unlock(IntPtr buffer) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(buffer)[UnlockSlot])(buffer);

    public static void SetCurrentLength(IntPtr buffer, int length) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(buffer)[SetCurrentLengthSlot])(buffer, (uint)length);

    //--------------------------------------------------------------------------------
    // IMFMediaEventGenerator / ICodecAPI / IMFShutdown
    //--------------------------------------------------------------------------------

    // The next event and its status, waits for one or returns NoEventsAvailable.
    // A failure means the generator is shut down.
    public static int GetEvent(IntPtr generator, bool wait, out uint type, out int status)
    {
        IntPtr mediaEvent;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(generator)[GetEventSlot])(generator, wait ? 0 : EventNoWait, &mediaEvent);
        if (hr < 0)
        {
            type = 0;
            status = hr;
            return hr;
        }

        uint eventType;
        int eventStatus;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl(mediaEvent)[GetTypeSlot])(mediaEvent, &eventType);
        ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(mediaEvent)[GetStatusSlot])(mediaEvent, &eventStatus);
        Release(mediaEvent);
        type = eventType;
        status = eventStatus;
        return hr;
    }

    public static int SetCodecValue(IntPtr codecApi, Guid property, uint value)
    {
        var variant = new Variant { Type = VariantUInt32, UInt32 = value };
        return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Variant*, int>)Vtbl(codecApi)[SetValueSlot])(codecApi, &property, &variant);
    }

    public static int SetCodecValue(IntPtr codecApi, Guid property, bool value)
    {
        var variant = new Variant { Type = VariantBool, Bool = value ? (short)-1 : (short)0 };
        return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Variant*, int>)Vtbl(codecApi)[SetValueSlot])(codecApi, &property, &variant);
    }

    public static void Shutdown(IntPtr shutdown) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(shutdown)[ShutdownSlot])(shutdown);

    //--------------------------------------------------------------------------------
    // Native
    //--------------------------------------------------------------------------------

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MFTEnumEx(Guid category, uint flags, RegisterTypeInfo* input, RegisterTypeInfo* output, IntPtr* activates, uint* count);

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MFCreateMediaType(IntPtr* type);

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MFCreateSample(IntPtr* sample);

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MFCreateMemoryBuffer(uint maxLength, IntPtr* buffer);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoTaskMemFree(IntPtr pointer);

    //--------------------------------------------------------------------------------
    // Structures
    //--------------------------------------------------------------------------------

    // MFT_REGISTER_TYPE_INFO
    [StructLayout(LayoutKind.Sequential)]
    private struct RegisterTypeInfo
    {
        public Guid MajorType;
        public Guid Subtype;
    }

    // MFT_OUTPUT_STREAM_INFO
    [StructLayout(LayoutKind.Sequential)]
    public struct OutputStreamInfo
    {
        public uint Flags;
        public uint Size;
        public uint Alignment;
    }

    // MFT_OUTPUT_DATA_BUFFER
    [StructLayout(LayoutKind.Sequential)]
    public struct OutputDataBuffer
    {
        public uint StreamId;
        public IntPtr Sample;
        public uint Status;
        public IntPtr Events;
    }

    // VARIANT (VT_UI4 / VT_BOOL only)
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public uint UInt32;

        [FieldOffset(8)]
        public short Bool;
    }
}

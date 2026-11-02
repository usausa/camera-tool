namespace CameraTool.Vision;

using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

// Called on the encoder thread, the data is valid only during the call
public delegate void H264OutputCallback(ReadOnlySpan<byte> data, bool keyFrame, long timestamp);

// H.264 encoder of Media Foundation called through the COM vtable.
// A hardware encoder (asynchronous MFT) is preferred, and the Microsoft software encoder (synchronous MFT) is the fallback.
// Encode copies the frame to a pooled input sample on the caller thread, and a dedicated thread runs the encoder
// and passes the output to the callback. When the encoder is busy, the latest frame replaces the waiting one.
// No managed memory is allocated for a frame.
public sealed unsafe class H264Encoder : IDisposable
{
    private const int InputCount = 4;

    private const int InputFree = 0;

    private const int InputFilling = 1;

    private const int InputPending = 2;

    private const int InputSubmitted = 3;

    private const int NoInput = -1;

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly H264EncoderOptions options;

    private readonly H264OutputCallback callback;

    // 100 ns units
    private readonly long duration;

    private readonly int frameLength;

    private readonly Thread thread;

    private readonly ManualResetEventSlim started = new();

    private readonly AutoResetEvent inputArrived = new(false);

    // Guards the input samples against the release
    private readonly Lock inputSync = new();

    private readonly IntPtr[] inputSamples = new IntPtr[InputCount];

    private readonly IntPtr[] inputBuffers = new IntPtr[InputCount];

    private readonly int[] inputStates = new int[InputCount];

    private IntPtr activate;

    private IntPtr transform;

    private IntPtr codecApi;

    private IntPtr events;

    private IntPtr outputSample;

    private bool isAsync;

    private bool providesSamples;

    private bool released;

    private int pending = NoInput;

    private volatile bool stopping;

    private volatile bool keyFrameRequested;

    private long droppedFrames;

    private Exception? failure;

    public string Name { get; private set; } = string.Empty;

    public bool IsHardware { get; private set; }

    // Frames not encoded because the encoder was busy
    public long DroppedFrames => Interlocked.Read(ref droppedFrames);

    public H264Encoder(H264EncoderOptions options, H264OutputCallback callback)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.FrameRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Bitrate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.KeyFrameInterval);
        if (((options.Width % 2) != 0) || ((options.Height % 2) != 0))
        {
            throw new ArgumentException("The frame size must be even.", nameof(options));
        }

        this.options = options;
        this.callback = callback;
        duration = 10_000_000L / options.FrameRate;
        frameLength = (options.Width * options.Height * 3) / 2;

        // The Media Foundation objects are created and used on the encoder thread
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "H.264 encoder"
        };
        thread.Start();
        started.Wait();
        if (failure is not null)
        {
            thread.Join();
            started.Dispose();
            inputArrived.Dispose();
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    public void Dispose()
    {
        if (stopping)
        {
            return;
        }

        stopping = true;
        inputArrived.Set();
        if (!thread.Join(StopTimeout))
        {
            // The encoder did not finish the drain, the shutdown ends the wait for the event
            lock (inputSync)
            {
                if (!released)
                {
                    var shutdown = MediaFoundation.QueryInterface(transform, MediaFoundation.IidShutdown);
                    if (shutdown != IntPtr.Zero)
                    {
                        MediaFoundation.Shutdown(shutdown);
                        MediaFoundation.Release(shutdown);
                    }
                }
            }

            thread.Join();
        }

        started.Dispose();
        inputArrived.Dispose();
    }

    // Applied to the next frame
    public void RequestKeyFrame() => keyFrameRequested = true;

    //--------------------------------------------------------------------------------
    // Input (caller thread)
    //--------------------------------------------------------------------------------

    // NV12 of the size of the options, timestamp: 100 ns units (returned with the output).
    // Returns false when the frame is dropped (no free input) or the encoder is stopped.
    public bool Encode(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, long timestamp)
    {
        var width = options.Width;
        var height = options.Height;
        if ((yStride < width) || (y.Length < ((height - 1) * yStride) + width))
        {
            throw new ArgumentException("Invalid Y plane.", nameof(y));
        }

        if ((uvStride < width) || (uv.Length < ((((height / 2) - 1) * uvStride) + width)))
        {
            throw new ArgumentException("Invalid UV plane.", nameof(uv));
        }

        lock (inputSync)
        {
            if (stopping || released)
            {
                return false;
            }

            var slot = AcquireInput();
            if (slot == NoInput)
            {
                Interlocked.Increment(ref droppedFrames);
                return false;
            }

            var buffer = inputBuffers[slot];
            if (MediaFoundation.Lock(buffer, out var data, out _) < 0)
            {
                Volatile.Write(ref inputStates[slot], InputFree);
                return false;
            }

            try
            {
                for (var row = 0; row < height; row++)
                {
                    y.Slice(row * yStride, width).CopyTo(new Span<byte>(data + (row * width), width));
                }

                var uvData = data + (width * height);
                for (var row = 0; row < height / 2; row++)
                {
                    uv.Slice(row * uvStride, width).CopyTo(new Span<byte>(uvData + (row * width), width));
                }
            }
            finally
            {
                MediaFoundation.Unlock(buffer);
            }

            MediaFoundation.SetCurrentLength(buffer, frameLength);
            MediaFoundation.SetSampleTime(inputSamples[slot], timestamp, duration);
            Volatile.Write(ref inputStates[slot], InputPending);

            // The input the encoder has not taken yet is replaced
            var previous = Interlocked.Exchange(ref pending, slot);
            if (previous != NoInput)
            {
                Volatile.Write(ref inputStates[previous], InputFree);
                Interlocked.Increment(ref droppedFrames);
            }
        }

        inputArrived.Set();
        return true;
    }

    // A free input, or a submitted one the encoder has released
    private int AcquireInput()
    {
        for (var i = 0; i < InputCount; i++)
        {
            var state = Volatile.Read(ref inputStates[i]);
            if (((state == InputFree) || ((state == InputSubmitted) && IsReleased(inputSamples[i]) && IsReleased(inputBuffers[i]))) &&
                (Interlocked.CompareExchange(ref inputStates[i], InputFilling, state) == state))
            {
                return i;
            }
        }

        return NoInput;
    }

    // Only one owner holds the object (the sample is held by the pool, the buffer by the sample)
    private static bool IsReleased(IntPtr pointer)
    {
        var count = Marshal.AddRef(pointer);
        Marshal.Release(pointer);
        return count == 2;
    }

    //--------------------------------------------------------------------------------
    // Encoder thread
    //--------------------------------------------------------------------------------

    private void Run()
    {
        var initialized = MediaFoundation.CoInitializeEx(IntPtr.Zero, MediaFoundation.CoInitMultithreaded) >= 0;
        var startup = MediaFoundation.MFStartup(MediaFoundation.Version, MediaFoundation.StartupLite) >= 0;
        try
        {
            try
            {
                if (!startup)
                {
                    throw new InvalidOperationException("Media Foundation is not available.");
                }

                Initialize();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                failure = ex;
                return;
            }
            finally
            {
                started.Set();
            }

            if (isAsync)
            {
                RunAsync();
            }
            else
            {
                RunSync();
            }
        }
        finally
        {
            ReleaseObjects();
            if (startup)
            {
                _ = MediaFoundation.MFShutdown();
            }

            if (initialized)
            {
                MediaFoundation.CoUninitialize();
            }
        }
    }

    // The asynchronous encoder asks for an input and notifies an output with events
    private void RunAsync()
    {
        var draining = false;
        while (true)
        {
            var type = MediaFoundation.GetEvent(events);
            if (type == MediaFoundation.EventNeedInput)
            {
                if (!draining && !WaitAndSubmit())
                {
                    // Stopping: the remaining output is passed before the end
                    draining = true;
                    MediaFoundation.ProcessMessage(transform, MediaFoundation.NotifyEndOfStream);
                    MediaFoundation.ProcessMessage(transform, MediaFoundation.CommandDrain);
                }
            }
            else if (type == MediaFoundation.EventHaveOutput)
            {
                ProcessOutput();
            }
            else if ((type == MediaFoundation.EventDrainComplete) || (type == 0))
            {
                break;
            }
        }
    }

    // The synchronous encoder outputs the frame for each input
    private void RunSync()
    {
        while (!stopping)
        {
            inputArrived.WaitOne();
            if (Submit())
            {
                while (ProcessOutput())
                {
                }
            }
        }

        MediaFoundation.ProcessMessage(transform, MediaFoundation.NotifyEndOfStream);
        MediaFoundation.ProcessMessage(transform, MediaFoundation.CommandDrain);
        while (ProcessOutput())
        {
        }
    }

    // False when stopping
    private bool WaitAndSubmit()
    {
        while (!stopping)
        {
            if (Submit())
            {
                return true;
            }

            inputArrived.WaitOne();
        }

        return false;
    }

    private bool Submit()
    {
        var slot = Interlocked.Exchange(ref pending, NoInput);
        if (slot == NoInput)
        {
            return false;
        }

        if (keyFrameRequested && (codecApi != IntPtr.Zero))
        {
            keyFrameRequested = false;
            MediaFoundation.SetCodecValue(codecApi, MediaFoundation.ForceKeyFrame, 1u);
        }

        var hr = MediaFoundation.ProcessInput(transform, inputSamples[slot]);
        if ((hr == MediaFoundation.NotAccepting) && !isAsync)
        {
            while (ProcessOutput())
            {
            }

            hr = MediaFoundation.ProcessInput(transform, inputSamples[slot]);
        }

        Volatile.Write(ref inputStates[slot], InputSubmitted);
        return hr >= 0;
    }

    // Passes one output to the callback, false when the encoder needs more input
    private bool ProcessOutput()
    {
        var buffer = new MediaFoundation.OutputDataBuffer { Sample = providesSamples ? IntPtr.Zero : outputSample };
        var hr = MediaFoundation.ProcessOutput(transform, &buffer);
        MediaFoundation.Release(buffer.Events);
        if (hr == MediaFoundation.TransformStreamChange)
        {
            // The output type is set again
            var type = MediaFoundation.GetOutputAvailableType(transform, 0);
            var result = (type != IntPtr.Zero) && (MediaFoundation.SetOutputType(transform, type) >= 0);
            MediaFoundation.Release(type);
            return result;
        }

        if (hr < 0)
        {
            return false;
        }

        try
        {
            Deliver(buffer.Sample);
        }
        finally
        {
            if (providesSamples)
            {
                MediaFoundation.Release(buffer.Sample);
            }
        }

        return true;
    }

    private void Deliver(IntPtr sample)
    {
        var timestamp = MediaFoundation.GetSampleTime(sample);
        var keyFrame = MediaFoundation.TryGetUInt32(sample, MediaFoundation.CleanPoint, out var cleanPoint) && (cleanPoint != 0);
        if (MediaFoundation.ConvertToContiguousBuffer(sample, out var buffer) < 0)
        {
            return;
        }

        try
        {
            if (MediaFoundation.Lock(buffer, out var data, out var length) < 0)
            {
                return;
            }

            try
            {
                callback(new ReadOnlySpan<byte>(data, length), keyFrame, timestamp);
            }
            finally
            {
                MediaFoundation.Unlock(buffer);
            }
        }
        finally
        {
            MediaFoundation.Release(buffer);
        }
    }

    //--------------------------------------------------------------------------------
    // Setup
    //--------------------------------------------------------------------------------

    private void Initialize()
    {
        // Hardware encoders first, the first one that accepts the settings is used
        var candidates = new List<(IntPtr Activate, string Name, bool Hardware)>();
        if (options.PreferHardware)
        {
            candidates.AddRange(MediaFoundation.EnumerateEncoders(MediaFoundation.EnumHardware | MediaFoundation.EnumAsync | MediaFoundation.EnumSortAndFilter).Select(static x => (x.Activate, x.Name, true)));
        }

        candidates.AddRange(MediaFoundation.EnumerateEncoders(MediaFoundation.EnumSync | MediaFoundation.EnumLocal | MediaFoundation.EnumSortAndFilter).Select(static x => (x.Activate, x.Name, false)));
        foreach (var candidate in candidates)
        {
            if ((transform == IntPtr.Zero) && TryConfigure(candidate.Activate))
            {
                activate = candidate.Activate;
                Name = candidate.Name;
                IsHardware = candidate.Hardware;
            }
            else
            {
                MediaFoundation.Release(candidate.Activate);
            }
        }

        if (transform == IntPtr.Zero)
        {
            throw new InvalidOperationException("H.264 encoder not found.");
        }

        for (var i = 0; i < InputCount; i++)
        {
            inputSamples[i] = MediaFoundation.CreateSample(frameLength, out inputBuffers[i]);
        }

        var info = MediaFoundation.GetOutputStreamInfo(transform);
        providesSamples = (info.Flags & (MediaFoundation.OutputProvidesSamples | MediaFoundation.OutputCanProvideSamples)) != 0;
        if (!providesSamples)
        {
            outputSample = MediaFoundation.CreateSample((int)Math.Max(info.Size, (uint)frameLength), out _);
        }

        Marshal.ThrowExceptionForHR(MediaFoundation.ProcessMessage(transform, MediaFoundation.NotifyBeginStreaming));
        Marshal.ThrowExceptionForHR(MediaFoundation.ProcessMessage(transform, MediaFoundation.NotifyStartOfStream));
    }

    private bool TryConfigure(IntPtr candidateActivate)
    {
        if (MediaFoundation.ActivateObject(candidateActivate, MediaFoundation.IidTransform, out var candidate) < 0)
        {
            return false;
        }

        var candidateCodecApi = IntPtr.Zero;
        var candidateEvents = IntPtr.Zero;
        try
        {
            var candidateAsync = false;
            var attributes = MediaFoundation.GetAttributes(candidate);
            if (attributes != IntPtr.Zero)
            {
                // An asynchronous encoder is locked until the client unlocks it
                if (MediaFoundation.TryGetUInt32(attributes, MediaFoundation.TransformAsync, out var value) && (value != 0))
                {
                    candidateAsync = true;
                    MediaFoundation.SetUInt32(attributes, MediaFoundation.TransformAsyncUnlock, 1);
                }

                MediaFoundation.Release(attributes);
            }

            // Some encoders apply the codec values before the types, others after them
            candidateCodecApi = MediaFoundation.QueryInterface(candidate, MediaFoundation.IidCodecApi);
            SetCodecValues(candidateCodecApi);
            if (!TrySetTypes(candidate))
            {
                return false;
            }

            SetCodecValues(candidateCodecApi);
            if (candidateAsync)
            {
                candidateEvents = MediaFoundation.QueryInterface(candidate, MediaFoundation.IidEventGenerator);
                if (candidateEvents == IntPtr.Zero)
                {
                    return false;
                }
            }

            transform = candidate;
            codecApi = candidateCodecApi;
            events = candidateEvents;
            isAsync = candidateAsync;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (transform != candidate)
            {
                MediaFoundation.Release(candidateEvents);
                MediaFoundation.Release(candidateCodecApi);
                MediaFoundation.Release(candidate);
                MediaFoundation.ShutdownObject(candidateActivate);
            }
        }
    }

    // Unsupported values are ignored (for example the B frame count of a hardware encoder that has no B frames)
    private void SetCodecValues(IntPtr candidateCodecApi)
    {
        if (candidateCodecApi == IntPtr.Zero)
        {
            return;
        }

        MediaFoundation.SetCodecValue(candidateCodecApi, MediaFoundation.LowLatencyMode, true);
        // eAVEncCommonRateControlMode_CBR
        MediaFoundation.SetCodecValue(candidateCodecApi, MediaFoundation.RateControlMode, 0u);
        MediaFoundation.SetCodecValue(candidateCodecApi, MediaFoundation.MeanBitrate, (uint)options.Bitrate);
        MediaFoundation.SetCodecValue(candidateCodecApi, MediaFoundation.GopSize, (uint)options.KeyFrameInterval);
        MediaFoundation.SetCodecValue(candidateCodecApi, MediaFoundation.BPictureCount, 0u);
    }

    // Output type first (an encoder offers the input types for the output type)
    private bool TrySetTypes(IntPtr candidate)
    {
        var width = options.Width;
        var height = options.Height;
        var output = MediaFoundation.CreateVideoType(MediaFoundation.FormatH264, width, height, options.FrameRate);
        try
        {
            MediaFoundation.SetUInt32(output, MediaFoundation.AverageBitrate, (uint)options.Bitrate);
            MediaFoundation.SetUInt32(output, MediaFoundation.Profile, MediaFoundation.ProfileMain);
            SetColor(output);
            if (MediaFoundation.SetOutputType(candidate, output) < 0)
            {
                return false;
            }
        }
        finally
        {
            MediaFoundation.Release(output);
        }

        var input = MediaFoundation.CreateVideoType(MediaFoundation.FormatNv12, width, height, options.FrameRate);
        try
        {
            SetColor(input);
            if (MediaFoundation.SetInputType(candidate, input) >= 0)
            {
                return true;
            }
        }
        finally
        {
            MediaFoundation.Release(input);
        }

        // An NV12 type offered by the encoder
        for (uint i = 0; ; i++)
        {
            var available = MediaFoundation.GetInputAvailableType(candidate, i);
            if (available == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (MediaFoundation.TryGetGuid(available, MediaFoundation.Subtype, out var subtype) && (subtype == MediaFoundation.FormatNv12))
                {
                    MediaFoundation.SetUInt64(available, MediaFoundation.FrameSize, ((ulong)(uint)width << 32) | (uint)height);
                    MediaFoundation.SetUInt64(available, MediaFoundation.FrameRate, ((ulong)(uint)options.FrameRate << 32) | 1);
                    SetColor(available);
                    return MediaFoundation.SetInputType(candidate, available) >= 0;
                }
            }
            finally
            {
                MediaFoundation.Release(available);
            }
        }
    }

    // Range and colors of the input written to the stream (VUI): BT.601 matrix as the converters of this project, BT.709 primaries and transfer.
    // The NVIDIA encoder writes all of them, the Microsoft software encoder only the range (a decoder then assumes the BT.709 matrix).
    private void SetColor(IntPtr type)
    {
        MediaFoundation.SetUInt32(type, MediaFoundation.NominalRange, options.FullRange ? MediaFoundation.NominalRangeFull : MediaFoundation.NominalRangeVideo);
        MediaFoundation.SetUInt32(type, MediaFoundation.YuvMatrix, MediaFoundation.MatrixBt601);
        MediaFoundation.SetUInt32(type, MediaFoundation.VideoPrimaries, MediaFoundation.PrimariesBt709);
        MediaFoundation.SetUInt32(type, MediaFoundation.TransferFunction, MediaFoundation.TransferBt709);
    }

    // Encoder thread, at the end
    private void ReleaseObjects()
    {
        if (transform != IntPtr.Zero)
        {
            MediaFoundation.ProcessMessage(transform, MediaFoundation.NotifyEndStreaming);
        }

        lock (inputSync)
        {
            released = true;
            for (var i = 0; i < InputCount; i++)
            {
                MediaFoundation.Release(inputSamples[i]);
                inputSamples[i] = IntPtr.Zero;
                inputBuffers[i] = IntPtr.Zero;
            }
        }

        MediaFoundation.Release(outputSample);
        MediaFoundation.Release(events);
        MediaFoundation.Release(codecApi);
        MediaFoundation.Release(transform);
        if (activate != IntPtr.Zero)
        {
            MediaFoundation.ShutdownObject(activate);
            MediaFoundation.Release(activate);
        }

        outputSample = IntPtr.Zero;
        events = IntPtr.Zero;
        codecApi = IntPtr.Zero;
        transform = IntPtr.Zero;
        activate = IntPtr.Zero;
    }
}

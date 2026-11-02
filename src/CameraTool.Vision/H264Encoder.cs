namespace CameraTool.Vision;

using System.Globalization;
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

    private const int InputPending = 1;

    private const int InputSubmitted = 2;

    private const int NoInput = -1;

    // E_FAIL, for an error event without a status
    private const int UnspecifiedError = unchecked((int)0x80004005);

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    // An encoder that works ahead asks for the next input before passing the output,
    // so the wait for a frame is short while an output is still to come
    private static readonly TimeSpan OutputWaitInterval = TimeSpan.FromMilliseconds(2);

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

    // Changed by the caller thread in inputSync, and by the encoder thread only for the pending input it took
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

    private volatile bool faulted;

    private int faultCode;

    private long droppedFrames;

    private Exception? failure;

    public string Name { get; private set; } = string.Empty;

    public bool IsHardware { get; private set; }

    // Frames not encoded because the encoder was busy
    public long DroppedFrames => Interlocked.Read(ref droppedFrames);

    // The encoder stopped working (for example the device was lost or reset) and encodes no more frames
    public bool IsFaulted => faulted;

    // HRESULT of the failure
    public int FaultCode => Volatile.Read(ref faultCode);

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
    // Returns false when the frame is dropped (no free input) or the encoder is stopped or faulted.
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
            if (stopping || released || faulted)
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

    // A free input, or a submitted one the encoder has released (the state is not changed until the input is filled)
    private int AcquireInput()
    {
        for (var i = 0; i < InputCount; i++)
        {
            var state = Volatile.Read(ref inputStates[i]);
            if ((state == InputFree) || ((state == InputSubmitted) && IsReleased(inputSamples[i]) && IsReleased(inputBuffers[i])))
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

    // The asynchronous encoder asks for inputs and notifies outputs with events.
    // While an input is asked for, only the queued events are taken and the thread waits for a frame.
    private void RunAsync()
    {
        var requests = 0;
        var outputs = 0;
        var draining = false;
        while (!faulted)
        {
            var hr = MediaFoundation.GetEvent(events, requests == 0, out var type, out var status);
            if (hr == MediaFoundation.NoEventsAvailable)
            {
                if (stopping)
                {
                    // The remaining output is passed before the end
                    draining = true;
                    requests = 0;
                    MediaFoundation.ProcessMessage(transform, MediaFoundation.NotifyEndOfStream);
                    MediaFoundation.ProcessMessage(transform, MediaFoundation.CommandDrain);
                }
                else if (Submit())
                {
                    requests--;
                    outputs++;
                }
                else
                {
                    inputArrived.WaitOne(outputs > 0 ? OutputWaitInterval : Timeout.InfiniteTimeSpan);
                }

                continue;
            }

            if (hr < 0)
            {
                // Shut down by Dispose when the drain does not finish, otherwise the encoder failed
                if (!stopping)
                {
                    Fault(hr);
                }

                return;
            }

            if ((type == MediaFoundation.EventError) || (status < 0))
            {
                Fault(status < 0 ? status : UnspecifiedError);
                return;
            }

            if (type == MediaFoundation.EventNeedInput)
            {
                if (!draining)
                {
                    requests++;
                }
            }
            else if (type == MediaFoundation.EventHaveOutput)
            {
                if (ProcessOutput())
                {
                    outputs = Math.Max(outputs - 1, 0);
                }
            }
            else if (type == MediaFoundation.EventDrainComplete)
            {
                return;
            }
        }
    }

    // The synchronous encoder outputs the frame for each input
    private void RunSync()
    {
        while (!stopping && !faulted)
        {
            inputArrived.WaitOne();
            if (Submit())
            {
                while (ProcessOutput())
                {
                }
            }
        }

        if (faulted)
        {
            return;
        }

        MediaFoundation.ProcessMessage(transform, MediaFoundation.NotifyEndOfStream);
        MediaFoundation.ProcessMessage(transform, MediaFoundation.CommandDrain);
        while (ProcessOutput())
        {
        }
    }

    private void Fault(int code)
    {
        Volatile.Write(ref faultCode, code);
        faulted = true;
    }

    // False when no frame is waiting, a failed input faults the encoder
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
        if (hr < 0)
        {
            Fault(hr);
        }

        return true;
    }

    // Passes one output to the callback, false when the encoder has no output (a failure faults the encoder)
    private bool ProcessOutput()
    {
        var buffer = new MediaFoundation.OutputDataBuffer { Sample = providesSamples ? IntPtr.Zero : outputSample };
        var hr = MediaFoundation.ProcessOutput(transform, &buffer);
        MediaFoundation.Release(buffer.Events);
        if (hr == MediaFoundation.TransformStreamChange)
        {
            // The output type is set again
            var type = MediaFoundation.GetOutputAvailableType(transform, 0);
            hr = type != IntPtr.Zero ? MediaFoundation.SetOutputType(transform, type) : UnspecifiedError;
            MediaFoundation.Release(type);
            if (hr < 0)
            {
                Fault(hr);
                return false;
            }

            return true;
        }

        if (hr < 0)
        {
            // The synchronous encoder has no more output until the next input
            if (hr != MediaFoundation.TransformNeedMoreInput)
            {
                Fault(hr);
            }

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
        for (var i = 0; i < InputCount; i++)
        {
            inputSamples[i] = MediaFoundation.CreateSample(frameLength, out inputBuffers[i]);
        }

        // Hardware encoders first, the first one that accepts the settings and starts streaming is used
        // (a hardware encoder can fail to start, for example when its sessions are used up by other applications)
        var candidates = new List<(IntPtr Activate, string Name, bool Hardware)>();
        var lastError = 0;
        try
        {
            if (options.PreferHardware)
            {
                candidates.AddRange(MediaFoundation.EnumerateEncoders(MediaFoundation.EnumHardware | MediaFoundation.EnumAsync | MediaFoundation.EnumSortAndFilter).Select(static x => (x.Activate, x.Name, true)));
            }

            candidates.AddRange(MediaFoundation.EnumerateEncoders(MediaFoundation.EnumSync | MediaFoundation.EnumLocal | MediaFoundation.EnumSortAndFilter).Select(static x => (x.Activate, x.Name, false)));
            foreach (var candidate in candidates)
            {
                if (TryStart(candidate.Activate, ref lastError))
                {
                    activate = candidate.Activate;
                    Name = candidate.Name;
                    IsHardware = candidate.Hardware;
                    return;
                }
            }
        }
        finally
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Activate != activate)
                {
                    MediaFoundation.Release(candidate.Activate);
                }
            }
        }

        throw new InvalidOperationException(String.Create(CultureInfo.InvariantCulture, $"No H.264 encoder could be started. code=[0x{lastError:X8}]"));
    }

    // Configures the encoder and starts streaming, false when the encoder does not accept the settings or fails to start
    private bool TryStart(IntPtr candidateActivate, ref int lastError)
    {
        var hr = MediaFoundation.ActivateObject(candidateActivate, MediaFoundation.IidTransform, out var candidate);
        if (hr < 0)
        {
            lastError = hr;
            return false;
        }

        var candidateCodecApi = IntPtr.Zero;
        var candidateEvents = IntPtr.Zero;
        var candidateOutputSample = IntPtr.Zero;
        var succeeded = false;
        try
        {
            var candidateAsync = false;
            var attributes = MediaFoundation.GetAttributes(candidate);
            if (attributes != IntPtr.Zero)
            {
                try
                {
                    // An asynchronous encoder is locked until the client unlocks it
                    if (MediaFoundation.TryGetUInt32(attributes, MediaFoundation.TransformAsync, out var value) && (value != 0))
                    {
                        candidateAsync = true;
                        MediaFoundation.SetUInt32(attributes, MediaFoundation.TransformAsyncUnlock, 1);
                    }
                }
                finally
                {
                    MediaFoundation.Release(attributes);
                }
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

            var info = MediaFoundation.GetOutputStreamInfo(candidate);
            var candidateProvidesSamples = (info.Flags & (MediaFoundation.OutputProvidesSamples | MediaFoundation.OutputCanProvideSamples)) != 0;
            if (!candidateProvidesSamples)
            {
                candidateOutputSample = MediaFoundation.CreateSample((int)Math.Max(info.Size, (uint)frameLength), out _);
            }

            MediaFoundation.ThrowIfFailed(MediaFoundation.ProcessMessage(candidate, MediaFoundation.NotifyBeginStreaming));
            MediaFoundation.ThrowIfFailed(MediaFoundation.ProcessMessage(candidate, MediaFoundation.NotifyStartOfStream));

            transform = candidate;
            codecApi = candidateCodecApi;
            events = candidateEvents;
            outputSample = candidateOutputSample;
            isAsync = candidateAsync;
            providesSamples = candidateProvidesSamples;
            succeeded = true;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            lastError = ex.HResult;
            return false;
        }
        finally
        {
            if (!succeeded)
            {
                MediaFoundation.Release(candidateOutputSample);
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

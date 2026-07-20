using System.Runtime.InteropServices;

namespace SiegeFX.Runtime.Capture;

/// <summary>SC-RECORD-PROC-AUDIO — WASAPI PROCESS loopback capture
/// (ActivateAudioInterfaceAsync + VAD\Process_Loopback, Win10 2004+).
/// Captures THIS process's audio as submitted to the mixer — BEFORE the
/// endpoint master volume is applied — so a recording's soundtrack no
/// longer follows the user's Windows volume slider (the device-loopback
/// tap, like OBS "Desktop Audio", records post-volume: quiet system =
/// quiet clip). Side benefit: only the game's own audio lands in the
/// clip — no Discord pings, no notification dings.
///
/// The engine performs format conversion to whatever we request at
/// Initialize; we ask for interleaved 16-bit PCM directly so the
/// recorder's audio path needs no float conversion. Delivery is
/// event-driven on a dedicated reader thread; chunks arrive via
/// <see cref="DataAvailable"/> exactly like the NAudio fallback path.</summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    public int SampleRate { get; }
    public int Channels { get; }

    /// <summary>Interleaved 16-bit PCM chunk at (SampleRate, Channels).
    /// Fired on the reader thread.</summary>
    public event Action<byte[]>? DataAvailable;

    /// <summary>Process loopback shipped in Windows 10 2004 (build 19041).</summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 19041;

    object? _audioClient;          // IAudioClient RCW
    object? _captureClient;        // IAudioCaptureClient RCW
    AutoResetEvent? _samplesReady;
    Thread? _reader;
    volatile bool _stopping;

    public ProcessLoopbackCapture(int sampleRate = 48000, int channels = 2)
    {
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>Activate + initialize + start the capture. Runs the COM
    /// activation on an MTA pool thread (ActivateAudioInterfaceAsync
    /// rejects STA callers). Throws on any failure — callers fall back
    /// to device loopback.</summary>
    public void Start()
    {
        Task.Run(ActivateAndStart).GetAwaiter().GetResult();
    }

    void ActivateAndStart()
    {
        var activationParams = new AudioClientActivationParams
        {
            ActivationType = ActivationTypeProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = (uint)Environment.ProcessId,
                ProcessLoopbackMode = LoopbackModeIncludeTree,
            },
        };
        nint paramsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParams>());
        nint propVarPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
        try
        {
            Marshal.StructureToPtr(activationParams, paramsPtr, false);
            var propVar = new PropVariantBlob
            {
                Vt = 65, // VT_BLOB
                CbSize = (uint)Marshal.SizeOf<AudioClientActivationParams>(),
                PBlobData = paramsPtr,
            };
            Marshal.StructureToPtr(propVar, propVarPtr, false);

            var handler = new ActivateCompletionHandler();
            var iid = IidIAudioClient;
            ActivateAudioInterfaceAsync(VirtualLoopbackDevice, in iid, propVarPtr,
                handler, out var operation);
            if (!handler.Done.WaitOne(TimeSpan.FromSeconds(3)))
                throw new TimeoutException("process-loopback activation timed out");
            operation.GetActivateResult(out int activateHr, out var activated);
            Marshal.ThrowExceptionForHR(activateHr);
            var audioClient = (IAudioClient)activated;

            // Interleaved s16 at our rate/channels — the engine converts.
            var wfx = new WaveFormatEx
            {
                FormatTag = 1, // PCM
                Channels = (ushort)Channels,
                SamplesPerSec = (uint)SampleRate,
                AvgBytesPerSec = (uint)(SampleRate * Channels * 2),
                BlockAlign = (ushort)(Channels * 2),
                BitsPerSample = 16,
                CbSize = 0,
            };
            nint wfxPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            try
            {
                Marshal.StructureToPtr(wfx, wfxPtr, false);
                var sessionGuid = Guid.Empty;
                audioClient.Initialize(ShareModeShared,
                    StreamFlagsLoopback | StreamFlagsEventCallback,
                    2_000_000 /* 200ms in hns */, 0, wfxPtr, ref sessionGuid);
            }
            finally { Marshal.FreeHGlobal(wfxPtr); }

            _samplesReady = new AutoResetEvent(false);
            audioClient.SetEventHandle(_samplesReady.SafeWaitHandle.DangerousGetHandle());
            var svcIid = IidIAudioCaptureClient;
            audioClient.GetService(ref svcIid, out var svc);
            _captureClient = svc;
            _audioClient = audioClient;
            audioClient.Start();

            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "siegefx-proc-loopback" };
            _reader.Start();
        }
        finally
        {
            Marshal.FreeHGlobal(propVarPtr);
            Marshal.FreeHGlobal(paramsPtr);
        }
    }

    void ReadLoop()
    {
        // SC-CRASH-CAPTURE — a fault on this background thread must never
        // take the process silently; log it and let recording continue
        // video-only (the silence-fill keeps the mux fed).
        try { ReadLoopCore(); }
        catch (Exception ex)
        {
            Console.WriteLine("[record] process-loopback reader died: " +
                $"{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
        }
    }

    void ReadLoopCore()
    {
        var capture = (IAudioCaptureClient)_captureClient!;
        int blockAlign = Channels * 2;
        while (!_stopping)
        {
            _samplesReady!.WaitOne(100);
            if (_stopping) break;
            while (true)
            {
                uint packet;
                try { capture.GetNextPacketSize(out packet); }
                catch { return; } // client torn down mid-loop
                if (packet == 0) break;
                capture.GetBuffer(out nint data, out uint frames, out uint flags, out _, out _);
                if (frames > 0)
                {
                    var chunk = new byte[frames * blockAlign];
                    if ((flags & BufferFlagSilent) == 0)
                        Marshal.Copy(data, chunk, 0, chunk.Length);
                    // silent packets stay zeroed — real silence, kept so the
                    // track's timeline matches the game's quiet moments.
                    DataAvailable?.Invoke(chunk);
                }
                capture.ReleaseBuffer(frames);
            }
        }
    }

    public void Stop()
    {
        _stopping = true;
        _samplesReady?.Set();
        try { _reader?.Join(500); } catch { }
        try { (_audioClient as IAudioClient)?.Stop(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        if (_captureClient is not null) { try { Marshal.FinalReleaseComObject(_captureClient); } catch { } _captureClient = null; }
        if (_audioClient is not null) { try { Marshal.FinalReleaseComObject(_audioClient); } catch { } _audioClient = null; }
        _samplesReady?.Dispose();
        _samplesReady = null;
    }

    // ---- interop ---------------------------------------------------------

    const string VirtualLoopbackDevice = @"VAD\Process_Loopback";
    const int ActivationTypeProcessLoopback = 1;
    const int LoopbackModeIncludeTree = 0;
    const int ShareModeShared = 0;
    const uint StreamFlagsLoopback = 0x00020000;
    const uint StreamFlagsEventCallback = 0x00040000;
    const uint BufferFlagSilent = 0x2;
    static readonly Guid IidIAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    static readonly Guid IidIAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    [StructLayout(LayoutKind.Sequential)]
    struct AudioClientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AudioClientActivationParams
    {
        public int ActivationType;
        public AudioClientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariantBlob
    {
        public ushort Vt;
        public ushort Reserved1, Reserved2, Reserved3;
        public uint CbSize;
        public nint PBlobData;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort CbSize;
    }

    [DllImport("mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        in Guid riid, nint activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult,
            [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    sealed class ActivateCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        public readonly ManualResetEvent Done = new(false);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation _) => Done.Set();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, uint streamFlags, long bufferDuration,
            long periodicity, nint format, ref Guid audioSessionGuid);
        void GetBufferSize(out uint numBufferFrames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closestMatch);
        void GetMixFormat(out nint format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(nint eventHandle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out nint data, out uint numFramesRead, out uint flags,
            out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(uint numFramesRead);
        void GetNextPacketSize(out uint numFramesInNextPacket);
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Security.Cryptography;
using Windows.Storage;

namespace SiegeFX.Runtime.Capture;

/// <summary>SC-RECORD — in-game video recorder. Windows.Graphics.Capture
/// grabs the game window at the compositor level (zero GL readback — the
/// render loop never sees it), a MediaStreamSource feeds those BGRA
/// surfaces plus a WASAPI loopback audio tap into MediaTranscoder, and
/// the hardware H.264 encoder writes an .mp4. The approach and its
/// pitfalls are inherited from a sibling project's proven recorder:
///  - the encoding profile must be built EXPLICITLY (VideoEncodingQuality
///    presets throw MF_E_TRANSFORM_TYPE_NOT_SET with MediaStreamSource);
///  - video timestamps ride the frames' QPC clock, never fixed per-frame
///    increments (dropped frames would fast-forward the clip);
///  - Stop() only signals — the transcode finalizes on its own thread and
///    cleanup runs in that continuation, so the render thread never
///    blocks;
///  - audio silence-fills when the game is quiet (WASAPI loopback goes
///    dark with no active session) but only while behind the video clock,
///    so filler can't outrun real time.</summary>
public sealed class WgcRecorder
{
    // ---- public surface -------------------------------------------------

    public bool IsRecording { get; private set; }
    public string? OutputPath { get; private set; }
    public TimeSpan Elapsed => _clock?.Elapsed ?? TimeSpan.Zero;

    /// <summary>Status lines for the game's message strip. Produced on
    /// worker threads; the host drains once per frame on the render
    /// thread.</summary>
    public readonly ConcurrentQueue<string> StatusLines = new();

    public static bool IsSupported
    {
        get
        {
            try { return GraphicsCaptureSession.IsSupported(); }
            catch { return false; }
        }
    }

    // ---- capture state --------------------------------------------------

    IDirect3DDevice? _d3dDevice;
    GraphicsCaptureItem? _item;
    Direct3D11CaptureFramePool? _framePool;
    GraphicsCaptureSession? _session;
    Direct3D11CaptureFrame? _latestFrame;
    readonly object _frameLock = new();
    readonly AutoResetEvent _frameReady = new(false);
    TimeSpan? _firstFrameTime;
    long _lastVideoTicks;
    volatile bool _stopping;
    Stopwatch? _clock;

    // ---- audio state ----------------------------------------------------

    WasapiLoopbackCapture? _loopback;
    BlockingCollection<byte[]>? _audioQueue;
    int _audioRate = 48000;
    int _audioChannels = 2;
    long _audioTicks;

    // ---- media pipeline -------------------------------------------------

    MediaStreamSource? _mss;
    VideoStreamDescriptor? _videoDesc;

    /// <summary>Begin recording the given window. Synchronous part stands
    /// up capture; encoding spins up on a worker. Returns false (with a
    /// status line) when capture can't start.</summary>
    public bool Start(nint hwnd, string outputDir, string fileStem)
    {
        if (IsRecording) return false;
        if (!IsSupported)
        {
            StatusLines.Enqueue("Recording unavailable — Windows 10 1903+ required.");
            return false;
        }
        try
        {
            Directory.CreateDirectory(outputDir);
            _stopping = false;
            _firstFrameTime = null;
            _lastVideoTicks = 0;
            _audioTicks = 0;

            _d3dDevice = CreateD3DDevice();
            _item = CreateItemForWindow(hwnd);
            var size = _item.Size;
            if (size.Width < 32 || size.Height < 32)
                throw new InvalidOperationException("window too small to record");
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _d3dDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(_item);
            // Yellow capture border (Win11) — cosmetic; older builds throw.
            try { _session.IsBorderRequired = false; } catch { }
            _item.Closed += (_, _) => Stop();

            StartAudio();
            BuildMediaSource(size.Width, size.Height);

            OutputPath = Path.Combine(outputDir, fileStem + ".mp4");
            _clock = Stopwatch.StartNew();
            _session.StartCapture();
            IsRecording = true;

            var dir = outputDir; var stem = fileStem;
            int w = size.Width, h = size.Height;
            Task.Run(() => RunTranscodeAsync(dir, stem, w, h));
            return true;
        }
        catch (Exception ex)
        {
            StatusLines.Enqueue($"Recording failed to start ({ex.Message}).");
            CleanupCapture();
            IsRecording = false;
            return false;
        }
    }

    /// <summary>Non-blocking stop: raise the flags, wake the pull
    /// handlers, and let the transcode continuation finalize the file and
    /// tear everything down.</summary>
    public void Stop()
    {
        if (!IsRecording || _stopping) return;
        _stopping = true;
        _frameReady.Set();
        try { _audioQueue?.CompleteAdding(); } catch { }
        try { _loopback?.StopRecording(); } catch { }
    }

    // ---- capture plumbing -----------------------------------------------

    void OnFrameArrived(Direct3D11CaptureFramePool sender, object? args)
    {
        if (_stopping) return;
        try
        {
            var frame = sender.TryGetNextFrame();
            if (frame is null) return;
            lock (_frameLock)
            {
                _latestFrame?.Dispose(); // encoder missed it — keep newest
                _latestFrame = frame;
            }
            _frameReady.Set();
        }
        catch { /* pool torn down mid-callback */ }
    }

    void StartAudio()
    {
        try
        {
            _loopback = new WasapiLoopbackCapture();
            var wf = _loopback.WaveFormat;
            _audioRate = wf.SampleRate;
            _audioChannels = Math.Clamp(wf.Channels, 1, 2);
            _audioQueue = new BlockingCollection<byte[]>(boundedCapacity: 256);
            _loopback.DataAvailable += OnAudioData;
            _loopback.StartRecording();
        }
        catch (Exception ex)
        {
            // No render device / exclusive-mode session — record video-only.
            StatusLines.Enqueue($"Recording without audio ({ex.Message}).");
            try { _loopback?.Dispose(); } catch { }
            _loopback = null;
            _audioQueue = null;
        }
    }

    /// <summary>Loopback callback → interleaved 16-bit PCM chunks. The mix
    /// format is float32 at the device rate on modern Windows; >2-channel
    /// devices downmix by dropping surrounds (front L/R carry the game).</summary>
    void OnAudioData(object? sender, WaveInEventArgs e)
    {
        var q = _audioQueue;
        var lb = _loopback;
        if (q is null || lb is null || _stopping || e.BytesRecorded == 0) return;
        var wf = lb.WaveFormat;
        int srcCh = wf.Channels, dstCh = _audioChannels;
        byte[] outBuf;
        if (wf.Encoding == WaveFormatEncoding.IeeeFloat && wf.BitsPerSample == 32)
        {
            int frames = e.BytesRecorded / (4 * srcCh);
            outBuf = new byte[frames * dstCh * 2];
            var src = e.Buffer;
            int o = 0;
            for (int f = 0; f < frames; f++)
            {
                int b = f * 4 * srcCh;
                for (int c = 0; c < dstCh; c++)
                {
                    float v = BitConverter.ToSingle(src, b + c * 4);
                    short s = (short)Math.Clamp((int)(v * 32767f), short.MinValue, short.MaxValue);
                    outBuf[o++] = (byte)s;
                    outBuf[o++] = (byte)(s >> 8);
                }
            }
        }
        else if (wf.Encoding == WaveFormatEncoding.Pcm && wf.BitsPerSample == 16 && srcCh == dstCh)
        {
            outBuf = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, outBuf, 0, e.BytesRecorded);
        }
        else return; // exotic mix format — skip rather than corrupt the track
        q.TryAdd(outBuf); // bounded: drop under pressure instead of stalling WASAPI
    }

    void BuildMediaSource(int w, int h)
    {
        var vprops = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8, (uint)w, (uint)h);
        vprops.FrameRate.Numerator = 60;
        vprops.FrameRate.Denominator = 1;
        _videoDesc = new VideoStreamDescriptor(vprops);
        if (_audioQueue is not null)
        {
            var aprops = AudioEncodingProperties.CreatePcm(
                (uint)_audioRate, (uint)_audioChannels, 16);
            _mss = new MediaStreamSource(_videoDesc, new AudioStreamDescriptor(aprops));
        }
        else
            _mss = new MediaStreamSource(_videoDesc);
        _mss.BufferTime = TimeSpan.Zero;
        _mss.CanSeek = false;
        _mss.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        _mss.SampleRequested += OnSampleRequested;
    }

    void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        if (request.StreamDescriptor is VideoStreamDescriptor) ServeVideo(request);
        else ServeAudio(request);
    }

    void ServeVideo(MediaStreamSourceSampleRequest request)
    {
        var deferral = request.GetDeferral();
        try
        {
            while (true)
            {
                Direct3D11CaptureFrame? frame;
                lock (_frameLock) { frame = _latestFrame; _latestFrame = null; }
                if (frame is not null)
                {
                    // First consumed frame anchors t=0; QPC deltas after.
                    _firstFrameTime ??= frame.SystemRelativeTime;
                    var ts = frame.SystemRelativeTime - _firstFrameTime.Value;
                    var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, ts);
                    // The surface is pool-owned; the frame must outlive the
                    // encoder's read. Processed fires when the sample is
                    // consumed — dispose there, never eagerly.
                    sample.Processed += (_, _) => frame.Dispose();
                    request.Sample = sample;
                    Interlocked.Exchange(ref _lastVideoTicks, ts.Ticks);
                    return;
                }
                if (_stopping) return; // no sample set → end of stream
                _frameReady.WaitOne(250);
            }
        }
        finally { deferral.Complete(); }
    }

    void ServeAudio(MediaStreamSourceSampleRequest request)
    {
        var q = _audioQueue;
        if (q is null) return;
        var deferral = request.GetDeferral();
        try
        {
            int blockAlign = _audioChannels * 2;
            while (true)
            {
                byte[]? chunk = null;
                try { q.TryTake(out chunk, 100); }
                catch (ObjectDisposedException) { return; }
                if (chunk is not null)
                {
                    var sample = MediaStreamSample.CreateFromBuffer(
                        CryptographicBuffer.CreateFromByteArray(chunk),
                        TimeSpan.FromTicks(_audioTicks));
                    long frames = chunk.Length / blockAlign;
                    _audioTicks += frames * TimeSpan.TicksPerSecond / _audioRate;
                    sample.Duration = TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / _audioRate);
                    request.Sample = sample;
                    return;
                }
                if (q.IsAddingCompleted || _stopping) return; // EOS
                // Quiet game = no loopback packets. Keep the mux fed with
                // silence, but only while the audio clock trails video —
                // filler must never outrun real time or the tracks skew.
                if (_audioTicks < Interlocked.Read(ref _lastVideoTicks))
                {
                    int fillFrames = _audioRate / 50; // 20 ms
                    var silence = new byte[fillFrames * blockAlign];
                    var sample = MediaStreamSample.CreateFromBuffer(
                        CryptographicBuffer.CreateFromByteArray(silence),
                        TimeSpan.FromTicks(_audioTicks));
                    _audioTicks += fillFrames * TimeSpan.TicksPerSecond / _audioRate;
                    sample.Duration = TimeSpan.FromTicks(fillFrames * TimeSpan.TicksPerSecond / _audioRate);
                    request.Sample = sample;
                    return;
                }
            }
        }
        finally { deferral.Complete(); }
    }

    // ---- encode ---------------------------------------------------------

    async Task RunTranscodeAsync(string outputDir, string fileStem, int w, int h)
    {
        string? finishedPath = null;
        try
        {
            // Explicit profile — presets break with MediaStreamSource input.
            var profile = new MediaEncodingProfile
            {
                Container = new ContainerEncodingProperties { Subtype = MediaEncodingSubtypes.Mpeg4 },
                Video = VideoEncodingProperties.CreateH264(),
            };
            profile.Video.Width = (uint)(w & ~1); // H.264 wants even dims
            profile.Video.Height = (uint)(h & ~1);
            profile.Video.Bitrate = (uint)Math.Clamp((long)w * h * 10, 8_000_000, 40_000_000);
            profile.Video.FrameRate.Numerator = 60;
            profile.Video.FrameRate.Denominator = 1;
            if (_audioQueue is not null)
                profile.Audio = AudioEncodingProperties.CreateAac(
                    (uint)_audioRate, (uint)_audioChannels, 192000);

            var folder = await StorageFolder.GetFolderFromPathAsync(outputDir);
            var file = await folder.CreateFileAsync(fileStem + ".mp4",
                CreationCollisionOption.GenerateUniqueName);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prep = await transcoder.PrepareMediaStreamSourceTranscodeAsync(
                _mss, stream, profile);
            if (!prep.CanTranscode)
                throw new InvalidOperationException($"encoder rejected the stream ({prep.FailureReason})");
            await prep.TranscodeAsync();
            finishedPath = file.Path;
        }
        catch (Exception ex)
        {
            StatusLines.Enqueue($"Recording failed ({ex.Message}).");
        }
        finally
        {
            var length = _clock?.Elapsed ?? TimeSpan.Zero;
            CleanupCapture();
            IsRecording = false;
            if (finishedPath is not null)
            {
                OutputPath = finishedPath;
                StatusLines.Enqueue(
                    $"Recording saved ({length:mm\\:ss}) — {Path.GetFileName(finishedPath)}");
            }
        }
    }

    void CleanupCapture()
    {
        _stopping = true;
        try { _session?.Dispose(); } catch { }
        _session = null;
        if (_framePool is not null)
        {
            try { _framePool.FrameArrived -= OnFrameArrived; } catch { }
            try { _framePool.Dispose(); } catch { }
            _framePool = null;
        }
        lock (_frameLock) { _latestFrame?.Dispose(); _latestFrame = null; }
        _item = null;
        try { _d3dDevice?.Dispose(); } catch { }
        _d3dDevice = null;
        if (_loopback is not null)
        {
            try { _loopback.DataAvailable -= OnAudioData; } catch { }
            try { _loopback.Dispose(); } catch { }
            _loopback = null;
        }
        try { _audioQueue?.Dispose(); } catch { }
        _audioQueue = null;
        _mss = null;
        _videoDesc = null;
        _clock = null;
    }

    // ---- WinRT / D3D interop --------------------------------------------

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software,
        uint flags, nint featureLevels, uint numFeatureLevels, uint sdkVersion,
        out nint device, out uint featureLevel, out nint context);

    [DllImport("d3d11.dll")]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(nint hstring);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(nint window, ref Guid iid, out nint result);
        [PreserveSig] int CreateForMonitor(nint monitor, ref Guid iid, out nint result);
    }

    static readonly Guid IidIDxgiDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    static readonly Guid IidIGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>A dedicated D3D11 device for the capture pool — independent
    /// of the game's GL context (WGC hands us compositor surfaces; the two
    /// pipelines never touch).</summary>
    static IDirect3DDevice CreateD3DDevice()
    {
        const uint DriverTypeHardware = 1;
        const uint BgraSupport = 0x20;
        const uint SdkVersion = 7;
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, DriverTypeHardware, 0,
            BgraSupport, 0, 0, SdkVersion, out nint device, out _, out nint context));
        try
        {
            var iid = IidIDxgiDevice;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out nint dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out nint winrt));
                try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winrt); }
                finally { Marshal.Release(winrt); }
            }
            finally { Marshal.Release(dxgi); }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    /// <summary>GraphicsCaptureItem for a raw HWND — only reachable through
    /// the IGraphicsCaptureItemInterop factory (no projected API). The
    /// requested IID must be the ITEM INTERFACE guid, not the runtime
    /// class.</summary>
    static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out nint hstr));
        try
        {
            var interopIid = typeof(IGraphicsCaptureItemInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstr, ref interopIid, out nint factoryPtr));
            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                var itemIid = IidIGraphicsCaptureItem;
                Marshal.ThrowExceptionForHR(interop.CreateForWindow(hwnd, ref itemIid, out nint itemPtr));
                try { return GraphicsCaptureItem.FromAbi(itemPtr); }
                finally { Marshal.Release(itemPtr); }
            }
            finally { Marshal.Release(factoryPtr); }
        }
        finally { WindowsDeleteString(hstr); }
    }
}

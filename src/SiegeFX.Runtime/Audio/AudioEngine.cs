using Silk.NET.OpenAL;

namespace SiegeFX.Runtime.Audio;

/// <summary>
/// Tiny OpenAL Soft wrapper. Phase 18a-only feature set: 2D playback of
/// pre-registered PCM clips through a fixed pool of voices. No streaming,
/// no per-source positioning, no listener orientation — adding spatial
/// audio is the Phase 18b job once monsters need to make noise.
///
/// All public methods are no-ops when <see cref="TryCreate"/> returned
/// null (no audio device available — CI, headless box, broken driver).
/// Callers can keep <c>_audio?.Play("zap_cast")</c> as a one-liner without
/// branching, which is what RenderHost relies on.
/// </summary>
public sealed unsafe class AudioEngine : IDisposable
{
    readonly AL _al;
    readonly ALContext _alc;
    readonly Device* _device;
    readonly Context* _context;
    readonly Dictionary<string, uint> _bufferByClip = new(StringComparer.OrdinalIgnoreCase);
    readonly uint[] _sourcePool;
    int _nextSource;
    bool _disposed;

    AudioEngine(AL al, ALContext alc, Device* device, Context* context, uint[] sources)
    {
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        _sourcePool = sources;
    }

    /// <summary>Open the default device, create + activate a context, and
    /// pre-allocate <paramref name="voices"/> sources. Returns null on any
    /// OpenAL failure — the runtime should still be playable without sound.</summary>
    public static AudioEngine? TryCreate(int voices = 16)
    {
        AL? al = null;
        ALContext? alc = null;
        Device* device = null;
        Context* context = null;

        try
        {
            alc = ALContext.GetApi(soft: true);
            al  = AL.GetApi(soft: true);
            device = alc.OpenDevice("");
            if (device == null)
            {
                Console.Error.WriteLine("  audio: OpenDevice failed (no default device)");
                return null;
            }
            context = alc.CreateContext(device, null);
            if (context == null)
            {
                Console.Error.WriteLine("  audio: CreateContext failed");
                alc.CloseDevice(device);
                return null;
            }
            if (!alc.MakeContextCurrent(context))
            {
                Console.Error.WriteLine("  audio: MakeContextCurrent failed");
                alc.DestroyContext(context);
                alc.CloseDevice(device);
                return null;
            }

            var sources = new uint[voices];
            fixed (uint* p = sources) al.GenSources(voices, p);

            Console.WriteLine($"  audio: OpenAL Soft up ({voices} voices)");
            return new AudioEngine(al, alc, device, context, sources);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  audio: init failed — {ex.Message}");
            try
            {
                if (context != null) alc?.DestroyContext(context);
                if (device  != null) alc?.CloseDevice(device);
            }
            catch { }
            return null;
        }
    }

    /// <summary>Decode a WAV byte array, upload to a buffer, and key it by
    /// <paramref name="id"/>. Subsequent calls with the same id replace the
    /// previous buffer. Returns false (and logs) on any failure so the
    /// caller can keep going — a missing SFX is not a fatal error.</summary>
    public bool RegisterClip(string id, byte[] wavBytes)
    {
        if (_disposed) return false;
        try
        {
            var clip = WavLoader.Parse(wavBytes);
            var fmt = (clip.Channels, clip.BitsPerSample) switch
            {
                (1, 8)  => BufferFormat.Mono8,
                (1, 16) => BufferFormat.Mono16,
                (2, 8)  => BufferFormat.Stereo8,
                (2, 16) => BufferFormat.Stereo16,
                _ => throw new InvalidDataException(
                    $"unsupported {clip.Channels}ch/{clip.BitsPerSample}b combo"),
            };

            uint buf;
            _al.GenBuffers(1, &buf);
            fixed (byte* p = clip.Samples)
                _al.BufferData(buf, fmt, p, clip.Samples.Length, clip.SampleRate);

            if (_bufferByClip.TryGetValue(id, out var prev))
            {
                _al.DeleteBuffers(1, &prev);
            }
            _bufferByClip[id] = buf;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  audio: clip '{id}' load failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Play a registered clip on the next pool source (round-robin).
    /// If the source is currently busy, OpenAL replaces its buffer and starts
    /// over — that's the desired behavior for cast SFX (rapid-fire keys
    /// shouldn't queue, the latest cast wins).</summary>
    public void Play(string id, float gain = 1f)
    {
        if (_disposed) return;
        if (!_bufferByClip.TryGetValue(id, out var buf)) return;

        uint src = _sourcePool[_nextSource];
        _nextSource = (_nextSource + 1) % _sourcePool.Length;

        _al.SourceStop(src);
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceFloat.Gain, gain);
        _al.SourcePlay(src);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            for (int i = 0; i < _sourcePool.Length; i++)
                _al.SourceStop(_sourcePool[i]);
            fixed (uint* p = _sourcePool) _al.DeleteSources(_sourcePool.Length, p);

            foreach (var buf in _bufferByClip.Values)
            {
                var b = buf;
                _al.DeleteBuffers(1, &b);
            }
            _bufferByClip.Clear();

            _alc.MakeContextCurrent(null);
            if (_context != null) _alc.DestroyContext(_context);
            if (_device  != null) _alc.CloseDevice(_device);
        }
        catch { }
    }
}

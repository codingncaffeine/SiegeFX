using System.Numerics;
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
    // Phase 18b — variant groups (e.g. "melee_swing" → 4 swing wavs).
    // Play(group) picks a random member; degenerates to a single id if
    // only one was registered. The dictionary is _separate_ from
    // _bufferByClip so simple ids and group ids can share namespace
    // without colliding (e.g. "melee_swing" group vs "melee_swing_01"
    // single — Play looks up groups first, falls back to single).
    readonly Dictionary<string, List<string>> _variantsByGroup =
        new(StringComparer.OrdinalIgnoreCase);
    readonly uint[] _sourcePool;
    int _nextSource;
    readonly Random _variantRng = new();
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

            // Phase 18c — clamped inverse-distance attenuation. Past
            // MaxDistance the source contributes 0 gain rather than
            // continuing to fade slowly, which keeps a goblin scream
            // 200 units away from leaking into the mix as a faint hiss.
            // Per-source ReferenceDistance/MaxDistance/RolloffFactor are
            // still set in PlayAt so different SFX can have different
            // audible ranges later (footsteps shorter than spell casts).
            al.DistanceModel(DistanceModel.InverseDistanceClamped);

            Console.WriteLine($"  audio: OpenAL Soft up ({voices} voices, " +
                              $"InverseDistanceClamped attenuation)");
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
                // OpenAL refuses DeleteBuffers on a buffer still bound to
                // a source — would return AL_INVALID_OPERATION and leak.
                // Walk the pool, stop + unbind any source pointing at the
                // old buffer, then delete. Re-registration only happens
                // on re-LoadPlayActors but the guard keeps the contract
                // clean for a future hot-reload path.
                for (int i = 0; i < _sourcePool.Length; i++)
                {
                    _al.GetSourceProperty(_sourcePool[i], GetSourceInteger.Buffer, out int bound);
                    if ((uint)bound == prev)
                    {
                        _al.SourceStop(_sourcePool[i]);
                        _al.SetSourceProperty(_sourcePool[i], SourceInteger.Buffer, 0);
                    }
                }
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

    /// <summary>Phase 18b — register a list of clip ids as one named group.
    /// <see cref="Play(string,float)"/> with the group id picks a random
    /// member each call. Used for "this happens often" SFX where DS1 ships
    /// 4-5 variants to avoid a single-asset machine-gun feel (swings,
    /// flesh hits). Per-variant clips must already be registered via
    /// <see cref="RegisterClip"/> before being grouped.</summary>
    public void RegisterGroup(string groupId, params string[] clipIds)
    {
        if (_disposed) return;
        var alive = new List<string>(clipIds.Length);
        foreach (var id in clipIds)
        {
            if (_bufferByClip.ContainsKey(id)) alive.Add(id);
            else Console.Error.WriteLine(
                $"  audio: group '{groupId}' missing variant '{id}' — typo or unregistered clip");
        }
        if (alive.Count == 0)
        {
            Console.Error.WriteLine($"  audio: group '{groupId}' empty — skipping");
            return;
        }
        _variantsByGroup[groupId] = alive;
    }

    /// <summary>Play a registered clip (or random member of a registered
    /// group) on the next pool source (round-robin). 2D playback —
    /// SourceRelative=true with position (0,0,0) keeps the SFX
    /// listener-locked, which is what we want for "this comes from the
    /// player" cues (cast, level-up, swing). For "this happens out
    /// there in the world" cues, see <see cref="PlayAt"/>.</summary>
    public void Play(string id, float gain = 1f)
    {
        if (!Resolve(id, out uint buf)) return;
        uint src = NextSource();
        _al.SourceStop(src);
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceFloat.Gain, gain);
        // Listener-relative + zero position = always centered, no falloff.
        _al.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
        _al.SetSourceProperty(src, SourceVector3.Position, 0f, 0f, 0f);
        _al.SourcePlay(src);
    }

    /// <summary>Phase 18c — play with a world-space position. The source
    /// pans + attenuates against the listener pose set via
    /// <see cref="UpdateListener"/>. Reference and max distance are
    /// chosen for the DS1 unit scale (≈1 unit = 1 ft): full volume out
    /// to ~6 units, audible to ~40 units, silent past that.</summary>
    public void PlayAt(string id, Vector3 worldPos, float gain = 1f,
                       float refDistance = 6f, float maxDistance = 40f)
    {
        if (!Resolve(id, out uint buf)) return;
        uint src = NextSource();
        _al.SourceStop(src);
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceFloat.Gain, gain);
        _al.SetSourceProperty(src, SourceBoolean.SourceRelative, false);
        _al.SetSourceProperty(src, SourceFloat.ReferenceDistance, refDistance);
        _al.SetSourceProperty(src, SourceFloat.MaxDistance, maxDistance);
        _al.SetSourceProperty(src, SourceFloat.RolloffFactor, 1.0f);
        // SiegeFX world coords already match OpenAL's frame: Camera.Forward
        // at yaw=0 is (0,0,-1), so -Z is forward in both systems. No flip
        // needed — an early review-fix attempt double-flipped and panned
        // every spatial cue backwards.
        _al.SetSourceProperty(src, SourceVector3.Position,
                              worldPos.X, worldPos.Y, worldPos.Z);
        _al.SourcePlay(src);
    }

    /// <summary>Phase 18c — listener pose for spatial mixing. Call once
    /// per render frame from RenderHost with the player's position +
    /// facing. Up vector is fixed Y-up; pitch/roll get sampled from the
    /// camera, not the player. Idempotent — OpenAL just stores the new
    /// pose against the next mix.</summary>
    public void UpdateListener(Vector3 pos, Vector3 forward, Vector3 up)
    {
        if (_disposed) return;
        _al.SetListenerProperty(ListenerVector3.Position, pos.X, pos.Y, pos.Z);
        // Orientation is at-vector then up-vector, contiguous. No Z flip:
        // SiegeFX and OpenAL share the -Z=forward convention, so passing
        // Camera.Forward through verbatim keeps panning consistent with
        // PlayAt. (An earlier review-fix attempt flipped Z here and in
        // PlayAt, which made every spatial cue pan backwards.)
        Span<float> ori = stackalloc float[6]
        {
            forward.X, forward.Y, forward.Z,
            up.X,      up.Y,      up.Z,
        };
        fixed (float* p = ori) _al.SetListenerProperty(ListenerFloatArray.Orientation, p);
    }

    bool Resolve(string id, out uint buf)
    {
        buf = 0;
        if (_disposed) return false;
        if (_variantsByGroup.TryGetValue(id, out var variants))
        {
            var pick = variants[_variantRng.Next(variants.Count)];
            return _bufferByClip.TryGetValue(pick, out buf);
        }
        return _bufferByClip.TryGetValue(id, out buf);
    }

    uint NextSource()
    {
        uint src = _sourcePool[_nextSource];
        _nextSource = (_nextSource + 1) % _sourcePool.Length;
        return src;
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

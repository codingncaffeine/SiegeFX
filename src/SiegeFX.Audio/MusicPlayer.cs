using NLayer;
using Silk.NET.OpenAL;

namespace SiegeFX.Audio;

/// <summary>Phase 22-SC-MUSIC-A — streaming mp3 → OpenAL playback.
/// DS1 ships 131 mp3 music tracks at /sound/music/s_m_*.mp3 inside
/// Sound.dsres (1-4 MB each, 5-10 minutes long). Loading each fully
/// into an OpenAL buffer would burn megabytes the moment a region
/// loaded; we stream instead — keep three OpenAL buffers in flight
/// and refill drained ones from the NLayer mp3 decoder on each
/// <see cref="Tick"/> call.
///
/// <para>Lifetime: one MusicPlayer for the whole session. <see cref="Play"/>
/// stops any current track and starts a new one; <see cref="Stop"/>
/// flushes the queue. Safe to call all methods if <see cref="TryCreate"/>
/// returned null (no audio device — same graceful-fail contract
/// AudioEngine uses).</para>
///
/// <para>Why separate from <see cref="AudioEngine"/>: SFX clips are
/// short (a few KB), preloaded once, and play many concurrent voices.
/// Music is multi-megabyte, single-channel, and needs streaming
/// because eagerly buffering a 4 MB mp3 means a 30+ MB decoded PCM
/// (6 minutes at 44.1 kHz × 16-bit × 2ch). Streaming caps the live
/// PCM footprint at three buffer chunks — about a quarter-second of
/// audio at 44.1 kHz / 16-bit / stereo.</para></summary>
public sealed unsafe class MusicPlayer : IDisposable
{
    readonly AL _al;
    readonly uint _source;
    readonly uint[] _bufferPool;
    // 16-bit stereo at 44.1 kHz = 176400 bytes/sec; ~92ms per chunk so a
    // three-chunk queue keeps ~280ms of audio ahead of the listener — plenty
    // of headroom for a 60-fps Tick to refill before underrun, but small
    // enough that a Stop reacts within a quarter-second.
    const int ChunkBytes = 16384;
    // NLayer 1.16.0's byte-array ReadSamples overload emits noise on the
    // DS1 mp3 corpus (verified via int16 dump: chaotic adjacent samples,
    // CLI playback = static). The float[] overload is the NAudio-canonical
    // path. We decode floats then pack to int16 PCM ourselves.
    const int FloatsPerChunk = ChunkBytes / 2; // 16-bit int = 1 float in
    readonly float[] _floatScratch = new float[FloatsPerChunk];
    readonly byte[] _pcmScratch = new byte[ChunkBytes];

    MpegFile? _decoder;
    BufferFormat _decodedFormat;
    int _decodedSampleRate;
    bool _eosReached;
    bool _disposed;
    bool _loop;
    byte[]? _trackBytes; // kept around so we can re-seat the decoder on loop
    float _volume = 0.7f; // music defaults a touch under SFX so the mix doesn't drown effects

    /// <summary>True once <see cref="Play"/> has queued at least one chunk
    /// and we haven't observed the OpenAL source go fully Stopped (which
    /// happens naturally at end-of-track if the queue drained and EOS
    /// hit). Read it from the host loop to know when to advance to the
    /// next track in a playlist.</summary>
    public bool IsPlaying
    {
        get
        {
            if (_disposed || _decoder is null) return false;
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            return state == (int)SourceState.Playing || state == (int)SourceState.Paused;
        }
    }

    MusicPlayer(AL al, uint source, uint[] buffers)
    {
        _al = al;
        _source = source;
        _bufferPool = buffers;
    }

    /// <summary>Construct on top of the existing <see cref="AudioEngine"/>'s
    /// OpenAL context. Returns null if AudioEngine.TryCreate returned null
    /// (no device) — caller branches on null the same way it does for
    /// AudioEngine. Cheap to call: GenSources(1) + GenBuffers(3) only.</summary>
    public static MusicPlayer? TryCreate(AudioEngine? engine)
    {
        if (engine is null) return null;
        var al = engine.GetAl();
        if (al is null) return null;
        try
        {
            uint src;
            al.GenSources(1, &src);
            al.SetSourceProperty(src, SourceFloat.Gain, 0.7f);
            // Music is non-positional — it should ride at full volume in
            // both ears regardless of camera placement. SourceRelative=true
            // pins the source at the listener so distance attenuation and
            // panning stay flat.
            al.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
            al.SetSourceProperty(src, SourceVector3.Position, 0f, 0f, 0f);

            var bufs = new uint[3];
            fixed (uint* p = bufs) al.GenBuffers(bufs.Length, p);
            return new MusicPlayer(al, src, bufs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  music: MusicPlayer init failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>Stop any current track, decode <paramref name="mp3Bytes"/>
    /// header to learn channel count + sample rate, prime the queue with
    /// three chunks, and start playback. Safe to call back-to-back without
    /// an explicit Stop in between — the new Play implies a stop. Returns
    /// false if NLayer can't open the stream. <paramref name="loop"/>
    /// (default true) re-seats the decoder at EOS so DS1 region/menu
    /// music loops indefinitely; pass false for one-shot stings.</summary>
    public bool Play(byte[] mp3Bytes, bool loop = true)
    {
        if (_disposed) return false;
        Stop();
        try
        {
            _trackBytes = mp3Bytes;
            _loop = loop;
            _decoder = new MpegFile(new MemoryStream(mp3Bytes, writable: false));
            _decodedSampleRate = _decoder.SampleRate;
            _decodedFormat = _decoder.Channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
            _eosReached = false;
            // Prime every buffer in the pool before starting playback so
            // the source has the full ~280ms cushion the first time it
            // hits the speakers; otherwise the first frame plays a single
            // chunk's worth and underflows on the next tick.
            int primed = 0;
            int totalBytes = 0;
            for (int i = 0; i < _bufferPool.Length; i++)
            {
                int before = _eosReached ? -1 : 0;
                if (!FillBuffer(_bufferPool[i])) break;
                uint b = _bufferPool[i];
                _al.SourceQueueBuffers(_source, 1, &b);
                primed++;
                totalBytes += ChunkBytes; // approximate; real fill could be < ChunkBytes only at EOS
            }
            if (primed == 0) { Stop(); return false; }
            Console.WriteLine($"  music: prime — {primed} chunk(s), {_decodedSampleRate}Hz {(_decodedFormat == BufferFormat.Stereo16 ? "stereo" : "mono")}16, ~{(totalBytes * 1000) / Math.Max(1, _decodedSampleRate * (_decodedFormat == BufferFormat.Stereo16 ? 4 : 2))}ms cushion");
            _al.SourcePlay(_source);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  music: Play failed — {ex.Message}");
            Stop();
            return false;
        }
    }

    /// <summary>Stop playback and release the decoder. Idempotent — safe
    /// to call when nothing's playing.</summary>
    public void Stop()
    {
        if (_disposed) return;
        _al.SourceStop(_source);
        // Phase 22-SC-MUSIC-FOLD — alSourcei(src, AL_BUFFER, 0) atomically
        // detaches every buffer from the source regardless of which are
        // queued vs processed. The original drain-loop relied on
        // SourceUnqueueBuffers in a state where SourceStop is async on
        // some drivers — buffers wouldn't be in the "Processed" state
        // yet, alSourceUnqueueBuffers would return AL_INVALID_OPERATION,
        // and the local counter would still tick down (decoupled from
        // the actual AL state) leaving buffers attached. Subsequent
        // DeleteBuffers in Dispose would then leak. The single-binding
        // detach is the canonical idiom for "stop + clear queue".
        _al.SetSourceProperty(_source, SourceInteger.Buffer, 0);
        _decoder?.Dispose();
        _decoder = null;
        _trackBytes = null;
        _eosReached = false;
        _loop = false;
    }

    /// <summary>Set music volume in [0,1]. Independent of SFX volume —
    /// the host carries one slider for music, one for SFX, in the
    /// eventual settings UI (Phase 21e).</summary>
    public void SetVolume(float volume)
    {
        if (_disposed) return;
        _volume = Math.Clamp(volume, 0f, 1f);
        _al.SetSourceProperty(_source, SourceFloat.Gain, _volume);
    }

    public float Volume => _volume;

    /// <summary>Pump the queue: for every buffer the source has finished
    /// with (BuffersProcessed > 0), decode the next chunk and re-queue.
    /// Call once per frame from the host loop. Cheap when nothing's
    /// playing (one OpenAL state read). Returns true while a track is
    /// actively streaming, false once EOS has been hit AND the queue
    /// has drained — caller can use the false transition to advance
    /// to the next track in a playlist.</summary>
    public bool Tick()
    {
        if (_disposed || _decoder is null) return false;

        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        while (processed-- > 0)
        {
            uint reusable;
            _al.SourceUnqueueBuffers(_source, 1, &reusable);
            if (_eosReached) continue; // don't bother refilling once the decoder ran out
            if (!FillBuffer(reusable)) continue;
            _al.SourceQueueBuffers(_source, 1, &reusable);
        }

        // Phase 23-SC-OPTIONS-FOLD — distinguish natural EOS from buffer
        // underrun. Music starts during the synchronous LoadPlayActors
        // region build (5800+ static props, neighbor-region preload, nav
        // mesh weld); no Tick fires until the render loop spins up several
        // seconds later. The 3-buffer 280ms cushion drains long before then,
        // so the source goes Stopped from underrun rather than from the
        // decoder running out. The pre-fold path treated every Stopped
        // state as "track ended", cleared _currentMusicTrack on the host
        // side, and the player heard 1-2 seconds of static then nothing.
        // Recovery: if the source stopped while we still have decoder
        // bytes ahead of us, refill any queued buffer and SourcePlay
        // again. Real EOS = _eosReached is set AND the queue is empty,
        // and only then do we report "track finished" so the host
        // advances.
        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
        if (state != (int)SourceState.Playing && state != (int)SourceState.Paused)
        {
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);
            if (_eosReached && queued == 0)
            {
                // Track finished. Caller can drive the next track via Play().
                return false;
            }
            // Stopped mid-track: either an underrun we need to ride out, or
            // EOS partway through the pool with the tail still queued.
            // Either way, kick the source back to Playing so the queued
            // buffers actually drain. SourcePlay on a queue with content
            // is the canonical OpenAL "resume after underrun" idiom.
            if (queued > 0) _al.SourcePlay(_source);
        }
        return true;
    }

    /// <summary>Decode up to <see cref="ChunkBytes"/> bytes of PCM into
    /// the buffer's data store. Returns false at end-of-stream (caller
    /// stops re-queueing this buffer; the source drains and stops on its
    /// own).</summary>
    bool FillBuffer(uint buffer)
    {
        if (_decoder is null) return false;
        int floatsWritten = 0;
        while (floatsWritten < FloatsPerChunk)
        {
            // NLayer's float ReadSamples is the NAudio-canonical path —
            // returns interleaved [-1, 1] float samples. The byte-array
            // overload in 1.16.0 produces garbage on at least the DS1
            // corpus, which is why we go through floats.
            int got = _decoder.ReadSamples(_floatScratch, floatsWritten,
                                           FloatsPerChunk - floatsWritten);
            if (got <= 0)
            {
                // Loop mode: re-seat the decoder at the start and keep
                // filling this same chunk so the loop point is sample-
                // continuous (no audible click from queueing a partial
                // tail buffer). Re-creating MpegFile on the cached bytes
                // is cheap relative to the rest of the audio path and
                // avoids depending on NLayer's seek implementation.
                if (_loop && _trackBytes is not null)
                {
                    _decoder.Dispose();
                    _decoder = new MpegFile(new MemoryStream(_trackBytes, writable: false));
                    continue;
                }
                _eosReached = true;
                break;
            }
            floatsWritten += got;
        }
        if (floatsWritten == 0) return false;
        // Pack floats → little-endian int16 PCM. Hot path; clamp + cast
        // is faster than Math.Clamp/Math.Round and matches the canonical
        // NAudio quantization (truncation, not dither — fine for music).
        for (int i = 0; i < floatsWritten; i++)
        {
            float f = _floatScratch[i] * 32767f;
            short s = f >  32767f ?  (short) 32767 :
                      f < -32768f ?  (short)-32768 :
                                     (short)f;
            _pcmScratch[i * 2]     = (byte)(s & 0xff);
            _pcmScratch[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        int byteCount = floatsWritten * 2;
        fixed (byte* p = _pcmScratch)
            _al.BufferData(buffer, _decodedFormat, p, byteCount, _decodedSampleRate);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        uint src = _source;
        _al.DeleteSources(1, &src);
        for (int i = 0; i < _bufferPool.Length; i++)
        {
            uint buf = _bufferPool[i];
            _al.DeleteBuffers(1, &buf);
        }
    }
}

// Phase 22-SC-MUSIC-FOLD — the AudioEngineMusicAccess extension that
// previously lived in this file was deleted; MusicPlayer.TryCreate
// calls AudioEngine.GetAl() directly. Both types live in the same
// SiegeFX.Audio assembly so the internal accessor is enough.

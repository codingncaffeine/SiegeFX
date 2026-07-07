using System;
using System.IO;
using System.Media;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Plays DS1 audio in-app. WAV (SFX + voices) plays through <see cref="SoundPlayer"/> from
/// the original RIFF/PCM bytes (no header rebuild) with a rendered waveform; MP3 (music) plays
/// through <see cref="MediaPlayer"/> from a temp file (Media Foundation decode, no external libs).
/// Disposable — stops playback and cleans the temp file when the preview is swapped out.</summary>
public sealed class AudioViewerViewModel : ObservableObject, IDisposable
{
    public string Name { get; }
    public string Info { get; }
    public bool IsWav { get; }
    public bool IsMp3 => !IsWav;

    private readonly byte[] _bytes;

    // WAV playback
    private SoundPlayer? _player;
    private MemoryStream? _wavStream;
    private ImageSource? _waveform;
    public ImageSource? Waveform { get => _waveform; private set => SetProperty(ref _waveform, value); }

    // MP3 playback
    private MediaPlayer? _media;
    private string? _tempPath;
    private double _volume = 0.8;
    public double Volume
    {
        get => _volume;
        set { if (SetProperty(ref _volume, value) && _media is not null) _media.Volume = value; }
    }

    private string _status = "";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public RelayCommand PlayCommand { get; }
    public RelayCommand StopCommand { get; }

    public AudioViewerViewModel(string name, byte[] bytes, bool isWav)
    {
        Name = name;
        _bytes = bytes;
        IsWav = isWav;
        PlayCommand = new RelayCommand(_ => Play());
        StopCommand = new RelayCommand(_ => Stop());

        if (isWav)
        {
            try
            {
                var wav = WavAudio.Parse(bytes);
                Info = $"WAV · {wav.SampleRate:N0} Hz · {wav.Channels} ch · {wav.BitsPerSample}-bit · {wav.DurationSeconds:F2}s · {Format.Bytes(bytes.Length)}";
                BuildWaveform(wav);
            }
            catch (Exception ex)
            {
                Info = "WAV (metadata unavailable): " + ex.Message;
            }
        }
        else
        {
            Info = $"MP3 · {Format.Bytes(bytes.Length)} · press Play to decode";
        }
    }

    private void BuildWaveform(WavAudio wav)
    {
        const int w = 900, h = 200;
        var (mins, maxs) = wav.Envelope(w);
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = 0x16; px[i + 1] = 0x14; px[i + 2] = 0x14; px[i + 3] = 0xFF; }

        int mid = h / 2, amp = h / 2 - 2;
        for (int x = 0; x < w; x++)
        {
            int y0 = mid - (int)(maxs[x] * amp);
            int y1 = mid - (int)(mins[x] * amp);
            if (y0 > y1) (y0, y1) = (y1, y0);
            for (int y = y0; y <= y1; y++)
            {
                if ((uint)y >= (uint)h) continue;
                int pi = (y * w + x) * 4;
                px[pi] = 0x57; px[pi + 1] = 0xA6; px[pi + 2] = 0xD8; px[pi + 3] = 0xFF; // bronze
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        Waveform = bmp;
    }

    private void Play()
    {
        if (IsWav) PlayWav(); else PlayMp3();
    }

    private void PlayWav()
    {
        try
        {
            _player?.Stop();
            _wavStream?.Dispose();
            _wavStream = new MemoryStream(_bytes, writable: false);
            _player = new SoundPlayer(_wavStream);
            _player.Play(); // non-blocking; renders on its own thread from the in-memory buffer
            Status = "Playing…";
        }
        catch (Exception ex) { Status = "Playback failed: " + ex.Message; }
    }

    private void PlayMp3()
    {
        try
        {
            _media ??= new MediaPlayer { Volume = _volume };
            if (_tempPath is null)
            {
                _tempPath = Path.Combine(Path.GetTempPath(), "siegesmith_preview_" + Guid.NewGuid().ToString("N") + ".mp3");
                File.WriteAllBytes(_tempPath, _bytes);
            }
            _media.Open(new Uri(_tempPath));
            _media.Play();
            Status = "Playing…";
        }
        catch (Exception ex) { Status = "Playback failed: " + ex.Message; }
    }

    private void Stop()
    {
        try { _player?.Stop(); } catch { }
        try { _media?.Stop(); } catch { }
        Status = "Stopped";
    }

    public void Dispose()
    {
        try { _player?.Stop(); } catch { }
        _wavStream?.Dispose();
        if (_media is not null)
        {
            try { _media.Stop(); _media.Close(); } catch { }
        }
        if (_tempPath is not null)
        {
            try { File.Delete(_tempPath); } catch { }
            _tempPath = null;
        }
    }
}

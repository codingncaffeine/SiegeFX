using System;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>Minimal RIFF/WAVE reader for the audio preview: parses the fmt chunk
/// (channels/rate/bits) and locates the PCM data chunk, and builds a downsampled min/max envelope
/// for the waveform display. DS1 wavs are plain PCM (8/16-bit, mono/stereo), so this covers the
/// whole corpus; anything else throws and the caller degrades to the hex view. The original bytes
/// are already a valid RIFF/PCM stream, so playback hands them straight to SoundPlayer — no header
/// rebuild needed.</summary>
public sealed class WavAudio
{
    public int Channels { get; private init; }
    public int BitsPerSample { get; private init; }
    public int SampleRate { get; private init; }
    public int DataOffset { get; private init; }
    public int DataLength { get; private init; }

    private readonly byte[] _bytes;
    private WavAudio(byte[] bytes) => _bytes = bytes;

    public double DurationSeconds
    {
        get
        {
            int frame = Channels * (BitsPerSample / 8);
            return frame == 0 ? 0 : DataLength / (double)(SampleRate * frame);
        }
    }

    public static WavAudio Parse(byte[] bytes)
    {
        if (bytes.Length < 44) throw new InvalidOperationException("WAV too small");
        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
            throw new InvalidOperationException("not a RIFF file");
        if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
            throw new InvalidOperationException("not a WAVE file");

        int pos = 12, channels = 0, bits = 0, rate = 0, dataOff = 0, dataLen = 0;
        while (pos + 8 <= bytes.Length)
        {
            string id = Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int start = pos + 8;
            if (start + size > bytes.Length) size = bytes.Length - start;

            if (id == "fmt " && size >= 16)
            {
                short fmt = BitConverter.ToInt16(bytes, start);
                if (fmt != 1) throw new InvalidOperationException($"WAV format {fmt} not PCM");
                channels = BitConverter.ToInt16(bytes, start + 2);
                rate = BitConverter.ToInt32(bytes, start + 4);
                bits = BitConverter.ToInt16(bytes, start + 14);
            }
            else if (id == "data")
            {
                dataOff = start;
                dataLen = size;
            }

            int next = start + size + (size & 1); // word-aligned chunks
            if (next <= pos) break;
            pos = next;
        }

        if (channels is not (1 or 2) || bits is not (8 or 16) || rate == 0 || dataLen == 0)
            throw new InvalidOperationException("unsupported or empty WAV");

        return new WavAudio(bytes)
        {
            Channels = channels, BitsPerSample = bits, SampleRate = rate,
            DataOffset = dataOff, DataLength = dataLen,
        };
    }

    /// <summary>Per-column [min,max] amplitude envelope in [-1,1] (channels averaged), for the
    /// waveform image. Strided so a long clip stays cheap.</summary>
    public (float[] Mins, float[] Maxs) Envelope(int columns)
    {
        columns = Math.Max(1, columns);
        var mins = new float[columns];
        var maxs = new float[columns];

        int bytesPerSample = BitsPerSample / 8;
        int frame = bytesPerSample * Channels;
        int frames = frame == 0 ? 0 : DataLength / frame;
        if (frames == 0) return (mins, maxs);

        for (int c = 0; c < columns; c++)
        {
            long f0 = (long)c * frames / columns;
            long f1 = (long)(c + 1) * frames / columns;
            if (f1 <= f0) f1 = f0 + 1;
            long step = Math.Max(1, (f1 - f0) / 256);

            float mn = float.MaxValue, mx = float.MinValue;
            for (long f = f0; f < f1 && f < frames; f += step)
            {
                float acc = 0;
                for (int ch = 0; ch < Channels; ch++)
                {
                    int off = DataOffset + (int)(f * frame) + ch * bytesPerSample;
                    float s = BitsPerSample == 16
                        ? BitConverter.ToInt16(_bytes, off) / 32768f
                        : (_bytes[off] - 128) / 128f;
                    acc += s;
                }
                acc /= Channels;
                if (acc < mn) mn = acc;
                if (acc > mx) mx = acc;
            }
            mins[c] = mn == float.MaxValue ? 0f : mn;
            maxs[c] = mx == float.MinValue ? 0f : mx;
        }
        return (mins, maxs);
    }
}

namespace SiegeFX.Runtime.Audio;

/// <summary>
/// Bare-bones RIFF/WAVE PCM parser. DS1 audio assets ship as plain
/// PCM .wav (8/16-bit, mono/stereo, 22 kHz), so we don't need IMA-ADPCM
/// or extensible-format support to play cast SFX. Anything more exotic
/// throws so the caller falls back silently rather than playing garbage.
/// </summary>
public static class WavLoader
{
    public sealed class PcmClip
    {
        public required byte[] Samples { get; init; }
        public required int    Channels { get; init; }       // 1 or 2
        public required int    BitsPerSample { get; init; }  // 8 or 16
        public required int    SampleRate { get; init; }     // Hz
    }

    public static PcmClip Parse(byte[] bytes)
    {
        if (bytes.Length < 44) throw new InvalidDataException("WAV too small");
        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
            throw new InvalidDataException("not a RIFF file");
        if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
            throw new InvalidDataException("not a WAVE file");

        // Walk top-level chunks after the WAVE marker. DS1 wavs typically have
        // fmt then data with no junk in between, but we scan generally so a
        // rare LIST/bext chunk doesn't trip the loader.
        int pos = 12;
        short channels = 0, bits = 0;
        int sampleRate = 0;
        byte[]? pcm = null;

        while (pos + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int dataStart = pos + 8;
            if (dataStart + size > bytes.Length) size = bytes.Length - dataStart;

            if (id == "fmt ")
            {
                short fmt = BitConverter.ToInt16(bytes, dataStart + 0);
                if (fmt != 1) throw new InvalidDataException(
                    $"WAV format {fmt} not supported (need PCM=1)");
                channels   = BitConverter.ToInt16(bytes, dataStart + 2);
                sampleRate = BitConverter.ToInt32(bytes, dataStart + 4);
                bits       = BitConverter.ToInt16(bytes, dataStart + 14);
            }
            else if (id == "data")
            {
                pcm = new byte[size];
                Buffer.BlockCopy(bytes, dataStart, pcm, 0, size);
            }

            // Chunks are word-aligned: pad odd sizes by one byte.
            int next = dataStart + size + (size & 1);
            if (next <= pos) break;
            pos = next;
        }

        if (channels == 0 || bits == 0 || sampleRate == 0 || pcm is null)
            throw new InvalidDataException("WAV missing fmt or data chunk");
        if (channels is not (1 or 2)) throw new InvalidDataException(
            $"WAV channels={channels} not supported (need 1 or 2)");
        if (bits is not (8 or 16)) throw new InvalidDataException(
            $"WAV bits={bits} not supported (need 8 or 16)");

        return new PcmClip
        {
            Samples = pcm,
            Channels = channels,
            BitsPerSample = bits,
            SampleRate = sampleRate,
        };
    }
}

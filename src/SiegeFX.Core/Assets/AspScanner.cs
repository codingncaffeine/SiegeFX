using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Heuristic walker over Dungeon Siege .asp (Animatable Aspect) files.
///
/// ASP chunks are 4-byte ASCII FourCC identifiers immediately followed by a 4-byte
/// version stamp (typically 0x01 0x02 0x00 0x00 = "1.2"). There is no central
/// table of contents and each chunk body layout differs, so parsing requires
/// chunk-type-specific logic. This scanner is the discovery step: it finds the
/// position and identity of every well-formed chunk marker so downstream code
/// can hand each one to the appropriate parser.
/// </summary>
public static class AspScanner
{
    public readonly record struct Chunk(FourCC Id, int Offset, int VersionRaw)
    {
        /// <summary>e.g. "1.2.0.0" for a raw 0x00020001 little-endian.</summary>
        public string Version =>
            $"{VersionRaw & 0xFF}.{(VersionRaw >> 8) & 0xFF}.{(VersionRaw >> 16) & 0xFF}.{(VersionRaw >> 24) & 0xFF}";
    }

    private static readonly HashSet<FourCC> Known = new()
    {
        // Structural
        new('B','M','S','H'), // base mesh header (skeleton + mesh name)
        new('B','O','N','H'), // bone header
        new('B','S','U','B'), // sub-mesh info
        new('B','S','M','M'), // sub-mesh material map
        new('B','V','T','X'), // vertex position block
        new('B','C','R','N'), // corner (UV/normal/weights/indices)
        new('W','C','R','N'), // weighted corner variant
        new('B','T','R','I'), // triangle list
        new('B','V','M','P'), // vertex→bone map
        new('S','T','C','H'), // stitch info
        new('R','P','O','S'), // root position
        new('B','V','W','L'), // vertex weight list
        new('B','E','N','D'), // marker before BENDINFO trailer
    };

    /// <summary>
    /// Returns every chunk marker found in the file. Current strategy: any 4-byte ASCII
    /// FourCC immediately followed by a valid-looking version word (major in [1..9]),
    /// with the FourCC appearing in our known table. This catches every real chunk
    /// in DS1 ASPs and ignores false positives inside vertex blob data.
    /// </summary>
    public static List<Chunk> Scan(ReadOnlySpan<byte> data)
    {
        var chunks = new List<Chunk>();
        for (var i = 0; i + 8 <= data.Length; i++)
        {
            if (!IsUpperAscii(data[i]) || !IsUpperAscii(data[i + 1]) ||
                !IsUpperAscii(data[i + 2]) || !IsUpperAscii(data[i + 3]))
                continue;
            var id = new FourCC(data[i], data[i + 1], data[i + 2], data[i + 3]);
            if (!Known.Contains(id)) continue;

            // Every real DS1 ASP version is x.y.0.0; the high two bytes being nonzero
            // almost always means we hit lucky ASCII inside a float blob, not a real header.
            var verMajor = data[i + 4];
            if (verMajor is < 1 or > 9) continue;
            if (data[i + 6] != 0 || data[i + 7] != 0) continue;

            var versionRaw = data[i + 4] | (data[i + 5] << 8) | (data[i + 6] << 16) | (data[i + 7] << 24);
            chunks.Add(new Chunk(id, i, versionRaw));
            // Skip past this chunk's header so the next iteration can't start inside the
            // 8 bytes we just consumed. Loop's i++ will move us past the version word.
            i += 7;
        }
        return chunks;
    }

    private static bool IsUpperAscii(byte b) => b >= 'A' && b <= 'Z';
}

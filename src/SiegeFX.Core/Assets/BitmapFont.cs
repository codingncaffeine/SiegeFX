namespace SiegeFX.Core.Assets;

/// <summary>
/// DS1 bitmap font: a glyph atlas (.raw, BGRA) + per-font metadata from
/// <c>/ui/fonts/fonts.gas</c> (<c>height</c>, <c>startrange</c>, <c>endrange</c>).
///
/// DS1 marks glyph column boundaries with pure magenta pixels (R=255 G=0 B=255,
/// alpha=0) on dedicated "marker rows" — these are invisible at runtime because
/// alpha=0 discards, but we use them at load time to reconstruct each glyph's
/// rectangle. The marker row sits at the BOTTOM of its cell (cell y = markerY -
/// height + 1 .. markerY) and codepoints are assigned in left-to-right order
/// across markers, with cell rows iterated BOTTOM-UP — that is, the deepest
/// marker row in the atlas holds <c>startrange</c> through
/// <c>startrange + N0 - 1</c>, the row above it picks up the next batch, etc.
///
/// Within a marker row, N markers yield N glyph cells (NOT N-1): the FIRST cell
/// runs from x=0 up to the first marker (this is where the space glyph lives —
/// authored as an empty rectangle), and each subsequent cell runs from
/// markers[i-1]+1 to markers[i]-1.
/// </summary>
public sealed class BitmapFont
{
    public string Name { get; }
    public int Height { get; }
    public int StartRange { get; }
    public int EndRange { get; }
    public RawImage Atlas { get; }
    public Glyph[] Glyphs { get; }

    public readonly record struct Glyph(int Codepoint, int X, int Y, int Width, int Height, int Advance);

    public BitmapFont(string name, int height, int startRange, int endRange,
                     RawImage atlas, Glyph[] glyphs)
    {
        Name = name;
        Height = height;
        StartRange = startRange;
        EndRange = endRange;
        Atlas = atlas;
        Glyphs = glyphs;
    }

    /// <summary>Find the glyph for <paramref name="codepoint"/> or null when unmapped.</summary>
    public Glyph? Find(int codepoint)
    {
        if (codepoint < StartRange || codepoint >= EndRange) return null;
        var g = Glyphs[codepoint - StartRange];
        return g.Width <= 0 ? null : g;
    }

    /// <summary>Build a font from a parsed <c>fonts.gas</c> entry plus its atlas.
    /// <paramref name="atlasBytes"/> is the raw .raw file contents.</summary>
    /// <summary>Resolve a font by its DS1 name (e.g. <c>b_gui_fnt_12p_copperplate-light</c>)
    /// from an <see cref="AssetResolver"/>. Reads <c>/ui/fonts/fonts.gas</c> for height +
    /// startrange + endrange, then loads the matching <c>.raw</c> atlas. Returns null if
    /// the font entry or atlas is missing in any indexed tank.</summary>
    public static BitmapFont? TryLoadByName(AssetResolver resolver, string fontName)
    {
        if (!resolver.TryLoadByBasename("fonts.gas", out var fontsGasBytes)) return null;
        var doc = GasDocument.Load(fontsGasBytes);
        foreach (var node in doc.Roots)
        {
            // Header looks like "t:font,n:b_gui_fnt_12p_copperplate-light" — match on the n: piece.
            if (!node.Header.Contains("font", StringComparison.OrdinalIgnoreCase)) continue;
            var nKey = "n:" + fontName;
            if (!node.Header.Contains(nKey, StringComparison.OrdinalIgnoreCase)) continue;

            int height = 0, startRange = 0, endRange = 0;
            string textureName = fontName;
            foreach (var attr in node.Attributes)
            {
                if (string.Equals(attr.Name, "height", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(attr.Value, out height);
                else if (string.Equals(attr.Name, "startrange", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(attr.Value, out startRange);
                else if (string.Equals(attr.Name, "endrange", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(attr.Value, out endRange);
                else if (string.Equals(attr.Name, "texture", StringComparison.OrdinalIgnoreCase))
                    textureName = attr.Value;
            }
            if (height <= 0 || endRange <= startRange) return null;
            if (!resolver.TryLoadByBasename(textureName + ".raw", out var atlasBytes)) return null;
            return Load(fontName, height, startRange, endRange, atlasBytes);
        }
        return null;
    }

    public static BitmapFont Load(string name, int height, int startRange, int endRange, byte[] atlasBytes)
    {
        if (height <= 0) throw new ArgumentException("font height must be positive");
        if (endRange <= startRange) throw new ArgumentException("font endrange must exceed startrange");
        var atlas = RawImage.Load(atlasBytes);
        var pixels = atlas.Pixels;
        var w = atlas.Width;
        var h = atlas.Height;

        int wantedGlyphs = endRange - startRange;
        var glyphs = new Glyph[wantedGlyphs];

        // Step 1: find every "marker row" — any scanline containing at least one
        // pure-magenta sentinel pixel — and record the x positions of its markers
        // in ascending order. Markers delimit glyph columns: glyph N spans the x
        // range strictly between marker N and marker N+1 in that row.
        var markerRows = new List<(int Y, List<int> Xs)>();
        for (int y = 0; y < h; y++)
        {
            List<int>? xs = null;
            int rowBase = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int o = rowBase + x * 4;
                // BGRA: B=pixels[o], G=pixels[o+1], R=pixels[o+2]
                if (pixels[o + 2] == 255 && pixels[o + 1] == 0 && pixels[o] == 255)
                {
                    xs ??= new List<int>();
                    xs.Add(x);
                }
            }
            if (xs is not null && xs.Count >= 2) markerRows.Add((y, xs));
        }

        // Step 2: walk marker rows BOTTOM-UP. The deepest marker row holds the
        // first batch of codepoints starting at startRange. Each row's FIRST cell
        // is the implicit-leading rect from x=0 to the first marker (the space
        // glyph for the bottommost row); subsequent cells sit between markers.
        markerRows.Reverse();
        int idx = 0;
        foreach (var (markerY, markerXs) in markerRows)
        {
            int cellTop = markerY - height + 1;
            if (cellTop < 0) continue;

            // Cell 0: implicit start at x=0, ends at first marker (exclusive).
            {
                int gx = 0;
                int gw = markerXs[0];
                int codepoint = startRange + idx;
                int advance = gw + 1;
                glyphs[idx++] = new Glyph(codepoint, gx, cellTop, gw, height, advance);
                if (idx >= wantedGlyphs) break;
            }

            // Cells 1..N-1: between consecutive markers.
            for (int i = 1; i < markerXs.Count && idx < wantedGlyphs; i++)
            {
                int gx = markerXs[i - 1] + 1;
                int gw = markerXs[i] - markerXs[i - 1] - 1;
                int codepoint = startRange + idx;
                int advance = gw + 1;
                glyphs[idx++] = new Glyph(codepoint, gx, cellTop, gw, height, advance);
            }
            if (idx >= wantedGlyphs) break;
        }

        // Step 3: fill any tail slots we couldn't detect (typically the high-Latin/
        // extended range that ships partially-populated). They render as nothing
        // but advance a sane amount so text containing them doesn't collapse.
        while (idx < wantedGlyphs)
        {
            int codepoint = startRange + idx;
            glyphs[idx++] = new Glyph(codepoint, 0, 0, 0, height, height / 3);
        }

        // The first cell of the bottom-most marker row IS the space glyph in DS1
        // atlases — already authored as an empty rect. Override its advance so a
        // zero-width space doesn't visually swallow inter-word gaps.
        if (startRange <= ' ' && ' ' < endRange)
        {
            int spaceIdx = ' ' - startRange;
            var sp = glyphs[spaceIdx];
            if (sp.Width <= 0)
                glyphs[spaceIdx] = new Glyph(' ', sp.X, sp.Y, 0, height, Math.Max(3, height / 3));
        }

        return new BitmapFont(name, height, startRange, endRange, atlas, glyphs);
    }
}

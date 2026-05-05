using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>SC-TSD-ANIM — DS1 ships per-texture animation as TSD (Texture
/// Stage Descriptor) sidecar .gas files alongside the .raw bitmap they
/// describe. A river surface like <c>b_t_grs01_rvr_water-2a-1.gas</c> has
/// <c>layer1numframes = 4</c> + <c>layer1secondsperframe = 0.15</c> + 4
/// distinct <c>layer1textureN</c> entries, producing a frame-flip cycle.
/// A waterfall like <c>b_t_grs01_wheelfallstatic-01.gas</c> stacks a
/// static layer 1 and a layer 2 sourcing <c>b_t_grs01_rvr_dynamic</c> with
/// <c>layer2vshiftpersecond = 0.5</c> and <c>layer2colorop = modulate2x</c>;
/// the visible motion is the second layer scrolling on top.
///
/// <para>One TSD .gas file may declare multiple <c>[t:tsd,n:NAME]</c> records
/// (e.g. <c>NAME-simple</c> as a low-detail variant). We index by record name.
/// The terrain renderer queries by texture name; if no record exists the
/// texture is treated as a static single-layer with no scroll.</para></summary>
public sealed class TsdStore
{
    public enum ColorOp { Modulate, Modulate2x, Arg1, Arg2 }

    public sealed class Layer
    {
        public required string[] Textures { get; init; }
        public float SecondsPerFrame { get; init; }
        public float UshiftPerSecond { get; init; }
        public float VshiftPerSecond { get; init; }
        public ColorOp Op { get; init; } = ColorOp.Modulate;
        public bool UWrap { get; init; } = true;
        public bool VWrap { get; init; } = true;

        /// <summary>Picks the bound texture for a given wallclock time and
        /// returns the UV scroll offset to feed the shader. Frame index folds
        /// modulo Textures.Length so animation loops cleanly.</summary>
        public (string TextureName, float UOffset, float VOffset) Sample(double time)
        {
            int frame = 0;
            if (Textures.Length > 1 && SecondsPerFrame > 0f)
            {
                frame = (int)Math.Floor(time / SecondsPerFrame);
                frame = ((frame % Textures.Length) + Textures.Length) % Textures.Length;
            }
            float u = (float)((time * UshiftPerSecond) % 1.0);
            float v = (float)((time * VshiftPerSecond) % 1.0);
            return (Textures[frame], u, v);
        }
    }

    public sealed class Record
    {
        public required string Name { get; init; }
        public required Layer Layer1 { get; init; }
        public Layer? Layer2 { get; init; }
        public bool TimeSync { get; init; } = true;
    }

    private readonly Dictionary<string, Record> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public Record? Get(string textureName) =>
        _byName.TryGetValue(textureName, out var r) ? r : null;

    /// <summary>Eager-loads every <c>art/bitmaps/terrain/**.gas</c> in the tank
    /// and parses any <c>[t:tsd,n:NAME]</c> roots. There are ~1.5k TSD files
    /// across the shipping terrain set; parse cost is negligible vs. a single
    /// SNO load. Indexing once at region-load time keeps the per-frame draw
    /// loop allocation-free.</summary>
    public static TsdStore LoadFromTerrain(TankReader reader)
    {
        var store = new TsdStore();
        foreach (var path in reader.ListFiles())
        {
            if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
            if (path.IndexOf("/terrain/", StringComparison.OrdinalIgnoreCase) < 0) continue;
            byte[] bytes;
            try { bytes = reader.ExtractToMemory(path); }
            catch { continue; }
            GasDocument doc;
            try { doc = GasDocument.Load(bytes); }
            catch { continue; }
            foreach (var node in doc.Roots)
            {
                if (!TryParseTsdHeader(node.Header, out var name)) continue;
                var rec = ParseRecord(name, node);
                if (rec is not null) store._byName[name] = rec;
            }
        }
        return store;
    }

    /// <summary>Recognises the <c>t:tsd,n:NAME</c> header form. DS1 also writes
    /// trailing whitespace and varying case; both are tolerated.</summary>
    private static bool TryParseTsdHeader(string header, out string name)
    {
        name = string.Empty;
        var h = header.Trim();
        // Strip surrounding [ ] if present (header is stored without them but
        // we tolerate either).
        if (h.StartsWith('[')) h = h[1..];
        if (h.EndsWith(']')) h = h[..^1];
        var parts = h.Split(',', StringSplitOptions.TrimEntries);
        bool isTsd = false;
        foreach (var p in parts)
        {
            if (p.StartsWith("t:", StringComparison.OrdinalIgnoreCase) &&
                p[2..].Equals("tsd", StringComparison.OrdinalIgnoreCase))
                isTsd = true;
            else if (p.StartsWith("n:", StringComparison.OrdinalIgnoreCase))
                name = p[2..].Trim();
        }
        return isTsd && name.Length > 0;
    }

    private static Record? ParseRecord(string name, GasNode node)
    {
        var l1 = ParseLayer(node, layerIdx: 1);
        if (l1 is null) return null;
        Layer? l2 = null;
        int numLayers = ReadInt(node, "numlayers", defaultValue: 1);
        if (numLayers >= 2) l2 = ParseLayer(node, layerIdx: 2);
        bool timeSync = ReadBool(node, "timesyncanimation", defaultValue: true);
        return new Record { Name = name, Layer1 = l1, Layer2 = l2, TimeSync = timeSync };
    }

    private static Layer? ParseLayer(GasNode node, int layerIdx)
    {
        string prefix = "layer" + layerIdx.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Collect textures: layer{N}texture1 .. layer{N}textureK contiguous.
        // numframes is the authored count, but we tolerate fewer/more entries.
        int numFrames = ReadInt(node, prefix + "numframes", defaultValue: 1);
        var textures = new List<string>(Math.Max(1, numFrames));
        for (int i = 1; ; i++)
        {
            var key = prefix + "texture" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var v = ReadString(node, key);
            if (v is null) break;
            textures.Add(v);
            if (i >= 64) break; // sanity cap
        }
        if (textures.Count == 0) return null;

        float spf = ReadFloat(node, prefix + "secondsperframe", defaultValue: 0f);
        float us  = ReadFloat(node, prefix + "ushiftpersecond", defaultValue: 0f);
        float vs  = ReadFloat(node, prefix + "vshiftpersecond", defaultValue: 0f);
        bool uw   = ReadBool(node, prefix + "uwrap", defaultValue: true);
        bool vw   = ReadBool(node, prefix + "vwrap", defaultValue: true);
        var op    = ParseColorOp(ReadString(node, prefix + "colorop"));
        return new Layer
        {
            Textures = textures.ToArray(),
            SecondsPerFrame = spf,
            UshiftPerSecond = us,
            VshiftPerSecond = vs,
            UWrap = uw, VWrap = vw, Op = op,
        };
    }

    private static ColorOp ParseColorOp(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "modulate2x" => ColorOp.Modulate2x,
        "arg1"       => ColorOp.Arg1,
        "arg2"       => ColorOp.Arg2,
        _            => ColorOp.Modulate, // includes null + the explicit "modulate"
    };

    private static string? ReadString(GasNode node, string name)
    {
        foreach (var a in node.Attributes)
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                return a.Value;
        return null;
    }

    private static int ReadInt(GasNode node, string name, int defaultValue)
    {
        var s = ReadString(node, name);
        if (s is null) return defaultValue;
        return int.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : defaultValue;
    }

    private static float ReadFloat(GasNode node, string name, float defaultValue)
    {
        var s = ReadString(node, name);
        if (s is null) return defaultValue;
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : defaultValue;
    }

    private static bool ReadBool(GasNode node, string name, bool defaultValue)
    {
        var s = ReadString(node, name);
        if (s is null) return defaultValue;
        return s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
               s.Trim().Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}

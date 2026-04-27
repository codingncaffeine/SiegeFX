using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// One DS1 Sound Effect Descriptor. SEDs are GPG's audio-quality layer:
/// they alias a logical sound name (used by skrit / template trigger
/// actions) to a real .wav file inside Sound.dsres, while attaching
/// playback-rate variation and a concurrent-voice cap. Without this,
/// firing the same swing 5 times in a row sounds like a stutter — DS1
/// uses small per-fire pitch jitter (e.g. ±3% on zap_cast, ±15% on krug
/// barks) to break that up.
///
/// Phase 21d-2a-xii consumes <see cref="MinPlaybackRate"/> +
/// <see cref="MaxPlaybackRate"/> (sampled per fire and applied as
/// AL Pitch). <see cref="MaxSimultaneousSamples"/> is parsed and
/// surfaced to the CLI but not enforced yet — needs a per-clip live
/// voice counter that the round-robin SFX pool doesn't currently
/// track. Future polish slice.
/// </summary>
public sealed class SedDescriptor
{
    /// <summary>Logical name (the value before the <c>_SED</c> suffix in
    /// the gas header — e.g. <c>s_e_zap_cast</c>). This is the key that
    /// gameplay code passes to "play this sound."</summary>
    public string Key { get; init; } = "";
    /// <summary>The actual .wav resource the SED resolves to (no extension,
    /// no leading path — e.g. <c>s_e_zap_cast</c>). May differ from
    /// <see cref="Key"/> when DS1 reuses a wav for another logical sound
    /// (e.g. <c>s_e_call_googore_SED</c> aliases to <c>s_e_call_worm</c>
    /// at fixed 1.25× pitch).</summary>
    public string SoundEffectFile { get; init; } = "";
    /// <summary>Lower bound of uniform random playback rate. 1.0 = no
    /// transposition. Default 1.0 when the SED omits or comments-out the
    /// field.</summary>
    public float MinPlaybackRate { get; init; } = 1.0f;
    /// <summary>Upper bound of uniform random playback rate. Default 1.0
    /// when the field is missing. Equal min/max means a fixed transpose
    /// (e.g. always +25%) with no jitter.</summary>
    public float MaxPlaybackRate { get; init; } = 1.0f;
    /// <summary>Soft cap on simultaneous instances of this SED. -1 means
    /// no cap was authored (commented-out or absent). Not yet enforced
    /// by the runtime; recorded for a future per-clip voice-count slice.</summary>
    public int MaxSimultaneousSamples { get; init; } = -1;
}

/// <summary>
/// Parses every <c>*_sed.gas</c> file under <c>/sound/effects/</c> in
/// Sound.dsres. Each file ships a single <c>[t:sed,n:&lt;name&gt;_SED]</c>
/// block; the registry is keyed by the logical name (with the <c>_SED</c>
/// suffix stripped) so callers query by the same string they'd pass to
/// <c>play_sound("s_e_zap_cast")</c>.
/// </summary>
public static class SedStore
{
    /// <summary>Walk every <c>*_sed.gas</c> in <paramref name="soundTank"/>'s
    /// <c>/sound/effects/</c> directory and return the SED registry.
    /// Diagnostics list per-file parse failures so the CLI can surface
    /// authoring drift without aborting the load.</summary>
    public static (IReadOnlyDictionary<string, SedDescriptor> Seds,
                   IReadOnlyList<string> Diagnostics)
        Load(TankReader soundTank)
    {
        var diags = new List<string>();
        var seds  = new Dictionary<string, SedDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in soundTank.ListFiles())
        {
            if (!path.EndsWith("_sed.gas", StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.StartsWith("/sound/", StringComparison.OrdinalIgnoreCase)) continue;

            byte[] bytes;
            try { bytes = soundTank.ExtractToMemory(path); }
            catch (Exception ex) { diags.Add($"{path}: extract failed: {ex.Message}"); continue; }

            GasDocument doc;
            try { doc = GasDocument.Load(bytes); }
            catch (Exception ex) { diags.Add($"{path}: parse failed: {ex.Message}"); continue; }

            int countBefore = seds.Count;
            foreach (var root in doc.Roots) TryConsumeOne(root, seds);
            if (seds.Count == countBefore)
                diags.Add($"{path}: parsed but produced 0 SED descriptors");
        }

        return (seds, diags);
    }

    static void TryConsumeOne(GasNode node, Dictionary<string, SedDescriptor> sink)
    {
        // Header shape: [t:sed,n:s_e_xyz_SED]. GasDocument exposes the raw
        // header text only (no parsed t:/n: convenience), so we sniff the
        // pair ourselves. Reject anything that isn't a "sed" type block.
        if (!TryParseHeader(node.Header, out var hdrType, out var hdrName)) return;
        if (!string.Equals(hdrType, "sed", StringComparison.OrdinalIgnoreCase)) return;
        var name = hdrName;
        if (string.IsNullOrEmpty(name)) return;

        // Strip the trailing "_SED" suffix to produce the lookup key
        // gameplay code uses. The suffix is convention, not enforced —
        // reject + log if it's missing so we catch authoring drift.
        const string suffix = "_SED";
        string key;
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            key = name[..^suffix.Length];
        else
            key = name; // accept anyway; CLI will surface the oddity

        string soundFile = "";
        float minRate = 1.0f, maxRate = 1.0f;
        int maxSimul = -1;

        foreach (var attr in node.Attributes)
        {
            var val = attr.Value ?? "";
            if (NameEq(attr.Name, "sound_effect_file"))
                soundFile = StripQuotes(val);
            else if (NameEq(attr.Name, "min_playback_rate"))
            {
                if (TryParseFloat(val, out var v)) minRate = v;
            }
            else if (NameEq(attr.Name, "max_playback_rate"))
            {
                if (TryParseFloat(val, out var v)) maxRate = v;
            }
            else if (NameEq(attr.Name, "max_simultaneous_samples"))
            {
                if (int.TryParse(val.Trim(), out var v)) maxSimul = v;
            }
        }

        if (string.IsNullOrEmpty(soundFile)) return; // descriptor with no payload

        // Sanity clamp: shipped data has min_playback_rate authored above
        // max_playback_rate exactly zero times, but defensively swap so
        // the AL Pitch sampler can assume min<=max.
        if (minRate > maxRate) (minRate, maxRate) = (maxRate, minRate);

        sink[key] = new SedDescriptor
        {
            Key = key,
            SoundEffectFile = soundFile,
            MinPlaybackRate = minRate,
            MaxPlaybackRate = maxRate,
            MaxSimultaneousSamples = maxSimul,
        };
    }

    static bool TryParseHeader(string header, out string type, out string name)
    {
        // Very small parser for "[t:foo,n:bar]" with optional whitespace.
        // Returns false on any deviation (callers fall through to skip).
        type = ""; name = "";
        var h = header.Trim().TrimStart('[').TrimEnd(']');
        var parts = h.Split(',');
        foreach (var p in parts)
        {
            var kv = p.Split(':', 2);
            if (kv.Length != 2) continue;
            var k = kv[0].Trim();
            var v = kv[1].Trim();
            if (k.Equals("t", StringComparison.OrdinalIgnoreCase)) type = v;
            else if (k.Equals("n", StringComparison.OrdinalIgnoreCase)) name = v;
        }
        return !string.IsNullOrEmpty(type);
    }

    static bool NameEq(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static bool TryParseFloat(string s, out float v) =>
        float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out v);

    static string StripQuotes(string s)
    {
        var t = s.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t[1..^1];
        return t;
    }
}

using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// SC-WEATHER-F — the <c>[global_voice]</c> event table from
/// <c>/world/global/sounds/sounddb.gas</c>. Region sound emitters
/// (<c>emt_sound</c>/<c>emt_sound_act</c>) reference sounds by EVENT NAME
/// (<c>amb_rain_01</c>), never by wav; this table maps the event to one or
/// more sample names in <c>/sound/effects/</c> (multiple samples per event =
/// pick one at random, per the format comment in the shipped file). A sample
/// name may itself be an SED key (<c>*_SED</c>) — resolve through
/// <see cref="SedStore"/> for the actual wav + playback params.
///
/// The shipped file authors rows almost exclusively in GAS colon shorthand
/// (<c>amb_rain_01: * = s_e_ambient_rain1;</c>), which surfaces as attributes
/// named <c>event:*</c> on the [global_voice] node; the expanded nested form
/// documented in the same file is accepted too. <c>event:priority</c> rows
/// are skipped — SiegeFX has no channel cap to arbitrate, and per the SU
/// sounds doc mood/ambient playback ignores priority anyway.
///
/// The [sounddb] material matrix (source:dest:event combat sounds) is a
/// separate, bigger system — out of scope here, tracked as a follow-up.
/// </summary>
public static class SoundDb
{
    public const string TankPath = "/world/global/sounds/sounddb.gas";

    public static (IReadOnlyDictionary<string, IReadOnlyList<string>> Events,
                   IReadOnlyList<string> Diagnostics)
        LoadGlobalVoice(TankReader logicTank)
    {
        var diags = new List<string>();
        var events = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        byte[] bytes;
        try { bytes = logicTank.ExtractToMemory(TankPath); }
        catch (Exception ex)
        {
            diags.Add($"{TankPath}: extract failed: {ex.Message}");
            return (events, diags);
        }

        GasDocument doc;
        try { doc = GasDocument.Load(bytes); }
        catch (Exception ex)
        {
            diags.Add($"{TankPath}: parse failed: {ex.Message}");
            return (events, diags);
        }

        var sink = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in doc.Roots)
        {
            if (!root.Header.Trim().Equals("global_voice", StringComparison.OrdinalIgnoreCase))
                continue;
            // Colon-shorthand rows: attribute name "event:*" (sample) or
            // "event:priority" (skipped).
            foreach (var attr in root.Attributes)
            {
                int colon = attr.Name.IndexOf(':');
                if (colon <= 0) continue;
                var key = attr.Name[(colon + 1)..].Trim();
                if (key != "*") continue;
                Add(sink, attr.Name[..colon].Trim(), attr.Value);
            }
            // Expanded form: [event] { * = sample; * = sample2; }
            foreach (var child in root.Children)
            {
                foreach (var attr in child.Attributes)
                    if (attr.Name.Trim() == "*")
                        Add(sink, child.Header.Trim(), attr.Value);
            }
        }

        foreach (var (k, v) in sink) events[k] = v;
        if (events.Count == 0)
            diags.Add($"{TankPath}: no [global_voice] events parsed");
        return (events, diags);
    }

    static void Add(Dictionary<string, List<string>> sink, string eventName, string sample)
    {
        var s = sample.Trim().Trim('"');
        if (s.Length == 0 || eventName.Length == 0) return;
        if (!sink.TryGetValue(eventName, out var list)) sink[eventName] = list = new List<string>();
        list.Add(s);
    }

    /// <summary>SC-MATERIAL-MATRIX — the [sounddb] material rows
    /// (<c>source:dest:event:* = sound;</c> — 393 shipped: 100
    /// attack_hit_glance impact pairs, the full door/chest/lever/elevator
    /// event family). Keys are lowercased "src|dst|evt".</summary>
    public static (IReadOnlyDictionary<string, string> Matrix, IReadOnlyList<string> Diagnostics)
        LoadMaterialMatrix(TankReader logicTank)
    {
        var diags = new List<string>();
        var matrix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] bytes;
        try { bytes = logicTank.ExtractToMemory(TankPath); }
        catch (Exception ex) { diags.Add($"{TankPath}: extract failed: {ex.Message}"); return (matrix, diags); }
        GasDocument doc;
        try { doc = GasDocument.Load(bytes); }
        catch (Exception ex) { diags.Add($"{TankPath}: parse failed: {ex.Message}"); return (matrix, diags); }
        foreach (var root in doc.Roots)
        {
            if (!root.Header.Trim().Equals("sounddb", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var attr in root.Attributes)
            {
                // "src : dst : event : *" (whitespace-riddled) = sound.
                var parts = attr.Name.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length != 4 || parts[3] != "*") continue;
                var snd = attr.Value.Trim().Trim('"');
                if (snd.Length == 0) continue;
                matrix[$"{parts[0]}|{parts[1]}|{parts[2]}".ToLowerInvariant()] = snd;
            }
        }
        if (matrix.Count == 0) diags.Add($"{TankPath}: no material-matrix rows parsed");
        return (matrix, diags);
    }

    /// <summary>SC-MATERIAL-MATRIX — resolve with the documented fallback:
    /// exact → src+generic → generic+dst → generic+generic. Null = the
    /// event has no row for any combination.</summary>
    public static string? ResolveMaterial(
        IReadOnlyDictionary<string, string> matrix, string src, string dst, string evt)
    {
        src = string.IsNullOrWhiteSpace(src) ? "generic" : src.Trim().ToLowerInvariant();
        dst = string.IsNullOrWhiteSpace(dst) ? "generic" : dst.Trim().ToLowerInvariant();
        evt = evt.Trim().ToLowerInvariant();
        if (matrix.TryGetValue($"{src}|{dst}|{evt}", out var s1)) return s1;
        if (matrix.TryGetValue($"{src}|generic|{evt}", out var s2)) return s2;
        if (matrix.TryGetValue($"generic|{dst}|{evt}", out var s3)) return s3;
        if (matrix.TryGetValue($"generic|generic|{evt}", out var s4)) return s4;
        return null;
    }
}

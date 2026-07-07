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
}

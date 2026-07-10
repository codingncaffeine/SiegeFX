using System.Globalization;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>ALPHA-2A — the four message-driven logic components DS1 uses to
/// gate quests, doors and multi-step events (decoded from shipped instances):
///
/// - <c>[set_bool]</c> — on we_req_activate, set the named GLOBAL boolean
///   true (we_req_deactivate → false). hc_r1's "krug_in_hc_r1_dead".
/// - <c>[check_bool]</c> — on we_req_activate, read the named global bool
///   and post <c>message_if_true</c>/<c>message_if_false</c> to
///   <c>send_to_scid</c>. Shipped defaults: if_true = we_req_activate,
///   if_false = nothing (bt_r1 quest checkpoints author no messages;
///   path2sd's axe check authors both).
/// - <c>[generic_accumtrigger]</c> — count we_req_activate messages; on
///   reaching <c>num_til_send</c>, post we_req_activate to
///   <c>send_to_scid</c> once (hc_r1: 6 basement krug deaths → quest).
/// - <c>[msg_switch]</c> — alternating message toggle (lightable lamps).
///   Shipped instances author no fields; visual effect rides the point
///   light system (stubbed), so the runtime toggles state + logs.
///
/// The booleans are WORLD-scoped: bt_r1 checks bools that path2sd_a sets.
/// They live for the whole session and belong in the save file.</summary>
public enum LogicGizmoKind { SetBool, CheckBool, Accumulate, MsgSwitch, CheckQuest }

public sealed record LogicGizmoDef(
    uint Scid,
    string TemplateName,
    string RegionPath,
    LogicGizmoKind Kind,
    string BoolVariable,
    string MessageIfTrue,
    string MessageIfFalse,
    uint SendToScid,
    int NumTilSend);

public static class LogicGizmoStore
{
    static readonly (string Section, LogicGizmoKind Kind)[] Sections =
    {
        ("set_bool", LogicGizmoKind.SetBool),
        ("check_bool", LogicGizmoKind.CheckBool),
        ("generic_accumtrigger", LogicGizmoKind.Accumulate),
        ("msg_switch", LogicGizmoKind.MsgSwitch),
        // ALPHA-2F — [check_quest] { quest_name; send_to_scid }: quest-state
        // sibling of check_bool (ds_r1's temple purification chain). The
        // quest name rides the BoolVariable slot.
        ("check_quest", LogicGizmoKind.CheckQuest),
    };

    /// <summary>Scan every <c>objects/*.gas</c> file of <paramref name="regionPath"/>
    /// for placements carrying one of the four logic components (instance
    /// section first, template chain as marker fallback — shipped data
    /// authors the CONFIG on the instance). <paramref name="store"/> may be
    /// null; then only instance-authored sections are found.</summary>
    public static IReadOnlyList<LogicGizmoDef> Load(
        TankReader tank, string regionPath, TemplateStore? store)
    {
        var defs = new List<LogicGizmoDef>();
        var norm = regionPath.TrimEnd('/');
        var objPrefix = norm + "/objects/";
        foreach (var file in tank.ListFiles())
        {
            if (!file.StartsWith(objPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!file.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = file[objPrefix.Length..];
            if (fileName.Contains('/')) continue;
            var (placements, _) = RegionObjects.LoadPlacements(tank, norm, fileName);
            foreach (var p in placements)
            {
                foreach (var (section, kind) in Sections)
                {
                    GasNode? comp = null;
                    foreach (var c in p.Node.Children)
                        if (string.Equals(c.Header, section, StringComparison.OrdinalIgnoreCase)) { comp = c; break; }
                    bool viaTemplate = false;
                    if (comp is null && store is not null && store.TryGet(p.TemplateName, out var tpl))
                    {
                        for (var t = tpl; t is not null && comp is null; t = t.Specializes)
                            foreach (var c in t.Node.Children)
                                if (string.Equals(c.Header, section, StringComparison.OrdinalIgnoreCase)) { comp = c; viaTemplate = true; break; }
                    }
                    if (comp is null) continue;

                    string Attr(string name)
                    {
                        foreach (var a in comp.Attributes)
                            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                                return a.Value.Trim().Trim('"');
                        return "";
                    }
                    var ifTrue = Attr("message_if_true");
                    var ifFalse = Attr("message_if_false");
                    if (kind == LogicGizmoKind.CheckBool && ifTrue.Length == 0 && ifFalse.Length == 0)
                        ifTrue = "we_req_activate"; // shipped default: fire only when true

                    var variable = Attr("bool_variable");
                    if (variable.Length == 0) variable = Attr("quest_name");
                    defs.Add(new LogicGizmoDef(
                        Scid: p.Scid,
                        TemplateName: p.TemplateName,
                        RegionPath: norm,
                        Kind: kind,
                        BoolVariable: variable,
                        MessageIfTrue: ifTrue,
                        MessageIfFalse: ifFalse,
                        SendToScid: Hex(Attr("send_to_scid")),
                        NumTilSend: Int(Attr("num_til_send"))));
                    _ = viaTemplate; // parsed for future diagnostics
                    break; // one component kind per placement in shipped data
                }
            }
        }
        return defs;
    }

    static uint Hex(string v)
    {
        if (v.Length == 0) return 0;
        var s = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? v[2..] : v;
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? u : 0;
    }

    static int Int(string v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
}

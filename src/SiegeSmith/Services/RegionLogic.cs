using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>One condition or action call in a trigger row: a verb, its raw argument list, and the
/// optional row modifiers the engine honours. <c>Group</c> pairs a condition with the action of the
/// same group number; <c>WhenFalse</c> makes a condition fire on its falling edge.</summary>
public sealed class TriggerCall
{
    public string Verb = "";
    public string Args = "";
    public int? Group;
    public float? Delay;
    public bool WhenFalse;

    public TriggerCall Clone() => new() { Verb = Verb, Args = Args, Group = Group, Delay = Delay, WhenFalse = WhenFalse };

    /// <summary>Renders as the gas RHS, e.g. <c>when_false receive_world_message("x")</c> with the
    /// group()/delay() row options the engine parses.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        if (WhenFalse) sb.Append("when_false ");
        sb.Append(Verb).Append('(').Append(Args.Trim()).Append(')');
        if (Group is int g) sb.Append(" group(").Append(g).Append(')');
        if (Delay is float d) sb.Append(" delay(").Append(d.ToString("0.0######", CultureInfo.InvariantCulture)).Append(')');
        return sb.ToString();
    }

    public string Label => string.IsNullOrWhiteSpace(Verb) ? "(empty)" : $"{(WhenFalse ? "¬" : "")}{Verb}({Args.Trim()})";
}

/// <summary>One <c>[instance_triggers]</c> row: ANDed conditions, sequential actions, and the row flags.</summary>
public sealed class TriggerRow
{
    public ObservableCollection<TriggerCall> Conditions = new();
    public ObservableCollection<TriggerCall> Actions = new();
    public bool SingleShot = true;
    public bool FlipFlop;            // parsed but inert in SiegeFX (author-only)
    public bool StartActive = true;
    public float ResetDuration;
    public string OccupantsGroup = "";
}

/// <summary>A trigger volume placed on a node → <c>objects/special.gas</c>. A typo'd or absent template
/// silently never fires; <c>start_active=false</c> rows stay dormant until armed by a
/// <c>we_trigger_activate</c> world message.</summary>
public sealed class RegionTrigger
{
    public uint Scid;
    public string Template = "trigger_generic";
    public uint NodeGuid;
    public Vector3 LocalPos;
    public ObservableCollection<TriggerRow> Rows = new();

    public string Label => $"Trigger · {Template}";
    public string Detail
    {
        get
        {
            int c = 0, a = 0;
            foreach (var r in Rows) { c += r.Conditions.Count; a += r.Actions.Count; }
            return $"0x{Scid:X8} · {Rows.Count} row(s), {c} cond / {a} act · node 0x{NodeGuid:X8}";
        }
    }

    public static readonly string[] Conditions =
    {
        "receive_world_message", "actor_within_sphere", "party_member_within_sphere",
        "go_within_sphere", "party_member_within_bounding_box", "party_member_within_node",
        "party_member_entered_trigger_group", "party_member_left_trigger_group",
    };

    public static readonly string[] Actions =
    {
        "send_world_message", "mood_change", "set_interest_radius",
        "fade_node", "fade_nodes", "fade_nodes_global", "change_quest_state", "call_sfx_script",
    };
}

/// <summary>Writes <c>objects/special.gas</c> — one placement per trigger, each with its full
/// <c>[common][instance_triggers]</c> row set (multiple conditions/actions, flags, group/delay/when_false).</summary>
public static class TriggerGasWriter
{
    public static string Write(IReadOnlyList<RegionTrigger> triggers)
    {
        var sb = new StringBuilder();
        foreach (var t in triggers)
        {
            sb.Append($"[t:{t.Template},n:0x{t.Scid:X8}]\r\n{{\r\n");
            sb.Append("\t[placement]\r\n\t{\r\n");
            sb.Append("\t\torientation = 0,0,0,1;\r\n");
            sb.Append($"\t\tposition = {F(t.LocalPos.X)},{F(t.LocalPos.Y)},{F(t.LocalPos.Z)},0x{t.NodeGuid:X8};\r\n");
            sb.Append("\t}\r\n");
            sb.Append("\t[common]\r\n\t{\r\n\t\t[instance_triggers]\r\n\t\t{\r\n");
            foreach (var r in t.Rows)
            {
                sb.Append("\t\t\t[*]\r\n\t\t\t{\r\n");
                foreach (var c in r.Conditions)
                    if (!string.IsNullOrWhiteSpace(c.Verb)) sb.Append($"\t\t\t\tcondition* = {c.Render()};\r\n");
                foreach (var a in r.Actions)
                    if (!string.IsNullOrWhiteSpace(a.Verb)) sb.Append($"\t\t\t\taction* = {a.Render()};\r\n");
                sb.Append($"\t\t\t\tsingle_shot = {B(r.SingleShot)};\r\n");
                sb.Append($"\t\t\t\tstart_active = {B(r.StartActive)};\r\n");
                if (r.FlipFlop) sb.Append("\t\t\t\tflip_flop = true;\r\n");
                if (r.ResetDuration > 0f) sb.Append($"\t\t\t\treset_duration = {F(r.ResetDuration)};\r\n");
                if (!string.IsNullOrWhiteSpace(r.OccupantsGroup)) sb.Append($"\t\t\t\toccupants_group = {r.OccupantsGroup.Trim()};\r\n");
                sb.Append("\t\t\t}\r\n");
            }
            sb.Append("\t\t}\r\n\t}\r\n}\r\n");
        }
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);
    private static string B(bool v) => v ? "true" : "false";
}

public enum CmdKind { AiPatrol, AiDoJob, AiAttackObject, AnimationCommand, EnterNis, CameraCommand, CameraWaypoint, LeaveNis }

/// <summary>A command / NIS-cinematic gizmo placed on a node → <c>objects/command.gas</c>. Links to
/// other placements are by SCID (<c>next_scid</c> chains the NIS steps; <c>target1/2</c> / <c>client_scid</c>
/// point at the actor(s) a command drives).</summary>
public sealed class CommandPlacement
{
    public uint Scid;
    public uint NodeGuid;
    public Vector3 LocalPos;
    public CmdKind Kind = CmdKind.AiPatrol;
    public uint? NextScid;
    public uint? Target1;
    public uint? Target2;
    public uint? ClientScid;
    public float? Duration;
    public string Order = "";

    public string Template => Kind switch
    {
        CmdKind.AiPatrol => "cmd_ai_patrol",
        CmdKind.AiDoJob => "cmd_ai_dojob",
        CmdKind.AiAttackObject => "cmd_ai_t_attack_object",
        CmdKind.AnimationCommand => "cmd_animation_command",
        CmdKind.EnterNis => "cmd_enter_nis",
        CmdKind.CameraCommand => "cmd_camera_command",
        CmdKind.CameraWaypoint => "cmd_camera_waypoint",
        CmdKind.LeaveNis => "cmd_leave_nis",
        _ => "cmd_ai_patrol",
    };

    public string Label => $"Command · {Kind}";
    public string Detail => $"0x{Scid:X8}{(NextScid is uint n ? $" → 0x{n:X8}" : "")} · node 0x{NodeGuid:X8}";
}

/// <summary>Writes <c>objects/command.gas</c> — one placement per command gizmo with its SCID links.</summary>
public static class CommandGasWriter
{
    public static string Write(IReadOnlyList<CommandPlacement> commands)
    {
        var sb = new StringBuilder();
        foreach (var c in commands)
        {
            sb.Append($"[t:{c.Template},n:0x{c.Scid:X8}]\r\n{{\r\n");
            sb.Append("\t[placement]\r\n\t{\r\n");
            sb.Append("\t\torientation = 0,0,0,1;\r\n");
            sb.Append($"\t\tposition = {F(c.LocalPos.X)},{F(c.LocalPos.Y)},{F(c.LocalPos.Z)},0x{c.NodeGuid:X8};\r\n");
            sb.Append("\t}\r\n");
            if (c.NextScid is uint ns) sb.Append($"\tnext_scid = 0x{ns:X8};\r\n");
            if (c.Target1 is uint t1) sb.Append($"\ttarget1 = 0x{t1:X8};\r\n");
            if (c.Target2 is uint t2) sb.Append($"\ttarget2 = 0x{t2:X8};\r\n");
            if (c.ClientScid is uint cs) sb.Append($"\tclient_scid = 0x{cs:X8};\r\n");
            if (c.Duration is float d) sb.Append($"\tduration = {F(d)};\r\n");
            if (!string.IsNullOrWhiteSpace(c.Order)) sb.Append($"\torder = {c.Order.Trim()};\r\n");
            sb.Append("}\r\n");
        }
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);
}

/// <summary>One ordered line of a conversation. <c>ActivateQuest</c> references a QuestCatalog key.</summary>
public sealed class DialogueLine
{
    public int Order = 1;
    public string ScreenText = "";
    public string Sample = "";
    public string Choice = "";        // '' | more | potential_member
    public string ActivateQuest = "";
    public bool QuestDialog;
    public bool Nis;

    public string Label => string.IsNullOrWhiteSpace(ScreenText) ? $"line {Order}" : $"{Order}. {ScreenText}";
    public string Detail => (string.IsNullOrWhiteSpace(Sample) ? "" : $"vo:{Sample} ")
                          + (string.IsNullOrWhiteSpace(Choice) ? "" : $"[{Choice}] ")
                          + (string.IsNullOrWhiteSpace(ActivateQuest) ? "" : $"quest:{ActivateQuest}");

    public static readonly string[] Choices = { "", "more", "potential_member" };
}

/// <summary>A conversation keyed by <c>conversation_&lt;key&gt;</c>; bound to an NPC via that actor's
/// <c>[conversation][conversations]{ * = conversation_&lt;key&gt;; }</c>. <see cref="BoundActorScid"/>
/// records which placed actor it binds to (0 = unbound).</summary>
public sealed class Conversation
{
    public string Key = "custom";
    public uint BoundActorScid;
    public ObservableCollection<DialogueLine> Nodes = new();

    public string FullKey => Key.StartsWith("conversation_") ? Key : "conversation_" + Key;
    public string Label => FullKey;
    public string Detail => $"{Nodes.Count} line(s)" + (BoundActorScid != 0 ? $" · actor 0x{BoundActorScid:X8}" : " · unbound");
}

/// <summary>Writes <c>conversations/conversations.gas</c> — one bare <c>[conversation_KEY]</c> root per
/// conversation with ordered <c>[text*]</c> lines.</summary>
public static class ConversationGasWriter
{
    public static string Write(IReadOnlyList<Conversation> conversations)
    {
        var sb = new StringBuilder();
        foreach (var c in conversations)
        {
            sb.Append($"[{c.FullKey}]\r\n{{\r\n");
            foreach (var n in c.Nodes)
            {
                sb.Append("\t[text*]\r\n\t{\r\n");
                sb.Append($"\t\torder = {n.Order};\r\n");
                sb.Append($"\t\tscreen_text = \"{n.ScreenText.Replace("\"", "'")}\";\r\n");
                if (!string.IsNullOrWhiteSpace(n.Sample)) sb.Append($"\t\tsample = {n.Sample.Trim()};\r\n");
                if (!string.IsNullOrWhiteSpace(n.Choice)) sb.Append($"\t\tchoice = {n.Choice.Trim()};\r\n");
                if (n.QuestDialog) sb.Append("\t\tquest_dialog = true;\r\n");
                if (n.Nis) sb.Append("\t\tnis = true;\r\n");
                if (!string.IsNullOrWhiteSpace(n.ActivateQuest)) sb.Append($"\t\tactivate_quest = {n.ActivateQuest.Trim()};\r\n");
                sb.Append("\t}\r\n");
            }
            sb.Append("}\r\n");
        }
        return sb.ToString();
    }
}

/// <summary>Compiled-in quest keys (from the engine's QuestCatalog). New quests need a C# change; the
/// editor only wires activation of these existing keys. A key outside this set tracks bare state.</summary>
public static class QuestCatalogKeys
{
    public static readonly string[] Keys =
    {
        "quest_for_gyorn", "quest_edgaar_basement", "quest_find_merik", "quest_destroy_gom",
        "quest_for_gyorn_mp", "quest_edgaar_basement_mp", "quest_find_merik_mp", "quest_destroy_gom_mp",
    };
}

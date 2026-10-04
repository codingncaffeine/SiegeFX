namespace SiegeFX.Core.Assets;

/// <summary>
/// SC-MOB-COMMANDS — which <c>command.gas</c> templates the runtime acts on,
/// and how. One table shared by <c>RenderHost.ActivateAiCommand</c> (the
/// we_req_activate dispatcher) and <c>siegefx region cmd-audit</c>, so the
/// audit's "STUB" column reports what the engine really leaves inert instead
/// of a hand-copied list that drifts as verbs land. The Core.Tests suite
/// cross-checks <see cref="Dispatched"/> against the dispatcher's case labels.
/// </summary>
public static class AiCommandCoverage
{
    public enum Kind
    {
        /// <summary>Recognized but not yet implemented — the set-piece is inert.</summary>
        Stub,
        /// <summary>Handled by a case in <c>RenderHost.ActivateAiCommand</c>.</summary>
        Dispatched,
        /// <summary>Driven by the NIS engine (camera chain, enter/leave).</summary>
        Nis,
        /// <summary>Indexed at region load and acted on outside the dispatcher
        /// (proximity runners, light fx rows, FireAnimCommand).</summary>
        Indexed,
        /// <summary>Not message-dispatched; positions feed BuildCommandRoute
        /// so an actor whose [mind] initial_command names one walks the chain
        /// as a patrol (the verb-specific orient-on-arrival nuance is lost).</summary>
        Route,
    }

    /// <summary>Case labels of <c>RenderHost.ActivateAiCommand</c>'s switch
    /// (everything above its <c>default:</c> arm).</summary>
    public static readonly IReadOnlySet<string> Dispatched = Set(
        "cmd_ai_c_move", "cmd_ai_c_move_orient", "cmd_ai_t_move", "cmd_ai_t_move_orient",
        "cmd_ai_t_patrol", "cmd_ai_t_patrol_orient",
        "cmd_auto_save", "cmd_report_gameplay_screen_player",
        "cmd_alignment_changer", "cmd_party_wrangler", "cmd_stop_party", "cmd_move_party",
        "cmd_selection_toggle", "fader_proxy", "cmd_ai_c_send_message",
        "cmd_ai_c_face", "cmd_ai_t_face", "cmd_ai_t_fidget", "cmd_camera_move",
        "cmd_ai_t_attack_catalyst", "cmd_ai_c_animate",
        "animate_object", "animate_chain", "animate_elevator", "nodal_tex_anim",
        "cmd_ai_t_guard", "preload_go", "light_enable", "camera_quake", "rock_beast_stomp");

    /// <summary>NIS gizmos indexed into <c>_nisCommands</c> and played by the
    /// NIS engine. (<c>cmd_camera_move</c> is indexed there too but also has a
    /// dispatcher case, so it lives in <see cref="Dispatched"/>.)</summary>
    public static readonly IReadOnlySet<string> Nis = Set(
        "cmd_enter_nis", "cmd_camera_command", "cmd_camera_waypoint", "cmd_leave_nis");

    /// <summary>Templates whose behavior is registered at region load.</summary>
    public static readonly IReadOnlySet<string> Indexed = Set(
        "cmd_animation_command",   // SC-ANIM-CMD: FireAnimCommand on we_req_activate
        "cmd_ai_t_attack_object",  // SC-SMASH: proximity smash runner (barn fence)
        "light_flicker",           // SC-LIGHT-GIZMOS: per-frame light fx rows
        "light_colorwave");

    /// <summary>Patrol-route verbs consumed by BuildCommandRoute.</summary>
    public static readonly IReadOnlySet<string> Route = Set(
        "cmd_ai_c_patrol", "cmd_ai_c_patrol_orient");

    public static Kind Classify(string templateName) =>
        Dispatched.Contains(templateName) ? Kind.Dispatched
        : Nis.Contains(templateName)      ? Kind.Nis
        : Indexed.Contains(templateName)  ? Kind.Indexed
        : Route.Contains(templateName)    ? Kind.Route
        : Kind.Stub;

    private static IReadOnlySet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}

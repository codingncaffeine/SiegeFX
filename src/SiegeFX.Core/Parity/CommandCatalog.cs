namespace SiegeFX.Core.Parity;

/// <summary>The paths by which the engine acts on a scripted command
/// template. A template can take more than one.</summary>
[Flags]
public enum CommandHandling
{
    /// <summary>Placed in the world, but nothing in the engine acts on it.</summary>
    None = 0,
    /// <summary>RenderHost.ActivateAiCommand acts on it when it is activated
    /// (we_req_activate from a trigger, a chain or a conversation).</summary>
    Activate = 1,
    /// <summary>Indexed into the NIS engine at region load (camera poses and
    /// the enter/leave pair of a non-interactive sequence).</summary>
    Nis = 2,
    /// <summary>Registered at region load and run by a runner of its own
    /// (light gizmos, smash set-pieces, animation commands, the inventory
    /// changer, the steam-valve puzzle).</summary>
    RegionLoad = 4,
    /// <summary>Its positions become the patrol route of an actor whose
    /// [mind] initial_command names it.</summary>
    PatrolRoute = 8,
}

/// <summary>How the engine acts on each scripted command template the world
/// places (the command.gas gizmos: AI orders, NIS cameras, light and
/// animation gizmos, party commands). The engine consults this table: the
/// activation dispatcher acts only on <see cref="CommandHandling.Activate"/>
/// entries and the NIS indexer takes exactly the <see cref="CommandHandling.Nis"/>
/// ones, so for those two paths the table is the behaviour, not a copy of it.
/// The region-load runners and patrol routes are template-specific code; their
/// entries name it. The audits (region cmd-audit, the parity ledger) read the
/// same table, so what they report as unimplemented is what the engine leaves
/// inert. Template names compare case-insensitively, as the engine reads them.</summary>
public static class CommandCatalog
{
    readonly record struct Entry(CommandHandling Handling, string Note);

    const CommandHandling Activate = CommandHandling.Activate;
    const CommandHandling Nis = CommandHandling.Nis;
    const CommandHandling RegionLoad = CommandHandling.RegionLoad;
    const CommandHandling PatrolRoute = CommandHandling.PatrolRoute;

    static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        // Moves and patrols.
        ["cmd_ai_c_move"]          = new(Activate, "hero / scripted move (a chain an actor patrols is left to it)"),
        ["cmd_ai_c_move_orient"]   = new(Activate, "hero / scripted move, facing on arrival"),
        ["cmd_ai_t_move"]          = new(Activate, "target actor walks to the gizmo"),
        ["cmd_ai_t_move_orient"]   = new(Activate, "target actor walks to the gizmo, facing on arrival"),
        ["cmd_ai_t_patrol"]        = new(Activate, "target actor walks the chain as a patrol"),
        ["cmd_ai_t_patrol_orient"] = new(Activate, "target actor patrols, facing on arrival"),
        ["cmd_ai_c_patrol"]        = new(PatrolRoute, "patrol route of the actor whose initial_command names it"),
        ["cmd_ai_c_patrol_orient"] = new(PatrolRoute, "patrol route with facing"),

        // Facing, fidgets, guards, attacks, animation.
        ["cmd_ai_c_face"]            = new(Activate, "face a direction"),
        ["cmd_ai_t_face"]            = new(Activate, "target actor faces"),
        ["cmd_ai_t_fidget"]          = new(Activate, "target actor fidgets"),
        ["cmd_ai_t_guard"]           = new(Activate, "target actor guards"),
        ["cmd_ai_t_attack_catalyst"] = new(Activate, "target actor attacks the catalyst"),
        ["cmd_ai_t_attack_object"]   = new(RegionLoad, "smash set-piece runner (actor breaks an object)"),
        ["cmd_ai_c_animate"]         = new(Activate, "the catalyst plays a chore_misc anim once; the chain moves on when it ends (job_play_anim)"),
        ["cmd_ai_c_equip"]           = new(Activate, "the catalyst walks to target1 and equips it (job_equip)"),
        ["cmd_ai_c_drop"]            = new(Activate, "the catalyst drops target1 from its slot into the world (job_drop)"),
        ["cmd_ai_c_stop"]            = new(Activate, "the catalyst stops what it is doing (job_stop)"),
        ["cmd_ai_c_send_message"]    = new(Activate, "send a world message"),
        ["cmd_animation_command"]    = new(RegionLoad, "animation command fired on activation"),
        ["animate_object"]           = new(Activate, "object animation"),
        ["animate_chain"]            = new(Activate, "chain animation"),
        ["animate_elevator"]         = new(Activate, "elevator animation"),
        ["nodal_tex_anim"]           = new(Activate, "terrain texture animation"),

        // Party and game flow.
        ["cmd_party_wrangler"]                = new(Activate, "party wrangler"),
        ["cmd_move_party"]                    = new(Activate, "move the party"),
        ["cmd_stop_party"]                    = new(Activate, "stop the party"),
        ["cmd_selection_toggle"]              = new(Activate, "selection toggle"),
        ["cmd_alignment_changer"]             = new(Activate, "alignment changer"),
        ["cmd_auto_save"]                     = new(Activate, "auto-save"),
        ["cmd_report_gameplay_screen_player"] = new(Activate, "gameplay-screen report"),
        ["cmd_inventory_changer"]             = new(RegionLoad, "inventory changer"),
        ["cmd_puzzle_steam"]                  = new(RegionLoad, "steam-valve puzzle"),
        ["preload_go"]                        = new(Activate, "object preload"),
        ["fader_proxy"]                       = new(Activate, "screen fader"),

        // Camera and NIS.
        ["cmd_enter_nis"]       = new(Nis, "enter a non-interactive sequence"),
        ["cmd_leave_nis"]       = new(Nis, "leave the sequence"),
        ["cmd_camera_command"]  = new(Nis, "NIS camera pose"),
        ["cmd_camera_waypoint"] = new(Nis, "NIS camera waypoint"),
        ["cmd_camera_move"]     = new(Activate | Nis, "camera move (also a 2 s NIS snap pose)"),
        ["camera_quake"]        = new(Activate, "camera shake"),
        ["rock_beast_stomp"]    = new(Activate, "rock beast stomp shake"),

        // Lights.
        ["light_enable"]    = new(Activate | RegionLoad, "light toggle (light gizmo runner)"),
        ["light_flicker"]   = new(RegionLoad, "light flicker (light gizmo runner)"),
        ["light_colorwave"] = new(RegionLoad, "light colour wave (light gizmo runner)"),
    };

    /// <summary>The paths that act on <paramref name="template"/>;
    /// <see cref="CommandHandling.None"/> when nothing does.</summary>
    public static CommandHandling Classify(string template) =>
        Entries.TryGetValue(template, out var e) ? e.Handling : CommandHandling.None;

    /// <summary>True when <paramref name="path"/> acts on <paramref name="template"/>.</summary>
    public static bool Handles(string template, CommandHandling path) =>
        (Classify(template) & path) != 0;

    /// <summary>What the engine does with the template, for audit output; "" when unknown.</summary>
    public static string Describe(string template) =>
        Entries.TryGetValue(template, out var e) ? e.Note : "";

    /// <summary>Every template some path acts on.</summary>
    public static IEnumerable<string> HandledTemplates => Entries.Keys;
}

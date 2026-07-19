using Silk.NET.Input;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>SC-OPTIONS-REBIND — the runtime key-binding registry.
///
/// <para>The catalog below is the COMPLETE non-dev binding list from DS1's
/// <c>/config/input_bindings.gas</c> [game] section (extracted from
/// Logic.dsres), in authored order, carrying each entry's authored
/// <c>screen_name</c> and its authored group — Party Controls (id 1),
/// View Controls (2), User Interface (3), Game Settings (4). Actions the
/// engine doesn't consume yet still appear and rebind (they persist and
/// light up as their systems land), exactly like DS1's own list which
/// includes multiplayer-only rows in single-player.</para>
///
/// <para>Binding tokens are strings — "h", "ctrl+a", "alt+q", "f9",
/// "wheel_up", "" (unbound) — so they serialize into prefs.json as-is.
/// An action holds two slots (Primary / Secondary) per the authored
/// options_bindings screen.</para></summary>
internal sealed class KeyBindingRegistry
{
    public sealed record Def(string Id, string Name, string Group,
                             string DefPrimary, string DefSecondary = "");

    public const string GroupParty = "Party Controls";
    public const string GroupView  = "View Controls";
    public const string GroupUi    = "User Interface";
    public const string GroupGame  = "Game Settings";

    /// <summary>Catalog in authored order. Duplicate-input entries in the
    /// gas (select_all = ctrl+a AND e; game_pause = space AND pause;
    /// zoom = keys AND wheel) fold into the two slots.</summary>
    public static readonly Def[] Defs = BuildDefs();

    static Def[] BuildDefs()
    {
        var d = new List<Def>
        {
            // ---- Party Controls (gas group id 1) ----
            new("party_heal_body_with_potions",  "Drink Health Potion", GroupParty, "h"),
            new("party_heal_magic_with_potions", "Drink Mana Potion",   GroupParty, "m"),
            new("attack", "Force Attack",     GroupParty, "a"),
            new("cast",   "Force Cast Spell", GroupParty, "c"),
            new("guard",  "Guard Character",  GroupParty, "g"),
            new("move",   "Move",             GroupParty, ""),
            new("stop",   "Stop",             GroupParty, "s"),
        };
        for (int i = 1; i <= 8; i++)
            d.Add(new($"get_group_{i}", $"Recall Party Group {i}", GroupParty, $"f{i}"));
        for (int i = 1; i <= 8; i++)
            d.Add(new($"set_group_{i}", $"Save Party Group {i}", GroupParty, $"ctrl+f{i}"));
        for (int i = 1; i <= 10; i++)
        {
            int digit = i % 10;
            d.Add(new($"set_awp_{i:00}", $"Save Weapon Config. {digit}", GroupParty, $"ctrl+{digit}"));
        }
        for (int i = 1; i <= 10; i++)
        {
            int digit = i % 10;
            d.Add(new($"get_awp_{i:00}", $"Recall Weapon Config. {digit}", GroupParty, $"{digit}"));
        }
        d.AddRange(new Def[]
        {
            new("move_order_free",    "Movement: Move Freely", GroupParty, "alt+q"),
            new("move_order_limited", "Movement: Engage Only", GroupParty, "alt+w"),
            new("move_order_never",   "Movement: Hold Ground", GroupParty, "alt+e"),
            new("fight_order_always",    "Attack: Fight Freely", GroupParty, "alt+a"),
            new("fight_order_back_only", "Attack: Defend",       GroupParty, "alt+s"),
            new("fight_order_never",     "Attack: Hold Fire",    GroupParty, "alt+d"),
            new("target_closest",   "Targeting: Target Closest",   GroupParty, "alt+z"),
            new("target_weakest",   "Targeting: Target Weakest",   GroupParty, "alt+c"),
            new("target_strongest", "Targeting: Target Strongest", GroupParty, "alt+x"),
            new("select_all_party_members", "Select All Party Members", GroupParty, "ctrl+a", "e"),
            new("select_next_player",   "Select Next Party Member", GroupParty, "period"),
            new("select_last_player",   "Select Last Party Member", GroupParty, "comma"),
            new("select_lead_character","Select Lead Party Member", GroupParty, "slash"),
            new("rotate_selected_slots",      "Quick Weapon Select",  GroupParty, "q"),
            new("rotate_primary_spell_slot",  "Cycle Active Spell 1", GroupParty, ""),
            new("rotate_secondary_spell_slot","Cycle Active Spell 2", GroupParty, ""),
            new("formation_increase_spacing", "Formation: Expand",   GroupParty, "lbracket"),
            new("formation_decrease_spacing", "Formation: Contract", GroupParty, "rbracket"),
            new("cycle_formations",           "Formation: Cycle",    GroupParty, ""),

            // ---- View Controls (gas group id 2) ----
            new("camera_track_toggle", "Camera: Track/Hold Toggle", GroupView, "t"),
            new("camera_rotate_left",  "Camera: Rotate Left",  GroupView, "left"),
            new("camera_rotate_right", "Camera: Rotate Right", GroupView, "right"),
            new("camera_rotate_up",    "Camera: Rotate Up",    GroupView, "up"),
            new("camera_rotate_down",  "Camera: Rotate Down",  GroupView, "down"),
            new("camera_zoom_out", "Camera: Zoom Out", GroupView, "minus",  "wheel_down"),
            new("camera_zoom_in",  "Camera: Zoom In",  GroupView, "equals", "wheel_up"),
            new("camera_free_look","Camera: Free Look", GroupView, "d"),

            // ---- User Interface (gas group id 3) ----
            new("game_pause",          "Pause Dungeon Siege",   GroupUi, "space", "pause"),
            new("toggle_game_timer",   "Game Timer",            GroupUi, "semicolon"),
            new("toggle_player_labels","Character Labels",      GroupUi, "l"),
            new("toggle_player_ranks", "Multiplayer Scoreboard",GroupUi, "backslash"),
            new("tutorial_tips",       "Adventurer's Handbook", GroupUi, "f12"),
            new("sort_inventory",      "Auto-Sort Inventory",   GroupUi, "k"),
            new("field_commands",      "Field Commands",        GroupUi, "f"),
            new("inventory",           "Inventory",             GroupUi, "i"),
            new("magic",               "Spell Book",            GroupUi, "b"),
            new("toggle_item_labels",  "Item Labels",           GroupUi, "alt"),
            new("toggle_mini_map",     "MegaMap",               GroupUi, "tab"),
            new("toggle_status_bars",  "Health/Mana Bars",      GroupUi, "x"),
            new("toggle_gui_edit_box",          "Chat Window",                GroupUi, "return"),
            new("toggle_gui_edit_box_team",     "Chat Window (Send to Team)", GroupUi, "shift+return"),
            new("toggle_gui_edit_box_everyone", "Chat Window (Send to All)",  GroupUi, "ctrl+return"),
            new("expert_gui_mode", "Minimize/Maximize Weapons Panel", GroupUi, "w"),
            // SC-KEY-AUDIT — SiegeFX extension row (not in the authored gas):
            // our UI splits the character sheet out of the inventory screen,
            // and its old hardcoded C shadowed the AUTHORED [cast] = key_c
            // (Force Cast Spell could never fire). P is unclaimed in the
            // authored map; rebindable like everything else.
            new("character_sheet", "Character Sheet", GroupUi, "p"),
            new("toggle_quest_log","Journal",       GroupUi, "j"),
            new("collect_loot",    "Collect Loot",  GroupUi, "z"),
            new("chat_history_up",   "Chat History: Scroll Up",    GroupUi, "pageup"),
            new("chat_history_down", "Chat History: Scroll Down",  GroupUi, "pagedown"),
            new("chat_history_clear","Chat History: Clear History",GroupUi, "end"),
            new("chat_history_lock", "Chat History: Lock Toggle",  GroupUi, "scrolllock"),
            new("disband_selected",  "Disband Selected Members",   GroupUi, "ctrl+d"),

            // ---- Game Settings (gas group id 4) ----
            new("game_speed_up",    "Game Speed: Increase", GroupGame, "ctrl+equals"),
            new("game_speed_down",  "Game Speed: Decrease", GroupGame, "ctrl+minus"),
            new("game_speed_reset", "Game Speed: Reset",    GroupGame, "ctrl+back"),
            new("quick_save",   "Quick Save",   GroupGame, "f9"),
            new("quick_load",   "Quick Load",   GroupGame, "f11"),
            new("save_game",    "Save Game",    GroupGame, "ctrl+s"),
            new("load_game",    "Load Game",    GroupGame, "ctrl+l"),
            new("game_options", "Game Options", GroupGame, "f10"),
            new("close_dialogs","Close Dialogs",GroupGame, ""),
        });
        return d.ToArray();
    }

    // Live map: id -> [primary, secondary] tokens. Seeded from defaults;
    // overrides layered from prefs.json at boot / on OK commit.
    readonly Dictionary<string, string[]> _map = new();

    public KeyBindingRegistry() => ResetToDefaults();

    public void ResetToDefaults()
    {
        _map.Clear();
        foreach (var def in Defs)
            _map[def.Id] = new[] { def.DefPrimary, def.DefSecondary };
    }

    /// <summary>Layer persisted overrides (prefs.json) over the defaults.
    /// Unknown ids are ignored so a stale prefs file can't poison the map.</summary>
    public void LoadOverrides(Dictionary<string, string[]>? overrides)
    {
        ResetToDefaults();
        if (overrides is null) return;
        foreach (var (id, slots) in overrides)
        {
            if (!_map.ContainsKey(id) || slots is null) continue;
            _map[id] = new[]
            {
                slots.Length > 0 ? Normalize(slots[0]) : "",
                slots.Length > 1 ? Normalize(slots[1]) : "",
            };
        }
    }

    /// <summary>Snapshot the current map (deep copy) — for prefs.json and
    /// the Options panel's staged-edit buffer.</summary>
    public Dictionary<string, string[]> Snapshot()
    {
        var copy = new Dictionary<string, string[]>(_map.Count);
        foreach (var (id, slots) in _map) copy[id] = (string[])slots.Clone();
        return copy;
    }

    /// <summary>Replace the live map wholesale (OK commit path).</summary>
    public void Apply(Dictionary<string, string[]> map)
    {
        foreach (var (id, slots) in map)
            if (_map.ContainsKey(id) && slots is { Length: 2 })
                _map[id] = (string[])slots.Clone();
    }

    public string[] Get(string id) =>
        _map.TryGetValue(id, out var s) ? s : new[] { "", "" };

    /// <summary>Does the pressed key + live modifier state match either
    /// slot of the action? Modifier matching is exact for ctrl/alt (a
    /// bare-key binding refuses to fire under a held modifier so "h"
    /// and a hypothetical "ctrl+h" can coexist); shift is only required
    /// when the token asks for it (DS1's ignore-shift default).</summary>
    public bool Matches(string id, Key key, bool ctrl, bool alt, bool shift)
    {
        var slots = Get(id);
        for (int i = 0; i < 2; i++)
        {
            if (!TryParse(slots[i], out var bKey, out var bCtrl, out var bAlt, out var bShift))
                continue;
            if (bKey != key) continue;
            if (bCtrl != ctrl || bAlt != alt) continue;
            if (bShift && !shift) continue;
            return true;
        }
        return false;
    }

    /// <summary>Per-frame poll for held camera keys: true while either
    /// slot's key is down with its modifiers satisfied.</summary>
    public bool AnyPressed(string id, IKeyboard kb)
    {
        bool ctrl = kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight);
        bool alt  = kb.IsKeyPressed(Key.AltLeft)  || kb.IsKeyPressed(Key.AltRight);
        var slots = Get(id);
        for (int i = 0; i < 2; i++)
        {
            if (!TryParse(slots[i], out var bKey, out var bCtrl, out var bAlt, out _))
                continue;
            if (bCtrl != ctrl || bAlt != alt) continue;
            if (kb.IsKeyPressed(bKey)) return true;
        }
        return false;
    }

    /// <summary>Is the action's CURRENT binding the bare Alt modifier
    /// (the authored toggle_item_labels special, fired on Alt release)?</summary>
    public bool IsBoundToBareAlt(string id)
    {
        var slots = Get(id);
        return slots[0] == "alt" || slots[1] == "alt";
    }

    // ---------------- token handling ----------------

    /// <summary>Compose a token from a captured keypress.</summary>
    public static string TokenFor(Key key, bool ctrl, bool alt, bool shift)
    {
        string? name = KeyToToken(key);
        if (name is null) return "";
        string mods = (ctrl ? "ctrl+" : "") + (alt ? "alt+" : "") + (shift ? "shift+" : "");
        return mods + name;
    }

    static string Normalize(string token) => (token ?? "").Trim().ToLowerInvariant();

    /// <summary>Parse "ctrl+alt+f1"-style tokens. Pure-modifier tokens
    /// ("alt") and wheel tokens ("wheel_up") return false — they're
    /// display/special-path entries, not KeyDown-matchable bindings.</summary>
    public static bool TryParse(string token, out Key key,
                                out bool ctrl, out bool alt, out bool shift)
    {
        key = Key.Unknown; ctrl = alt = shift = false;
        token = Normalize(token);
        if (token.Length == 0 || token == "alt" || token.StartsWith("wheel_")) return false;
        var parts = token.Split('+', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i])
            {
                case "ctrl":  ctrl = true; break;
                case "alt":   alt = true; break;
                case "shift": shift = true; break;
                default: return false;
            }
        }
        return TokenToKey(parts[^1], out key);
    }

    static readonly (string Token, Key Key)[] SpecialKeys =
    {
        ("0", Key.Number0), ("1", Key.Number1), ("2", Key.Number2),
        ("3", Key.Number3), ("4", Key.Number4), ("5", Key.Number5),
        ("6", Key.Number6), ("7", Key.Number7), ("8", Key.Number8),
        ("9", Key.Number9),
        ("minus", Key.Minus), ("equals", Key.Equal),
        ("lbracket", Key.LeftBracket), ("rbracket", Key.RightBracket),
        ("semicolon", Key.Semicolon), ("apostrophe", Key.Apostrophe),
        ("comma", Key.Comma), ("period", Key.Period), ("slash", Key.Slash),
        ("backslash", Key.BackSlash), ("grave", Key.GraveAccent),
        ("space", Key.Space), ("tab", Key.Tab), ("return", Key.Enter),
        ("back", Key.Backspace), ("escape", Key.Escape),
        ("pause", Key.Pause), ("scrolllock", Key.ScrollLock),
        ("pageup", Key.PageUp), ("pagedown", Key.PageDown),
        ("home", Key.Home), ("end", Key.End),
        ("insert", Key.Insert), ("delete", Key.Delete),
        ("left", Key.Left), ("right", Key.Right),
        ("up", Key.Up), ("down", Key.Down),
    };

    static bool TokenToKey(string t, out Key key)
    {
        foreach (var (tok, k) in SpecialKeys)
            if (tok == t) { key = k; return true; }
        if (t.Length == 1 && t[0] >= 'a' && t[0] <= 'z')
        { key = Key.A + (t[0] - 'a'); return true; }
        if (t.Length is 2 or 3 && t[0] == 'f'
            && int.TryParse(t[1..], out int fn) && fn is >= 1 and <= 24)
        { key = Key.F1 + (fn - 1); return true; }
        key = Key.Unknown;
        return false;
    }

    static string? KeyToToken(Key key)
    {
        foreach (var (tok, k) in SpecialKeys)
            if (k == key) return tok;
        if (key is >= Key.A and <= Key.Z)
            return ((char)('a' + (key - Key.A))).ToString();
        if (key is >= Key.F1 and <= Key.F24)
            return "f" + (1 + (key - Key.F1));
        if (key is >= Key.Keypad0 and <= Key.Keypad9)
            return null; // numpad reserved for the dev grip tuner
        return null;
    }

    /// <summary>Human-readable cell text, matching DS1's presentation
    /// ("ALT S", "CTRL A", "WHEEL UP", "F9", ".").</summary>
    public static string Display(string token)
    {
        token = Normalize(token);
        if (token.Length == 0) return "";
        if (token == "alt") return "ALT";
        if (token == "wheel_up") return "WHEEL UP";
        if (token == "wheel_down") return "WHEEL DOWN";
        var parts = token.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(p switch
            {
                "ctrl" => "CTRL", "alt" => "ALT", "shift" => "SHIFT",
                "minus" => "-", "equals" => "=", "comma" => ",",
                "period" => ".", "slash" => "/", "backslash" => "\\",
                "semicolon" => ";", "apostrophe" => "'", "grave" => "`",
                "lbracket" => "[", "rbracket" => "]",
                "space" => "SPACE", "return" => "ENTER", "back" => "BACKSPACE",
                _ => p.ToUpperInvariant(),
            });
        }
        return sb.ToString();
    }
}

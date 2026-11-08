using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Net;
using Silk.NET.Input;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>The host's Game Settings and Map Settings, drawn where the player
/// list sits, from the original's files:
/// staging_area_game_settings.gas (Player Limit, Difficulty, Time Limit and
/// Drop on Death drop-downs; Enable Team Play, Enable Player vs. Player, Allow
/// players to choose start locations, Restrict Players to New Characters,
/// Allow players to pause the game, Allow players to join game in progress;
/// Change Game Password...; Accept / Cancel) and
/// staging_area_map_settings.gas (Select Map, Select World Difficulty, Map
/// Description; Accept / Cancel). The panel edits a copy; Accept hands it
/// back. The original's Game Type and Character Respawn controls overlap
/// others in its file and were never shown by the shipped game; they are not
/// drawn here either.</summary>
internal sealed class MpSettingsPanel
{
    public enum View { Closed, Game, Map }
    public enum Action { None, Accept, Cancel }

    public View Mode { get; private set; } = View.Closed;
    public bool IsOpen => Mode != View.Closed;
    /// <summary>The rules being edited (Accept: the new ones).</summary>
    public MpGameSettings Draft { get; private set; } = new();
    /// <summary>The password being edited (null = unchanged).</summary>
    public string? PasswordDraft { get; private set; }
    public IReadOnlyList<MpMapInfo> Maps { get; private set; } = Array.Empty<MpMapInfo>();
    /// <summary>Players in the game now (the limit cannot go below it).</summary>
    public int Players = 1;

    readonly MpEditDialog _password = new();
    readonly AuthoredFrame _f = new();
    string _hover = "", _pressed = "", _open = "";
    int _comboScroll;
    Action _pending = Action.None;
    public Action ConsumeAction() { var a = _pending; _pending = Action.None; return a; }

    public bool Modal => _password.IsOpen;

    public void OpenGame(MpGameSettings current, string password)
    {
        Mode = View.Game; Draft = current; PasswordDraft = null; _open = ""; _pressed = "";
        _currentPassword = password;
    }
    string _currentPassword = "";

    public void OpenMap(MpGameSettings current, IReadOnlyList<MpMapInfo> maps)
    {
        Mode = View.Map; Draft = current; Maps = maps; _open = ""; _pressed = "";
    }

    public void Close() { Mode = View.Closed; _open = ""; _pressed = ""; _password.Close(); }

    // ---- the drop-downs --------------------------------------------------

    record Combo(string Name, int X0, int Y0, int X1, int Y1, int MaxRows, Func<IReadOnlyList<string>> Items,
                 Func<int> Current, Action<int> Pick);

    IEnumerable<Combo> Combos()
    {
        if (Mode == View.Game)
        {
            yield return new Combo("limit", 80, 118, 250, 138, 4,
                () => Enumerable.Range(MpGameSettings.MinPlayers, MpGameSettings.MaxPlayers - MpGameSettings.MinPlayers + 1)
                                .Select(n => $"{n} Players").ToList(),
                () => Draft.PlayerLimit - MpGameSettings.MinPlayers,
                i => Draft = Draft with { PlayerLimit = Math.Max(i + MpGameSettings.MinPlayers, Players) });
            yield return new Combo("difficulty", 80, 168, 250, 188, 6, () => MpGameSettings.DifficultyNames,
                () => Draft.Difficulty, i => Draft = Draft with { Difficulty = i });
            yield return new Combo("time", 80, 218, 250, 238, 6,
                () => MpGameSettings.TimeLimits.Select(MpGameSettings.TimeLimitName).ToList(),
                () => Math.Max(0, MpGameSettings.TimeLimits.ToList().IndexOf(Draft.TimeLimitMinutes)),
                i => Draft = Draft with { TimeLimitMinutes = MpGameSettings.TimeLimits[i] });
            yield return new Combo("drop", 80, 268, 250, 288, 6, () => MpGameSettings.DropNames,
                () => (int)Draft.DropOnDeath, i => Draft = Draft with { DropOnDeath = (MpDropOnDeath)i });
        }
        else if (Mode == View.Map)
        {
            yield return new Combo("world", 500, 100, 680, 120, 6,
                () => CurrentMap?.Worlds.Select(w => w.ScreenName).ToList() ?? new List<string> { Draft.WorldName },
                () => Math.Max(0, CurrentMap?.Worlds.ToList().FindIndex(w => w.Name == Draft.World) ?? 0),
                i =>
                {
                    if (CurrentMap is { } m && i < m.Worlds.Count)
                        Draft = Draft with { World = m.Worlds[i].Name, WorldName = m.Worlds[i].ScreenName };
                });
        }
    }

    MpMapInfo? CurrentMap => Maps.FirstOrDefault(m => m.Name == Draft.Map) ?? Maps.FirstOrDefault();

    // The checkboxes (box rect, then the label's text_box rect).
    static readonly (string Name, int X, int Y, int TX0, int TY0, int TX1, int TY1, string Text)[] Checks =
    {
        ("team", 315, 112, 339, 98, 485, 138, "Enable Team Play"),
        ("pvp", 315, 162, 339, 148, 485, 188, "Enable Player vs. Player"),
        ("start", 315, 212, 339, 198, 485, 238, "Allow players to choose start locations"),
        ("newonly", 315, 262, 339, 248, 485, 288, "Restrict Players to New Characters"),
        ("pause", 550, 112, 574, 98, 720, 138, "Allow players to pause the game"),
        ("join", 550, 162, 574, 148, 720, 188, "Allow players to join game in progress"),
    };

    bool Checked(string name) => name switch
    {
        "team" => Draft.TeamPlay,
        "pvp" => Draft.PlayerVsPlayer,
        "start" => Draft.AllowStartChoice,
        "newonly" => Draft.NewCharactersOnly,
        "pause" => Draft.AllowPause,
        "join" => Draft.AllowJoinInProgress,
        _ => false,
    };

    void Toggle(string name) => Draft = name switch
    {
        "team" => Draft with { TeamPlay = !Draft.TeamPlay },
        "pvp" => Draft with { PlayerVsPlayer = !Draft.PlayerVsPlayer },
        "start" => Draft with { AllowStartChoice = !Draft.AllowStartChoice },
        "newonly" => Draft with { NewCharactersOnly = !Draft.NewCharactersOnly },
        "pause" => Draft with { AllowPause = !Draft.AllowPause },
        "join" => Draft with { AllowJoinInProgress = !Draft.AllowJoinInProgress },
        _ => Draft,
    };

    // ---- hit testing ---------------------------------------------------

    string HitName(int px, int py)
    {
        bool H(int x0, int y0, int x1, int y1) => _f.Hit(x0, y0, x1, y1, px, py);
        // An open drop-down covers what is under it.
        if (_open.Length > 0 && Combos().FirstOrDefault(c => c.Name == _open) is { } open)
        {
            int n = Math.Min(open.MaxRows, open.Items().Count - _comboScroll);
            for (int i = 0; i < n; i++)
                if (H(open.X0, open.Y1 + 16 * i, open.X1, open.Y1 + 16 * (i + 1))) return "item" + i;
        }
        if (H(252, 308, 392, 324)) return "accept";
        if (H(408, 308, 548, 324)) return "cancel";
        foreach (var c in Combos()) if (H(c.X0, c.Y0, c.X1, c.Y1)) return "combo:" + c.Name;
        if (Mode == View.Game)
        {
            foreach (var ck in Checks)
                if (H(ck.X, ck.Y, ck.X + 16, ck.Y + 16) || H(ck.TX0, ck.Y - 2, ck.TX1, ck.Y + 18)) return "check:" + ck.Name;
            if (H(530, 270, 740, 286)) return "password";
        }
        else if (Mode == View.Map)
        {
            for (int i = 0; i < Maps.Count && 103 + 16 * (i + 1) <= 287; i++)
                if (H(223, 103 + 16 * i, 477, 103 + 16 * (i + 1))) return "map" + i;
        }
        return "";
    }

    AuthoredFrame.Press State(string n) => _pressed == n && _hover == n ? AuthoredFrame.Press.Down
        : _hover == n ? AuthoredFrame.Press.Hover : AuthoredFrame.Press.Up;

    // ---- input ---------------------------------------------------------

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        _f.Layout(vw, vh);
        if (_password.IsOpen) { _password.OnMouseMove(px, py, vw, vh); _hover = ""; return; }
        _hover = HitName(px, py);
    }

    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        _f.Layout(vw, vh);
        if (_password.IsOpen) { _password.OnMouseDown(px, py, vw, vh); return; }
        _pressed = HitName(px, py);
        if (_pressed.StartsWith("item", StringComparison.Ordinal)
            && Combos().FirstOrDefault(c => c.Name == _open) is { } open)
        {
            int i = _comboScroll + int.Parse(_pressed[4..]);
            if (i < open.Items().Count) open.Pick(i);
            _open = ""; _pressed = "";
            return;
        }
        if (_open.Length > 0 && !_pressed.StartsWith("combo:", StringComparison.Ordinal)) _open = "";
    }

    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsOpen) { _pressed = ""; return; }
        _f.Layout(vw, vh);
        if (_password.IsOpen)
        {
            _password.OnMouseUp(px, py, vw, vh);
            DrainPassword();
            return;
        }
        string hit = HitName(px, py);
        if (hit.Length > 0 && hit == _pressed) Press(hit);
        _pressed = "";
    }

    void Press(string hit)
    {
        if (hit == "accept") { _open = ""; _pending = Action.Accept; return; }
        if (hit == "cancel") { _open = ""; _pending = Action.Cancel; return; }
        if (hit == "password") { _password.Open(MpEditDialog.Kind.ChangePassword, PasswordDraft ?? _currentPassword); return; }
        if (hit.StartsWith("combo:", StringComparison.Ordinal))
        {
            string name = hit[6..];
            _open = _open == name ? "" : name;
            if (_open.Length > 0 && Combos().FirstOrDefault(c => c.Name == _open) is { } c)
                _comboScroll = Math.Clamp(c.Current() - c.MaxRows / 2, 0, Math.Max(0, c.Items().Count - c.MaxRows));
            return;
        }
        if (hit.StartsWith("check:", StringComparison.Ordinal)) { Toggle(hit[6..]); return; }
        if (hit.StartsWith("map", StringComparison.Ordinal) && int.TryParse(hit[3..], out int mi) && mi < Maps.Count)
        {
            var m = Maps[mi];
            var w = m.Worlds.FirstOrDefault(x => x.Name == Draft.World) ?? m.Worlds.FirstOrDefault();
            Draft = Draft with
            {
                Map = m.Name, MapName = m.ScreenName,
                World = w?.Name ?? "normal", WorldName = w?.ScreenName ?? "Regular",
            };
        }
    }

    void DrainPassword()
    {
        switch (_password.Consume())
        {
            case MpEditDialog.Result.Ok: PasswordDraft = _password.Text; _password.Close(); break;
            case MpEditDialog.Result.Cancel: _password.Close(); break;
        }
    }

    public void OnWheel(int delta)
    {
        if (!IsOpen || _open.Length == 0) return;
        if (Combos().FirstOrDefault(c => c.Name == _open) is { } c)
            _comboScroll = Math.Clamp(_comboScroll + (delta < 0 ? 1 : -1), 0, Math.Max(0, c.Items().Count - c.MaxRows));
    }

    /// <summary>A typed character for the password box. True when it was used.</summary>
    public bool OnChar(char c)
    {
        if (!IsOpen || !_password.IsOpen) return false;
        if (!char.IsControl(c)) _password.OnChar(c);
        return true;
    }

    public bool OnKey(Key key)
    {
        if (!IsOpen) return false;
        if (_password.IsOpen)
        {
            if (key == Key.Backspace) _password.OnChar('\b');
            else if (key is Key.Enter or Key.KeypadEnter) { _password.OnChar('\r'); DrainPassword(); }
            else if (key == Key.Escape) _password.Close();
            return true;
        }
        if (key == Key.Escape) { if (_open.Length > 0) _open = ""; else _pending = Action.Cancel; return true; }
        return false;
    }

    // ---- drawing -------------------------------------------------------

    public void Draw(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
    {
        if (!IsOpen) return;
        _f.Begin(vw, vh, icons, bars, text, tex, common);
        _f.Label(34, 70, 200, 90, Mode == View.Game ? "Game Settings" : "Map Settings", "20p");
        if (Mode == View.Game) DrawGame(); else DrawMap();
        _f.Button(252, 308, 392, 324, "Accept", State("accept"));
        _f.Button(408, 308, 548, 324, "Cancel", State("cancel"));
        foreach (var c in Combos()) DrawCombo(c);
        if (_open.Length > 0 && Combos().FirstOrDefault(c => c.Name == _open) is { } open) DrawDropDown(open);
    }

    public void DrawModal(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                          Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
        => _password.Draw(vw, vh, icons, bars, text, tex, common);

    void DrawGame()
    {
        _f.Label(80, 100, 250, 116, "Player Limit");
        _f.Label(80, 150, 250, 166, "Difficulty");
        _f.Label(80, 200, 250, 216, "Time Limit");
        _f.Label(80, 250, 250, 266, "Drop on Death");
        foreach (var ck in Checks)
        {
            _f.Window(ck.X, ck.Y, ck.X + 16, ck.Y + 16, "b_gui_cmn_checkbox");
            if (Checked(ck.Name)) _f.Window(ck.X, ck.Y, ck.X + 16, ck.Y + 16, "b_gui_cmn_checkbox_x");
            DrawWrapped(ck.TX0, ck.TY0, ck.TX1, ck.TY1, ck.Text,
                        _hover == "check:" + ck.Name ? AuthoredFrame.White : AuthoredFrame.White);
        }
        bool changed = PasswordDraft is not null && PasswordDraft != _currentPassword;
        _f.Button(530, 270, 740, 286, "Change Game Password...", State("password"));
        string pw = (PasswordDraft ?? _currentPassword).Length > 0 ? "This game has a password." : "This game has no password.";
        _f.Label(530, 290, 740, 304, changed ? pw + " (changed)" : pw, "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
    }

    void DrawMap()
    {
        _f.Label(220, 80, 400, 96, "Select Map", "14p");
        _f.Box(220, 100, 480, 290);
        for (int i = 0; i < Maps.Count && 103 + 16 * (i + 1) <= 287; i++)
        {
            int y = 103 + 16 * i;
            if (Maps[i].Name == Draft.Map) _f.Selection(223, y, 477, y + 16, 0.5f);
            _f.Label(227, y, 473, y + 16, Maps[i].ScreenName);
        }
        _f.Label(500, 80, 756, 96, "Select World Difficulty", "14p");
        _f.Label(500, 140, 756, 156, "Map Description", "14p");
        _f.Box(500, 160, 756, 290);
        string desc = CurrentMap?.Description ?? "";
        if (CurrentMap?.Worlds.FirstOrDefault(w => w.Name == Draft.World) is { Description.Length: > 0 } world)
            desc = desc.Length > 0 ? desc + "\n\n" + world.Description : world.Description;
        DrawWrapped(508, 168, 748, 282, desc, AuthoredFrame.White, center: true, top: true);
    }

    void DrawCombo(Combo c)
    {
        _f.Box(c.X0, c.Y0, c.X1, c.Y1);
        var items = c.Items();
        int cur = c.Current();
        _f.Label(c.X0 + 4, c.Y0 + 3, c.X1 - 20, c.Y1 - 2, cur >= 0 && cur < items.Count ? items[cur] : "");
        _f.Arrow(c.X1 - 18, c.Y0 + 2, c.X1 - 2, c.Y1 - 2, "down", State("combo:" + c.Name));
    }

    void DrawDropDown(Combo c)
    {
        var items = c.Items();
        int n = Math.Min(c.MaxRows, items.Count - _comboScroll);
        _f.Box(c.X0, c.Y1, c.X1, c.Y1 + 16 * n + 2, "cpbox_thin_dark");
        for (int i = 0; i < n; i++)
        {
            int idx = _comboScroll + i, y = c.Y1 + 1 + 16 * i;
            if (_hover == "item" + i || (idx == c.Current() && !_hover.StartsWith("item", StringComparison.Ordinal)))
                _f.Selection(c.X0 + 2, y, c.X1 - 2, y + 16, 0.8f);
            _f.Label(c.X0 + 6, y, c.X1 - 6, y + 16, items[idx]);
        }
    }

    // A text_box: word-wrapped lines, centered vertically in the rect (or from the top).
    void DrawWrapped(int x0, int y0, int x1, int y1, string msg, Vector4 ink, bool center = false, bool top = false)
    {
        int width = (int)MathF.Round((x1 - x0) * _f.Scale);
        var lines = new List<string>();
        foreach (var para in msg.Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && _f.Measure(next) > width) { lines.Add(line); line = word; }
                else line = next;
            }
            lines.Add(line);
        }
        int lh = Math.Max(1, (int)MathF.Round(_f.LineHeight() / _f.Scale) + 1);
        int y = top ? y0 : y0 + Math.Max(0, (y1 - y0 - lines.Count * lh) / 2);
        foreach (var l in lines)
        {
            if (y + lh > y1 + 1) break;
            _f.Label(x0, y, x1, y + lh, l, "", ink, center ? AuthoredFrame.Justify.Center : AuthoredFrame.Justify.Left);
            y += lh;
        }
    }
}

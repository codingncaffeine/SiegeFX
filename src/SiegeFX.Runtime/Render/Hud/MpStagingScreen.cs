using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Net;
using Silk.NET.Input;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>SC-MP-EOS P8 — the staging area, drawn from the original's
/// staging_area.gas and staging_area_players.gas over the multiplayer
/// backdrop: Map Settings and Game Settings (the host's), the player list
/// (Player / Team / Start Location / Character, one staging row per player,
/// the ready mark, your own row framed: your Team cell picks a team sign
/// (staging_area_select_team.gas) when team play is on, the host's Start
/// Location cell picks where the party starts when start locations may be
/// chosen, your Character cell opens Select Character), the chat window with
/// its entry box, the Settings Summary, Leave Game, and Start Game (host) or
/// "I'm Ready to Begin" (joiner) or Enter Game (a joiner arriving in a game
/// already under way). The co-op character screens (<see cref="MpCharacterPanel"/>)
/// and the settings screens (<see cref="MpSettingsPanel"/>) take the player
/// list's place while they are open.</summary>
internal sealed class MpStagingScreen
{
    public enum Action
    {
        None, Leave, Start, ToggleReady, EnterGame, ChooseCharacter, SendChat,
        OpenMapSettings, OpenGameSettings, PickTeam, PickStart,
    }

    public bool IsActive { get; set; }
    public bool IsHost;
    public bool LocalReady;
    /// <summary>The game is already under way: this joiner enters it (Enter
    /// Game) instead of marking ready.</summary>
    public bool InProgress;
    public string HostIp = "";
    public string Status = "";
    /// <summary>The host's rules (the Settings Summary, the Team and Start
    /// Location columns, the character screens' new-characters-only rule).</summary>
    public MpGameSettings Rules = new();
    /// <summary>The map's start locations (the host's Start Location drop-down).</summary>
    public IReadOnlyList<MpStartLocation> Starts = Array.Empty<MpStartLocation>();

    /// <summary>One player list row. Player 0 is the host; <see cref="Local"/>
    /// is this machine's player. Team: 0 = none, 1..16 a team sign.</summary>
    public readonly record struct Row(string Name, bool Host, bool Ready, string Character, bool Local, int Team = 0);
    public readonly List<Row> Players = new();
    public readonly List<string> Chat = new();
    /// <summary>This player's chosen character for its Character cell ("" = none yet).</summary>
    public string LocalCharacter = "";

    public readonly MpCharacterPanel Characters = new();
    public readonly MpSettingsPanel Settings = new();
    /// <summary>A joiner's password, asked again when the host turned it away.</summary>
    public readonly MpEditDialog Password = new();

    public string ChatInput { get; private set; } = "";
    /// <summary>The line Enter sent (read with the SendChat action).</summary>
    public string SentChat { get; private set; } = "";
    /// <summary>The team sign picked (read with PickTeam; 0 = none).</summary>
    public int PickedTeam { get; private set; }
    /// <summary>The start location picked (read with PickStart).</summary>
    public MpStartLocation? PickedStart { get; private set; }

    readonly AuthoredFrame _f = new();
    string _hover = "", _pressed = "";
    bool _teamOpen, _startOpen;
    int _summaryScroll;
    Action _pending = Action.None;
    public Action ConsumeAction() { var a = _pending; _pending = Action.None; return a; }

    bool PanelOpen => Characters.IsOpen || Settings.IsOpen;
    int LocalIndex => Players.FindIndex(r => r.Local);
    static int RowY(int i) => 98 + 30 * i;

    // staging_area_select_team.gas: a 90x200 box under the Team cell holding
    // the sixteen team signs, two to a row.
    (int X0, int Y0) TeamPopupAt
    {
        get
        {
            int y = RowY(Math.Max(0, LocalIndex)) + 24;
            if (y + 200 > 592) y = RowY(Math.Max(0, LocalIndex)) - 200;
            return (225, y);
        }
    }

    string HitName(int px, int py)
    {
        bool H(int x0, int y0, int x1, int y1) => _f.Hit(x0, y0, x1, y1, px, py);
        if (_teamOpen)
        {
            var (tx, ty) = TeamPopupAt;
            for (int t = 1; t <= MpTeams.Count; t++)
            {
                int col = (t - 1) % 2, row = (t - 1) / 2;
                int x = tx + 5 + col * 40, y = ty + 4 + row * 24;
                if (H(x, y, x + 40, y + 24)) return "teamsign" + t;
            }
            return H(tx, ty, tx + 90, ty + 200) ? "teampopup" : "";
        }
        if (_startOpen)
        {
            int y0 = RowY(Math.Max(0, LocalIndex)) + 20;
            for (int i = 0; i < Starts.Count; i++)
                if (H(320, y0 + 16 * i, 532, y0 + 16 * (i + 1))) return "startitem" + i;
            return "";
        }
        if (H(580, 470, 756, 486)) return "leave";
        if (IsHost) { if (H(580, 498, 756, 514)) return "start"; }
        else if (InProgress) { if (H(580, 498, 756, 514)) return "enter"; }
        else if (H(584, 498, 756, 514)) return "ready";
        if (IsHost && !PanelOpen)
        {
            if (H(446, 34, 606, 50)) return "mapsettings";
            if (H(616, 34, 776, 50)) return "gamesettings";
        }
        if (H(24, 490, 546, 514)) return "chat";
        if (H(556, 368, 776, 460)) return "summary";
        if (!PanelOpen)
        {
            int local = LocalIndex;
            if (local >= 0 && local < 8)
            {
                int y = RowY(local);
                if (H(541, y + 4, 762, y + 20)) return "character";
                if (Rules.TeamPlay && H(225, y + 2, 315, y + 22)) return "team";
                if (IsHost && Rules.AllowStartChoice && Starts.Count > 0 && H(320, y + 4, 532, y + 20)) return "startcell";
            }
        }
        return "";
    }

    AuthoredFrame.Press State(string name) =>
        _pressed == name && _hover == name ? AuthoredFrame.Press.Down
        : _hover == name ? AuthoredFrame.Press.Hover : AuthoredFrame.Press.Up;

    bool ModalUp => Characters.AskingDelete || Settings.Modal || Password.IsOpen;

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        _f.Layout(vw, vh);
        if (Password.IsOpen) { Password.OnMouseMove(px, py, vw, vh); _hover = ""; return; }
        Characters.OnMouseMove(px, py, vw, vh);
        Settings.OnMouseMove(px, py, vw, vh);
        _hover = ModalUp ? "" : HitName(px, py);
    }

    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        _f.Layout(vw, vh);
        if (Password.IsOpen) { Password.OnMouseDown(px, py, vw, vh); return; }
        if (Characters.AskingDelete) { Characters.OnMouseDown(px, py, vw, vh); return; }
        if (Settings.Modal) { Settings.OnMouseDown(px, py, vw, vh); return; }
        _pressed = HitName(px, py);
        if (_teamOpen && _pressed.Length == 0) { _teamOpen = false; return; }
        if (_startOpen && _pressed.Length == 0) { _startOpen = false; return; }
        if (_pressed.Length == 0)
        {
            Characters.OnMouseDown(px, py, vw, vh);
            Settings.OnMouseDown(px, py, vw, vh);
        }
    }

    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsActive) { _pressed = ""; return; }
        _f.Layout(vw, vh);
        if (Password.IsOpen) { Password.OnMouseUp(px, py, vw, vh); _pressed = ""; return; }
        if (Characters.AskingDelete || Settings.Modal || _pressed.Length == 0)
        {
            Characters.OnMouseUp(px, py, vw, vh);
            Settings.OnMouseUp(px, py, vw, vh);
            _pressed = "";
            return;
        }
        string hit = HitName(px, py);
        if (hit == _pressed)
        {
            if (hit.StartsWith("teamsign", StringComparison.Ordinal))
            {
                int t = int.Parse(hit[8..]);
                int mine = LocalIndex >= 0 ? Players[LocalIndex].Team : 0;
                PickedTeam = t == mine ? 0 : t;
                _teamOpen = false;
                _pending = Action.PickTeam;
            }
            else if (hit.StartsWith("startitem", StringComparison.Ordinal))
            {
                int i = int.Parse(hit[9..]);
                if (i < Starts.Count) { PickedStart = Starts[i]; _pending = Action.PickStart; }
                _startOpen = false;
            }
            else
                switch (hit)
                {
                    case "leave": _pending = Action.Leave; break;
                    case "start": _pending = Action.Start; break;
                    case "enter": _pending = Action.EnterGame; break;
                    case "ready": _pending = Action.ToggleReady; break;
                    case "character": _pending = Action.ChooseCharacter; break;
                    case "mapsettings": _pending = Action.OpenMapSettings; break;
                    case "gamesettings": _pending = Action.OpenGameSettings; break;
                    case "team": _teamOpen = true; break;
                    case "startcell": _startOpen = true; break;
                }
        }
        _pressed = "";
    }

    public void OnWheel(int delta)
    {
        if (!IsActive) return;
        if (_hover == "summary")
        {
            int lines = SummaryLines().Count;
            _summaryScroll = Math.Clamp(_summaryScroll - delta, 0, Math.Max(0, lines - SummaryRows));
            return;
        }
        Characters.OnWheel(delta);
        Settings.OnWheel(delta);
    }

    /// <summary>A typed character: the open screen's box when it takes one,
    /// else the chat entry.</summary>
    public void OnChar(char c)
    {
        if (!IsActive) return;
        if (Password.IsOpen) { if (!char.IsControl(c)) Password.OnChar(c); return; }
        if (Characters.OnChar(c) || Settings.OnChar(c)) return;
        if (c == '\b') { if (ChatInput.Length > 0) ChatInput = ChatInput[..^1]; return; }
        if (c >= ' ' && ChatInput.Length < 120) ChatInput += c;
    }

    public void OnKey(Key key)
    {
        if (!IsActive) return;
        if (Password.IsOpen)
        {
            if (key == Key.Backspace) Password.OnChar('\b');
            else if (key is Key.Enter or Key.KeypadEnter) Password.OnChar('\r');
            return;
        }
        if (Characters.OnKey(key) || Settings.OnKey(key)) return;
        if (key == Key.Escape && (_teamOpen || _startOpen)) { _teamOpen = _startOpen = false; return; }
        if (key == Key.Backspace) OnChar('\b');
        else if (key is Key.Enter or Key.KeypadEnter && ChatInput.Trim().Length > 0)
        {
            SentChat = ChatInput.Trim(); ChatInput = ""; _pending = Action.SendChat;
        }
    }

    const int SummaryRows = 6;

    List<string> SummaryLines()
    {
        var lines = Rules.Summary(Players.Count).ToList();
        if (IsHost && HostIp.Length > 0)
        {
            lines.Add("Your address:");
            if (HostIp.Length > 18) { lines.Add(HostIp[..16]); lines.Add(HostIp[16..]); }
            else lines.Add(HostIp);
        }
        return lines;
    }

    public void Draw(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
    {
        _f.Begin(vw, vh, icons, bars, text, tex, common);
        Characters.NewOnly = Rules.NewCharactersOnly;
        if (!Rules.TeamPlay) _teamOpen = false;
        if (!IsHost || !Rules.AllowStartChoice) _startOpen = false;

        MpSessionScreen.DrawBackdrop(_f);

        // staging_area.gas
        _f.Box(8, 8, 792, 592, "cpbox_wide");
        _f.Label(24, 28, 420, 50, "STAGING AREA", "20p");
        _f.Button(446, 34, 606, 50, "Map Settings", State("mapsettings"), IsHost && !PanelOpen);
        _f.Button(616, 34, 776, 50, "Game Settings", State("gamesettings"), IsHost && !PanelOpen);
        _f.Box(24, 60, 776, 340, "cpbox_wide");
        if (Characters.IsOpen) Characters.Draw(vw, vh, icons, bars, text, tex, common);
        else if (Settings.IsOpen) Settings.Draw(vw, vh, icons, bars, text, tex, common);
        else DrawPlayers();

        _f.Label(24, 348, 500, 366, "Chat Window", "14p");
        _f.Box(24, 368, 546, 486);
        int lineH = Math.Max(1, (int)MathF.Round(_f.LineHeight() / _f.Scale) + 2);
        int fit = Math.Max(1, (482 - 372) / lineH);
        int first = Math.Max(0, Chat.Count - fit);
        for (int i = first; i < Chat.Count; i++)
        {
            int y = 372 + (i - first) * lineH;
            _f.Label(28, y, 526, y + lineH, Chat[i]);
        }
        _f.Box(24, 490, 546, 514);
        bool caret = !PanelOpen && (Environment.TickCount64 / 500) % 2 == 0;
        if (ChatInput.Length > 0 || caret) _f.Label(28, 494, 542, 510, ChatInput + (caret ? "_" : ""));
        else _f.Label(28, 494, 542, 510, "Type a message, then press Enter", "", AuthoredFrame.Faint);

        // staging_game_settings_listbox: the Settings Summary.
        _f.Label(556, 348, 776, 366, "Settings Summary", "14p");
        _f.Box(556, 368, 776, 460);
        var summary = SummaryLines();
        _summaryScroll = Math.Clamp(_summaryScroll, 0, Math.Max(0, summary.Count - SummaryRows));
        for (int i = 0; i < SummaryRows && _summaryScroll + i < summary.Count; i++)
        {
            int idx = _summaryScroll + i;
            bool address = IsHost && HostIp.Length > 0 && idx >= summary.Count - (HostIp.Length > 18 ? 3 : 2);
            _f.Label(562, 372 + 14 * i, 770, 386 + 14 * i, summary[idx], "", address ? AuthoredFrame.Faint : null);
        }
        if (summary.Count > SummaryRows && _summaryScroll + SummaryRows < summary.Count)
            _f.Label(700, 444, 770, 458, "more...", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Right);

        _f.Button(580, 470, 756, 486, "Leave Game", State("leave"));
        if (IsHost) _f.Button(580, 498, 756, 514, "Start Game", State("start"), true, AuthoredFrame.Green);
        else if (InProgress) _f.Button(580, 498, 756, 514, "Enter Game", State("enter"));
        else
        {
            _f.Window(584, 498, 600, 514, "b_gui_cmn_checkbox");
            if (LocalReady) _f.Window(584, 498, 600, 514, "b_gui_cmn_checkbox_x");
            _f.Label(608, 490, 756, 520, "I'm Ready to Begin", "", _hover == "ready" ? AuthoredFrame.White : AuthoredFrame.Green);
        }

        _f.Box(24, 530, 776, 576);
        if (Status.Length > 0) _f.Label(32, 530, 768, 576, Status, "", null, AuthoredFrame.Justify.Center);

        if (_teamOpen) DrawTeamPopup();
        if (_startOpen) DrawStartDropDown();
        Characters.DrawModal(vw, vh, icons, bars, text, tex, common);
        Settings.DrawModal(vw, vh, icons, bars, text, tex, common);
        Password.Draw(vw, vh, icons, bars, text, tex, common);
    }

    // staging_area_players.gas
    void DrawPlayers()
    {
        _f.Box(32, 70, 768, 94);
        _f.Label(37, 73, 221, 91, "Player", "14p");
        _f.Box(225, 70, 315, 94, "cpbox_thin");
        _f.Label(228, 73, 312, 91, "Team", "14p", null, AuthoredFrame.Justify.Center);
        _f.Box(316, 70, 536, 94, "cpbox_thin");
        _f.Label(319, 73, 533, 91, "Start Location", "14p");
        _f.Box(537, 70, 766, 94, "cpbox_thin");
        _f.Label(543, 73, 760, 91, "Character", "14p");

        string start = Rules.StartName.Length > 0 ? Rules.StartName : Starts.FirstOrDefault(s => s.IsDefault)?.ScreenName ?? "Farmhouse";
        for (int i = 0; i < Math.Min(8, Players.Count); i++)
        {
            var p = Players[i];
            int y = RowY(i);
            if (p.Local) _f.Box(34, y - 2, 766, y + 26, "cpbox_thin");
            _f.Window(34, y, 766, y + 24, "b_gui_fe_m_mp_staging_player_bg", i % 2 == 0 ? 0.2f : 0.3f);
            _f.Box(34, y + 2, 224, y + 22, "cpbox_thin", 0.6f);
            if (p.Ready) _f.Window(36, y + 4, 52, y + 20, "b_gui_fe_m_mp_staging_ready");
            _f.Label(74, y + 5, 220, y + 19, p.Name);
            _f.Window(224, y + 4, 225, y + 20, "b_gui_fe_m_mp_staging_player_bg", 0.8f);

            // Team: the player's team sign (team play on), else nothing to pick.
            if (Rules.TeamPlay)
            {
                if (p.Local && (_hover == "team" || _teamOpen)) _f.Selection(227, y + 3, 313, y + 21, 0.5f);
                if (p.Team > 0) _f.Window(260, y + 2, 280, y + 22, MpTeams.Sign(p.Team));
                else _f.Label(228, y + 5, 312, y + 19, p.Local ? "Choose..." : "None", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
            }
            _f.Window(315, y + 4, 316, y + 20, "b_gui_fe_m_mp_staging_player_bg", 0.8f);

            // Start Location: the party starts together, where the host chose.
            _f.Box(320, y + 4, 532, y + 20, "cpbox_thin", 0.4f);
            string where = InProgress && !p.Host ? "Beside the host" : start;
            if (p.Local && p.Host && Rules.AllowStartChoice && Starts.Count > 0)
            {
                _f.Label(322, y + 5, 512, y + 19, where);
                _f.Arrow(514, y + 4, 530, y + 20, "down", State("startcell"));
            }
            else _f.Label(322, y + 5, 532, y + 19, where, "", p.Host ? null : AuthoredFrame.Faint);

            _f.Window(536, y + 4, 537, y + 20, "b_gui_fe_m_mp_staging_player_bg", 0.8f);
            _f.Box(537, y + 2, 766, y + 22, "cpbox_thin", 0.6f);
            if (p.Local)
                _f.Button(541, y + 4, 762, y + 20, LocalCharacter.Length > 0 ? LocalCharacter : "Select Character...",
                          State("character"), true, null, AuthoredFrame.Justify.Left);
            else
                _f.Label(543, y + 5, 760, y + 19, p.Character);
        }
    }

    void DrawTeamPopup()
    {
        var (tx, ty) = TeamPopupAt;
        _f.Box(tx, ty, tx + 90, ty + 200, "cpbox_thin_dark");
        int mine = LocalIndex >= 0 ? Players[LocalIndex].Team : 0;
        for (int t = 1; t <= MpTeams.Count; t++)
        {
            int col = (t - 1) % 2, row = (t - 1) / 2;
            int x = tx + 5 + col * 40, y = ty + 4 + row * 24;
            if (_hover == "teamsign" + t || t == mine) _f.Selection(x, y, x + 40, y + 24, t == mine ? 0.8f : 0.5f);
            _f.Window(x + 10, y + 2, x + 30, y + 22, MpTeams.Sign(t));
        }
    }

    void DrawStartDropDown()
    {
        int y0 = RowY(Math.Max(0, LocalIndex)) + 20;
        _f.Box(320, y0, 532, y0 + 16 * Starts.Count + 2, "cpbox_thin_dark");
        for (int i = 0; i < Starts.Count; i++)
        {
            var s = Starts[i];
            int y = y0 + 1 + 16 * i;
            if (_hover == "startitem" + i || s.Group == Rules.StartGroup) _f.Selection(322, y, 530, y + 16, 0.8f);
            int lv = s.RequiredLevel(Rules.World);
            _f.Label(326, y, 470, y + 16, s.ScreenName);
            if (lv > 0) _f.Label(470, y, 526, y + 16, $"level {lv}", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Right);
        }
    }
}

using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// The two game-list screens, drawn from the original's own files with its
/// own art (<see cref="AuthoredFrame"/>):
/// - internet_game_menu.gas: MULTIPLAYER INTERNET — the player name, the host
///   address box, the list (games on the internet game service), Connect /
///   Host Game / Remove Address / Close, the your-address strip and the help
///   strip.
/// - lan_game_menu.gas: MULTIPLAYER NETWORK — the player name, the games list
///   (the original's Game / Players / Ping columns), Join Game / Host Game /
///   Close and the help strip.
/// Host Game opens the original's HOST GAME box (host_game.gas: game name, a
/// password); a protected game asks for its password first (enter_password.gas).
/// All over the multiplayer backdrop (background.gas). Actions drain to the
/// game as <see cref="Action"/>s.
/// </summary>
internal sealed class MpSessionScreen
{
    public enum Mode { Internet, Network }
    public enum Action { None, Connect, Host, Remove, Close, Join }

    public Mode ScreenMode = Mode.Internet;
    public bool IsActive { get; set; }

    public string PlayerName = "";
    public string AddressEntry = "";
    public readonly List<string> RecentAddresses = new();
    public int SelectedAddress = -1;

    /// <summary>One listed game: its name, "players/limit", the round trip in ms
    /// ("" = not measured), whether it has a password and is under way, and the
    /// address that reaches it.</summary>
    public readonly record struct GameRow(string Name, string Players, string Ping, bool Password, bool InProgress, string Address);
    /// <summary>The listed games (the Network screen's LAN games, the Internet
    /// screen's games on the internet game service).</summary>
    public readonly List<GameRow> Games = new();
    public int SelectedGame = -1;
    public GameRow? Selected => SelectedGame >= 0 && SelectedGame < Games.Count ? Games[SelectedGame] : null;

    /// <summary>Connection status line; when non-empty it replaces the help text.</summary>
    public string Status = "";
    /// <summary>Internet mode: what the empty list says (searching, still
    /// signing in, or no internet play in this copy).</summary>
    public string InternetListNote = "Searching for internet games...";

    /// <summary>What the HOST GAME box was confirmed with (read with the Host action).</summary>
    public string HostGameName { get; private set; } = "";
    public string HostPassword { get; private set; } = "";
    /// <summary>The password typed for a protected game (read with Join / Connect).</summary>
    public string JoinPassword { get; private set; } = "";

    enum Focus { None, Name, Address, GameName, Password }
    Focus _focus = Focus.None;
    string _hover = "", _pressed = "";
    Action _pending = Action.None;
    public Action ConsumeAction() { var a = _pending; _pending = Action.None; return a; }

    // host_game.gas
    bool _hostOpen, _hostUsePassword;
    string _hostName = "", _hostPass = "";
    // enter_password.gas, then the action it guards
    readonly MpEditDialog _password = new();
    Action _afterPassword = Action.None;

    readonly AuthoredFrame _f = new();

    // ---- layout (the authored 800x600 rects) ----------------------------

    (int X0, int Y0, int X1, int Y1) ListRect => ScreenMode == Mode.Internet ? (133, 183, 477, 477) : (73, 157, 727, 481);
    const int RowH = 16;
    int VisibleRows { get { var r = ListRect; return (r.Y1 - r.Y0) / RowH; } }
    int _scroll;

    (string Name, int X0, int Y0, int X1, int Y1, string Label)[] Buttons() => ScreenMode == Mode.Internet
        ? new[]
        {
            ("connect", 500, 180, 650, 196, "Connect"),
            ("host", 500, 206, 650, 222, "Host Game"),
            ("remove", 500, 232, 650, 248, "Remove Address"),
            ("close", 500, 464, 650, 480, "Close"),
        }
        : new[]
        {
            ("join", 70, 494, 230, 510, "Join Game"),
            ("host", 250, 494, 400, 510, "Host Game"),
            ("close", 580, 494, 730, 510, "Close"),
        };

    string HitName(int px, int py)
    {
        bool H(int x0, int y0, int x1, int y1) => _f.Hit(x0, y0, x1, y1, px, py);
        if (_hostOpen)
        {
            if (H(260, 344, 390, 360)) return "host_ok";
            if (H(410, 344, 540, 360)) return "host_cancel";
            if (H(250, 282, 340, 298)) return "host_check";
            if (H(250, 244, 550, 266)) return "host_name";
            if (H(350, 278, 550, 300)) return "host_pass";
            return "";
        }
        foreach (var b in Buttons()) if (H(b.X0, b.Y0, b.X1, b.Y1)) return b.Name;
        if (H(290, 90, 510, 114)) return "name";
        if (ScreenMode == Mode.Internet && H(130, 150, 480, 174)) return "address";
        var l = ListRect;
        if (H(l.X0, l.Y0, l.X1, l.Y1)) return "list";
        return "";
    }

    AuthoredFrame.Press State(string n) => _pressed == n && _hover == n ? AuthoredFrame.Press.Down
        : _hover == n ? AuthoredFrame.Press.Hover : AuthoredFrame.Press.Up;

    bool JoinEnabled => ScreenMode == Mode.Internet || Selected is not null;

    // ---- input ---------------------------------------------------------

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        _f.Layout(vw, vh);
        if (_password.IsOpen) { _password.OnMouseMove(px, py, vw, vh); _hover = ""; return; }
        _hover = HitName(px, py);
    }

    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        _f.Layout(vw, vh);
        if (_password.IsOpen) { _password.OnMouseDown(px, py, vw, vh); return; }
        _pressed = HitName(px, py);
        switch (_pressed)
        {
            case "name": _focus = Focus.Name; break;
            case "address": _focus = Focus.Address; break;
            case "host_name": _focus = Focus.GameName; break;
            case "host_pass": _focus = Focus.Password; _hostUsePassword = true; break;
            case "list":
            {
                var l = _f.S(ListRect.X0, ListRect.Y0, ListRect.X1, ListRect.Y1);
                int row = _scroll + (int)((py - l.Y) / MathF.Max(1f, RowH * _f.Scale));
                if (row >= 0 && row < Games.Count)
                {
                    SelectedGame = row;
                    if (ScreenMode == Mode.Internet) AddressEntry = Games[row].Address;
                }
                break;
            }
            default:
                if (!_hostOpen && _pressed.Length == 0) _focus = Focus.None;
                break;
        }
    }

    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsActive) { _pressed = ""; return; }
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

    public void OnWheel(int delta)
    {
        if (!IsActive || _hostOpen || _password.IsOpen) return;
        _scroll = Math.Clamp(_scroll - delta, 0, Math.Max(0, Games.Count - VisibleRows));
    }

    void Press(string hit)
    {
        switch (hit)
        {
            case "connect": Guarded(Action.Connect); break;
            case "join": if (JoinEnabled) Guarded(Action.Join); break;
            case "remove": _pending = Action.Remove; break;
            case "close": _pending = Action.Close; break;
            case "host":
                _hostOpen = true;
                _hostName = PlayerName.Trim().Length > 0 ? $"{PlayerName.Trim()}'s Game" : "";
                _hostPass = ""; _hostUsePassword = false;
                _focus = Focus.GameName;
                break;
            case "host_check": _hostUsePassword = !_hostUsePassword; break;
            case "host_cancel": _hostOpen = false; _focus = Focus.None; break;
            case "host_ok": ConfirmHost(); break;
        }
    }

    void ConfirmHost()
    {
        if (_hostName.Trim().Length == 0) { Status = "Please enter a valid game name."; return; }
        if (_hostUsePassword && _hostPass.Length == 0) { Status = "Enter a password."; return; }
        HostGameName = _hostName.Trim();
        HostPassword = _hostUsePassword ? _hostPass : "";
        _hostOpen = false; _focus = Focus.None;
        _pending = Action.Host;
    }

    /// <summary>Join or Connect: a game that has a password asks for it first.</summary>
    void Guarded(Action act)
    {
        JoinPassword = "";
        bool listed = Selected is { } g && (ScreenMode == Mode.Network || g.Address == AddressEntry.Trim());
        if (listed && Selected!.Value.Password)
        {
            _afterPassword = act;
            _password.Open(MpEditDialog.Kind.EnterPassword);
            return;
        }
        _pending = act;
    }

    void DrainPassword()
    {
        switch (_password.Consume())
        {
            case MpEditDialog.Result.Ok:
                JoinPassword = _password.Text;
                _password.Close();
                _pending = _afterPassword;
                break;
            case MpEditDialog.Result.Cancel:
                _password.Close();
                break;
        }
    }

    public void OnChar(char c)
    {
        if (!IsActive) return;
        if (_password.IsOpen) { if (!char.IsControl(c)) _password.OnChar(c); return; }
        if (char.IsControl(c) || c == '|') return;
        switch (_focus)
        {
            case Focus.Name when PlayerName.Length < 24: PlayerName += c; break;
            case Focus.Address when AddressEntry.Length < 48: AddressEntry += c; break;
            case Focus.GameName when _hostName.Length < 32: _hostName += c; break;
            case Focus.Password when _hostPass.Length < MpEditDialog.MaxLength: _hostPass += c; break;
        }
    }

    /// <summary>Backspace/Enter. Enter in the address box connects (the
    /// authored ip_edit_box oneditselect); in the HOST GAME box it is OK; in
    /// the password box it is OK.</summary>
    public bool OnEditKey(bool backspace, bool enter)
    {
        if (!IsActive) return false;
        if (_password.IsOpen)
        {
            if (backspace) _password.OnChar('\b');
            else if (enter) _password.OnChar('\r');
            DrainPassword();
            return true;
        }
        if (backspace)
        {
            switch (_focus)
            {
                case Focus.Name when PlayerName.Length > 0: PlayerName = PlayerName[..^1]; break;
                case Focus.Address when AddressEntry.Length > 0: AddressEntry = AddressEntry[..^1]; break;
                case Focus.GameName when _hostName.Length > 0: _hostName = _hostName[..^1]; break;
                case Focus.Password when _hostPass.Length > 0: _hostPass = _hostPass[..^1]; break;
            }
        }
        if (enter)
        {
            if (_hostOpen) { ConfirmHost(); return true; }
            if (_focus == Focus.Address && ScreenMode == Mode.Internet) { Guarded(Action.Connect); return true; }
        }
        return false;
    }

    /// <summary>Close the boxes (the screen was left).</summary>
    public void ResetDialogs() { _hostOpen = false; _password.Close(); _focus = Focus.None; }

    // ---- drawing -------------------------------------------------------

    public void Draw(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> tex, Func<string, GlTexture?> common, string localAddress)
    {
        _f.Begin(vw, vh, icons, bars, text, tex, common);
        DrawBackdrop(_f);
        bool caret = (Environment.TickCount64 / 500) % 2 == 0;
        _f.Box(8, 8, 792, 592, "cpbox_wide");
        // title (rect 24,28,224,50): the original's font fit it; ours runs wider, so it may overrun the rect.
        _f.Label(24, 28, 420, 50, ScreenMode == Mode.Internet ? "MULTIPLAYER INTERNET" : "MULTIPLAYER NETWORK", "20p");

        // The player name (both files).
        _f.Label(290, 70, 510, 86, "Enter Player Name");
        _f.Box(290, 90, 510, 114);
        if (ScreenMode == Mode.Internet) _f.Window(294, 94, 506, 110, "b_gui_fe_m_single_prompt");
        _f.Label(294, 94, 506, 110, PlayerName + (_focus == Focus.Name && caret ? "_" : ""));

        if (ScreenMode == Mode.Internet) DrawInternet(caret);
        else DrawNetwork();

        foreach (var b in Buttons())
            _f.Button(b.X0, b.Y0, b.X1, b.Y1, b.Label, State(b.Name), b.Name != "join" || JoinEnabled);

        _f.Box(24, 530, 776, 576);
        string help = Status.Length > 0 ? Status
            : ScreenMode == Mode.Internet
            ? "Pick a game from the list, or enter a host's IP address, and click Connect. Click Host Game to start your own."
            : "Games hosted on your local network appear in the list. Click one, then Join Game, or click Host Game.";
        Paragraph(32, 534, 768, 572, help, Status.Length > 0 ? AuthoredFrame.White : AuthoredFrame.Faint);

        if (ScreenMode == Mode.Internet)
        {
            _f.Box(24, 498, 776, 520);
            _f.Label(28, 500, 771, 516, $"Your address: {localAddress}", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
        }

        if (_hostOpen) DrawHostGame(caret);
        _password.Draw(vw, vh, icons, bars, text, tex, common);
    }

    /// <summary>background.gas: the multiplayer backdrop, tile by tile.</summary>
    internal static void DrawBackdrop(AuthoredFrame f)
    {
        f.Window(0, 0, 256, 256, "b_gui_fe_m_mp_background_01"); f.Window(256, 0, 512, 256, "b_gui_fe_m_mp_background_02");
        f.Window(512, 0, 768, 256, "b_gui_fe_m_mp_background_03"); f.Window(0, 256, 256, 512, "b_gui_fe_m_mp_background_04");
        f.Window(256, 256, 512, 512, "b_gui_fe_m_mp_background_05"); f.Window(512, 256, 768, 512, "b_gui_fe_m_mp_background_06");
        f.Window(0, 512, 256, 600, "b_gui_fe_m_mp_background_07", 1f, 0, 0.65625f, 1, 1);
        f.Window(256, 512, 512, 600, "b_gui_fe_m_mp_background_07", 1f, 0, 0.3125f, 1, 0.65625f);
        f.Window(512, 512, 768, 600, "b_gui_fe_m_mp_background_08", 1f, 0, 0.65625f, 1, 1);
        f.Window(768, 0, 800, 256, "b_gui_fe_m_mp_background_09", 1f, 0, 0, 0.125f, 1);
        f.Window(768, 256, 800, 512, "b_gui_fe_m_mp_background_09", 1f, 0.125f, 0, 0.25f, 1);
        f.Window(768, 512, 800, 600, "b_gui_fe_m_mp_background_09", 1f, 0.25f, 0.65625f, 0.375f, 1);
    }

    void DrawInternet(bool caret)
    {
        _f.Label(130, 130, 480, 146, "Enter host's IP address, if joining");
        _f.Box(130, 150, 480, 174);
        _f.Window(134, 154, 476, 170, "b_gui_fe_m_single_prompt");
        _f.Label(134, 154, 476, 170, AddressEntry + (_focus == Focus.Address && caret ? "_" : ""));
        _f.Box(130, 180, 480, 480);
        var l = ListRect;
        if (Games.Count == 0)
        {
            Paragraph(l.X0 + 4, l.Y0 + 2, l.X1 - 4, l.Y0 + 60, InternetListNote, AuthoredFrame.Faint, top: true);
            return;
        }
        int rows = VisibleRows;
        for (int i = 0; i < rows && _scroll + i < Games.Count; i++)
        {
            var g = Games[_scroll + i];
            int y = l.Y0 + i * RowH;
            if (_scroll + i == SelectedGame) _f.Selection(l.X0, y, l.X1, y + RowH, 0.5f);
            string name = g.Name + (g.InProgress ? " (in progress)" : "");
            _f.Label(l.X0 + 4, y, l.X1 - 100, y + RowH, name);
            _f.Label(l.X1 - 96, y, l.X1 - 4, y + RowH, (g.Password ? "Password  " : "") + g.Players, "",
                     AuthoredFrame.Faint, AuthoredFrame.Justify.Right);
        }
    }

    // lan_game_menu.gas: the games listreport with the original's columns.
    void DrawNetwork()
    {
        _f.Box(70, 130, 730, 484);
        const int playersX = 520, pingX = 630;
        _f.Box(73, 133, playersX, 155, "cpbox_thin");
        _f.Label(78, 136, playersX - 4, 152, "Game", "14p");
        _f.Box(playersX, 133, pingX, 155, "cpbox_thin");
        _f.Label(playersX + 4, 136, pingX - 4, 152, "Players", "14p", null, AuthoredFrame.Justify.Center);
        _f.Box(pingX, 133, 727, 155, "cpbox_thin");
        _f.Label(pingX + 4, 136, 723, 152, "Ping", "14p", null, AuthoredFrame.Justify.Center);
        var l = ListRect;
        if (Games.Count == 0)
        {
            _f.Label(l.X0 + 6, l.Y0 + 4, l.X1 - 4, l.Y0 + 20, "Searching for games on your network...", "", AuthoredFrame.Faint);
            return;
        }
        int rows = VisibleRows;
        for (int i = 0; i < rows && _scroll + i < Games.Count; i++)
        {
            var g = Games[_scroll + i];
            int y = l.Y0 + 2 + i * RowH;
            if (_scroll + i == SelectedGame) _f.Selection(l.X0, y, l.X1, y + RowH, 0.5f);
            string name = g.Name + (g.InProgress ? " (in progress)" : "") + (g.Password ? " (password)" : "");
            _f.Label(l.X0 + 6, y, playersX - 6, y + RowH, name);
            _f.Label(playersX, y, pingX, y + RowH, g.Players, "", null, AuthoredFrame.Justify.Center);
            _f.Label(pingX, y, 727, y + RowH, g.Ping.Length > 0 ? g.Ping : "-", "", null, AuthoredFrame.Justify.Center);
        }
    }

    // host_game.gas
    void DrawHostGame(bool caret)
    {
        _f.Box(200, 150, 600, 384, "cpbox_wide");
        _f.Label(240, 166, 560, 186, "HOST GAME", "20p", null, AuthoredFrame.Justify.Center);
        _f.Box(220, 200, 580, 328);
        _f.Label(250, 224, 450, 240, "Game Name");
        _f.Box(250, 244, 550, 266);
        _f.Label(253, 247, 547, 263, _hostName + (_focus == Focus.GameName && caret ? "_" : ""));
        _f.Window(250, 282, 266, 298, "b_gui_cmn_checkbox");
        if (_hostUsePassword) _f.Window(250, 282, 266, 298, "b_gui_cmn_checkbox_x");
        _f.Label(270, 281, 340, 297, "Password", "", null, AuthoredFrame.Justify.Center);
        _f.Box(350, 278, 550, 300, "cpbox", _hostUsePassword ? 1f : 0.6f);
        _f.Label(353, 281, 547, 297, _hostPass + (_focus == Focus.Password && caret ? "_" : ""), "",
                 _hostUsePassword ? AuthoredFrame.White : AuthoredFrame.Disabled);
        _f.Button(260, 344, 390, 360, "OK", State("host_ok"));
        _f.Button(410, 344, 540, 360, "Cancel", State("host_cancel"));
    }

    // Word-wrapped lines in a rect, centered as a block (or from the top).
    void Paragraph(int x0, int y0, int x1, int y1, string msg, Vector4 ink, bool top = false)
    {
        int width = (int)MathF.Round((x1 - x0) * _f.Scale);
        var lines = new List<string>();
        string line = "";
        foreach (var word in msg.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && _f.Measure(next) > width) { lines.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        int lh = Math.Max(1, (int)MathF.Round(_f.LineHeight() / _f.Scale) + 2);
        int total = lines.Count * lh;
        int y = top ? y0 : y0 + Math.Max(0, (y1 - y0 - total) / 2);
        foreach (var l in lines)
        {
            if (y + lh > y1 + 2) break;
            _f.Label(x0, y, x1, y + lh, l, "", ink, top ? AuthoredFrame.Justify.Left : AuthoredFrame.Justify.Center);
            y += lh;
        }
    }
}

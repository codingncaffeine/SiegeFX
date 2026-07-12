using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// SC-MP-SESSION — the two provider session screens, built from the authored
/// /ui/interfaces/multiplayer/ gas:
/// - internet_game_menu.gas: title, player-name edit, host-address edit,
///   recent-IP listbox, CONNECT / HOST GAME / REMOVE ADDRESS / CLOSE, your-IP
///   strip + help strip, all over the 12-tile b_gui_fe_m_mp_background_* art.
/// - lan_game_menu.gas: title, player-name edit, games listreport,
///   JOIN / HOST GAME / CLOSE over the same background.
/// All rects are the authored 800×600 values, letterbox-scaled. Buttons are
/// authored text-on-parchment (texture=none) — hover brightens, press dims,
/// matching the era's screens. Transport actions drain to the host as
/// <see cref="Action"/>s; EOS / LAN wiring lands with the SC-MP-EOS phases.
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
    /// <summary>Network mode: discovered LAN sessions (display rows) and
    /// their host addresses (parallel list, JOIN target).</summary>
    public readonly List<string> LanGames = new();
    public readonly List<string> LanGameAddresses = new();
    public int SelectedGame = -1;
    /// <summary>Connection status line; when non-empty it replaces the help
    /// strip text (hosting/connecting/connected/failed + diagnostics hint).</summary>
    public string Status = "";

    enum Focus { None, Name, Address }
    Focus _focus = Focus.None;
    Action _hover = Action.None;
    Action _pressed = Action.None;
    Action _pending = Action.None;

    public Action ConsumeAction() { var a = _pending; _pending = Action.None; return a; }

    // Authored 800×600 rects (internet_game_menu.gas / lan_game_menu.gas).
    static readonly (int X0, int Y0, int X1, int Y1) RTitle    = (24, 28, 224, 50);
    static readonly (int X0, int Y0, int X1, int Y1) RNameLbl  = (290, 70, 510, 86);
    static readonly (int X0, int Y0, int X1, int Y1) RNameBox  = (290, 90, 510, 114);
    static readonly (int X0, int Y0, int X1, int Y1) RAddrLbl  = (130, 130, 480, 146);
    static readonly (int X0, int Y0, int X1, int Y1) RAddrBox  = (130, 150, 480, 174);
    static readonly (int X0, int Y0, int X1, int Y1) RList     = (130, 180, 480, 480);
    static readonly (int X0, int Y0, int X1, int Y1) RLanList  = (130, 130, 480, 480);
    static readonly (int X0, int Y0, int X1, int Y1) RBtn1     = (500, 180, 650, 196);
    static readonly (int X0, int Y0, int X1, int Y1) RBtn2     = (500, 206, 650, 222);
    static readonly (int X0, int Y0, int X1, int Y1) RBtn3     = (500, 232, 650, 248);
    static readonly (int X0, int Y0, int X1, int Y1) RClose    = (500, 464, 650, 480);
    static readonly (int X0, int Y0, int X1, int Y1) RYourIp   = (24, 498, 776, 520);
    static readonly (int X0, int Y0, int X1, int Y1) RHelp     = (24, 530, 776, 576);

    float _scale = 1f; int _dx, _dy;
    void Layout(int vw, int vh)
    {
        _scale = MathF.Min(vh / 600f, vw / 800f);
        _dx = (vw - (int)MathF.Round(800 * _scale)) / 2;
        _dy = (vh - (int)MathF.Round(600 * _scale)) / 2;
    }
    (int X, int Y, int W, int H) S((int X0, int Y0, int X1, int Y1) r) => (
        _dx + (int)MathF.Round(r.X0 * _scale), _dy + (int)MathF.Round(r.Y0 * _scale),
        (int)MathF.Round((r.X1 - r.X0) * _scale), (int)MathF.Round((r.Y1 - r.Y0) * _scale));
    static bool Hits((int X, int Y, int W, int H) r, int px, int py) =>
        px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;

    (Action act, (int X0, int Y0, int X1, int Y1) rect, string label)[] Buttons()
        => ScreenMode == Mode.Internet
            ? new[]
            {
                (Action.Connect, RBtn1, "CONNECT"),
                (Action.Host,    RBtn2, "HOST GAME"),
                (Action.Remove,  RBtn3, "REMOVE ADDRESS"),
                (Action.Close,   RClose, "CLOSE"),
            }
            : new[]
            {
                (Action.Join,    RBtn1, "JOIN GAME"),
                (Action.Host,    RBtn2, "HOST GAME"),
                (Action.Close,   RClose, "CLOSE"),
            };

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        Layout(vw, vh);
        _hover = Action.None;
        foreach (var (act, rect, _) in Buttons())
            if (Hits(S(rect), px, py)) { _hover = act; return; }
    }

    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        Layout(vw, vh);
        _pressed = Action.None;
        _focus = Focus.None;
        foreach (var (act, rect, _) in Buttons())
            if (Hits(S(rect), px, py)) { _pressed = act; return; }
        if (Hits(S(RNameBox), px, py)) { _focus = Focus.Name; return; }
        if (ScreenMode == Mode.Internet)
        {
            if (Hits(S(RAddrBox), px, py)) { _focus = Focus.Address; return; }
            // The list shows discovered EOS games; clicking one fills the address
            // box with its host id so CONNECT joins it (manual paste still works).
            var list = S(RList);
            if (Hits(list, px, py) && LanGames.Count > 0)
            {
                int row = (int)((py - list.Y) / MathF.Max(1f, 16 * _scale));
                if (row >= 0 && row < LanGames.Count)
                {
                    SelectedGame = row;
                    if (row < LanGameAddresses.Count) AddressEntry = LanGameAddresses[row];
                }
            }
        }
        else
        {
            var list = S(RLanList);
            if (Hits(list, px, py) && LanGames.Count > 0)
            {
                int row = (int)((py - list.Y) / MathF.Max(1f, 16 * _scale));
                if (row >= 0 && row < LanGames.Count) SelectedGame = row;
            }
        }
    }

    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsActive) { _pressed = Action.None; return; }
        Layout(vw, vh);
        foreach (var (act, rect, _) in Buttons())
            if (Hits(S(rect), px, py) && _pressed == act) _pending = act;
        _pressed = Action.None;
    }

    public void OnChar(char c)
    {
        if (!IsActive || _focus == Focus.None) return;
        if (char.IsControl(c)) return;
        if (_focus == Focus.Name && PlayerName.Length < 24) PlayerName += c;
        else if (_focus == Focus.Address && AddressEntry.Length < 48) AddressEntry += c;
    }

    /// <summary>Backspace/Enter handling. Returns true when Enter committed
    /// a connect (the authored ip_edit_box oneditselect behavior).</summary>
    public bool OnEditKey(bool backspace, bool enter)
    {
        if (!IsActive || _focus == Focus.None) return false;
        if (backspace)
        {
            if (_focus == Focus.Name && PlayerName.Length > 0) PlayerName = PlayerName[..^1];
            else if (_focus == Focus.Address && AddressEntry.Length > 0) AddressEntry = AddressEntry[..^1];
        }
        if (enter && _focus == Focus.Address && ScreenMode == Mode.Internet)
        {
            _pending = Action.Connect;
            return true;
        }
        return false;
    }

    public void Draw(int vw, int vh,
                     IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> getTex, string localIp)
    {
        Layout(vw, vh);
        int fs = Math.Max(1, (int)MathF.Round(_scale));

        // Authored 12-tile parchment background (background.gas verbatim:
        // rect + uvcoords per tile; gas V is bottom-up → flip for DrawIcon).
        var white = Vector4.One;
        void Tile(int x0, int y0, int x1, int y1, string tex,
                  float u0 = 0f, float gv0 = 0f, float u1 = 1f, float gv1 = 1f)
        {
            var t = getTex(tex);
            if (t is null) return;
            int sx = _dx + (int)MathF.Round(x0 * _scale), sy = _dy + (int)MathF.Round(y0 * _scale);
            int sw = (int)MathF.Round((x1 - x0) * _scale), sh = (int)MathF.Round((y1 - y0) * _scale);
            icons.DrawIcon(vw, vh, t, sx, sy, sw, sh, white, u0, 1f - gv1, u1, 1f - gv0);
        }
        Tile(0, 0, 256, 256,     "b_gui_fe_m_mp_background_01");
        Tile(256, 0, 512, 256,   "b_gui_fe_m_mp_background_02");
        Tile(512, 0, 768, 256,   "b_gui_fe_m_mp_background_03");
        Tile(0, 256, 256, 512,   "b_gui_fe_m_mp_background_04");
        Tile(256, 256, 512, 512, "b_gui_fe_m_mp_background_05");
        Tile(512, 256, 768, 512, "b_gui_fe_m_mp_background_06");
        Tile(0, 512, 256, 600,   "b_gui_fe_m_mp_background_07", 0f, 0.65625f, 1f, 1f);
        Tile(256, 512, 512, 600, "b_gui_fe_m_mp_background_07", 0f, 0.3125f, 1f, 0.65625f);
        Tile(512, 512, 768, 600, "b_gui_fe_m_mp_background_08", 0f, 0.65625f, 1f, 1f);
        Tile(768, 0, 800, 256,   "b_gui_fe_m_mp_background_09", 0f, 0f, 0.125f, 1f);
        Tile(768, 256, 800, 512, "b_gui_fe_m_mp_background_09", 0.125f, 0f, 0.25f, 1f);
        Tile(768, 512, 800, 600, "b_gui_fe_m_mp_background_09", 0.25f, 0.65625f, 0.375f, 1f);

        var boxFill   = new Vector4(0.06f, 0.045f, 0.03f, 0.72f);
        var boxBorder = new Vector4(0.42f, 0.34f, 0.22f, 1f);
        void Box((int X0, int Y0, int X1, int Y1) r)
        {
            var s = S(r);
            bars.DrawRect(vw, vh, s.X, s.Y, s.W, s.H, boxFill);
            bars.DrawBorder(vw, vh, s.X, s.Y, s.W, s.H, boxBorder);
        }
        void Label((int X0, int Y0, int X1, int Y1) r, string msg, Vector4 ink, bool center = false)
        {
            var s = S(r);
            int tw = text.MeasureWidth(msg, fs);
            int tx = center ? s.X + (s.W - tw) / 2 : s.X;
            text.DrawString(vw, vh, msg, tx, s.Y + Math.Max(0, (s.H - 12 * fs) / 2), ink, fs);
        }

        var inkTitle  = new Vector4(0.93f, 0.87f, 0.65f, 1f);
        var inkText   = new Vector4(0.88f, 0.84f, 0.72f, 1f);
        var inkFaint  = new Vector4(0.62f, 0.58f, 0.48f, 1f);

        Label(RTitle, ScreenMode == Mode.Internet ? "MULTIPLAYER  INTERNET" : "MULTIPLAYER  NETWORK", inkTitle);

        // Player name.
        Label(RNameLbl, "PLAYER NAME", inkFaint);
        Box(RNameBox);
        Label(RNameBox, PlayerName + (_focus == Focus.Name ? "_" : ""), inkText);

        if (ScreenMode == Mode.Internet)
        {
            Label(RAddrLbl, "HOST ADDRESS", inkFaint);
            Box(RAddrBox);
            Label(RAddrBox, AddressEntry + (_focus == Focus.Address ? "_" : ""), inkText);
            Box(RList);
            var list = S(RList);
            int rowH = (int)MathF.Round(16 * _scale);
            // Live internet games discovered via EOS; clicking one fills the
            // address box so CONNECT joins it (manual paste still works).
            for (int i = 0; i < LanGames.Count && (i + 1) * rowH <= list.H; i++)
            {
                if (i == SelectedGame)
                    bars.DrawRect(vw, vh, list.X + 2, list.Y + i * rowH, list.W - 4, rowH,
                        new Vector4(0.55f, 0.45f, 0.25f, 0.5f));
                text.DrawString(vw, vh, LanGames[i],
                    list.X + 4 * fs, list.Y + i * rowH + 2, inkText, fs);
            }
            if (LanGames.Count == 0)
                text.DrawString(vw, vh, "Searching for internet games...",
                    list.X + 4 * fs, list.Y + 4, inkFaint, fs);
        }
        else
        {
            Box(RLanList);
            var list = S(RLanList);
            int rowH = (int)MathF.Round(16 * _scale);
            for (int i = 0; i < LanGames.Count && (i + 1) * rowH <= list.H; i++)
            {
                if (i == SelectedGame)
                    bars.DrawRect(vw, vh, list.X + 2, list.Y + i * rowH, list.W - 4, rowH,
                        new Vector4(0.55f, 0.45f, 0.25f, 0.5f));
                text.DrawString(vw, vh, LanGames[i],
                    list.X + 4 * fs, list.Y + i * rowH + 2, inkText, fs);
            }
            if (LanGames.Count == 0)
                text.DrawString(vw, vh, "Searching for local games...",
                    list.X + 4 * fs, list.Y + 4, inkFaint, fs);
        }

        // Right-column text buttons (authored texture=none: text on
        // parchment; hover brightens, press dims).
        foreach (var (act, rect, label) in Buttons())
        {
            var ink = _pressed == act ? new Vector4(0.55f, 0.45f, 0.28f, 1f)
                     : _hover == act  ? new Vector4(1.00f, 0.95f, 0.75f, 1f)
                     : inkText;
            Label(rect, label, ink);
        }

        // Your-IP strip + help strip (authored bottom bands).
        Box(RYourIp);
        Label(RYourIp, $"YOUR IP ADDRESS:  {localIp}", inkFaint, center: true);
        Box(RHelp);
        string help = Status.Length > 0 ? Status
            : ScreenMode == Mode.Internet
            ? "Pick a game from the list (or paste a host id) and click CONNECT, or click HOST GAME to start your own."
            : "Games hosted on your local network appear in the list. Click one, then JOIN GAME.";
        Label(RHelp, help, Status.Length > 0 ? inkText : inkFaint, center: true);
    }
}

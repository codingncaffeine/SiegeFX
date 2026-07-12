using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>SC-MP-EOS P8 — the staging area, from staging_area.gas: player
/// roster (top), chat (bottom-left), game settings + host IP + ready
/// checkbox (right), and Leave / Start (host) / Ready+Enter (client)
/// buttons. Drawn over the same authored parchment as the session screens.
/// Characters are client-owned (retail .dsparty model): each player brings
/// their own character; the host owns the disposable world.</summary>
internal sealed class MpStagingScreen
{
    public enum Action { None, Leave, Start, ToggleReady }

    public bool IsActive { get; set; }
    public bool IsHost;
    public bool LocalReady;
    public string HostIp = "";
    public string Map = "Kingdom of Ehb";
    public string Difficulty = "Regular";
    public string Status = "";

    /// <summary>Roster rows: (name, isHost, isReady). Player 0 is the host.</summary>
    public readonly List<(string Name, bool Host, bool Ready)> Players = new();
    public readonly List<string> Chat = new();

    Action _hover = Action.None, _pressed = Action.None, _pending = Action.None;
    public Action ConsumeAction() { var a = _pending; _pending = Action.None; return a; }

    static readonly (int X0, int Y0, int X1, int Y1) RTitle    = (24, 28, 420, 50);
    static readonly (int X0, int Y0, int X1, int Y1) RRoster   = (24, 60, 776, 340);
    static readonly (int X0, int Y0, int X1, int Y1) RChatTtl  = (24, 348, 500, 366);
    static readonly (int X0, int Y0, int X1, int Y1) RChat     = (24, 368, 546, 486);
    static readonly (int X0, int Y0, int X1, int Y1) RSetTtl   = (556, 348, 776, 366);
    static readonly (int X0, int Y0, int X1, int Y1) RSettings = (556, 368, 776, 460);
    static readonly (int X0, int Y0, int X1, int Y1) RHostIpH  = (600, 368, 776, 384);
    static readonly (int X0, int Y0, int X1, int Y1) RHostIp   = (600, 392, 776, 408);
    static readonly (int X0, int Y0, int X1, int Y1) RReady    = (608, 490, 756, 514);
    static readonly (int X0, int Y0, int X1, int Y1) RLeave    = (580, 470, 756, 486);
    static readonly (int X0, int Y0, int X1, int Y1) RStart    = (580, 498, 756, 514);

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

    (Action act, (int X0, int Y0, int X1, int Y1) rect)[] Buttons() => IsHost
        ? new[] { (Action.Leave, RLeave), (Action.Start, RStart) }
        : new[] { (Action.Leave, RLeave), (Action.ToggleReady, RReady) };

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        Layout(vw, vh); _hover = Action.None;
        foreach (var (a, r) in Buttons()) if (Hits(S(r), px, py)) { _hover = a; return; }
    }
    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        Layout(vw, vh); _pressed = Action.None;
        foreach (var (a, r) in Buttons()) if (Hits(S(r), px, py)) { _pressed = a; return; }
    }
    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsActive) { _pressed = Action.None; return; }
        Layout(vw, vh);
        foreach (var (a, r) in Buttons()) if (Hits(S(r), px, py) && _pressed == a) _pending = a;
        _pressed = Action.None;
    }

    public void Draw(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> getTex)
    {
        Layout(vw, vh);
        int fs = Math.Max(1, (int)MathF.Round(_scale));
        var white = Vector4.One;
        void Tile(int x0, int y0, int x1, int y1, string tex, float u0 = 0, float gv0 = 0, float u1 = 1, float gv1 = 1)
        {
            var t = getTex(tex); if (t is null) return;
            int sx = _dx + (int)MathF.Round(x0 * _scale), sy = _dy + (int)MathF.Round(y0 * _scale);
            int sw = (int)MathF.Round((x1 - x0) * _scale), sh = (int)MathF.Round((y1 - y0) * _scale);
            icons.DrawIcon(vw, vh, t, sx, sy, sw, sh, white, u0, 1f - gv1, u1, 1f - gv0);
        }
        Tile(0, 0, 256, 256, "b_gui_fe_m_mp_background_01"); Tile(256, 0, 512, 256, "b_gui_fe_m_mp_background_02");
        Tile(512, 0, 768, 256, "b_gui_fe_m_mp_background_03"); Tile(0, 256, 256, 512, "b_gui_fe_m_mp_background_04");
        Tile(256, 256, 512, 512, "b_gui_fe_m_mp_background_05"); Tile(512, 256, 768, 512, "b_gui_fe_m_mp_background_06");
        Tile(0, 512, 256, 600, "b_gui_fe_m_mp_background_07", 0, 0.65625f, 1, 1); Tile(256, 512, 512, 600, "b_gui_fe_m_mp_background_07", 0, 0.3125f, 1, 0.65625f);
        Tile(512, 512, 768, 600, "b_gui_fe_m_mp_background_08", 0, 0.65625f, 1, 1); Tile(768, 0, 800, 256, "b_gui_fe_m_mp_background_09", 0, 0, 0.125f, 1);
        Tile(768, 256, 800, 512, "b_gui_fe_m_mp_background_09", 0.125f, 0, 0.25f, 1); Tile(768, 512, 800, 600, "b_gui_fe_m_mp_background_09", 0.25f, 0.65625f, 0.375f, 1);

        var fill = new Vector4(0.06f, 0.045f, 0.03f, 0.72f);
        var border = new Vector4(0.42f, 0.34f, 0.22f, 1f);
        void Box((int X0, int Y0, int X1, int Y1) r) { var s = S(r); bars.DrawRect(vw, vh, s.X, s.Y, s.W, s.H, fill); bars.DrawBorder(vw, vh, s.X, s.Y, s.W, s.H, border); }
        void Lbl((int X0, int Y0, int X1, int Y1) r, string m, Vector4 ink, bool ctr = false)
        {
            var s = S(r); int tw = text.MeasureWidth(m, fs); int tx = ctr ? s.X + (s.W - tw) / 2 : s.X;
            text.DrawString(vw, vh, m, tx, s.Y + Math.Max(0, (s.H - 12 * fs) / 2), ink, fs);
        }
        var inkT = new Vector4(0.93f, 0.87f, 0.65f, 1f);
        var ink = new Vector4(0.88f, 0.84f, 0.72f, 1f);
        var inkFaint = new Vector4(0.62f, 0.58f, 0.48f, 1f);
        var inkReady = new Vector4(0.55f, 0.90f, 0.45f, 1f);

        Lbl(RTitle, "STAGING AREA", inkT);

        // Roster.
        Box(RRoster);
        var ros = S(RRoster);
        int rowH = (int)MathF.Round(20 * _scale);
        for (int i = 0; i < Players.Count && (i + 1) * rowH <= ros.H; i++)
        {
            var (name, host, ready) = Players[i];
            string tag = host ? "  (HOST)" : ready ? "  READY" : "  not ready";
            text.DrawString(vw, vh, $"{i + 1}. {name}{tag}",
                ros.X + 6 * fs, ros.Y + 4 + i * rowH, host ? inkT : ready ? inkReady : ink, fs);
        }
        if (Players.Count == 0) text.DrawString(vw, vh, "Waiting for players...", ros.X + 6 * fs, ros.Y + 4, inkFaint, fs);

        // Chat.
        Lbl(RChatTtl, "CHAT", inkFaint);
        Box(RChat);
        var ch = S(RChat);
        int start = Math.Max(0, Chat.Count - (int)(ch.H / (14 * _scale)));
        for (int i = start; i < Chat.Count; i++)
            text.DrawString(vw, vh, Chat[i], ch.X + 4 * fs, ch.Y + 4 + (i - start) * (int)MathF.Round(14 * _scale), ink, fs);

        // Game settings + host IP.
        Lbl(RSetTtl, "GAME SETTINGS", inkFaint);
        Box(RSettings);
        var set = S(RSettings);
        text.DrawString(vw, vh, $"Map:  {Map}", set.X + 6 * fs, set.Y + 6, ink, fs);
        text.DrawString(vw, vh, $"Difficulty:  {Difficulty}", set.X + 6 * fs, set.Y + 6 + (int)MathF.Round(18 * _scale), ink, fs);
        text.DrawString(vw, vh, $"Players:  {Players.Count}/8", set.X + 6 * fs, set.Y + 6 + (int)MathF.Round(36 * _scale), ink, fs);
        if (IsHost) { Lbl(RHostIpH, "YOUR ADDRESS", inkFaint); Lbl(RHostIp, HostIp, ink); }

        // Buttons.
        foreach (var (a, r) in Buttons())
        {
            string label = a switch
            {
                Action.Leave => "LEAVE GAME",
                Action.Start => "START GAME",
                Action.ToggleReady => LocalReady ? "READY [x]" : "READY [ ]",
                _ => "",
            };
            var col = _pressed == a ? new Vector4(0.55f, 0.45f, 0.28f, 1f)
                    : _hover == a ? new Vector4(1f, 0.95f, 0.75f, 1f)
                    : a == Action.ToggleReady && LocalReady ? inkReady : ink;
            Lbl(r, label, col, ctr: true);
        }

        if (Status.Length > 0) Lbl((24, 530, 776, 576), Status, inkT, ctr: true);
    }
}

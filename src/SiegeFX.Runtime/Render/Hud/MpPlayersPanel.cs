using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// SC-MP-PLAYERS — DS1's in-game multiplayer player panel, Players tab
/// (<c>/ui/interfaces/multiplayer/in_game_player_panel_characters.gas</c>):
/// one row per connected player showing net name, character name, class,
/// STR/DEX/INT and the four skill levels under authored skill-icon column
/// headers (b_gui_ig_mnu_combat / ranged / nature-magic / combat-magic).
/// Authored 640×480 rects: dialog 16,90..624,410; list 28,126..612,368;
/// header band 32,130..608,152; rows start 34,158 at a 26px pitch (player1_bg
/// 158..182), eight rows. Scaled by <see cref="HudScale.Modal"/> and centered
/// like the other modal dialogs.
/// </summary>
public sealed class MpPlayersPanel
{
    public readonly record struct Row(
        string Player, string Character, string Class,
        int Str, int Dex, int Int,
        int Melee, int Ranged, int NMagic, int CMagic,
        bool IsLocal);

    public bool IsOpen;

    (float s, int ox, int oy) Frame(int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        return (s, (vw - (int)(640 * s)) / 2, (vh - (int)(480 * s)) / 2);
    }

    (int x, int y, int w, int h) Px((int x0, int y0, int x1, int y1) r, float s, int ox, int oy)
        => (ox + (int)(r.x0 * s), oy + (int)(r.y0 * s),
            (int)((r.x1 - r.x0) * s), (int)((r.y1 - r.y0) * s));

    static readonly (int, int, int, int) Dialog   = (16, 90, 624, 410);
    static readonly (int, int, int, int) ListBg   = (28, 126, 612, 368);
    static readonly (int, int, int, int) Header   = (32, 130, 608, 152);
    static readonly (int, int, int, int) CloseBtn = (560, 96, 616, 118);

    // Column x-extents (authored): player 36..116, character 136..266,
    // class 266..350, then centered numeric columns.
    const int RowY0 = 158, RowPitch = 26, RowH = 24, MaxRows = 8;
    static readonly (int x0, int x1)[] SkillCols =
        { (478, 494), (510, 526), (542, 558), (574, 590) };
    static readonly string[] SkillIcons =
        { "b_gui_ig_mnu_combat", "b_gui_ig_mnu_ranged",
          "b_gui_ig_mnu_nature-magic", "b_gui_ig_mnu_combat-magic" };

    public bool IsPointInPanel(int mx, int my, int vw, int vh)
    {
        if (!IsOpen) return false;
        var (s, ox, oy) = Frame(vw, vh);
        var p = Px(Dialog, s, ox, oy);
        return mx >= p.x && mx < p.x + p.w && my >= p.y && my < p.y + p.h;
    }

    public bool IsPointInClose(int mx, int my, int vw, int vh)
    {
        if (!IsOpen) return false;
        var (s, ox, oy) = Frame(vw, vh);
        var p = Px(CloseBtn, s, ox, oy);
        return mx >= p.x && mx < p.x + p.w && my >= p.y && my < p.y + p.h;
    }

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? commonChrome, Func<string, GlTexture?>? guiTex,
                     int vw, int vh, IReadOnlyList<Row> rows)
    {
        if (!IsOpen || icons is null || commonChrome is null || guiTex is null) return;
        var (s, ox, oy) = Frame(vw, vh);
        int fs = Math.Max(1, (int)MathF.Round(s));
        var ink   = new Vector4(0.88f, 0.84f, 0.70f, 1f);
        var gold  = new Vector4(0.91f, 0.85f, 0.63f, 1f);
        var local = new Vector4(0.72f, 0.92f, 0.70f, 1f);

        var dlg = Px(Dialog, s, ox, oy);
        bars.DrawRect(vw, vh, dlg.x, dlg.y, dlg.w, dlg.h, new Vector4(0.04f, 0.04f, 0.05f, 0.82f));
        NinePatch.DrawCpbox(icons, commonChrome, vw, vh, dlg.x, dlg.y, dlg.w, dlg.h, Vector4.One);

        string title = "PLAYERS";
        int tw = text.MeasureWidth(title, fs);
        text.DrawString(vw, vh, title, dlg.x + (dlg.w - tw) / 2, oy + (int)(98 * s), gold, fs);

        var lb = Px(ListBg, s, ox, oy);
        bars.DrawRect(vw, vh, lb.x, lb.y, lb.w, lb.h, new Vector4(0f, 0f, 0f, 0.35f));
        var hd = Px(Header, s, ox, oy);
        bars.DrawRect(vw, vh, hd.x, hd.y, hd.w, hd.h, new Vector4(1f, 1f, 1f, 0.08f));

        void Left(string t, int ax, int ay, Vector4 c) =>
            text.DrawString(vw, vh, t, ox + (int)(ax * s), oy + (int)(ay * s), c, fs);
        void Center(string t, int ax0, int ax1, int ay, Vector4 c)
        {
            int w = text.MeasureWidth(t, fs);
            int cx = ox + (int)((ax0 + ax1) * 0.5f * s) - w / 2;
            text.DrawString(vw, vh, t, cx, oy + (int)(ay * s), c, fs);
        }

        // Authored header row: text columns + the four skill icons.
        Left("Player", 36, 135, ink);
        Left("Character", 136, 135, ink);
        Left("Class", 266, 135, ink);
        Center("STR", 350, 390, 136, ink);
        Center("DEX", 390, 430, 136, ink);
        Center("INT", 430, 470, 136, ink);
        for (int i = 0; i < SkillCols.Length; i++)
        {
            var t = guiTex(SkillIcons[i]);
            if (t is null) continue;
            var (cx0, cx1) = SkillCols[i];
            var r = Px((cx0, 133, cx1, 149), s, ox, oy);
            icons.DrawIcon(vw, vh, t, r.x, r.y, r.w, r.h, Vector4.One, 0f, 0f, 1f, 1f);
        }

        int n = Math.Min(rows.Count, MaxRows);
        for (int i = 0; i < n; i++)
        {
            var row = rows[i];
            int y0 = RowY0 + i * RowPitch;
            var bg = Px((34, y0, 606, y0 + RowH), s, ox, oy);
            bars.DrawRect(vw, vh, bg.x, bg.y, bg.w, bg.h,
                          new Vector4(1f, 1f, 1f, i % 2 == 0 ? 0.05f : 0.09f));
            var c = row.IsLocal ? local : ink;
            int ty = y0 + 4;
            Left(row.Player, 40, ty, c);
            Left(row.Character, 140, ty, c);
            Left(row.Class, 270, ty, c);
            Center(row.Str.ToString(), 350, 390, ty, c);
            Center(row.Dex.ToString(), 390, 430, ty, c);
            Center(row.Int.ToString(), 430, 470, ty, c);
            Center(row.Melee.ToString(),  SkillCols[0].x0 - 8, SkillCols[0].x1 + 8, ty, c);
            Center(row.Ranged.ToString(), SkillCols[1].x0 - 8, SkillCols[1].x1 + 8, ty, c);
            Center(row.NMagic.ToString(), SkillCols[2].x0 - 8, SkillCols[2].x1 + 8, ty, c);
            Center(row.CMagic.ToString(), SkillCols[3].x0 - 8, SkillCols[3].x1 + 8, ty, c);
        }

        // Close button (authored button_close top-right of the dialog).
        var cb = Px(CloseBtn, s, ox, oy);
        ButtonChrome.Draw(icons, guiTex, vw, vh, cb.x, cb.y, cb.w, cb.h,
                          "button4", ButtonChrome.State.Up);
        string close = "CLOSE";
        int cw = text.MeasureWidth(close, fs);
        text.DrawString(vw, vh, close, cb.x + (cb.w - cw) / 2,
                        cb.y + (cb.h - 12 * fs) / 2, ink, fs);
    }
}

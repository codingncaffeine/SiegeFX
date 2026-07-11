using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Save;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// DS1's Load Game window in two poses driven from the same save data:
/// <list type="bullet">
///   <item><b>In-game</b> (Esc → LOAD GAME): a screen-centered cpbox floating
///   over the live world. Preview thumbnail left, HERO / MAP / ELAPSED info box
///   right, save list below, LOAD / DELETE / CANCEL.</item>
///   <item><b>Main-menu</b> (Single Player → LOAD GAME): a faithful rebuild of
///   the frontend <c>/ui/interfaces/frontend/load_game/load_game.gas</c> screen
///   — the authored 800×600 widget rects rendered with the real DS1 chrome
///   textures (<c>b_gui_cmn_listreport_tiled_bg</c> save list, <c>browntrim</c>
///   window frame, <c>brd_01</c> woodbox preview, <c>b_gui_fe_m_mn_3d_button_wood</c>
///   Load/Delete buttons, <c>b_gui_cmn_selection</c> highlight) over the frontend
///   shell. The ornate "LOAD GAME" scroll is the shared mainmenu chrome plate
///   (FrontendScene keeps subset 0, drops the SP title text) with the label
///   drawn here.</item>
/// </list>
///
/// <para>Both poses scale from their authored reference space to the viewport
/// (in-game via <see cref="HudScale.Modal"/> at 640×480; frontend via the
/// interface scale <c>min(vw/800, vh/600)</c> centered, the same mapping the
/// frontend chrome + About overlay use). The host drives it: <see cref="Open"/>
/// with the save list, route mouse/scroll/keys, and act on the
/// <see cref="Result"/> from <see cref="OnMouseUp"/>. The dialog owns no load
/// logic. The preview thumbnail uploads lazily on the frame the selection
/// changes and frees on <see cref="Close"/>.</para>
/// </summary>
public sealed class LoadGameDialog : IDisposable
{
    public bool IsOpen { get; private set; }

    /// <summary>true = the frontend (main-menu) pose; false = the in-game
    /// cpbox pose.</summary>
    public bool MainMenuStyle { get; private set; }

    public enum Result { None, Load, Delete, Cancel }

    // ---- in-game pose reference rects (640×480, HudScale.Modal) -------------
    private const int RefW = 640, RefH = 480;
    private static readonly (int x0, int y0, int x1, int y1) IgPanel   = (150,  56, 492, 430);
    private static readonly (int x0, int y0, int x1, int y1) IgTitle   = (246,  72, 388, 101);
    private static readonly (int x0, int y0, int x1, int y1) IgPreview = (171, 109, 258, 181);
    private static readonly (int x0, int y0, int x1, int y1) IgInfo    = (267, 109, 467, 181);
    private static readonly (int x0, int y0, int x1, int y1) IgList    = (171, 187, 467, 377);
    private static readonly (int x0, int y0, int x1, int y1) IgLoad    = (171, 388, 263, 404);
    private static readonly (int x0, int y0, int x1, int y1) IgDelete  = (273, 388, 365, 404);
    private static readonly (int x0, int y0, int x1, int y1) IgCancel  = (375, 388, 467, 404);

    // ---- frontend pose reference rects (800×600) — verbatim from
    //      load_game.gas so the layout is 1:1 with DS1.
    private const int FeW = 800, FeH = 600;
    private static readonly (int x0, int y0, int x1, int y1) FeInfo    = (226, 172, 470, 252); // loadsave_game_name_text
    private static readonly (int x0, int y0, int x1, int y1) FePreview = (483, 174, 573, 243); // preview_dialog_bg (woodbox)
    private static readonly (int x0, int y0, int x1, int y1) FeList    = (275, 262, 518, 483); // load_game_listbox
    private static readonly (int x0, int y0, int x1, int y1) FeLoad    = (299, 492, 386, 518); // load_button
    private static readonly (int x0, int y0, int x1, int y1) FeDelete  = (413, 492, 500, 518); // delete_button
    private static readonly (int x0, int y0, int x1, int y1) FePrev    = (237, 575, 338, 595); // button_previous
    private static readonly (int x0, int y0, int x1, int y1) FeNext    = (461, 575, 562, 595); // button_next (= load ok)
    // The loadmap.asp mesh draws the enclosing marble window + map backing +
    // selector spikes; this bound is only used for click-off-to-dismiss
    // hit-testing. The "LOAD GAME" title is mesh art too (mainmenu title drum
    // at sp2lg pose) — no 2D title here.
    private static readonly (int x0, int y0, int x1, int y1) FeWindow  = (214, 158, 584, 528);
    // preview_window gas rect anchors the thumbnail at 488,179 (the woodbox
    // interior); the box crops it to its own 90×69 frame.
    private static readonly (int x0, int y0, int x1, int y1) FeThumb   = (488, 179, 568, 238);
    private const int FeElementH = 13; // listbox setelementheight(13)
    // load_game_listbox font_color = 0x00959290 — silver-gray row text.
    private static readonly Vector4 FeRowInk = new(0x95 / 255f, 0x92 / 255f, 0x90 / 255f, 1f);

    private readonly List<SaveStore.SaveSlot> _saves = new();
    private int _selected = -1;
    private int _scrollRow;

    // Per-frame screen rects (Layout writes; hit-tests read).
    private (int x, int y, int w, int h) _sPanel, _sInfo, _sPreview, _sThumb,
                                          _sList, _sLoad, _sDelete, _sCancel, _sNext;
    private int _rowH, _visibleRows;

    // Frontend interface→screen mapping (800×600 gas space → pixels). Set from
    // FrontendScene.GetInterfaceMapping so the window scales in lockstep with
    // the 3D chrome (pillars/title) at any resolution; falls back to the plain
    // centred interface scale when the chrome mapping isn't available yet.
    private float _feOx, _feOy, _feSx, _feSy;

    /// <summary>Host-supplied chrome-projection mapper (from FrontendScene).
    /// Given (vw,vh) returns whether it resolved plus the 800×600→screen
    /// origin + per-axis scale. Null → plain <c>min(vw/800,vh/600)</c> fallback.</summary>
    public Func<int, int, (bool ok, float ox, float oy, float sx, float sy)>? FrontendMap;

    private enum Btn { None, Load, Delete, Cancel, Next }
    private Btn _pressed = Btn.None;
    private Btn _hover = Btn.None;

    // SC-MAINMENU-LOADGAME — hover/press state for the shell's PREVIOUS /
    // NEXT plates (backbutton.asp b2pn pose). The host layers the
    // art_mapping texture swap via DrawPreviousButton / DrawNextButton.
    public bool PrevHovered => _hover == Btn.Cancel;
    public bool PrevPressed => _pressed == Btn.Cancel;
    public bool NextHovered => _hover == Btn.Next;
    public bool NextPressed => _pressed == Btn.Next;

    private GlTexture? _thumbTex;
    private string? _thumbForPath;

    public SaveStore.SaveSlot? Selected =>
        _selected >= 0 && _selected < _saves.Count ? _saves[_selected] : null;

    public void Open(IReadOnlyList<SaveStore.SaveSlot> saves, bool mainMenuStyle)
    {
        _saves.Clear();
        _saves.AddRange(saves);
        MainMenuStyle = mainMenuStyle;
        _selected = _saves.Count > 0 ? 0 : -1;
        _scrollRow = 0;
        _pressed = Btn.None;
        _hover = Btn.None;
        InvalidateThumb();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _pressed = Btn.None;
        InvalidateThumb();
    }

    public void Tick(float dt) { }

    private void InvalidateThumb()
    {
        _thumbTex?.Dispose();
        _thumbTex = null;
        _thumbForPath = null;
    }

    // ---- layout ------------------------------------------------------------

    private void Layout(int vw, int vh)
    {
        if (MainMenuStyle) LayoutFrontend(vw, vh);
        else               LayoutInGame(vw, vh);
    }

    private void LayoutInGame(int vw, int vh)
    {
        _sPanel   = ScrIg(IgPanel, vw, vh);
        _sPreview = ScrIg(IgPreview, vw, vh);
        _sInfo    = ScrIg(IgInfo, vw, vh);
        _sList    = ScrIg(IgList, vw, vh);
        _sLoad    = ScrIg(IgLoad, vw, vh);
        _sDelete  = ScrIg(IgDelete, vw, vh);
        _sCancel  = ScrIg(IgCancel, vw, vh);
        _sNext    = default;

        float s = HudScale.Modal(vw, vh);
        _rowH = Math.Max(1, (int)MathF.Round(15 * s));
        int pad = (int)MathF.Round(4 * s);
        _visibleRows = Math.Max(1, (_sList.h - pad * 2) / _rowH);
    }

    private void LayoutFrontend(int vw, int vh)
    {
        // Prefer the chrome's own projection (letterbox + overscan) so the
        // window locks to the pillars/title; fall back to the plain centred
        // interface scale before the backdrop mesh has resolved.
        var m = FrontendMap?.Invoke(vw, vh) ?? (false, 0f, 0f, 0f, 0f);
        if (m.Item1)
        {
            _feOx = m.Item2; _feOy = m.Item3; _feSx = m.Item4; _feSy = m.Item5;
        }
        else
        {
            float s = MathF.Min(vw / (float)FeW, vh / (float)FeH);
            _feSx = _feSy = s;
            _feOx = (vw - FeW * s) / 2f;
            _feOy = (vh - FeH * s) / 2f;
        }

        _sPanel   = ScrFe(FeWindow, vw, vh);
        _sInfo    = ScrFe(FeInfo, vw, vh);
        _sPreview = ScrFe(FePreview, vw, vh);
        _sThumb   = ScrFe(FeThumb, vw, vh);
        _sList    = ScrFe(FeList, vw, vh);
        _sLoad    = ScrFe(FeLoad, vw, vh);
        _sDelete  = ScrFe(FeDelete, vw, vh);
        _sCancel  = ScrFe(FePrev, vw, vh); // Previous == back to Single Player
        _sNext    = ScrFe(FeNext, vw, vh); // Next == Load

        _rowH = Math.Max(1, (int)MathF.Round(FeElementH * _feSy));
        _visibleRows = Math.Max(1, _sList.h / _rowH);
    }

    // ---- input -------------------------------------------------------------

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        _hover = HitButton(px, py);
    }

    public bool OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return false;
        Layout(vw, vh);
        _pressed = HitButton(px, py);
        if (_pressed == Btn.None) TrySelectRow(px, py);
        return true;
    }

    public Result OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return Result.None;
        Layout(vw, vh);
        var up = HitButton(px, py);
        var was = _pressed;
        _pressed = Btn.None;
        if (up == Btn.None || up != was) return Result.None;
        return up switch
        {
            Btn.Load   => Selected is null ? Result.None : Result.Load,
            Btn.Next   => Selected is null ? Result.None : Result.Load,
            Btn.Delete => Selected is null ? Result.None : Result.Delete,
            Btn.Cancel => Result.Cancel,
            _          => Result.None,
        };
    }

    public void OnScroll(float dir)
    {
        if (!IsOpen) return;
        int maxScroll = Math.Max(0, _saves.Count - _visibleRows);
        _scrollRow = Math.Clamp(_scrollRow - Math.Sign(dir), 0, maxScroll);
    }

    public bool OnArrowKey(int dir)
    {
        if (!IsOpen) return false;
        MoveSelection(dir);
        return true;
    }

    private void MoveSelection(int dir)
    {
        if (_saves.Count == 0) return;
        _selected = Math.Clamp((_selected < 0 ? 0 : _selected) + dir, 0, _saves.Count - 1);
        if (_selected < _scrollRow) _scrollRow = _selected;
        else if (_selected >= _scrollRow + _visibleRows) _scrollRow = _selected - _visibleRows + 1;
        _scrollRow = Math.Clamp(_scrollRow, 0, Math.Max(0, _saves.Count - _visibleRows));
        InvalidateThumb();
    }

    /// <summary>True if the point falls on the dialog's window or one of the
    /// shell's PREVIOUS / NEXT plate hit-rects (which sit OUTSIDE the window,
    /// on the bottom bar). The host treats a click anywhere else as a
    /// back-out in the frontend pose.</summary>
    public bool IsInsidePanel(int px, int py, int vw, int vh)
    {
        Layout(vw, vh);
        return In(px, py, _sPanel) || In(px, py, _sCancel) || In(px, py, _sNext);
    }

    private Btn HitButton(int px, int py)
    {
        if (In(px, py, _sLoad))   return Btn.Load;
        if (In(px, py, _sDelete)) return Btn.Delete;
        if (In(px, py, _sCancel)) return Btn.Cancel;
        if (In(px, py, _sNext))   return Btn.Next; // frontend NEXT commits Load
        return Btn.None;
    }

    private void TrySelectRow(int px, int py)
    {
        if (!In(px, py, _sList)) return;
        // Frontend rows stack from the listbox top (DS1 listbox layout); the
        // in-game pose centres them, so keep the pad state-dependent.
        int pad = MainMenuStyle ? 0 : Math.Max(0, (_sList.h - _visibleRows * _rowH) / 2);
        int rel = py - (_sList.y + pad);
        if (rel < 0) return;
        int row = rel / _rowH + _scrollRow;
        if (row >= 0 && row < _saves.Count && row != _selected)
        {
            _selected = row;
            InvalidateThumb();
        }
    }

    private static bool In(int px, int py, (int x, int y, int w, int h) r)
        => r.w > 0 && r.h > 0 && px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;

    // ---- draw --------------------------------------------------------------

    public void Draw(GL gl, BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, Func<string, GlTexture?>? commonChrome,
                     int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        if (MainMenuStyle) DrawFrontend(gl, bars, text, icons, guiTex, commonChrome, vw, vh);
        else               DrawInGame(gl, bars, text, icons, guiTex, commonChrome, vw, vh);
    }

    // ===== frontend (main-menu) pose — authentic DS1 load_game.gas ==========

    private void DrawFrontend(GL gl, BarRenderer bars, TextRenderer text, IconRenderer? icons,
                              Func<string, GlTexture?>? guiTex, Func<string, GlTexture?>? commonChrome,
                              int vw, int vh)
    {
        float s = _feSy;
        int fs = Math.Max(1, (int)MathF.Round(s));
        bool haveChrome = icons is not null && commonChrome is not null;

        // Everything structural is mesh art drawn by FrontendScene's Load
        // Game chrome: the marble window + world-map list backing + selector
        // spikes (loadmap.asp), the ornate LOAD GAME title (mainmenu title
        // drum @ sp2lg), and the PREVIOUS / NEXT plates (backbutton @ b2pn).
        // This pose paints only the gas-authored 2D widgets on top.

        // loadsave_game_name_text — text_box at 226,172,470,252, justify =
        // center + center_height, font_color -1 (white). No background of its
        // own: the window mesh's dark info band shows through.
        if (Selected is { } sel)
        {
            var lines = InfoLines(sel);
            int lineH = Math.Max(text.LineHeight * fs, _rowH);
            int iy = _sInfo.y + Math.Max(0, (_sInfo.h - lines.Length * lineH) / 2);
            foreach (var line in lines)
            {
                int lw = text.MeasureWidth(line, fs);
                text.DrawString(vw, vh, line, _sInfo.x + (_sInfo.w - lw) / 2, iy, Vector4.One, fs);
                iy += lineH;
            }
        }

        // preview_window + preview_dialog_bg — the screenshot thumbnail at the
        // gas window anchor, framed by the common woodbox (b_gui_cmn_brd_01
        // nine-patch) drawn OVER it (gas draw order 26 then 27).
        EnsureThumb(gl);
        if (_thumbTex is not null && icons is not null && _sThumb.w > 0 && _sThumb.h > 0)
            icons.DrawIcon(vw, vh, _thumbTex, _sThumb.x, _sThumb.y, _sThumb.w, _sThumb.h, Vector4.One);
        if (haveChrome)
            DrawWoodbox(icons!, commonChrome!, vw, vh, _sPreview);

        // load_game_listbox — rows stack from the top over the mesh's world
        // map; silver-gray ink (gas font_color 0x959290), selection bar at
        // alpha 0.5 (gas selection_box), selected row ink goes dark for
        // contrast against the lit bar.
        var selTex = haveChrome ? commonChrome!("selection") : null;
        int lx = _sList.x + (int)MathF.Round(3 * s);
        int lw2 = _sList.w - (int)MathF.Round(6 * s);
        for (int i = 0; i < _visibleRows; i++)
        {
            int idx = _scrollRow + i;
            if (idx >= _saves.Count) break;
            var slot = _saves[idx];
            int ry = _sList.y + i * _rowH;
            if (idx == _selected)
            {
                if (selTex is not null)
                    icons!.DrawIcon(vw, vh, selTex, _sList.x, ry, _sList.w, _rowH,
                                    new Vector4(1f, 1f, 1f, 0.5f));
                else
                    bars.DrawRect(vw, vh, _sList.x, ry, _sList.w, _rowH,
                                  new Vector4(0.72f, 0.78f, 0.82f, 0.5f));
            }
            string label = RowLabel(slot);
            int rowTextH = text.LineHeight * fs;
            text.DrawString(vw, vh, Truncate(text, label, lw2, fs), lx,
                            ry + Math.Max(0, (_rowH - rowTextH) / 2),
                            idx == _selected ? new Vector4(0.10f, 0.12f, 0.14f, 1f) : FeRowInk, fs);
        }
        if (_saves.Count > _visibleRows)
        {
            var dim = new Vector4(0.60f, 0.57f, 0.50f, 1f);
            int ax = _sList.x + _sList.w - (int)MathF.Round(10 * s);
            if (_scrollRow > 0)
                text.DrawString(vw, vh, "^", ax, _sList.y, dim, fs);
            if (_scrollRow < _saves.Count - _visibleRows)
                text.DrawString(vw, vh, "v", ax, _sList.y + _sList.h - _rowH, dim, fs);
        }

        // load_button / delete_button — wood buttons (button_wood_up/hov/down
        // with the gas uvcoords crop), white 12p copperplate labels.
        bool canAct = Selected is not null;
        DrawWoodButton(bars, text, icons, guiTex, vw, vh, _sLoad,   "Load",   Btn.Load,   canAct, fs);
        DrawWoodButton(bars, text, icons, guiTex, vw, vh, _sDelete, "Delete", Btn.Delete, canAct, fs);
        // button_previous / button_next author NO texture — they're bare hit
        // rects over the 3D PREVIOUS / NEXT plates the chrome renders.
    }

    private string[] InfoLines(SaveStore.SaveSlot sel)
    {
        string hero = string.IsNullOrWhiteSpace(sel.HeroName) ? "-" : sel.HeroName.ToUpperInvariant();
        string map  = string.IsNullOrWhiteSpace(sel.MapName) ? "-" : sel.MapName.ToUpperInvariant();
        return new[] { $"HERO: {hero}", $"MAP: {map}", $"ELAPSED TIME: {FormatElapsedColon(sel.ElapsedSeconds)}" };
    }

    // The DS1 wood button (b_gui_fe_m_mn_3d_button_wood_*) with the gas
    // uvcoords crop (0, 0.1875, 0.679688, 1.0). The gas authors V in DS1's
    // bottom-up GL space (texture is 128×32; the art fills the TOP 26 px
    // visually = stored rows 7-31); IconRenderer's UV overload takes VISUAL
    // (top-down) coords, so the band converts to v 0 .. 1-0.1875 = 0.8125.
    // Passing the gas values raw selected the wrong band — the art rode ~7px
    // high in the rect with the texture's empty strip at the bottom, which
    // made the centred label read as off-centre on the wood.
    private void DrawWoodButton(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                                Func<string, GlTexture?>? guiTex, int vw, int vh,
                                (int x, int y, int w, int h) r, string label, Btn id, bool enabled, int fs)
    {
        string tk = !enabled ? "b_gui_fe_m_mn_3d_button_wood_up"
                  : _pressed == id ? "b_gui_fe_m_mn_3d_button_wood_down"
                  : _hover == id ? "b_gui_fe_m_mn_3d_button_wood_hov"
                                 : "b_gui_fe_m_mn_3d_button_wood_up";
        var tex = guiTex?.Invoke(tk);
        if (tex is not null && icons is not null)
            icons.DrawIcon(vw, vh, tex, r.x, r.y, r.w, r.h,
                           enabled ? Vector4.One : new Vector4(0.55f, 0.55f, 0.55f, 1f),
                           0f, 0f, 0.679688f, 0.8125f);
        else
        {
            bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.20f, 0.14f, 0.07f, 0.95f));
            bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.45f, 0.34f, 0.20f, 1f));
        }
        // gas text child: font_color -1 (white); hover/press feedback is the
        // wood texture swap only. disable_color 0x555f5f5f dims the label.
        var ink = enabled ? Vector4.One : new Vector4(0.37f, 0.37f, 0.37f, 1f);
        int lw = text.MeasureWidth(label, fs);
        int fh = text.LineHeight * fs;
        text.DrawString(vw, vh, label, r.x + (r.w - lw) / 2,
                        r.y + (r.h - fh) / 2, ink, fs);
    }

    // preview_dialog_bg — common_template=woodbox → b_gui_cmn_brd_01_* nine-
    // patch, no fill (the interior holds the thumbnail).
    private static void DrawWoodbox(IconRenderer icons, Func<string, GlTexture?> cmn,
                                    int vw, int vh, (int x, int y, int w, int h) r)
        => NinePatch.Draw(icons, vw, vh, r.x, r.y, r.w, r.h,
            tlCorner: cmn("brd_01_ul"), trCorner: cmn("brd_01_ur"),
            blCorner: cmn("brd_01_ll"), brCorner: cmn("brd_01_lr"),
            topSide:  cmn("brd_01_top"), bottomSide: cmn("brd_01_bot"),
            leftSide: cmn("brd_01_l"),  rightSide:  cmn("brd_01_r"),
            fill: null, tint: Vector4.One);


    // ===== in-game (cpbox) pose — unchanged from the working modal ==========

    private void DrawInGame(GL gl, BarRenderer bars, TextRenderer text, IconRenderer? icons,
                            Func<string, GlTexture?>? guiTex, Func<string, GlTexture?>? commonChrome,
                            int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        int fs = Math.Max(1, (int)MathF.Round(s));
        var parch = new Vector4(0.88f, 0.82f, 0.70f, 1f);
        var gold  = new Vector4(1f, 0.90f, 0.55f, 1f);
        var dim   = new Vector4(0.55f, 0.52f, 0.46f, 1f);

        bool chrome = icons is not null && commonChrome is not null;
        bars.DrawRect(vw, vh, _sPanel.x, _sPanel.y, _sPanel.w, _sPanel.h,
                      new Vector4(0.03f, 0.03f, 0.04f, 0.55f));
        void Frame((int x, int y, int w, int h) r)
        {
            if (chrome)
                NinePatch.DrawCpbox(icons!, commonChrome!, vw, vh, r.x, r.y, r.w, r.h, Vector4.One);
            else
            {
                bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.07f, 0.07f, 0.08f, 0.85f));
                bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.45f, 0.42f, 0.34f, 1f));
            }
        }

        Frame(_sPanel);
        Frame(_sPreview);
        Frame(_sInfo);
        Frame(_sList);

        int titleScale = Math.Max(1, (int)MathF.Round(s * 1.16f));
        const string title = "LOAD GAME";
        int tw = text.MeasureWidth(title, titleScale);
        text.DrawString(vw, vh, title, _sPanel.x + (_sPanel.w - tw) / 2,
                        ScrIg(IgTitle, vw, vh).y, gold, titleScale);

        EnsureThumb(gl);
        if (_thumbTex is not null && icons is not null)
        {
            int ins = Math.Max(1, (int)MathF.Round(3 * s));
            icons.DrawIcon(vw, vh, _thumbTex, _sPreview.x + ins, _sPreview.y + ins,
                           _sPreview.w - ins * 2, _sPreview.h - ins * 2, Vector4.One);
        }
        else if (Selected is not null)
        {
            string nm = "NO PREVIEW";
            int nmw = text.MeasureWidth(nm, fs);
            text.DrawString(vw, vh, nm, _sPreview.x + (_sPreview.w - nmw) / 2,
                            _sPreview.y + (_sPreview.h - text.LineHeight * fs) / 2, dim, fs);
        }

        int infoPad = (int)MathF.Round(8 * s);
        if (Selected is { } sel)
        {
            var lines = InfoLines(sel);
            int lineH = Math.Max(text.LineHeight * fs, _rowH);
            int iy = _sInfo.y + Math.Max(infoPad, (_sInfo.h - lines.Length * lineH) / 2);
            foreach (var line in lines)
            {
                int lw = text.MeasureWidth(line, fs);
                text.DrawString(vw, vh, line, _sInfo.x + _sInfo.w - infoPad - lw, iy, parch, fs);
                iy += lineH;
            }
        }

        int listPad = Math.Max(0, (_sList.h - _visibleRows * _rowH) / 2);
        int lx = _sList.x + (int)MathF.Round(8 * s);
        int lw2 = _sList.w - (int)MathF.Round(16 * s);
        for (int i = 0; i < _visibleRows; i++)
        {
            int idx = _scrollRow + i;
            if (idx >= _saves.Count) break;
            var slot = _saves[idx];
            int ry = _sList.y + listPad + i * _rowH;
            if (idx == _selected)
                bars.DrawRect(vw, vh, _sList.x + (int)MathF.Round(3 * s), ry,
                              _sList.w - (int)MathF.Round(6 * s), _rowH,
                              new Vector4(0.52f, 0.42f, 0.22f, 0.92f));
            string label = RowLabel(slot);
            int rowTextH = text.LineHeight * fs;
            text.DrawString(vw, vh, Truncate(text, label, lw2, fs), lx,
                            ry + Math.Max(0, (_rowH - rowTextH) / 2),
                            idx == _selected ? new Vector4(0.12f, 0.09f, 0.04f, 1f) : parch, fs);
        }
        if (_saves.Count > _visibleRows)
        {
            int ax = _sList.x + _sList.w - (int)MathF.Round(14 * s);
            if (_scrollRow > 0)
                text.DrawString(vw, vh, "^", ax, _sList.y + (int)MathF.Round(2 * s), dim, fs);
            if (_scrollRow < _saves.Count - _visibleRows)
                text.DrawString(vw, vh, "v", ax, _sList.y + _sList.h - _rowH, dim, fs);
        }

        bool canAct = Selected is not null;
        DrawCpButton(bars, text, icons, guiTex, vw, vh, _sLoad,   "Load",   Btn.Load,   canAct, fs);
        DrawCpButton(bars, text, icons, guiTex, vw, vh, _sDelete, "Delete", Btn.Delete, canAct, fs);
        DrawCpButton(bars, text, icons, guiTex, vw, vh, _sCancel, "Cancel", Btn.Cancel, true,   fs);
    }

    private void DrawCpButton(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                              Func<string, GlTexture?>? guiTex, int vw, int vh,
                              (int x, int y, int w, int h) r, string label, Btn id, bool enabled, int fs)
    {
        var state = !enabled ? ButtonChrome.State.Up
                  : _pressed == id ? ButtonChrome.State.Down
                  : _hover == id ? ButtonChrome.State.Hover
                                 : ButtonChrome.State.Up;
        var tint = enabled ? Vector4.One : new Vector4(0.55f, 0.55f, 0.55f, 1f);
        bool chrome = ButtonChrome.Draw(icons, guiTex, vw, vh, r.x, r.y, r.w, r.h, "button4", state, tint);
        if (!chrome)
        {
            bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.14f, 0.12f, 0.08f, 0.9f));
            bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.4f, 0.36f, 0.28f, 1f));
        }
        var ink = !enabled ? new Vector4(0.55f, 0.52f, 0.46f, 1f)
                : _hover == id ? new Vector4(1f, 0.96f, 0.85f, 1f)
                               : new Vector4(0.88f, 0.82f, 0.70f, 1f);
        int lw = text.MeasureWidth(label, fs);
        int fh = text.LineHeight * fs;
        text.DrawString(vw, vh, label, r.x + (r.w - lw) / 2,
                        r.y + (r.h - fh) / 2 + (_pressed == id ? fs : 0), ink, fs);
    }

    // ---- shared helpers ----------------------------------------------------

    private void EnsureThumb(GL gl)
    {
        if (Selected is not { } sel) { InvalidateThumb(); return; }
        if (_thumbForPath == sel.Path) return;
        _thumbTex?.Dispose();
        _thumbTex = null;
        _thumbForPath = sel.Path;
        if (ThumbnailCodec.TryDecode(sel.Thumbnail, out int w, out int h, out var rgba))
        {
            try { _thumbTex = new GlTexture(gl, rgba, w, h, nearestFilter: false); }
            catch { _thumbTex = null; }
        }
    }

    private static string RowLabel(SaveStore.SaveSlot slot)
        => (slot.IsQuicksave ? "[QUICKSAVE]" : slot.DisplayName).ToUpperInvariant();

    private static (int x, int y, int w, int h) ScrIg((int x0, int y0, int x1, int y1) r, int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        int ox = (int)MathF.Round((vw - RefW * s) / 2f);
        int oy = (int)MathF.Round((vh - RefH * s) / 2f);
        return (ox + (int)MathF.Round(r.x0 * s), oy + (int)MathF.Round(r.y0 * s),
                (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));
    }

    private (int x, int y, int w, int h) ScrFe((int x0, int y0, int x1, int y1) r, int vw, int vh)
        => ((int)MathF.Round(_feOx + r.x0 * _feSx), (int)MathF.Round(_feOy + r.y0 * _feSy),
            (int)MathF.Round((r.x1 - r.x0) * _feSx), (int)MathF.Round((r.y1 - r.y0) * _feSy));

    private static string FormatElapsedColon(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string Truncate(TextRenderer text, string s, int maxW, int fs)
    {
        if (text.MeasureWidth(s, fs) <= maxW) return s;
        while (s.Length > 1 && text.MeasureWidth(s + "...", fs) > maxW) s = s[..^1];
        return s + "...";
    }

    public void Dispose() => InvalidateThumb();
}

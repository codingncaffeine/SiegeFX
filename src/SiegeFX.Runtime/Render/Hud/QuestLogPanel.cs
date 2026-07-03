using System.Numerics;
using SiegeFX.Core.Actors;
using Rect = SiegeFX.Runtime.Render.Hud.InfoRailLayout.Rect;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// SC-QUEST-UI-C — DS1-authentic journal screen (toggled with 'L' / 'J').
/// Every rect, texture name, and uvcoord below is pasted verbatim from the
/// shipped <c>/ui/interfaces/backend/journal/journal.gas</c> (640×480
/// authored space): the four parchment quadrants (b_gui_ig_mnu_jnl_01..04),
/// the jbox chrome frames, the quest-picture window at top-left, the quest
/// listbox with its up/down arrows + slider, and the Show Dialogue / Close
/// buttons along the bottom. The per-quest picture comes from DS1's own
/// <c>quests.gas</c> <c>quest_image</c> table (b_gui_ig_mnu_jnl_quest_NN,
/// mined from World.dsmap) keyed off the selected entry — unmapped keys
/// (SiegeFX-only test quests) gracefully leave the parchment jbox blank.
///
/// The panel scales by min(viewportH/480, viewportW/640) and centers, same
/// convention as OptionsMenuPanel. Note the authored parchment extends to
/// y=520 — past the 480-line reference screen — so its bottom edge clips
/// off-screen exactly like it did in DS1.
///
/// "Show Dialogue" renders per the authored layout but is a logged stub —
/// the dialogue-chronicle view (journal.gas's <c>textbox_dialogues</c> /
/// <c>quest_dialogues</c> group) lands in a later slice.
/// </summary>
public sealed class QuestLogPanel
{
    // ─── journal.gas 640×480 authored rects (x1,y1,x2,y2 verbatim) ───────
    // Parchment quadrants, draw_order 1..4 (window_0..window_3):
    static readonly Rect RectParch0 = new(149,   8, 405, 264); // b_gui_ig_mnu_jnl_01
    static readonly Rect RectParch1 = new(405,   8, 533, 264); // b_gui_ig_mnu_jnl_02
    static readonly Rect RectParch2 = new(149, 263, 405, 519); // b_gui_ig_mnu_jnl_03
    static readonly Rect RectParch3 = new(405, 264, 533, 520); // b_gui_ig_mnu_jnl_04
    // Union of the quadrants — the swallow-clicks bounds for the modal.
    static readonly Rect RectBounds = new(149,   8, 533, 520);

    static readonly Rect RectTitleBox    = new(177,  19, 455,  48); // dialog_box_journal_text (jbox, draw 90)
    static readonly Rect RectTitleText   = new(181,  22, 451,  46); // text_journal "Journal", 20p, centered
    static readonly Rect RectCloseX      = new(468,  24, 484,  40); // button_x (b_gui_cmn_jbox_x_up/-hov/-down)
    static readonly Rect RectHeaderBg    = new(177,  52, 479, 139); // dialog_box_header_bg (jbox, draw 9)
    static readonly Rect RectPortraitBox = new(181,  56, 260, 135); // dialog_box_0 (jbox, draw 16)
    static readonly Rect RectPortrait    = new(182,  57, 259, 134); // window_quest_picture (draw 18)
    static readonly Rect RectDescBg      = new(265,  56, 475, 135); // dialog_box_desc_bg (jbox, draw 18)
    static readonly Rect RectDescText    = new(270,  57, 470, 134); // text_box_desc, 12p, justify center, center_height
    static readonly Rect RectMainBg      = new(177, 143, 479, 437); // dialog_box_main_bg (jbox, draw 11)
    static readonly Rect RectListBg      = new(181, 147, 475, 405); // dialog_box_lb_bg (jbox, draw 12)
    static readonly Rect RectList        = new(185, 150, 443, 402); // listbox_quests, setelementheight(15)
    static readonly Rect RectSliderBg    = new(448, 151, 471, 401); // dialog_box_slider_bg (jbox, draw 27)
    static readonly Rect RectSlider      = new(449, 173, 470, 379); // slider_quests track
    static readonly Rect RectArrowUp     = new(449, 152, 470, 174); // button_quests_up
    static readonly Rect RectArrowDown   = new(449, 379, 470, 401); // button_quests_down
    static readonly Rect RectBtn1Bg      = new(181, 409, 326, 431); // dialog_box_button_1_bg (jbox, draw 14)
    static readonly Rect RectBtn1        = new(182, 410, 325, 430); // button_journal_1 (b_gui_cmn_jbox_fill)
    // NB: gas insets the caption sub-rects (211.., 387..) to the right of
    // each face; DS1 renders the label centered on the face, so DrawButton
    // centers on RectBtn1/RectBtn2 and these are kept only as provenance.
    static readonly Rect RectBtn2Bg      = new(330, 409, 475, 431); // dialog_box_button_2_bg (jbox, draw 15)
    static readonly Rect RectBtn2        = new(331, 410, 474, 430); // button_journal_2 (b_gui_cmn_jbox_fill)

    /// <summary>listbox_quests authors <c>oncreated = setelementheight(15)</c>.</summary>
    const int ElementH = 15;

    // window_quest_picture uvcoords = 0,0.398438,0.601563,1 (gas-native
    // bottom-up). Converted per InfoRailLayout.Uv.Screen() — the visible
    // portrait is the top-left 77×77 of the 128×128 quest raw.
    static readonly InfoRailLayout.Uv PortraitUv = new(0f, 0.398438f, 0.601563f, 1f);
    // b_gui_ig_mnu_jnl_arrow_up/_down uvcoords = 0,0.3125,0.65625,1 —
    // the 21×22 arrow art in the top-left of a 32×32 raw.
    static readonly InfoRailLayout.Uv ArrowUv    = new(0f, 0.3125f,   0.65625f,  1f);

    // window_quest_completed — background_color 0x776A4528, f alpha 0.5.
    // DS1 flashes it via alphaanimation(1.0,0.0,0.6) when a quest
    // completes; we render the steady-state tint over the picture while
    // the selected entry is completed (no anim runtime in this slice).
    static readonly Vector4 CompletedTint =
        new(0x6A / 255f, 0x45 / 255f, 0x28 / 255f, (0x77 / 255f) * 0.5f);

    // ─── DS1 quests.gas quest_image table (World.dsmap, mined verbatim) ──
    // Keyed by normalized quest key: lowercase, ",N" stage suffix and "_mp"
    // twin suffix stripped (the _mp rows in quests.gas reuse the SP image).
    static readonly Dictionary<string, string> QuestImages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["quest_for_gyorn"]           = "b_gui_ig_mnu_jnl_quest_01",
            ["quest_edgaar_basement"]     = "b_gui_ig_mnu_jnl_quest_02",
            ["quest_sister_message"]      = "b_gui_ig_mnu_jnl_quest_03",
            ["quest_drunkard_tower"]      = "b_gui_ig_mnu_jnl_quest_04",
            ["quest_gyorn_seek_overseer"] = "b_gui_ig_mnu_jnl_quest_05",
            ["quest_open_gate"]           = "b_gui_ig_mnu_jnl_quest_06",
            ["quest_free_torg"]           = "b_gui_ig_mnu_jnl_quest_07",
            ["quest_torg_seek_overseer"]  = "b_gui_ig_mnu_jnl_quest_08",
            ["quest_apprentice_books"]    = "b_gui_ig_mnu_jnl_quest_09",
            ["quest_ice_dungeon"]         = "b_gui_ig_mnu_jnl_quest_10",
            ["quest_find_merik"]          = "b_gui_ig_mnu_jnl_quest_11",
            ["quest_fort_kroth"]          = "b_gui_ig_mnu_jnl_quest_12",
            ["quest_fort_kroth2"]         = "b_gui_ig_mnu_jnl_quest_12",
            ["quest_merik_staff"]         = "b_gui_ig_mnu_jnl_quest_13",
            ["quest_kill_bandits"]        = "b_gui_ig_mnu_jnl_quest_14",
            ["quest_water_dungeon"]       = "b_gui_ig_mnu_jnl_quest_15",
            ["quest_purify_temple"]       = "b_gui_ig_mnu_jnl_quest_16",
            ["quest_purify_temple_2"]     = "b_gui_ig_mnu_jnl_quest_16",
            ["quest_subdue_village"]      = "b_gui_ig_mnu_jnl_quest_17",
            ["quest_slay_dragon"]         = "b_gui_ig_mnu_jnl_quest_18",
            ["quest_journey_castle"]      = "b_gui_ig_mnu_jnl_quest_19",
            ["quest_find_king"]           = "b_gui_ig_mnu_jnl_quest_20",
            ["quest_find_artifacts"]      = "b_gui_ig_mnu_jnl_quest_21",
            ["quest_destroy_gom"]         = "b_gui_ig_mnu_jnl_quest_22",
            ["quest_destroy_gom2"]        = "b_gui_ig_mnu_jnl_quest_22",
        };

    // ─── interactive state ────────────────────────────────────────────────
    enum Hit { None, Parchment, ShowDialogue, CloseButton, CloseX, ArrowUp, ArrowDown, ListRow }

    string? _selectedKey;
    int  _scroll;
    Hit  _hover   = Hit.None;
    Hit  _pressed = Hit.None;
    bool _closeRequested;
    // SC-QUEST-UI-D — the "Show Dialogue" button toggles the main box between
    // the quest listbox and the selected quest's recorded conversation (DS1's
    // quest_dialogues group, which reuses the same rect). The caption flips to
    // "Show Quests" while the chronicle is up, matching the game.
    bool _showDialogue;

    // Rebuilt by Layout() from the live viewport each Draw / input call so
    // a mid-frame resize can't desync hit rects (OptionsMenuPanel pattern).
    float _scale = 1f;
    int _dx, _dy;
    int _fontScale = 1;

    void Layout(int viewportW, int viewportH)
    {
        // Height-driven scale preserves the authored 4:3 aspect; the
        // viewportW clamp keeps portrait-orientation windows from pushing
        // the panel past the edges. Centered both ways like the options
        // dialog (dy only kicks in when the window is wider than 4:3).
        _scale = MathF.Min(viewportH / 480f, viewportW / 640f);
        _dx = (viewportW - (int)MathF.Round(640 * _scale)) / 2;
        _dy = (viewportH - (int)MathF.Round(480 * _scale)) / 2;
        // Integer font scale keeps the bitmap font crisp (no bilinear
        // smear); same derivation as OptionsMenuPanel's _fontScale.
        _fontScale = System.Math.Max(1, (int)MathF.Round(_scale));
    }

    (int X, int Y, int W, int H) S(Rect r) => (
        _dx + (int)MathF.Round(r.X0 * _scale),
        _dy + (int)MathF.Round(r.Y0 * _scale),
        (int)MathF.Round(r.W * _scale),
        (int)MathF.Round(r.H * _scale));

    static bool Hits((int X, int Y, int W, int H) r, int px, int py) =>
        px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;

    // ─── input ────────────────────────────────────────────────────────────

    /// <summary>Caller consumes this after OnMouseDown returns true —
    /// set by the Close button and the corner X (both author
    /// <c>rollover_help = journal_close</c>).</summary>
    public bool ConsumeCloseRequest()
    {
        bool r = _closeRequested;
        _closeRequested = false;
        return r;
    }

    /// <summary>LMB-down router. Returns true when the click landed inside
    /// the parchment (the caller swallows it so the world never sees a
    /// click-to-move through journal chrome); false lets it fall through.
    /// Row clicks select (portrait + description retarget on next Draw),
    /// arrows scroll one element, Close/X raise the close request, and
    /// Show Dialogue is the logged stub.</summary>
    public bool OnMouseDown(int mx, int my, int viewportW, int viewportH, QuestJournal journal)
    {
        Layout(viewportW, viewportH);
        var hit = HitTest(mx, my);
        _pressed = hit;
        switch (hit)
        {
            case Hit.CloseButton:
            case Hit.CloseX:
                _closeRequested = true;
                _showDialogue = false; // reopen to the quest list, as DS1 does
                _scroll = 0;
                return true;
            case Hit.ShowDialogue:
                // Toggle the quest_dialogues group. Scroll resets so each
                // view opens at its top (the two share one scroll offset,
                // exactly as DS1's shared slider does).
                _showDialogue = !_showDialogue;
                _scroll = 0;
                return true;
            case Hit.ArrowUp:
                _scroll = System.Math.Max(0, _scroll - 1);
                return true;
            case Hit.ArrowDown:
                _scroll++; // clamped against the live entry count in Draw
                return true;
            case Hit.ListRow:
            {
                // In dialogue view the same rect holds scrolling text, not
                // selectable rows — swallow the click without reselecting.
                if (_showDialogue) return true;
                var entries = BuildEntries(journal);
                var list = S(RectList);
                int rowH = System.Math.Max(1, (int)MathF.Round(ElementH * _scale));
                int visible = System.Math.Max(1, list.H / rowH);
                _scroll = System.Math.Clamp(_scroll, 0, System.Math.Max(0, entries.Count - visible));
                int idx = _scroll + (my - list.Y) / rowH;
                if (idx >= 0 && idx < entries.Count) _selectedKey = entries[idx].Key;
                return true;
            }
            case Hit.Parchment:
                return true; // swallow — modal chrome
            default:
                return false;
        }
    }

    /// <summary>Releases the button press latch. Returns true when the
    /// up-edge landed inside the parchment so the caller can swallow it.</summary>
    public bool OnMouseUp(int mx, int my, int viewportW, int viewportH)
    {
        Layout(viewportW, viewportH);
        _pressed = Hit.None;
        return HitTest(mx, my) != Hit.None;
    }

    /// <summary>Hover tracking for the buttons / arrows / corner X (gas
    /// authors vertexcolor 0xff999999 rollover on the jbox_fill buttons,
    /// setalpha(0.8) on the arrows, and the -hov X texture).</summary>
    public void OnMouseMove(int mx, int my, int viewportW, int viewportH)
    {
        Layout(viewportW, viewportH);
        _hover = HitTest(mx, my);
    }

    Hit HitTest(int mx, int my)
    {
        if (Hits(S(RectCloseX),    mx, my)) return Hit.CloseX;
        if (Hits(S(RectBtn1),      mx, my)) return Hit.ShowDialogue;
        if (Hits(S(RectBtn2),      mx, my)) return Hit.CloseButton;
        if (Hits(S(RectArrowUp),   mx, my)) return Hit.ArrowUp;
        if (Hits(S(RectArrowDown), mx, my)) return Hit.ArrowDown;
        if (Hits(S(RectList),      mx, my)) return Hit.ListRow;
        if (Hits(S(RectBounds),    mx, my)) return Hit.Parchment;
        return Hit.None;
    }

    // ─── drawing ──────────────────────────────────────────────────────────

    /// <param name="resolveTexture">Maps a texture basename (e.g.
    /// <c>b_gui_ig_mnu_jnl_01</c>) to its cached <see cref="GlTexture"/> —
    /// RenderHost passes TryGetGuiTexture. Null (or a null <paramref
    /// name="icons"/>) drops the panel to a flat-rect fallback so the
    /// journal stays usable if the art fails to resolve.</param>
    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     int viewportW, int viewportH, QuestJournal journal,
                     Func<string, GlTexture?>? resolveTexture)
    {
        Layout(viewportW, viewportH);
        var entries = BuildEntries(journal);
        EnsureSelection(entries);

        var white  = new Vector4(1f, 1f, 1f, 1f);              // listbox text_color 0xFFFFFFFF
        var dimInk = new Vector4(0.72f, 0.70f, 0.62f, 1f);     // completed/failed rows (SiegeFX shading; gas authors one color)
        var dim    = new Vector4(0f, 0f, 0f, 0.55f);           // modal backdrop (interface=true / disable_camera=true)

        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, dim);

        bool art = icons is not null && resolveTexture is not null;
        GlTexture? p0 = art ? resolveTexture!("b_gui_ig_mnu_jnl_01") : null;
        GlTexture? p1 = art ? resolveTexture!("b_gui_ig_mnu_jnl_02") : null;
        GlTexture? p2 = art ? resolveTexture!("b_gui_ig_mnu_jnl_03") : null;
        GlTexture? p3 = art ? resolveTexture!("b_gui_ig_mnu_jnl_04") : null;
        art &= p0 is not null && p1 is not null && p2 is not null && p3 is not null;

        // Parchment (window_0..3, draw_order 1-4). Authored to y=520 — the
        // bottom edge clips off-screen exactly as it did on DS1's 480-line
        // reference screen.
        if (art)
        {
            DrawTex(icons!, viewportW, viewportH, p0!, S(RectParch0), white);
            DrawTex(icons!, viewportW, viewportH, p1!, S(RectParch1), white);
            DrawTex(icons!, viewportW, viewportH, p2!, S(RectParch2), white);
            DrawTex(icons!, viewportW, viewportH, p3!, S(RectParch3), white);
        }
        else
        {
            var (bx, by, bw, bh) = S(RectBounds);
            bars.DrawRect  (viewportW, viewportH, bx, by, bw, bh, new Vector4(0.55f, 0.47f, 0.34f, 0.96f));
            bars.DrawBorder(viewportW, viewportH, bx, by, bw, bh, new Vector4(0.30f, 0.24f, 0.15f, 1f));
        }

        // jbox chrome, in gas draw_order: header (9) → main (11) → list
        // (12) → button bgs (14/15) → portrait frame (16) → desc frame (18).
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectHeaderBg));
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectMainBg));
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectListBg));
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectBtn1Bg));
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectBtn2Bg));
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectPortraitBox));
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectDescBg));

        int fontH = (text.HasFont ? text.Font!.Height : 12) * _fontScale;
        QuestEntry? sel = SelectedEntry(entries);

        // window_quest_picture — the per-quest portrait image from DS1's
        // quests.gas quest_image table. Unmapped keys leave the jbox blank.
        if (sel is not null && art)
        {
            var imageName = MapQuestImage(sel.Key);
            var portrait = imageName is not null ? resolveTexture!(imageName) : null;
            if (portrait is not null)
            {
                var (px, py, pw, ph) = S(RectPortrait);
                var (u0, v0, u1, v1) = PortraitUv.Screen();
                icons!.DrawIcon(viewportW, viewportH, portrait, px, py, pw, ph, white, u0, v0, u1, v1);
            }
        }
        // window_quest_completed — steady-state tint while the selected
        // quest is done (DS1 one-shot-flashes the same fill on completion).
        if (sel is not null && (sel.State == QuestState.Completed || sel.State == QuestState.Failed))
        {
            var (px, py, pw, ph) = S(RectPortrait);
            bars.DrawRect(viewportW, viewportH, px, py, pw, ph, CompletedTint);
        }

        // text_box_desc — objective text, word-wrapped, centered both ways
        // (justify=center + center_height=true). A progress counter line
        // rides under it when the entry carries a goal.
        if (sel is not null)
        {
            var (dxr, dyr, dw, dh) = S(RectDescText);
            int lineH = fontH + 2 * _fontScale;
            var lines = new List<string>();
            string desc = sel.Definition?.ObjectiveText is { Length: > 0 } ot ? ot : Pretty(sel.Key);
            foreach (var l in Wrap(desc, text, dw, _fontScale)) lines.Add(l);
            if (ProgressLine(sel) is { } progress) lines.Add(progress);
            int maxLines = System.Math.Max(1, dh / lineH);
            if (lines.Count > maxLines) lines.RemoveRange(maxLines, lines.Count - maxLines);
            int blockY = dyr + (dh - lines.Count * lineH) / 2;
            for (int i = 0; i < lines.Count; i++)
            {
                int lw2 = text.MeasureWidth(lines[i], _fontScale);
                text.DrawString(viewportW, viewportH, lines[i],
                                dxr + (dw - lw2) / 2, blockY + i * lineH, white, _fontScale);
            }
        }

        // Main box (listbox_quests / textbox_dialogues share the rect). The
        // Show Dialogue button toggles which one occupies it; both scroll
        // through the shared slider, so each reports its own (total, visible).
        var (lx, ly, lw, lh) = S(RectList);
        int total, visible;
        if (_showDialogue)
        {
            (total, visible) = DrawDialogueView(bars, text, viewportW, viewportH,
                                                sel, lx, ly, lw, lh, fontH, white, dimInk);
        }
        else
        {
            int rowH = System.Math.Max(1, (int)MathF.Round(ElementH * _scale));
            visible = System.Math.Max(1, lh / rowH);
            total = entries.Count;
            _scroll = System.Math.Clamp(_scroll, 0, System.Math.Max(0, total - visible));
            var selTex = art ? resolveTexture!("b_gui_cmn_selection") : null;
            int pad = System.Math.Max(2, (int)MathF.Round(3 * _scale));
            for (int i = 0; i < visible; i++)
            {
                int idx = _scroll + i;
                if (idx >= entries.Count) break;
                var e = entries[idx];
                int rowY = ly + i * rowH;
                if (string.Equals(e.Key, _selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    // selection_box — b_gui_cmn_selection at alpha 0.5.
                    if (selTex is not null && icons is not null)
                        icons.DrawIcon(viewportW, viewportH, selTex, lx, rowY, lw, rowH,
                                       new Vector4(1f, 1f, 1f, 0.5f));
                    else
                        bars.DrawRect(viewportW, viewportH, lx, rowY, lw, rowH,
                                      new Vector4(0.35f, 0.28f, 0.18f, 0.5f));
                }
                var label = e.Definition?.ScreenName is { Length: > 0 } sn ? sn : Pretty(e.Key);
                label = FitToWidth(label, text, lw - pad * 2, _fontScale);
                bool closed = e.State == QuestState.Completed || e.State == QuestState.Failed;
                text.DrawString(viewportW, viewportH, label,
                                lx + pad, rowY + (rowH - fontH) / 2, closed ? dimInk : white, _fontScale);
            }
            if (entries.Count == 0)
            {
                const string empty = "No quests yet - find an NPC and accept one.";
                int ew = text.MeasureWidth(empty, _fontScale);
                text.DrawString(viewportW, viewportH, empty,
                                lx + (lw - ew) / 2, ly + (lh - fontH) / 2, dimInk, _fontScale);
            }
        }

        // slider chrome — jbox track bg (draw 27), thumb, then arrows
        // (draw 61/88 puts them above the thumb).
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectSliderBg));
        DrawSliderThumb(bars, icons, resolveTexture, viewportW, viewportH,
                        total, visible);
        DrawArrow(bars, icons, resolveTexture, viewportW, viewportH, S(RectArrowUp),
                  "b_gui_ig_mnu_jnl_arrow_up", Hit.ArrowUp);
        DrawArrow(bars, icons, resolveTexture, viewportW, viewportH, S(RectArrowDown),
                  "b_gui_ig_mnu_jnl_arrow_down", Hit.ArrowDown);

        // Bottom buttons — jbox_fill face + label. gas rollover vertexcolor
        // is 0xff999999, pressed 0xff555555. Button 1's caption flips to
        // "Show Quests" while the dialogue chronicle is up, as in DS1.
        DrawButton(bars, text, icons, resolveTexture, viewportW, viewportH,
                   S(RectBtn1), _showDialogue ? "Show Quests" : "Show Dialogue",
                   Hit.ShowDialogue, fontH, white);
        DrawButton(bars, text, icons, resolveTexture, viewportW, viewportH,
                   S(RectBtn2), "Close", Hit.CloseButton, fontH, white);

        // Corner X (button_x, draw 38) — -up/-hov/-down state textures.
        var xName = _pressed == Hit.CloseX ? "b_gui_cmn_jbox_x_down"
                  : _hover   == Hit.CloseX ? "b_gui_cmn_jbox_x_hov"
                  :                          "b_gui_cmn_jbox_x_up";
        var xTex = art ? resolveTexture!(xName) : null;
        var (cx, cy, cw, ch) = S(RectCloseX);
        if (xTex is not null && icons is not null)
        {
            icons.DrawIcon(viewportW, viewportH, xTex, cx, cy, cw, ch, white);
        }
        else
        {
            bars.DrawRect  (viewportW, viewportH, cx, cy, cw, ch, new Vector4(0.20f, 0.16f, 0.10f, 1f));
            bars.DrawBorder(viewportW, viewportH, cx, cy, cw, ch, dimInk);
            int xw = text.MeasureWidth("X", _fontScale);
            text.DrawString(viewportW, viewportH, "X", cx + (cw - xw) / 2, cy + (ch - fontH) / 2, white, _fontScale);
        }

        // Title band — dialog_box_journal_text (draw 90) + text_journal
        // (draw 91). gas authors the title at 20p vs the 12p body font;
        // one extra integer step approximates the ratio with our single
        // bitmap font.
        Jbox(bars, icons, resolveTexture, viewportW, viewportH, S(RectTitleBox));
        int titleScale = _fontScale + 1;
        var (tx, ty, tw, th) = S(RectTitleText);
        int titleH = (text.HasFont ? text.Font!.Height : 12) * titleScale;
        int titleW = text.MeasureWidth("Journal", titleScale);
        text.DrawString(viewportW, viewportH, "Journal",
                        tx + (tw - titleW) / 2, ty + (th - titleH) / 2, white, titleScale);
    }

    // ─── draw helpers ─────────────────────────────────────────────────────

    static void DrawTex(IconRenderer icons, int vw, int vh, GlTexture tex,
                        (int X, int Y, int W, int H) r, Vector4 tint)
        => icons.DrawIcon(vw, vh, tex, r.X, r.Y, r.W, r.H, tint);

    /// <summary>common_template=jbox frame (NinePatch over the
    /// b_gui_cmn_jbox_* set), with a flat-rect fallback when art is out.</summary>
    static void Jbox(BarRenderer bars, IconRenderer? icons, Func<string, GlTexture?>? resolve,
                     int vw, int vh, (int X, int Y, int W, int H) r)
    {
        if (icons is not null && resolve is not null && resolve("b_gui_cmn_jbox_ul") is not null)
        {
            NinePatch.DrawJbox(icons, name => resolve("b_gui_cmn_" + name),
                               vw, vh, r.X, r.Y, r.W, r.H, new Vector4(1f, 1f, 1f, 1f));
            return;
        }
        bars.DrawRect  (vw, vh, r.X, r.Y, r.W, r.H, new Vector4(0.12f, 0.09f, 0.05f, 0.85f));
        bars.DrawBorder(vw, vh, r.X, r.Y, r.W, r.H, new Vector4(0.45f, 0.38f, 0.26f, 1f));
    }

    void DrawArrow(BarRenderer bars, IconRenderer? icons, Func<string, GlTexture?>? resolve,
                   int vw, int vh, (int X, int Y, int W, int H) r, string texName, Hit id)
    {
        // gas: onrollover setalpha(0.8), onlbuttondown setalpha(0.5).
        float alpha = _pressed == id ? 0.5f : _hover == id ? 0.8f : 1f;
        var tex = (icons is not null && resolve is not null) ? resolve(texName) : null;
        if (tex is not null && icons is not null)
        {
            var (u0, v0, u1, v1) = ArrowUv.Screen();
            icons.DrawIcon(vw, vh, tex, r.X, r.Y, r.W, r.H, new Vector4(1f, 1f, 1f, alpha), u0, v0, u1, v1);
            return;
        }
        bars.DrawRect  (vw, vh, r.X, r.Y, r.W, r.H, new Vector4(0.25f, 0.20f, 0.12f, alpha));
        bars.DrawBorder(vw, vh, r.X, r.Y, r.W, r.H, new Vector4(0.45f, 0.38f, 0.26f, alpha));
    }

    /// <summary>slider_button — b_gui_cmn_slider_jnl_top/_mid/_bot caps +
    /// stretched middle, positioned along the authored track rect
    /// proportional to the scroll window. Fills the track when everything
    /// fits (nothing to scroll).</summary>
    void DrawSliderThumb(BarRenderer bars, IconRenderer? icons, Func<string, GlTexture?>? resolve,
                         int vw, int vh, int total, int visible)
    {
        var (sx, sy, sw, sh) = S(RectSlider);
        int thumbH, thumbY;
        if (total <= visible)
        {
            thumbH = sh;
            thumbY = sy;
        }
        else
        {
            thumbH = System.Math.Max((int)MathF.Round(16 * _scale), sh * visible / total);
            int maxScroll = total - visible;
            thumbY = sy + (sh - thumbH) * _scroll / System.Math.Max(1, maxScroll);
        }
        var top = (icons is not null && resolve is not null) ? resolve("b_gui_cmn_slider_jnl_top") : null;
        var mid = (icons is not null && resolve is not null) ? resolve("b_gui_cmn_slider_jnl_mid") : null;
        var bot = (icons is not null && resolve is not null) ? resolve("b_gui_cmn_slider_jnl_bot") : null;
        if (top is not null && mid is not null && bot is not null && icons is not null)
        {
            var white = new Vector4(1f, 1f, 1f, 1f);
            int capH = System.Math.Min((int)MathF.Round(8 * _scale), thumbH / 2);
            icons.DrawIcon(vw, vh, top, sx, thumbY, sw, capH, white);
            icons.DrawIcon(vw, vh, mid, sx, thumbY + capH, sw, thumbH - capH * 2, white);
            icons.DrawIcon(vw, vh, bot, sx, thumbY + thumbH - capH, sw, capH, white);
            return;
        }
        bars.DrawRect(vw, vh, sx, thumbY, sw, thumbH, new Vector4(0.40f, 0.33f, 0.22f, 0.9f));
    }

    void DrawButton(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                    Func<string, GlTexture?>? resolve, int vw, int vh,
                    (int X, int Y, int W, int H) face,
                    string caption, Hit id, int fontH, Vector4 ink)
    {
        // gas button messages: onrollover vertexcolor(0xff999999),
        // onlbuttondown vertexcolor(0xff555555).
        var tint = _pressed == id ? new Vector4(0.333f, 0.333f, 0.333f, 1f)
                 : _hover   == id ? new Vector4(0.6f, 0.6f, 0.6f, 1f)
                 :                  new Vector4(1f, 1f, 1f, 1f);
        var fill = (icons is not null && resolve is not null) ? resolve("b_gui_cmn_jbox_fill") : null;
        if (fill is not null && icons is not null)
            icons.DrawIcon(vw, vh, fill, face.X, face.Y, face.W, face.H, tint);
        else
            bars.DrawRect(vw, vh, face.X, face.Y, face.W, face.H,
                          new Vector4(0.20f * tint.X, 0.16f * tint.Y, 0.10f * tint.Z, 1f));
        // Caption centered on the button FACE (the authored text sub-rect is
        // inset to the right; centering there shoved the label off-center).
        int cw = text.MeasureWidth(caption, _fontScale);
        text.DrawString(vw, vh, caption,
                        face.X + (face.W - cw) / 2, face.Y + (face.H - fontH) / 2, ink, _fontScale);
    }

    /// <summary>SC-QUEST-UI-D — textbox_dialogues. The selected quest's
    /// recorded conversation, word-wrapped and left-justified into the shared
    /// list rect (font_type b_gui_fnt_12p_copperplate-light, justify=left),
    /// scrolled by the shared slider. Returns (total lines, visible lines) so
    /// the caller can size the slider thumb.</summary>
    (int Total, int Visible) DrawDialogueView(BarRenderer bars, TextRenderer text,
        int vw, int vh, QuestEntry? sel, int lx, int ly, int lw, int lh,
        int fontH, Vector4 ink, Vector4 dimInk)
    {
        int pad   = System.Math.Max(2, (int)MathF.Round(3 * _scale));
        int lineH = fontH + 2 * _fontScale;
        int visible = System.Math.Max(1, lh / lineH);

        var lines = new List<string>();
        if (sel is not null && sel.DialogueLog.Count > 0)
        {
            for (int i = 0; i < sel.DialogueLog.Count; i++)
            {
                foreach (var wl in Wrap(sel.DialogueLog[i], text, lw - pad * 2, _fontScale))
                    lines.Add(wl);
                if (i < sel.DialogueLog.Count - 1) lines.Add(""); // blank between beats
            }
        }

        if (lines.Count == 0)
        {
            const string none = "No recorded conversation for this quest.";
            int nw = text.MeasureWidth(none, _fontScale);
            text.DrawString(vw, vh, none, lx + (lw - nw) / 2, ly + (lh - fontH) / 2, dimInk, _fontScale);
            return (0, visible);
        }

        _scroll = System.Math.Clamp(_scroll, 0, System.Math.Max(0, lines.Count - visible));
        for (int i = 0; i < visible; i++)
        {
            int idx = _scroll + i;
            if (idx >= lines.Count) break;
            if (lines[idx].Length == 0) continue;
            text.DrawString(vw, vh, lines[idx], lx + pad, ly + i * lineH, ink, _fontScale);
        }
        return (lines.Count, visible);
    }

    // ─── data helpers ─────────────────────────────────────────────────────

    /// <summary>Listbox order — active quests first, then completed/failed,
    /// each group in journal insertion order.</summary>
    static List<QuestEntry> BuildEntries(QuestJournal journal)
    {
        var list = new List<QuestEntry>();
        foreach (var e in journal.Entries)
            if (e.State == QuestState.Active) list.Add(e);
        foreach (var e in journal.Entries)
            if (e.State == QuestState.Completed || e.State == QuestState.Failed) list.Add(e);
        return list;
    }

    void EnsureSelection(List<QuestEntry> entries)
    {
        if (entries.Count == 0) { _selectedKey = null; return; }
        foreach (var e in entries)
            if (string.Equals(e.Key, _selectedKey, StringComparison.OrdinalIgnoreCase)) return;
        _selectedKey = entries[0].Key;
    }

    QuestEntry? SelectedEntry(List<QuestEntry> entries)
    {
        foreach (var e in entries)
            if (string.Equals(e.Key, _selectedKey, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    /// <summary>Normalize a journal key to its quests.gas row: lowercase,
    /// strip the SiegeFX ",N" stage suffix, then the "_mp" twin suffix
    /// (quests.gas points both twins at the same quest_image).</summary>
    static string? MapQuestImage(string key)
    {
        var k = key;
        int comma = k.IndexOf(',');
        if (comma >= 0) k = k[..comma];
        if (k.EndsWith("_mp", StringComparison.OrdinalIgnoreCase)) k = k[..^3];
        return QuestImages.TryGetValue(k, out var image) ? image : null;
    }

    /// <summary>Counter line for the desc box when the selected entry has a
    /// goal — kill first (matches the compass tracker's priority), then
    /// talk, then pickup. Null when the definition carries no counters.</summary>
    static string? ProgressLine(QuestEntry e)
    {
        var def = e.Definition;
        if (def is null) return null;
        if (def.KillCountGoal   > 0) return $"({e.KillProgress} / {def.KillCountGoal})";
        if (def.TalkCountGoal   > 0) return $"({e.TalkProgress} / {def.TalkCountGoal})";
        if (def.PickupCountGoal > 0) return $"({e.PickupProgress} / {def.PickupCountGoal})";
        return null;
    }

    static string FitToWidth(string s, TextRenderer text, int maxPx, int pixelScale)
    {
        if (text.MeasureWidth(s, pixelScale) <= maxPx) return s;
        while (s.Length > 1 && text.MeasureWidth(s + "..", pixelScale) > maxPx)
            s = s[..^1];
        return s + "..";
    }

    /// <summary>Word-wrap to a pixel width at the given font scale. Same
    /// shape as DialoguePanel's wrapper; falls back to the raw line when
    /// no font is loaded so the diagnostic case stays readable.</summary>
    static IEnumerable<string> Wrap(string raw, TextRenderer text, int maxWidthPx, int pixelScale)
    {
        if (string.IsNullOrEmpty(raw)) { yield return ""; yield break; }
        if (!text.HasFont) { yield return raw; yield break; }

        var words = raw.Split(' ');
        var line  = "";
        foreach (var w in words)
        {
            var probe = line.Length == 0 ? w : line + " " + w;
            if (text.MeasureWidth(probe, pixelScale) <= maxWidthPx) { line = probe; continue; }
            if (line.Length > 0) yield return line;
            line = w;
        }
        if (line.Length > 0) yield return line;
    }

    /// <summary>Strip the conventional <c>quest_</c> prefix and convert remaining
    /// underscores to spaces with a title-case hint on the first letter of each
    /// word. DS1 keys are lowercase-snake (<c>quest_edgaar_basement</c>); cheap
    /// transformation gets us a readable label for entries the catalog doesn't
    /// cover (the authored ScreenName wins whenever a definition is bound).</summary>
    static string Pretty(string key)
    {
        var s = key;
        if (s.StartsWith("quest_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
        else if (s.StartsWith("Quest_", StringComparison.Ordinal))      s = s.Substring(6);
        s = s.Replace('_', ' ').Trim();
        if (s.Length == 0) return key;
        var chars = s.ToCharArray();
        bool atStart = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (atStart && char.IsLetter(chars[i])) { chars[i] = char.ToUpperInvariant(chars[i]); atStart = false; }
            else if (chars[i] == ' ') atStart = true;
        }
        return new string(chars);
    }
}

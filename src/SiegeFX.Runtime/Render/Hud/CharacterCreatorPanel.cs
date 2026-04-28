using System.Numerics;
using SiegeFX.Runtime;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// 21d-2a-viii — pre-spawn character creator. Gates <c>TrySpawnPlayer</c>
/// until the player picks a variant + name and clicks "Begin".
///
/// <para>Layout is sourced from the shipped DS1
/// <c>/ui/interfaces/frontend/character_select/character_select.gas</c>
/// (extracted to <c>_scratch_charsel.gas</c> beside the repo). Reference
/// resolution is 800×600 — DS1's frontend coordinate space — and rects scale
/// to the live viewport without letterboxing. Slice viii-d closed the modal
/// backdrop, the wood/iron panel chrome, the side decoration pillars, the
/// stone preview backdrop, and the gas-authentic axis labels. Sprite-atlas
/// glyphs (◄►) and live character preview remain deferred — both need a
/// textured-quad renderer BarRenderer doesn't have yet (see
/// <c>project_siegefx_creator_polish_backlog.md</c>).</para>
///
/// <para>Six axes match DS1's gas-authored buttons. Gender toggles
/// farmboy/farmgirl. Head cycles pos_a1..pos_a7 (DS1 ships head + body
/// baked together). Face and Hair both walk the slot-0 skin atlas
/// (<c>b_c_gah_*_skin_NN.raw</c>) — Face at stride 1, Hair at coprime
/// stride 7 — because each shipped skin file bakes face + hair + arms
/// into one image. We tried runtime composition with the separate
/// <c>b_c_gah_*_hair_NNN.raw</c> overlay files; the result blurred face
/// details, so we walk shipped variants instead. Shirt and Pants follow
/// the same pattern on the slot-1 pants atlas (<c>b_c_pos_aN_NNN.raw</c>),
/// which packs shirt + pants into one image.</para>
/// </summary>
internal sealed class CharacterCreatorPanel
{
    public bool IsOpen { get; set; }
    public bool Confirmed { get; private set; }
    public bool Cancelled { get; private set; }

    /// <summary>Mutable picker state — buttons increment/decrement axes in place.
    /// Read by the host on <see cref="Confirmed"/> = true to drive
    /// <see cref="HeroVariantPicker.BuildOverride"/>.</summary>
    public HeroVariantPicker Picker { get; private set; } = new()
    {
        Gender = HeroGender.Boy,
        BodyTypeIdx = 0,        // pos_a1
        SkinSuffix = "01",
        HairSuffix = "001",
        ShirtIdx = 0,
        PantsSuffix = "001",
    };

    public string HeroName { get; private set; } = "";

    /// <summary>21d-2a-viii-FE-2 — listener rect in viewport pixels for the
    /// live hero preview. Hero preview drag/click hit-testing reads this so
    /// the preview rect's position stays in one source of truth (the gas
    /// reference rect above) while still scaling to the live viewport.</summary>
    public (int X, int Y, int W, int H) ListenerRectInViewport(int viewportW, int viewportH)
    {
        float sx = viewportW / 800f, sy = viewportH / 600f;
        return Scale(RectListener, sx, sy);
    }

    // 800×600 reference rects copied verbatim from character_select.gas.
    // (left, top, right, bottom). Note DS1 gas rects are exclusive-end; we
    // store as (x, y, w, h) below.
    private static readonly (int X, int Y, int W, int H) RectGenderL  = R(176, 204, 213, 224);
    private static readonly (int X, int Y, int W, int H) RectGenderR  = R(340, 204, 377, 224);
    private static readonly (int X, int Y, int W, int H) RectHeadL    = R(176, 255, 213, 275);
    private static readonly (int X, int Y, int W, int H) RectHeadR    = R(340, 255, 377, 275);
    private static readonly (int X, int Y, int W, int H) RectFaceL    = R(176, 306, 213, 326);
    private static readonly (int X, int Y, int W, int H) RectFaceR    = R(340, 305, 377, 325);
    private static readonly (int X, int Y, int W, int H) RectHairL    = R(176, 355, 213, 375);
    private static readonly (int X, int Y, int W, int H) RectHairR    = R(340, 355, 377, 375);
    private static readonly (int X, int Y, int W, int H) RectShirtL   = R(176, 403, 213, 423);
    private static readonly (int X, int Y, int W, int H) RectShirtR   = R(340, 403, 377, 423);
    private static readonly (int X, int Y, int W, int H) RectPantsL   = R(176, 453, 213, 473);
    private static readonly (int X, int Y, int W, int H) RectPantsR   = R(340, 453, 377, 473);
    // Nudged up from the gas-shipped rect (297,518,530,542) to align with the
    // visible Hero/Name plate mesh; mesh placement diverges from the gas
    // 800×600 reference because the chrome ASPs are placed by PRS, not by gas
    // pixel coords. Y top = 450 dialed in by eye against the rendered chrome.
    private static readonly (int X, int Y, int W, int H) RectName     = R(287, 462, 520, 486);
    private static readonly (int X, int Y, int W, int H) RectListener = R(408,  73, 649, 494);
    private static readonly (int X, int Y, int W, int H) RectPrev     = R(237, 575, 338, 595);
    private static readonly (int X, int Y, int W, int H) RectBegin    = R(461, 575, 562, 595);

    private static (int, int, int, int) R(int l, int t, int r, int b) => (l, t, r - l, b - t);

    // Buttons rebuilt each frame from the scaled rects. Cheap data widgets.
    private readonly MenuButton _genderL = new("\u25C0", 0, 0, 0, 0);
    private readonly MenuButton _genderR = new("\u25B6", 0, 0, 0, 0);
    private readonly MenuButton _headL   = new("\u25C0", 0, 0, 0, 0);
    private readonly MenuButton _headR   = new("\u25B6", 0, 0, 0, 0);
    private readonly MenuButton _faceL   = new("\u25C0", 0, 0, 0, 0);
    private readonly MenuButton _faceR   = new("\u25B6", 0, 0, 0, 0);
    private readonly MenuButton _hairL   = new("\u25C0", 0, 0, 0, 0);
    private readonly MenuButton _hairR   = new("\u25B6", 0, 0, 0, 0);
    private readonly MenuButton _shirtL  = new("\u25C0", 0, 0, 0, 0);
    private readonly MenuButton _shirtR  = new("\u25B6", 0, 0, 0, 0);
    private readonly MenuButton _pantsL  = new("\u25C0", 0, 0, 0, 0);
    private readonly MenuButton _pantsR  = new("\u25B6", 0, 0, 0, 0);
    private readonly MenuButton _begin   = new("Begin",  0, 0, 0, 0);
    private readonly MenuButton _prev    = new("Cancel", 0, 0, 0, 0);

    // Cycle bounds. Skin tones ship 1..32, hair atlases ship 1..29, pants
    // 1..41 per body type. We clamp generously and let the resolver report
    // MISS for out-of-range — better feedback than guessing per-body caps.
    private const int SkinMin = 1, SkinMax = 32;
    private const int HairMin = 1, HairMax = 29;
    private const int PantsMin = 1, PantsMax = 41;
    // Shirt is a stride into the pants atlas (DS1 packs shirt + pants into
    // one b_c_pos_aN_NNN file). Six shirt-stride positions × the 7-wide
    // coprime stride covers most of the pants range so the user sees
    // distinct shirt/pant combos without re-treading.
    private const int ShirtMax = 5;

    public void Reset()
    {
        IsOpen = true;
        Confirmed = false;
        Cancelled = false;
        Picker = new HeroVariantPicker
        {
            Gender = HeroGender.Boy,
            BodyTypeIdx = 0,
            SkinSuffix = "01",
            HairSuffix = "001",
            ShirtIdx = 0,
            PantsSuffix = "001",
        };
        HeroName = "";
    }

    private static (int X, int Y, int W, int H) Scale(
        (int X, int Y, int W, int H) r, float sx, float sy)
        => ((int)MathF.Round(r.X * sx),
            (int)MathF.Round(r.Y * sy),
            (int)MathF.Round(r.W * sx),
            (int)MathF.Round(r.H * sy));

    private void Layout(int viewportW, int viewportH,
        out (int X, int Y, int W, int H) listener,
        out (int X, int Y, int W, int H) name)
    {
        float sx = viewportW / 800f, sy = viewportH / 600f;
        void Apply(MenuButton b, (int X, int Y, int W, int H) src)
        {
            var (x, y, w, h) = Scale(src, sx, sy);
            b.X = x; b.Y = y; b.Width = w; b.Height = h;
        }
        Apply(_genderL, RectGenderL); Apply(_genderR, RectGenderR);
        Apply(_headL,   RectHeadL);   Apply(_headR,   RectHeadR);
        Apply(_faceL,   RectFaceL);   Apply(_faceR,   RectFaceR);
        Apply(_hairL,   RectHairL);   Apply(_hairR,   RectHairR);
        Apply(_shirtL,  RectShirtL);  Apply(_shirtR,  RectShirtR);
        Apply(_pantsL,  RectPantsL);  Apply(_pantsR,  RectPantsR);
        Apply(_begin,   RectBegin);   Apply(_prev,    RectPrev);
        listener = Scale(RectListener, sx, sy);
        name     = Scale(RectName, sx, sy);
    }

    public void OnMouseMove(int px, int py)
    {
        if (!IsOpen) return;
        _genderL.UpdateHover(px, py); _genderR.UpdateHover(px, py);
        _headL.UpdateHover(px, py);   _headR.UpdateHover(px, py);
        _faceL.UpdateHover(px, py);   _faceR.UpdateHover(px, py);
        _hairL.UpdateHover(px, py);   _hairR.UpdateHover(px, py);
        _shirtL.UpdateHover(px, py);  _shirtR.UpdateHover(px, py);
        _pantsL.UpdateHover(px, py);  _pantsR.UpdateHover(px, py);
        _begin.UpdateHover(px, py);   _prev.UpdateHover(px, py);
    }

    /// <summary>Returns true if the panel consumed the click (host suppresses
    /// world click-to-move while open).</summary>
    public bool OnMouseDown(int px, int py)
    {
        if (!IsOpen) return false;
        _genderL.TryPress(px, py); _genderR.TryPress(px, py);
        _headL.TryPress(px, py);   _headR.TryPress(px, py);
        _faceL.TryPress(px, py);   _faceR.TryPress(px, py);
        _hairL.TryPress(px, py);   _hairR.TryPress(px, py);
        _shirtL.TryPress(px, py);  _shirtR.TryPress(px, py);
        _pantsL.TryPress(px, py);  _pantsR.TryPress(px, py);
        _begin.TryPress(px, py);   _prev.TryPress(px, py);
        return true;
    }

    public bool OnMouseUp(int px, int py)
    {
        if (!IsOpen) return false;

        // Body axis (pos_aN) is the only "head" axis we wire to the resolver.
        if (_genderR.Release(px, py)) Picker = With(g: Toggle(Picker.Gender));
        else if (_genderL.Release(px, py)) Picker = With(g: Toggle(Picker.Gender));
        else if (_headR.Release(px, py)) Picker = With(b: Cycle(Picker.BodyTypeIdx, +1, 0, 6));
        else if (_headL.Release(px, py)) Picker = With(b: Cycle(Picker.BodyTypeIdx, -1, 0, 6));
        // Face cycles SkinSuffix — DS1's slot-0 texture (b_c_*_skin_NN) packs
        // face + hair + arms into one .raw, so "Face" is the most accurate
        // label for what changing this axis actually does.
        else if (_faceR.Release(px, py)) Picker = With(s: PadN(CycleStr(Picker.SkinSuffix, +1, SkinMin, SkinMax), 2));
        else if (_faceL.Release(px, py)) Picker = With(s: PadN(CycleStr(Picker.SkinSuffix, -1, SkinMin, SkinMax), 2));
        // Hair cycles HairSuffix → b_c_*_hair_NNN.raw, composed onto the
        // skin atlas at runtime by RenderHost.LoadComposedSkinHair so the
        // hero's hair colour changes independently of skin tone.
        else if (_hairR.Release(px, py)) Picker = With(hh: PadN(CycleStr(Picker.HairSuffix, +1, HairMin, HairMax), 3));
        else if (_hairL.Release(px, py)) Picker = With(hh: PadN(CycleStr(Picker.HairSuffix, -1, HairMin, HairMax), 3));
        // Shirt walks the pants atlas by a coprime stride (see picker.BuildOverride).
        else if (_shirtR.Release(px, py)) Picker = With(si: Cycle(Picker.ShirtIdx, +1, 0, ShirtMax));
        else if (_shirtL.Release(px, py)) Picker = With(si: Cycle(Picker.ShirtIdx, -1, 0, ShirtMax));
        else if (_pantsR.Release(px, py)) Picker = With(p: PadN(CycleStr(Picker.PantsSuffix, +1, PantsMin, PantsMax), 3));
        else if (_pantsL.Release(px, py)) Picker = With(p: PadN(CycleStr(Picker.PantsSuffix, -1, PantsMin, PantsMax), 3));
        else if (_begin.Release(px, py))
        {
            // Begin always proceeds; an empty name is allowed (host can fill in
            // a default later — viii-c persists the chosen name).
            Confirmed = true;
            IsOpen = false;
            return true;
        }
        else if (_prev.Release(px, py))
        {
            // Cancel = give up creator and fall through to env-var defaults.
            Cancelled = true;
            IsOpen = false;
            return true;
        }
        return true;
    }

    /// <summary>Append a printable char to the hero name, or backspace.
    /// Excluded chars match character_select.gas's edit_box rule
    /// (<c>excluded_chars = [["&lt;&gt;:/\|?*.%;]]</c>) and we cap at 14 like
    /// <c>max_string_size</c>.</summary>
    public void OnChar(char c)
    {
        if (!IsOpen) return;
        if (c == '\b')
        {
            if (HeroName.Length > 0) HeroName = HeroName[..^1];
            return;
        }
        if (c < ' ' || c > '~') return;
        if ("<>:/\\|?*.%;\"".IndexOf(c) >= 0) return;
        if (HeroName.Length >= 14) return;
        HeroName += c;
    }

    /// <summary>Functional overlay drawn ON TOP of the FrontendScene chrome.
    /// Renders the input controls (axis-cycling arrow buttons, name field
    /// outline + typed text, Previous / Begin) at their character_select.gas
    /// positions so the menu is clickable / typeable. No modal backdrop or
    /// wood-iron card — the chrome supplies that visual layer underneath.
    /// Pixel-aligned bone-driven button placement is a follow-up; until
    /// then the gas-authored 800×600 rects sit close enough that clicking
    /// the visible chrome buttons hits the right axis.</summary>
    public void DrawTextOverlay(BarRenderer bars, TextRenderer text,
        int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out var listener, out var name);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk = new Vector4(0.65f, 0.60f, 0.50f, 1f);
        float sx = viewportW / 800f;

        // Hero name — chrome ASP draws the Hero/Name plate; the original DS1
        // edit_box left-aligns text against the black middle's left edge rather
        // than centring. Match that: use centred-X minus 40 viewport px so the
        // typed text starts near the plate's inner left wall. Text colour for
        // this field is near-white (DS1 edit_box ink) rather than the cream
        // axis-row ink, so it reads cleanly on the dark plate centre.
        var shown = string.IsNullOrEmpty(HeroName) ? "Enter Name" : HeroName;
        var nameInk    = new Vector4(0.96f, 0.96f, 0.94f, 1f);
        var nameDimInk = new Vector4(0.72f, 0.72f, 0.70f, 1f);
        var color = string.IsNullOrEmpty(HeroName) ? nameDimInk : nameInk;
        int tw = text.MeasureWidth(shown);
        int th = text.HasFont ? text.Font!.Height : 14;
        text.DrawString(viewportW, viewportH, shown,
            name.X + Math.Max(0, (name.W - tw) / 2) - 40,
            name.Y + Math.Max(0, (name.H - th) / 2),
            color);

        // Axis rows — buttons + live state label between them. Face owns the
        // skin-texture cycle (face / hair / arms region); Hair and Shirt are
        // decorative-only counters because DS1 doesn't author separate hair
        // or shirt textures (see picker docs).
        DrawAxisRow(bars, text, viewportW, viewportH, _genderL, _genderR,
            $"Gender: {(Picker.Gender == HeroGender.Girl ? "Farmgirl" : "Farmboy")}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _headL, _headR,
            $"Head: pos_a{(Picker.BodyTypeIdx < 0 ? 1 : Picker.BodyTypeIdx + 1)}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _faceL, _faceR,
            $"Face: {Picker.SkinSuffix ?? "--"}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _hairL, _hairR,
            $"Hair: {Picker.HairSuffix ?? "---"}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _shirtL, _shirtR,
            $"Shirt: {Picker.ShirtIdx + 1}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _pantsL, _pantsR,
            $"Pants: {Picker.PantsSuffix ?? "---"}", ink);

        // Begin / Cancel rects are gas-positioned at 461,575 / 237,575 — exactly
        // where the chrome's backbutton mesh draws the Previous/Next graphics.
        // Don't draw our solid-colour fills on top; just text labels so the
        // user can tell which is which until proper bone-projected click rects
        // come online. Hit testing remains active via the rects in Layout().
        DrawCentered(text, viewportW, viewportH, _prev.Label,
            _prev.X, _prev.Y + (_prev.Height - (text.HasFont ? text.Font!.Height : 14)) / 2,
            _prev.Width, ink);
        DrawCentered(text, viewportW, viewportH, _begin.Label,
            _begin.X, _begin.Y + (_begin.Height - (text.HasFont ? text.Font!.Height : 14)) / 2,
            _begin.Width, ink);
    }

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out var listener, out var name);

        // Modal — fully opaque so the world doesn't bleed through. The DS1
        // creator runs fullscreen with no game behind it.
        var modalBg   = new Vector4(0.04f, 0.03f, 0.02f, 1f);
        // Wooden-frame palette (warm browns); iron bands on top/bottom.
        var wood      = new Vector4(0.18f, 0.13f, 0.08f, 1f);
        var woodLite  = new Vector4(0.30f, 0.22f, 0.13f, 1f);
        var iron      = new Vector4(0.22f, 0.20f, 0.18f, 1f);
        var ironHi    = new Vector4(0.42f, 0.38f, 0.34f, 1f);
        var border    = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var ink       = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk    = new Vector4(0.65f, 0.60f, 0.50f, 1f);
        var fieldFill = new Vector4(0.04f, 0.03f, 0.02f, 1f);
        // Stone backdrop in the preview rect — cold mottled grey so a
        // future live-render pass reads as a hero-against-stage.
        var stoneBack = new Vector4(0.16f, 0.16f, 0.18f, 1f);
        var stoneHi   = new Vector4(0.22f, 0.22f, 0.24f, 1f);

        // Item 1 — Modal backdrop. Fully opaque cover.
        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, modalBg);

        float sx = viewportW / 800f, sy = viewportH / 600f;

        // Item 2 — Wooden frame chrome with iron bands top + bottom.
        // The card spans the central column of the 800×600 reference layout.
        int cardX = (int)MathF.Round(150 * sx);
        int cardY = (int)MathF.Round( 30 * sy);
        int cardW = (int)MathF.Round(530 * sx);
        int cardH = (int)MathF.Round(560 * sy);

        // Outer wood plate, then a lighter inner bevel for depth.
        int woodPad = Math.Max(2, (int)MathF.Round(6 * sy));
        bars.DrawRect(viewportW, viewportH, cardX, cardY, cardW, cardH, wood);
        bars.DrawRect(viewportW, viewportH,
            cardX + woodPad, cardY + woodPad,
            cardW - woodPad * 2, cardH - woodPad * 2, woodLite);
        // Inner panel where the controls live (recessed look).
        int innerPad = Math.Max(4, (int)MathF.Round(14 * sy));
        bars.DrawRect(viewportW, viewportH,
            cardX + innerPad, cardY + innerPad,
            cardW - innerPad * 2, cardH - innerPad * 2, modalBg);
        bars.DrawBorder(viewportW, viewportH, cardX, cardY, cardW, cardH, border);

        // Iron bands top + bottom (DS1 chrome riff).
        int bandH = Math.Max(8, (int)MathF.Round(22 * sy));
        bars.DrawRect(viewportW, viewportH, cardX, cardY, cardW, bandH, iron);
        bars.DrawRect(viewportW, viewportH, cardX, cardY + 1, cardW, 2, ironHi);
        bars.DrawRect(viewportW, viewportH, cardX, cardY + cardH - bandH, cardW, bandH, iron);
        bars.DrawRect(viewportW, viewportH, cardX, cardY + cardH - 3, cardW, 2, ironHi);

        // Item 3 — Side decorations. Two vertical wood pillars flanking the
        // card, with iron rivets suggesting massive gear assemblies. We can't
        // bind the b_gui_heromenu sprite atlas without a textured-quad path
        // (BarRenderer is solid-color), so this is a stylised stand-in built
        // from solid bands rather than an authored sprite.
        int gearW = Math.Max(20, (int)MathF.Round(110 * sx));
        int gearMargin = (int)MathF.Round(20 * sx);
        DrawSidePillar(bars, viewportW, viewportH,
            cardX - gearMargin - gearW, cardY, gearW, cardH, wood, woodLite, iron, ironHi);
        DrawSidePillar(bars, viewportW, viewportH,
            cardX + cardW + gearMargin, cardY, gearW, cardH, wood, woodLite, iron, ironHi);

        // Item 4 — Stone backdrop in preview rect (replaces the prior empty
        // stage colour). Mottled grey + a brighter highlight band suggesting
        // ground plane. Live render of the picked variant is item 5 — that
        // requires offscreen FBO infra (see creator polish backlog).
        bars.DrawRect  (viewportW, viewportH, listener.X, listener.Y, listener.W, listener.H, stoneBack);
        // Crude horizon band 2/3 down the rect for visual interest.
        int horizonY = listener.Y + (int)(listener.H * 0.62f);
        int horizonH = Math.Max(2, (int)MathF.Round(3 * sy));
        bars.DrawRect(viewportW, viewportH, listener.X, horizonY, listener.W, horizonH, stoneHi);
        bars.DrawBorder(viewportW, viewportH, listener.X, listener.Y, listener.W, listener.H, border);
        // Placeholder line so the user knows what's going to live there once
        // the offscreen render lands.
        var phLabel = "(Hero Preview)";
        int phW = text.MeasureWidth(phLabel);
        int phH = text.HasFont ? text.Font!.Height : 14;
        text.DrawString(viewportW, viewportH, phLabel,
            listener.X + (listener.W - phW) / 2,
            listener.Y + (listener.H - phH) / 2, dimInk);

        // Name field
        bars.DrawRect  (viewportW, viewportH, name.X, name.Y, name.W, name.H, fieldFill);
        bars.DrawBorder(viewportW, viewportH, name.X, name.Y, name.W, name.H, border);

        // Title sits on the iron band.
        DrawCentered(text, viewportW, viewportH, "Choose Hero",
            cardX, cardY + Math.Max(2, bandH / 2 - (text.HasFont ? text.Font!.Height : 14) / 2),
            cardW, ink);

        // Item 6 — Axis labels match DS1's gas-authored button names
        // (button_gender / _head / _face / _hair / _shirt / _pants).
        // The underlying mutations are unchanged: head→pos_aN body, shirt→
        // skin/shirt texture (b_c_*_skin_NN). Face/hair stay decorative.
        DrawAxisRow(bars, text, viewportW, viewportH, _genderL, _genderR,
            $"Gender: {(Picker.Gender == HeroGender.Girl ? "Farmgirl" : "Farmboy")}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _headL, _headR,
            $"Head: pos_a{(Picker.BodyTypeIdx < 0 ? 1 : Picker.BodyTypeIdx + 1)}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _faceL, _faceR,
            $"Face: {Picker.SkinSuffix ?? "--"}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _hairL, _hairR,
            $"Hair: {Picker.HairSuffix ?? "---"}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _shirtL, _shirtR,
            $"Shirt: {Picker.ShirtIdx + 1}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _pantsL, _pantsR,
            $"Pants: {Picker.PantsSuffix ?? "---"}", ink);

        // Hero name (placeholder if empty).
        var shown = string.IsNullOrEmpty(HeroName) ? "<click & type a name>" : HeroName;
        var color = string.IsNullOrEmpty(HeroName) ? dimInk : ink;
        text.DrawString(viewportW, viewportH, shown,
            name.X + (int)(6 * sx),
            name.Y + Math.Max(0, (name.H - (text.HasFont ? text.Font!.Height : 14)) / 2),
            color);

        _begin.Draw(bars, text, viewportW, viewportH);
        _prev.Draw (bars, text, viewportW, viewportH);
    }

    /// <summary>Solid-quad rendering of a wooden side pillar with five iron
    /// "rivet" bands. Stand-in for the shipped gear sprite atlas (which would
    /// need a textured-quad render path BarRenderer doesn't have yet).</summary>
    private static void DrawSidePillar(BarRenderer bars, int vw, int vh,
        int x, int y, int w, int h,
        Vector4 wood, Vector4 woodLite, Vector4 iron, Vector4 ironHi)
    {
        if (w <= 0 || x < 0) return;
        bars.DrawRect(vw, vh, x, y, w, h, wood);
        int pad = Math.Max(2, w / 12);
        bars.DrawRect(vw, vh, x + pad, y + pad, w - pad * 2, h - pad * 2, woodLite);
        // Five evenly-spaced iron bands suggesting gear-axis rivets.
        int bandH = Math.Max(8, h / 18);
        int gap = (h - bandH * 5) / 6;
        for (int i = 0; i < 5; i++)
        {
            int by = y + gap + i * (bandH + gap);
            bars.DrawRect(vw, vh, x + pad, by, w - pad * 2, bandH, iron);
            bars.DrawRect(vw, vh, x + pad, by + 1, w - pad * 2, 2, ironHi);
        }
    }

    /// <summary>Draws the live spinner state label between the L/R click rects.
    /// The L/R rects themselves are NOT drawn here — the chrome ASPs (menubars
    /// / heromenu) supply the visible arrow graphics. Drawing solid-colour
    /// MenuButton rects on top hides the chrome and looks like an empty UI
    /// stacked over the real one. The buttons remain hit-testable; only
    /// their visual fill is suppressed in DrawTextOverlay mode.</summary>
    private static void DrawAxisRow(BarRenderer bars, TextRenderer text,
        int vw, int vh, MenuButton left, MenuButton right, string label, Vector4 ink)
    {
        int innerL = left.X + left.Width;
        int innerR = right.X;
        int lw = text.MeasureWidth(label);
        int lh = text.HasFont ? text.Font!.Height : 14;
        int lx = innerL + (innerR - innerL - lw) / 2;
        int ly = left.Y + (left.Height - lh) / 2;
        text.DrawString(vw, vh, label, lx, ly, ink);
    }

    private static void DrawCentered(TextRenderer text, int vw, int vh,
        string s, int x, int y, int w, Vector4 ink)
    {
        int sw = text.MeasureWidth(s);
        text.DrawString(vw, vh, s, x + (w - sw) / 2, y, ink);
    }

    // --- picker mutation helpers --------------------------------------------

    private static HeroGender Toggle(HeroGender g)
        => g == HeroGender.Boy ? HeroGender.Girl : HeroGender.Boy;

    private static int Cycle(int n, int delta, int min, int max)
    {
        int span = max - min + 1;
        int v = ((n - min + delta) % span + span) % span + min;
        return v;
    }

    private static int CycleStr(string? cur, int delta, int min, int max)
    {
        int n = min;
        if (!string.IsNullOrEmpty(cur)
            && int.TryParse(cur, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            n = parsed;
        return Cycle(n, delta, min, max);
    }

    private static string PadN(int n, int width)
        => n.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0');

    private HeroVariantPicker With(
        HeroGender? g = null, int? b = null, string? s = null,
        string? hh = null, int? si = null, string? p = null)
        => new()
        {
            Gender      = g ?? Picker.Gender,
            BodyTypeIdx = b ?? Picker.BodyTypeIdx,
            SkinSuffix  = s ?? Picker.SkinSuffix,
            HairSuffix  = hh ?? Picker.HairSuffix,
            ShirtIdx    = si ?? Picker.ShirtIdx,
            PantsSuffix = p ?? Picker.PantsSuffix,
        };
}

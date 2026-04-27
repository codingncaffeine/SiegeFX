using System.Numerics;
using SiegeFX.Runtime;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// 21d-2a-viii-b — pre-spawn character creator. Gates <c>TrySpawnPlayer</c>
/// until the player picks a variant + name and clicks "Begin".
///
/// <para>Layout is sourced from the shipped DS1
/// <c>/ui/interfaces/frontend/character_select/character_select.gas</c>
/// (extracted to <c>_scratch_charsel.gas</c> beside the repo). Reference
/// resolution is 800×600 — DS1's frontend coordinate space — and rects scale
/// to the live viewport without letterboxing. The panel does NOT yet bind the
/// shipped <c>b_gui_heromenu*</c> sprite-atlas cells to the 14 ◄► buttons:
/// that's a viii-c concern (the per-button sub-cell index lives in
/// <c>art_mapping.gas</c> and demands an atlas-sampling code path). For now
/// the buttons are framed rects with TextRenderer ◄► glyphs — gas-authored
/// rects are the load-bearing authenticity claim of this slice.</para>
///
/// <para>Six axes are exposed (gender, head, face, hair, shirt, pants) but
/// only gender/body/skin/pants reach <see cref="HeroVariantPicker"/> today —
/// head/face/hair stay decorative until viii-c proves they map to discrete
/// shipped meshes rather than baked variants of pos_aN. Body cycles the gas
/// "head" axis (the row that swaps the pos_aN mesh).</para>
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
        PantsSuffix = "001",
    };

    public string HeroName { get; private set; } = "";

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
    private static readonly (int X, int Y, int W, int H) RectName     = R(297, 518, 530, 542);
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

    // Cycle bounds. Skin tones ship 1..32, pants colors 1..41 per body type
    // (numbers vary; we clamp generously and let the resolver report MISS for
    // out-of-range — better feedback than guessing the per-body cap here).
    private const int SkinMin = 1, SkinMax = 32;
    private const int PantsMin = 1, PantsMax = 41;

    // Decorative-only axes (see class doc): cycle for visual feedback but no
    // resolver wiring yet.
    private int _faceIdx = 1, _hairIdx = 1;

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
            PantsSuffix = "001",
        };
        HeroName = "";
        _faceIdx = 1;
        _hairIdx = 1;
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
        else if (_faceR.Release(px, py)) _faceIdx = Cycle(_faceIdx, +1, 1, 8);
        else if (_faceL.Release(px, py)) _faceIdx = Cycle(_faceIdx, -1, 1, 8);
        else if (_hairR.Release(px, py)) _hairIdx = Cycle(_hairIdx, +1, 1, 8);
        else if (_hairL.Release(px, py)) _hairIdx = Cycle(_hairIdx, -1, 1, 8);
        else if (_shirtR.Release(px, py)) Picker = With(s: PadN(CycleStr(Picker.SkinSuffix, +1, SkinMin, SkinMax), 2));
        else if (_shirtL.Release(px, py)) Picker = With(s: PadN(CycleStr(Picker.SkinSuffix, -1, SkinMin, SkinMax), 2));
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

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out var listener, out var name);

        var dim    = new Vector4(0f, 0f, 0f, 0.70f);
        var panel  = new Vector4(0.10f, 0.08f, 0.06f, 0.95f);
        var border = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk = new Vector4(0.65f, 0.60f, 0.50f, 1f);
        var fieldFill = new Vector4(0.04f, 0.03f, 0.02f, 1f);
        var stage     = new Vector4(0.05f, 0.06f, 0.10f, 1f);

        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, dim);

        // Panel chrome — a single big card with a header. 800×600 base; the
        // shipped gas doesn't author an explicit panel rect, the menu sits on
        // the screen background. We frame the controls with a chrome card so
        // the buttons read as a unit on top of the world (the real DS1 menu
        // ships a fullscreen background plate; viii-c can swap that in).
        float sx = viewportW / 800f, sy = viewportH / 600f;
        int cardX = (int)MathF.Round(150 * sx);
        int cardY = (int)MathF.Round( 50 * sy);
        int cardW = (int)MathF.Round(530 * sx);
        int cardH = (int)MathF.Round(520 * sy);
        bars.DrawRect  (viewportW, viewportH, cardX, cardY, cardW, cardH, panel);
        bars.DrawBorder(viewportW, viewportH, cardX, cardY, cardW, cardH, border);

        // Listener frame (3D preview reservation — slice viii-c will mount a
        // live SkinnedMesh into this rect).
        bars.DrawRect  (viewportW, viewportH, listener.X, listener.Y, listener.W, listener.H, stage);
        bars.DrawBorder(viewportW, viewportH, listener.X, listener.Y, listener.W, listener.H, border);

        // Name field
        bars.DrawRect  (viewportW, viewportH, name.X, name.Y, name.W, name.H, fieldFill);
        bars.DrawBorder(viewportW, viewportH, name.X, name.Y, name.W, name.H, border);

        // Title + axis labels.
        DrawCentered(text, viewportW, viewportH, "Choose Hero",
            cardX, cardY + (int)(8 * sy), cardW, ink);

        DrawAxisRow(bars, text, viewportW, viewportH, _genderL, _genderR,
            $"Gender: {(Picker.Gender == HeroGender.Girl ? "Farmgirl" : "Farmboy")}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _headL, _headR,
            $"Body: pos_a{(Picker.BodyTypeIdx < 0 ? 1 : Picker.BodyTypeIdx + 1)}", ink);
        DrawAxisRow(bars, text, viewportW, viewportH, _faceL, _faceR,
            $"Face: {_faceIdx}", dimInk);
        DrawAxisRow(bars, text, viewportW, viewportH, _hairL, _hairR,
            $"Hair: {_hairIdx}", dimInk);
        DrawAxisRow(bars, text, viewportW, viewportH, _shirtL, _shirtR,
            $"Skin: {Picker.SkinSuffix ?? "--"}", ink);
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

    private static void DrawAxisRow(BarRenderer bars, TextRenderer text,
        int vw, int vh, MenuButton left, MenuButton right, string label, Vector4 ink)
    {
        left.Draw(bars, text, vw, vh);
        right.Draw(bars, text, vw, vh);
        // Label sits between the L/R buttons, centered between their inner edges.
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
        HeroGender? g = null, int? b = null, string? s = null, string? p = null)
        => new()
        {
            Gender      = g ?? Picker.Gender,
            BodyTypeIdx = b ?? Picker.BodyTypeIdx,
            SkinSuffix  = s ?? Picker.SkinSuffix,
            PantsSuffix = p ?? Picker.PantsSuffix,
        };
}

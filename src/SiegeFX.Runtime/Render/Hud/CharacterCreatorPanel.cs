using System.Numerics;
using SiegeFX.Runtime;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 29-CD-CREATOR — character creator hit-test + state panel.
/// Owns the 12 spinner-arrow click rects (gender / head / face / hair /
/// shirt / pants × left/right), the 3D-preview listener rect, the name
/// edit-box rect, the keyboard input for the name, and the cycling
/// logic for <see cref="HeroVariantPicker"/>.
///
/// <para>Does NOT render anything itself — visuals come from
/// <see cref="FrontendScene.DrawHeromenuButton"/> per-widget asp draws
/// driven by <c>art_mapping.gas</c>, plus <c>HeroPreviewRenderer</c>
/// for the 3D character. <c>Confirmed</c> / <c>Cancelled</c> flags are
/// set EXTERNALLY by the host's <c>CharacterSelectMenuPanel</c>
/// Next / Previous bottom-nav buttons (per <c>character_select.gas</c>
/// notify(on_change_map) / notify(back_to_single_player)).</para>
///
/// <para>Layout uses the same uniform-letterbox scale
/// (<c>MathF.Min(vh/600f, vw/800f)</c>) <see cref="SinglePlayerMenuPanel"/>
/// uses, so hit rects line up with the chrome rendered through
/// <see cref="FrontendScene.BuildSharedSceneModel"/> on widescreen
/// viewports. The pre-Phase-29 panel used independent X/Y scaling which
/// caused "click the visible chrome arrow doesn't fire."</para>
/// </summary>
internal sealed class CharacterCreatorPanel
{
    /// <summary>Phase 29-CD-CREATOR — one action per spinner click.
    /// Maps 1:1 to the <c>art_mapping.gas[button_*_left/right]</c>
    /// widgets and their corresponding <c>character_select.gas</c>
    /// notify names (<c>previous_character</c> / <c>next_character</c>
    /// / <c>char_prev_head</c> / <c>char_next_head</c> / etc).</summary>
    public enum Action
    {
        None,
        GenderLeft,  GenderRight,
        HeadLeft,    HeadRight,
        FaceLeft,    FaceRight,
        HairLeft,    HairRight,
        ShirtLeft,   ShirtRight,
        PantsLeft,   PantsRight,
    }

    public bool IsOpen { get; set; }

    /// <summary>Set by the host (via the CharacterSelect bottom-nav
    /// "Next" button per <c>character_select.gas</c>'s
    /// notify(on_change_map)). Drives <see cref="HeroVariantPicker.BuildOverride"/>
    /// + spawn.</summary>
    public bool Confirmed { get; set; }

    /// <summary>Set by the host (via the CharacterSelect bottom-nav
    /// "Previous" button per notify(back_to_single_player)). Falls
    /// through to env-var defaults.</summary>
    public bool Cancelled { get; set; }

    public HeroVariantPicker Picker { get; private set; } = new()
    {
        Gender = HeroGender.Boy,
        BodyTypeIdx = 0,
        SkinSuffix = "01",
        HairSuffix = "001",
        ShirtIdx = 0,
        PantsSuffix = "001",
    };

    public string HeroName { get; private set; } = "";

    // 800×600 reference rects. character_select.gas authors arrows as
    // 37×20 spinners 164px apart (X 176-213 / 340-377), but those rects
    // only cover the OUTER ARROW TIPS — the heromenu.asp L/R subsets
    // sample UVs that span "arrow tip + adjacent half of plate," so a
    // 37px-wide overlay leaves the plate UV pixels un-overlaid and the
    // bar reads as floating arrows with a gap. Widened each rect to
    // 100px so L and R meet at gas X ~277 — same row Y as gas, native
    // 20px height. Name plate bumped up 5px from gas (518 → 513).
    static readonly (Action Act, int X, int Y, int W, int H)[] Buttons =
    {
        // Per-row spinner widths span HALF the bar so L and R touch
        // in the middle, matching the chrome's authored layout. The
        // heromenu.asp L/R subsets sample UVs that include the arrow
        // tip AND the adjacent half of the plate, so each per-widget
        // overlay needs to be ~half the bar wide (~100px in 800x600
        // reference) to land its texture on the same area the chrome
        // bakes. trace-pose heromenu_begin@1.0 confirmed L mesh-X
        // [-0.971, -0.505] meets R mesh-X [-0.505, -0.036] at -0.505.
        // Gas hit rects (37px) only covered the outer tips and left
        // a visible gap where the plate-half pixels weren't being
        // overlaid, hence the "arrows separated" look.
        // 2px overlap at the L/R meeting edge so bilinear filtering at
        // the quad boundary doesn't produce a visible seam. L extends
        // to gas X 278; R starts at gas X 275; both 102 wide.
        (Action.GenderLeft,  176, 204, 102, 20),
        (Action.GenderRight, 275, 204, 102, 20),
        (Action.HeadLeft,    176, 255, 102, 20),
        (Action.HeadRight,   275, 255, 102, 20),
        (Action.FaceLeft,    176, 306, 102, 20),
        (Action.FaceRight,   275, 306, 102, 20),
        (Action.HairLeft,    176, 355, 102, 20),
        (Action.HairRight,   275, 355, 102, 20),
        (Action.ShirtLeft,   176, 403, 102, 20),
        (Action.ShirtRight,  275, 403, 102, 20),
        (Action.PantsLeft,   176, 453, 102, 20),
        (Action.PantsRight,  275, 453, 102, 20),
    };
    // Listener (3D preview) — character_select.gas[t:listener,n:listener] rect 408,73,649,494.
    const int ListenerGasX = 408, ListenerGasY = 73, ListenerGasW = 649 - 408, ListenerGasH = 494 - 73;
    // Name edit box — gas authors 297,518,530,542; bumped up 5px so the
    // typed text sits a hair higher above the prev/next pair.
    const int NameGasX = 297, NameGasY = 513, NameGasW = 530 - 297, NameGasH = 542 - 518;

    Action _hovered = Action.None;
    Action _pressed = Action.None;
    int _fontScale = 1;
    (int X, int Y, int W, int H)[] _rects = new (int, int, int, int)[Buttons.Length];
    (int X, int Y, int W, int H) _listenerRect;
    (int X, int Y, int W, int H) _nameRect;

    public int FontScale => _fontScale;

    // Cycle bounds. Skin atlases ship 1..32, hair 1..29, pants 1..41.
    const int SkinMin = 1, SkinMax = 32;
    const int HairMin = 1, HairMax = 29;
    const int PantsMin = 1, PantsMax = 41;
    // Shirt is a stride into the pants atlas (DS1 packs shirt+pants into
    // one b_c_pos_aN_NNN file). 6 stride positions × the 7-wide coprime
    // fold covers most of the pants range without re-treading.
    const int ShirtMax = 5;

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
        _hovered = Action.None;
        _pressed = Action.None;
    }

    public void ClearHover() => _hovered = Action.None;

    void Layout(int viewportW, int viewportH)
    {
        // Same uniform-letterbox scale as SinglePlayerMenuPanel /
        // CharacterSelectMenuPanel — matches the chrome rendered
        // through FrontendScene.BuildSharedSceneModel.
        float scale = MathF.Min(viewportH / 600f, viewportW / 800f);
        int authoredW = (int)MathF.Round(800 * scale);
        int authoredH = (int)MathF.Round(600 * scale);
        int dx = (viewportW - authoredW) / 2;
        int dy = (viewportH - authoredH) / 2;
        _fontScale = Math.Max(1, (int)MathF.Round(scale));
        for (int i = 0; i < Buttons.Length; i++)
        {
            var b = Buttons[i];
            _rects[i] = (
                dx + (int)MathF.Round(b.X * scale),
                dy + (int)MathF.Round(b.Y * scale),
                (int)MathF.Round(b.W * scale),
                (int)MathF.Round(b.H * scale));
        }
        _listenerRect = (
            dx + (int)MathF.Round(ListenerGasX * scale),
            dy + (int)MathF.Round(ListenerGasY * scale),
            (int)MathF.Round(ListenerGasW * scale),
            (int)MathF.Round(ListenerGasH * scale));
        _nameRect = (
            dx + (int)MathF.Round(NameGasX * scale),
            dy + (int)MathF.Round(NameGasY * scale),
            (int)MathF.Round(NameGasW * scale),
            (int)MathF.Round(NameGasH * scale));
    }

    /// <summary>21d-2a-viii-FE-2 — listener rect in viewport pixels for
    /// the live hero preview. <see cref="HeroPreviewRenderer"/> reads
    /// this so the preview rect's position is sourced from one place.</summary>
    public (int X, int Y, int W, int H) ListenerRectInViewport(int viewportW, int viewportH)
    {
        Layout(viewportW, viewportH);
        return _listenerRect;
    }

    /// <summary>Phase 29-CD-CREATOR — name edit-box rect in viewport
    /// pixels. Host renders the prompt sprite + typed text through
    /// TextRenderer at this rect.</summary>
    public (int X, int Y, int W, int H) NameRectInViewport(int viewportW, int viewportH)
    {
        Layout(viewportW, viewportH);
        return _nameRect;
    }

    static bool Hits((int X, int Y, int W, int H) r, int px, int py) =>
        px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;

    public void OnMouseMove(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH);
        _hovered = HitTest(px, py);
    }

    /// <summary>Returns true if the panel's hit rects consumed the
    /// click (host suppresses world click-to-move while open).</summary>
    public bool OnMouseDown(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return false;
        Layout(viewportW, viewportH);
        _pressed = HitTest(px, py);
        return _pressed != Action.None;
    }

    public bool OnMouseUp(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return false;
        Layout(viewportW, viewportH);
        var up = HitTest(px, py);
        bool consumed = false;
        if (up != Action.None && up == _pressed)
        {
            ApplyCycle(up);
            consumed = true;
        }
        _pressed = Action.None;
        return consumed;
    }

    /// <summary>Phase 29-CD-CREATOR — per-button rect + state for the
    /// host's <see cref="FrontendScene.DrawHeromenuButton"/> call +
    /// hover overlays.</summary>
    public bool TryGetButtonStateAndRect(Action act, int viewportW, int viewportH,
                                         out int x, out int y, out int w, out int h,
                                         out bool hovered, out bool pressed)
    {
        Layout(viewportW, viewportH);
        for (int i = 0; i < Buttons.Length; i++)
        {
            if (Buttons[i].Act != act) continue;
            x = _rects[i].X; y = _rects[i].Y; w = _rects[i].W; h = _rects[i].H;
            hovered = _hovered == act;
            pressed = _pressed == act;
            return true;
        }
        x = y = w = h = 0;
        hovered = pressed = false;
        return false;
    }

    Action HitTest(int px, int py)
    {
        for (int i = 0; i < Buttons.Length; i++)
            if (Hits(_rects[i], px, py)) return Buttons[i].Act;
        return Action.None;
    }

    /// <summary>Phase 29-CD-CREATOR — apply the cycle delta for a
    /// spinner click. Called from <see cref="OnMouseUp"/> when the
    /// release lands on the same rect the press did.</summary>
    void ApplyCycle(Action act)
    {
        // SC-CD-COMPOSITE — each lever now drives its real, label-matching axis
        // (Head=hairstyle, Hair=color, Face=skin, Shirt/Pants=clothing). Indices
        // cycle unbounded; the renderer wraps each by the shipped-variant count.
        switch (act)
        {
            case Action.GenderLeft:
            case Action.GenderRight:
                Picker = With(g: Toggle(Picker.Gender));
                break;
            case Action.HeadLeft:   Picker = With(style: Picker.StyleIdx - 1); break;
            case Action.HeadRight:  Picker = With(style: Picker.StyleIdx + 1); break;
            case Action.FaceLeft:   Picker = With(face:  Picker.FaceIdx  - 1); break;
            case Action.FaceRight:  Picker = With(face:  Picker.FaceIdx  + 1); break;
            case Action.HairLeft:   Picker = With(color: Picker.ColorIdx - 1); break;
            case Action.HairRight:  Picker = With(color: Picker.ColorIdx + 1); break;
            case Action.ShirtLeft:  Picker = With(shirt: Picker.ShirtIdx - 1); break;
            case Action.ShirtRight: Picker = With(shirt: Picker.ShirtIdx + 1); break;
            case Action.PantsLeft:  Picker = With(pants: Picker.PantsIdx - 1); break;
            case Action.PantsRight: Picker = With(pants: Picker.PantsIdx + 1); break;
        }
    }

    /// <summary>Append a printable char to the hero name, or backspace.
    /// Excluded chars match character_select.gas's edit_box rule
    /// (<c>excluded_chars = [["&lt;&gt;:/\|?*.%;]]</c>) and we cap at
    /// 14 like <c>max_string_size</c>.</summary>
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

    // --- picker mutation helpers --------------------------------------------

    static HeroGender Toggle(HeroGender g)
        => g == HeroGender.Boy ? HeroGender.Girl : HeroGender.Boy;

    static int Cycle(int n, int delta, int min, int max)
    {
        int span = max - min + 1;
        int v = ((n - min + delta) % span + span) % span + min;
        return v;
    }

    static int CycleStr(string? cur, int delta, int min, int max)
    {
        int n = min;
        if (!string.IsNullOrEmpty(cur)
            && int.TryParse(cur, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            n = parsed;
        return Cycle(n, delta, min, max);
    }

    static string PadN(int n, int width)
        => n.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0');

    HeroVariantPicker With(
        HeroGender? g = null, int? style = null, int? color = null,
        int? face = null, int? shirt = null, int? pants = null)
        => new()
        {
            Gender      = g ?? Picker.Gender,
            // Suffix fields are preserved (they feed the default mesh/spawn
            // plumbing); the levers now drive the index axes below.
            BodyTypeIdx = Picker.BodyTypeIdx,
            SkinSuffix  = Picker.SkinSuffix,
            HairSuffix  = Picker.HairSuffix,
            PantsSuffix = Picker.PantsSuffix,
            StyleIdx    = style ?? Picker.StyleIdx,
            ColorIdx    = color ?? Picker.ColorIdx,
            FaceIdx     = face  ?? Picker.FaceIdx,
            ShirtIdx    = shirt ?? Picker.ShirtIdx,
            PantsIdx    = pants ?? Picker.PantsIdx,
        };
}

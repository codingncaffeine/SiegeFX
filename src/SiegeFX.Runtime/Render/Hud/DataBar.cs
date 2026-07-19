using System;
using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 22-A SC-HUD-DATABAR — DS1's always-on bottom-row HUD button strip.
///
/// Source: <c>/ui/interfaces/backend/data_bar/data_bar.gas</c> (extracted as
/// <c>hud_data_bar.gas</c>; see <c>research_ds1_hud_authoritative.md</c>).
/// Reference resolution is 640×480; every rect below is in that space and
/// scales linearly to viewport height. Right-anchored buttons keep their
/// distance from the right edge in widescreen.
///
/// The widget is "pure data" — it owns rect math, hover/press state, and
/// renders itself. The host (RenderHost) owns the textures, dispatches
/// clicks, and drives the pause/play / labels toggle externally.
/// </summary>
public sealed class DataBar
{
    /// <summary>DS1 reference resolution; all rects in this widget are
    /// authored against 640×480.</summary>
    public const int RefW = 640;
    public const int RefH = 480;

    /// <summary>Stable button id; mapped to a notify-string in RenderHost's
    /// dispatch. Order matches data_bar.gas authoring order.</summary>
    public enum ButtonId
    {
        Pause,           // pause/play swap-pair at rect 10,441,37,469
        HealthPotion,    // 45,439,67,471
        ManaPotion,      // 71,439,93,471
        Labels,          // 507,439,539,471 (right-anchored)
        MegaMap,         // 541,439,568,470 (right-anchored)
        QuestLog,        // 575,439,603,471 (right-anchored)
        Menu,            // 611,439,635,471 (right-anchored, "door" icon)
    }

    /// <summary>One button slot — its 640×480-ref rect plus authoring metadata.
    /// `RightAnchorPx` is the gas's `right_anchor=N` value (distance from the
    /// right edge of the reference frame); when nonzero, the slot's X is
    /// recomputed against the viewport width on draw so widescreen layouts
    /// keep the icon glued to the right side of the screen instead of
    /// floating in the middle of a stretched dockbar.
    /// <para>UV-crop fields come straight from data_bar.gas's `uvcoords`
    /// attribute. DS1 ships every HUD icon as a 32×32 RAW with transparent
    /// padding on one or two edges and crops to the visible artwork via
    /// uvcoords; rendering with default (0..1, 0..1) UVs stretches the
    /// padded edges INTO the visible rect and makes the icon look squashed
    /// or stretched. Default (0,0,1,1) when the gas authors no crop.</para></summary>
    public readonly struct Slot
    {
        public readonly ButtonId Id;
        public readonly int X, Y, W, H;       // 640×480 reference rect
        public readonly int RightAnchorPx;    // 0 = anchor to left edge
        public readonly float U0, V0, U1, V1; // texture UV crop, gas uvcoords

        public Slot(ButtonId id, int x, int y, int w, int h,
                    int rightAnchor = 0,
                    float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
        {
            Id = id; X = x; Y = y; W = w; H = h; RightAnchorPx = rightAnchor;
            U0 = u0; V0 = v0; U1 = u1; V1 = v1;
        }
    }

    static readonly Slot[] _slots =
    {
        // Left edge — pause/play and the two potion quick-slots. UVs from
        // data_bar.gas: pause/play uvcoords=0,0.125,0.84375,1; potions
        // uvcoords=0,0,0.6875,1. Both pause and play share rect 10,441,37,469
        // per gas (only one visible at a time based on _isPaused).
        new(ButtonId.Pause,        10, 441, 27, 28, rightAnchor: 0,
            u0: 0f, v0: 0.125f, u1: 0.84375f, v1: 1f),
        new(ButtonId.HealthPotion, 45, 439, 22, 32, rightAnchor: 0,
            u0: 0f, v0: 0f, u1: 0.6875f, v1: 1f),
        new(ButtonId.ManaPotion,   71, 439, 22, 32, rightAnchor: 0,
            u0: 0f, v0: 0f, u1: 0.6875f, v1: 1f),
        // Right edge — gas authors these with right_anchor=N. We carry the
        // anchor value so Draw repositions against the viewport's right
        // edge instead of the 640-ref left edge.
        // window_labels uvcoords=0,0,1,1 (no crop authored).
        new(ButtonId.Labels,   507, 439, 32, 32, rightAnchor: 133,
            u0: 0f, v0: 0f, u1: 1f, v1: 1f),
        // button_mega_map uvcoords=0,0.03125,0.84375,1.
        new(ButtonId.MegaMap,  541, 439, 27, 31, rightAnchor: 99,
            u0: 0f, v0: 0.03125f, u1: 0.84375f, v1: 1f),
        // button_quest_log uvcoords=0,0,0.875,1.
        new(ButtonId.QuestLog, 575, 439, 28, 32, rightAnchor: 65,
            u0: 0f, v0: 0f, u1: 0.875f, v1: 1f),
        // button_menu uvcoords=0,0,0.75,1.
        new(ButtonId.Menu,     611, 439, 24, 32, rightAnchor: 29,
            u0: 0f, v0: 0f, u1: 0.75f, v1: 1f),
    };

    public static System.Collections.Generic.IReadOnlyList<Slot> Slots => _slots;

    // Dockbar background — rect 0,449,640,480 stretched across the full
    // viewport. b_gui_ig_mnu_statusbar is the chrome texture.
    public const int BgY = 449;
    public const int BgH = 31;

    /// <summary>Per-button hover state for highlight rendering and tooltip
    /// dispatch. Updated by <see cref="UpdateHover"/>; consumed by Draw.</summary>
    bool[] _hover = new bool[Enum.GetValues<ButtonId>().Length];

    /// <summary>Per-button press state. Set by <see cref="MouseDown"/>,
    /// cleared by <see cref="MouseUp"/>. A click is "press + release inside
    /// the same rect" — matches DS1's UI feel and the existing MenuButton
    /// convention.</summary>
    bool[] _pressed = new bool[Enum.GetValues<ButtonId>().Length];

    /// <summary>Map a 640×480 reference X to viewport X, honoring right-anchor.
    /// Vertical math always scales by viewport height — bottom anchoring is
    /// implicit because every button's Y is in the lower band of the frame.</summary>
    /// <summary>SC-DATABAR-DOCK (blindspot E8) — the manual: the status bar
    /// sits at the bottom by default and can be dragged to the TOP. When
    /// docked top, the authored bottom band (449..480) mirrors to y=0.</summary>
    public static bool DockTop;

    public static (int X, int Y, int W, int H) ProjectRect(in Slot slot, int viewportW, int viewportH)
    {
        // ALPHA-2V RE-BASELINE — element SIZE follows the shared HUD
        // baseline (+ UI-scale knob); ANCHORING stays window-relative.
        // The bar is authored in the bottom band (Y 439..471 of 480), so
        // Y projects from the BOTTOM edge — scaling the absolute Y by a
        // clamped factor would strand the bar mid-screen.
        float scale = HudScale.Hud(viewportH);
        int w = (int)Math.Round(slot.W * scale);
        int h = (int)Math.Round(slot.H * scale);
        int y = DockTop
            ? (int)Math.Round((slot.Y - BgY) * scale)
            : viewportH - (int)Math.Round((RefH - slot.Y) * scale);
        int x;
        if (slot.RightAnchorPx > 0)
        {
            // gas's right_anchor=N is the distance from the right edge in
            // ref-space to the LEFT edge of the rect. Scale and subtract
            // from viewportW so widescreen keeps the icon glued.
            int anchorPx = (int)Math.Round(slot.RightAnchorPx * scale);
            x = viewportW - anchorPx;
        }
        else
        {
            x = (int)Math.Round(slot.X * scale);
        }
        return (x, y, w, h);
    }

    /// <summary>Project the dockbar bg rect (full-width band along the bottom
    /// of the viewport). Stretches X across the whole viewport since the gas
    /// authors stretch_x=true on the dockbar.</summary>
    public static (int X, int Y, int W, int H) ProjectBgRect(int viewportW, int viewportH)
    {
        // Bottom-anchored like ProjectRect (band authored 449..480);
        // SC-DATABAR-DOCK mirrors the band to the top edge when docked.
        float scale = HudScale.Hud(viewportH);
        int h = (int)Math.Round(BgH * scale);
        int y = DockTop ? 0 : viewportH - (int)Math.Round((RefH - BgY) * scale);
        return (0, y, viewportW, h);
    }

    public bool HitTest(in Slot slot, int viewportW, int viewportH, int px, int py)
    {
        var (x, y, w, h) = ProjectRect(slot, viewportW, viewportH);
        return px >= x && px < x + w && py >= y && py < y + h;
    }

    /// <summary>Update hover flags from the latest cursor position. Call from
    /// the per-frame mouse-move tick. Returns the hovered button, or null
    /// when the cursor is outside every slot.</summary>
    public ButtonId? UpdateHover(int viewportW, int viewportH, int px, int py)
    {
        ButtonId? hit = null;
        for (int i = 0; i < _slots.Length; i++)
        {
            bool h = HitTest(_slots[i], viewportW, viewportH, px, py);
            _hover[(int)_slots[i].Id] = h;
            if (h) hit = _slots[i].Id;
        }
        return hit;
    }

    public bool IsHover(ButtonId id) => _hover[(int)id];
    public bool IsPressed(ButtonId id) => _pressed[(int)id];

    /// <summary>Currently hovered button id, or null when the cursor
    /// isn't over any data-bar slot. Drives the rollover_help tooltip
    /// the host draws at gas rect 95,450,501,479 (data_bar.gas:273
    /// text_box_info).</summary>
    public ButtonId? CurrentHover
    {
        get
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_hover[(int)_slots[i].Id]) return _slots[i].Id;
            return null;
        }
    }

    /// <summary>DS1 rollover_help text for a data-bar button. Strings
    /// are the canonical in-game tooltip text (sourced from the DS1
    /// in-game help system + Sybex strategy guide button references).
    /// The Labels button has two variants depending on toggle state:
    /// labels-on → "Hide..."; labels-off → "Show..." (gas:316/329
    /// dynamically swaps via setrolloverhelp).</summary>
    public static string TooltipFor(ButtonId id, bool isPaused = false, bool labelsOn = true)
        => id switch
        {
            ButtonId.Pause        => isPaused ? "Play (Hotkey: Space)" : "Pause (Hotkey: Space)",
            ButtonId.HealthPotion => "Drink Health Potion (Hotkey: \\)",
            ButtonId.ManaPotion   => "Drink Mana Potion (Hotkey: ;)",
            ButtonId.Labels       => labelsOn
                ? "Hide labels for items on the ground (Hotkey: Alt)"
                : "Show labels for items on the ground (Hotkey: Alt)",
            ButtonId.MegaMap      => "World Map (Hotkey: Tab)",
            ButtonId.QuestLog     => "Quest Log (Hotkey: L)",
            ButtonId.Menu         => "Menu (Hotkey: Esc)",
            _ => "",
        };

    /// <summary>Mouse-down inside a slot starts a press. Returns the
    /// button-id captured, or null when the click missed every slot. Caller
    /// should consume LMB upstream when this returns non-null.</summary>
    public ButtonId? MouseDown(int viewportW, int viewportH, int px, int py)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (!HitTest(_slots[i], viewportW, viewportH, px, py)) continue;
            _pressed[(int)_slots[i].Id] = true;
            return _slots[i].Id;
        }
        return null;
    }

    /// <summary>Mouse-up resolves a press into a click. Returns the button-id
    /// of the click when release lands inside the same rect that started
    /// the press; otherwise null (drag-off cancels). Always clears all
    /// press flags on release.</summary>
    public ButtonId? MouseUp(int viewportW, int viewportH, int px, int py)
    {
        ButtonId? clicked = null;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (!_pressed[(int)_slots[i].Id]) continue;
            _pressed[(int)_slots[i].Id] = false;
            if (HitTest(_slots[i], viewportW, viewportH, px, py))
                clicked = _slots[i].Id;
        }
        return clicked;
    }

    /// <summary>Cancel all in-flight presses (e.g. a modal opened mid-press).</summary>
    public void CancelPress()
    {
        for (int i = 0; i < _pressed.Length; i++) _pressed[i] = false;
    }
}

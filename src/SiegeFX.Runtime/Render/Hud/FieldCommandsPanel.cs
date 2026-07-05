using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 27 — DS1's Field Commands panel
/// (<c>/ui/interfaces/backend/field_commands/field_commands.gas</c>): the
/// bottom-right party-command cluster. Six formation radios (radio_group
/// radio_formations), three order rows (movement / attack / targeting) — each a
/// button_4 label showing the active order plus a three-crystal radio group —
/// select-all, disband, a follow toggle, the collect-loot / chat / minimize
/// utility row, and the right-edge collapse tab. Rects are the authored 640×480
/// reference, right-anchored and scaled by viewportH/480 (matches the other HUD
/// panels). Order labels come from the active radio in each group; the DS1
/// defaults are Engage / Defend / Target Closest.
///
/// Two independent collapse controls, matching DS1:
///  • the tall right-edge tab (field_commands_min/max) folds only the command
///    controls — the order rows and formation radios;
///  • the small minimize button (minimize_fc/maximize_fc) minimizes the whole
///    panel down to just the collect-loot bag beside it.
/// </summary>
public sealed class FieldCommandsPanel
{
    public const int RefRes = 480;
    public static float Scale(int viewportH) => viewportH / (float)RefRes;

    public enum Action
    {
        None,
        FormRow, FormDoubleRow, FormColumn, FormDoubleColumn, FormPyramid, FormCircle,
        MoveFree, MoveEngage, MoveHoldGround,
        AtkFree, AtkFightback, AtkHoldFire,
        TgtClosest, TgtWeakest, TgtStrongest,
        SelectAll, Disband, ToggleFollow, CollectLoot, Chat,
        CycleMovement, CycleAttack, CycleTargeting,
        MinimizeFc, CollapseFc,
    }

    // Authored rects (x0,y0,x1,y1) in the 640×480 reference.
    static readonly (Action A, (int, int, int, int) R)[] Formations =
    {
        (Action.FormRow,          (428, 401, 453, 425)),
        (Action.FormDoubleRow,    (452, 401, 477, 425)),
        (Action.FormColumn,       (475, 401, 500, 425)),
        (Action.FormDoubleColumn, (499, 401, 524, 425)),
        (Action.FormPyramid,      (523, 401, 548, 425)),
        (Action.FormCircle,       (547, 401, 572, 425)),
    };
    // Three crystal-radio order rows. Each option is a 12×13 crystal; the gas
    // orders them left→right as authored below.
    static readonly (Action A, (int, int, int, int) R)[] Movement =
    {
        (Action.MoveFree,       (574, 354, 586, 367)),
        (Action.MoveEngage,     (587, 354, 599, 367)),
        (Action.MoveHoldGround, (600, 354, 612, 367)),
    };
    static readonly (Action A, (int, int, int, int) R)[] Attack =
    {
        (Action.AtkFree,      (574, 370, 586, 383)),
        (Action.AtkFightback, (587, 370, 599, 383)),
        (Action.AtkHoldFire,  (600, 370, 612, 383)),
    };
    // gas: closest | weakest | strongest (was previously mis-ordered).
    static readonly (Action A, (int, int, int, int) R)[] Targeting =
    {
        (Action.TgtClosest,   (574, 386, 586, 399)),
        (Action.TgtWeakest,   (587, 386, 599, 399)),
        (Action.TgtStrongest, (600, 386, 612, 399)),
    };

    // Order-label buttons (button_4 chrome). The chrome stops just short of the
    // crystals at x574 so they read as a separate group.
    static readonly (Action Cycle, (int, int, int, int) R)[] OrderButtons =
    {
        (Action.CycleMovement,  (428, 353, 573, 369)),
        (Action.CycleAttack,    (428, 369, 573, 385)),
        (Action.CycleTargeting, (428, 385, 573, 401)),
    };

    // The collect-loot bag and the small minimize/maximize button share the
    // bottom-right corner; when the panel is minimized only these two survive.
    static readonly (int, int, int, int) LootBag   = (571, 425, 603, 445);
    static readonly (int, int, int, int) MiniButton = (602, 425, 635, 445);
    // Right-edge command-fold tab (fieldcom_r expanded → fieldcom_l folded).
    static readonly (int, int, int, int) CollapseTab = (615, 351, 636, 426);

    // Icon buttons blitted straight from their b_gui_* raws (always-on set).
    static readonly (Action A, (int, int, int, int) R)[] TopIcons =
    {
        (Action.ToggleFollow, (519, 284, 563, 308)),
        (Action.SelectAll,    (571, 332, 603, 352)),
        (Action.Disband,      (602, 332, 635, 353)),
    };

    public readonly record struct State(
        Action Formation, Action Movement, Action Attack, Action Targeting,
        bool Follow, bool CommandsCollapsed, bool Minimized);

    static (int x, int y, int w, int h) Px((int x0, int y0, int x1, int y1) r, float s, int originX)
        => (originX + (int)MathF.Round(r.x0 * s), (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));

    static bool In((int x, int y, int w, int h) p, int px, int py)
        => px >= p.x && px < p.x + p.w && py >= p.y && py < p.y + p.h;

    public Action HitTest(int px, int py, int viewportW, int viewportH,
                          bool minimized, bool commandsCollapsed)
    {
        float s = Scale(viewportH);
        int originX = viewportW - (int)MathF.Round(640f * s);

        // Minimized: only the loot bag and the maximize button are live.
        if (minimized)
        {
            if (In(Px(LootBag, s, originX), px, py))    return Action.CollectLoot;
            if (In(Px(MiniButton, s, originX), px, py)) return Action.MinimizeFc;
            return Action.None;
        }

        // Command-fold tab and the small minimize button are always live.
        if (In(Px(CollapseTab, s, originX), px, py)) return Action.CollapseFc;
        if (In(Px(MiniButton, s, originX), px, py))  return Action.MinimizeFc;

        // Order + formation controls only when the commands aren't folded.
        // Crystals first — they overlay the right edge of the order buttons.
        if (!commandsCollapsed)
        {
            foreach (var g in new[] { Movement, Attack, Targeting })
                foreach (var o in g) if (In(Px(o.R, s, originX), px, py)) return o.A;
            foreach (var f in Formations) if (In(Px(f.R, s, originX), px, py)) return f.A;
            foreach (var b in OrderButtons) if (In(Px(b.R, s, originX), px, py)) return b.Cycle;
        }

        foreach (var b in TopIcons) if (In(Px(b.R, s, originX), px, py)) return b.A;
        if (In(Px(LootBag, s, originX), px, py)) return Action.CollectLoot;
        return Action.None;
    }

    // Formation radio icon per action (DS1's six formations use form1-4/6/7;
    // form5 is unused). Selected radios swap the _up face for the pressed _dwn.
    static string FormationTexture(Action a) => a switch
    {
        Action.FormRow          => "b_gui_ig_mnu_form1",
        Action.FormDoubleRow    => "b_gui_ig_mnu_form2",
        Action.FormColumn       => "b_gui_ig_mnu_form3",
        Action.FormDoubleColumn => "b_gui_ig_mnu_form4",
        Action.FormPyramid      => "b_gui_ig_mnu_form6",
        Action.FormCircle       => "b_gui_ig_mnu_form7",
        _                       => "",
    };

    // The order-row buttons display the CURRENT selection's label (DS1 sets the
    // button text to the active radio in that group), all-caps like the original.
    static string OrderLabel(Action a) => a switch
    {
        Action.MoveFree       => "MOVE FREELY",
        Action.MoveEngage     => "ENGAGE",
        Action.MoveHoldGround => "HOLD GROUND",
        Action.AtkFree        => "ATTACK FREELY",
        Action.AtkFightback   => "DEFEND",
        Action.AtkHoldFire    => "HOLD FIRE",
        Action.TgtClosest     => "TARGET CLOSEST",
        Action.TgtWeakest     => "TARGET WEAKEST",
        Action.TgtStrongest   => "TARGET STRONGEST",
        _                     => "",
    };

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, int viewportW, int viewportH, State st)
    {
        // Every control is a b_gui_* raw blitted at its authored rect with the
        // authored UV crop (converted from the RAWs' bottom-up frame). Without
        // the icon renderer / atlas (headless) nothing draws.
        if (icons is null || guiTex is null) return;

        float s = Scale(viewportH);
        int originX = viewportW - (int)MathF.Round(640f * s);
        int fontScale = System.Math.Max(1, (int)MathF.Round(s));
        var ink = new Vector4(0.88f, 0.84f, 0.70f, 1f);

        // DS1 RAWs are stored bottom-up and the gas authors uvcoords in that
        // frame, so convert to the renderer's top-down space before drawing:
        // vMin = 1 - gasV1, vMax = 1 - gasV0 (the inventory/data_bar flip rule).
        void Blit(string tex, (int, int, int, int) r, float gu0, float gv0, float gu1, float gv1)
        {
            var t = guiTex(tex);
            if (t is null) return;
            var p = Px(r, s, originX);
            icons.DrawIcon(viewportW, viewportH, t, p.x, p.y, p.w, p.h, Vector4.One,
                           gu0, 1f - gv1, gu1, 1f - gv0);
        }

        // Minimized: only the loot bag and the maximize button survive
        // (minimize-up's right half = the maximize glyph).
        if (st.Minimized)
        {
            Blit("b_gui_ig_mnu_get_loot_up", LootBag,    0f, 0.375f, 1f, 1f);
            Blit("b_gui_ig_mnu_minimize-up", MiniButton, 0.5f, 0.375f, 1f, 1f);
            return;
        }

        // Follow checkbox (docked above the main cluster).
        Blit(st.Follow ? "b_gui_ig_mnu_follow_on_up" : "b_gui_ig_mnu_follow_off_up",
             (519, 284, 563, 308), 0f, 0.25f, 0.6875f, 1f);

        // Select-all / disband icon buttons.
        Blit("b_gui_ig_mnu_select_up",  (571, 332, 603, 352), 0f, 0.375f,   1f,       1f);
        Blit("b_gui_ig_mnu_disband_up", (602, 332, 635, 353), 0f, 0.34375f, 1.03125f, 1f);

        // Command controls: order rows + formation radios, hidden when the
        // command-fold tab is engaged.
        if (!st.CommandsCollapsed)
        {
            // Order rows: a button_4 push-button showing the current order, then
            // the group's three selection crystals (lit = active).
            void Row((Action A, (int, int, int, int) R)[] grp, Action sel, (int, int, int, int) btnRect)
            {
                var bp = Px(btnRect, s, originX);
                ButtonChrome.Draw(icons, guiTex, viewportW, viewportH,
                                  bp.x, bp.y, bp.w, bp.h, "button4", ButtonChrome.State.Up);
                string label = OrderLabel(sel);
                int lw = text.MeasureWidth(label, fontScale);
                int fh = 12 * fontScale;
                text.DrawString(viewportW, viewportH, label,
                                bp.x + (bp.w - lw) / 2, bp.y + (bp.h - fh) / 2, ink, fontScale);
                foreach (var o in grp)
                    Blit(o.A == sel ? "b_gui_cmn_crystal_on_up" : "b_gui_cmn_crystal_off_up",
                         o.R, 0f, 0.1875f, 0.8125f, 1f);
            }
            Row(Movement,  st.Movement,  OrderButtons[0].R);
            Row(Attack,    st.Attack,    OrderButtons[1].R);
            Row(Targeting, st.Targeting, OrderButtons[2].R);

            // Formation radios (selected → pressed face).
            foreach (var f in Formations)
                Blit(FormationTexture(f.A) + (f.A == st.Formation ? "_dwn" : "_up"),
                     f.R, 0f, 0.25f, 0.78125f, 1f);
        }

        // Bottom utility row: chat (MP), collect-loot bag, minimize button.
        Blit("b_gui_ig_mnu_chat_up",     (540, 425, 572, 445), 0f, 0.375f, 1f,        1f);
        Blit("b_gui_ig_mnu_get_loot_up", LootBag,              0f, 0.375f, 1f,        1f);
        Blit("b_gui_ig_mnu_minimize-up", MiniButton,           0f, 0.375f, 0.515625f, 1f);

        // Right-edge command-fold tab: fieldcom_r when open, fieldcom_l folded.
        Blit(st.CommandsCollapsed ? "b_gui_ig_mnu_fieldcom_l_up" : "b_gui_ig_mnu_fieldcom_r_up",
             CollapseTab, 0f, 0.414063f, 0.65625f, 1f);
    }
}

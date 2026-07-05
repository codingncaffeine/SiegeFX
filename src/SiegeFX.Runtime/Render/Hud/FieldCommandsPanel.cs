using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 27 — DS1's field-commands panel
/// (<c>/ui/interfaces/backend/field_commands/field_commands.gas</c>): the
/// bottom-right party-command cluster. Six formation buttons (radio_group
/// radio_formations), three crystal-radio order groups (movement / attack /
/// targeting), plus select-all, disband, and a follow toggle. Rects are the
/// authored 640×480 reference, right-anchored and scaled by viewportH/480.
///
/// Chrome uses the authored b_gui_* textures when they resolve, falling back
/// to labelled cells/crystals (the VendorPanel approach) so the panel is
/// legible even if a raw is missing.
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
        TgtClosest, TgtStrongest, TgtWeakest,
        SelectAll, Disband, ToggleFollow,
    }

    // Authored rects (x0,y0,x1,y1) in the 640×480 reference.
    static readonly (Action A, (int, int, int, int) R, string Label)[] Formations =
    {
        (Action.FormRow,          (428, 401, 453, 425), "Row"),
        (Action.FormDoubleRow,    (452, 401, 477, 425), "2Row"),
        (Action.FormColumn,       (475, 401, 500, 425), "Col"),
        (Action.FormDoubleColumn, (499, 401, 524, 425), "2Col"),
        (Action.FormPyramid,      (523, 401, 548, 425), "Pyr"),
        (Action.FormCircle,       (547, 401, 572, 425), "Cir"),
    };
    // Three crystal-radio order rows. Each option is a 12×13 crystal.
    static readonly (Action A, (int, int, int, int) R)[] Movement =
    {
        (Action.MoveFree, (574, 354, 586, 367)), (Action.MoveEngage, (587, 354, 599, 367)),
        (Action.MoveHoldGround, (600, 354, 612, 367)),
    };
    static readonly (Action A, (int, int, int, int) R)[] Attack =
    {
        (Action.AtkFree, (574, 370, 586, 383)), (Action.AtkFightback, (587, 370, 599, 383)),
        (Action.AtkHoldFire, (600, 370, 612, 383)),
    };
    static readonly (Action A, (int, int, int, int) R)[] Targeting =
    {
        (Action.TgtClosest, (574, 386, 586, 399)), (Action.TgtStrongest, (587, 386, 599, 399)),
        (Action.TgtWeakest, (600, 386, 612, 399)),
    };
    static readonly (Action A, (int, int, int, int) R, string Label)[] Buttons =
    {
        (Action.SelectAll,    (571, 332, 603, 352), "All"),
        (Action.Disband,      (602, 332, 635, 353), "Dis"),
        (Action.ToggleFollow, (519, 284, 563, 308), "Follow"),
    };
    static readonly (int, int, int, int) WindowPanel = (571, 351, 616, 402);

    public readonly record struct State(
        Action Formation, Action Movement, Action Attack, Action Targeting, bool Follow);

    static (int x, int y, int w, int h) Px((int x0, int y0, int x1, int y1) r, float s, int originX)
        => (originX + (int)MathF.Round(r.x0 * s), (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));

    static bool In((int x, int y, int w, int h) p, int px, int py)
        => px >= p.x && px < p.x + p.w && py >= p.y && py < p.y + p.h;

    public Action HitTest(int px, int py, int viewportW, int viewportH)
    {
        float s = Scale(viewportH);
        int originX = viewportW - (int)MathF.Round(640f * s);
        foreach (var f in Formations) if (In(Px(f.R, s, originX), px, py)) return f.A;
        foreach (var g in new[] { Movement, Attack, Targeting })
            foreach (var o in g) if (In(Px(o.R, s, originX), px, py)) return o.A;
        foreach (var b in Buttons) if (In(Px(b.R, s, originX), px, py)) return b.A;
        return Action.None;
    }

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, int viewportW, int viewportH, State st)
    {
        float s = Scale(viewportH);
        int originX = viewportW - (int)MathF.Round(640f * s);
        var ink   = new Vector4(0.88f, 0.84f, 0.70f, 1f);
        var dim   = new Vector4(0.58f, 0.56f, 0.47f, 1f);
        var onCol = new Vector4(0.95f, 0.85f, 0.40f, 1f);
        var panel = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        var edge  = new Vector4(0.667f, 0.655f, 0.557f, 1f);

        // Backdrop over the whole cluster (formations row + order crystals +
        // buttons) so the controls read against the world.
        {
            var frame = Px((424, 330, 638, 428), s, originX);
            if (icons is not null && guiTex is not null)
                NinePatch.DrawCpboxThinDark(icons, guiTex, viewportW, viewportH,
                    frame.x, frame.y, frame.w, frame.h, Vector4.One);
            else
            {
                bars.DrawRect(viewportW, viewportH, frame.x, frame.y, frame.w, frame.h, panel);
                bars.DrawBorder(viewportW, viewportH, frame.x, frame.y, frame.w, frame.h, edge);
            }
        }

        // Formation cells — highlight the active one.
        foreach (var f in Formations)
        {
            var p = Px(f.R, s, originX);
            bool active = f.A == st.Formation;
            bars.DrawRect(viewportW, viewportH, p.x, p.y, p.w, p.h,
                active ? new Vector4(0.20f, 0.18f, 0.10f, 1f) : new Vector4(0.10f, 0.10f, 0.12f, 1f));
            bars.DrawBorder(viewportW, viewportH, p.x, p.y, p.w, p.h, active ? onCol : dim);
            int lw = text.MeasureWidth(f.Label);
            text.DrawString(viewportW, viewportH, f.Label,
                p.x + (p.w - lw) / 2, p.y + p.h / 3, active ? ink : dim);
        }

        // Order crystals: filled = selected in its group.
        void Row((Action A, (int, int, int, int) R)[] grp, Action sel, string label)
        {
            var lblP = Px((428, grp[0].R.Item2, 470, grp[0].R.Item2 + 13), s, originX);
            text.DrawString(viewportW, viewportH, label, lblP.x, lblP.y, dim);
            foreach (var o in grp)
            {
                var p = Px(o.R, s, originX);
                bool on = o.A == sel;
                bars.DrawRect(viewportW, viewportH, p.x, p.y, p.w, p.h,
                    on ? onCol : new Vector4(0.12f, 0.12f, 0.14f, 1f));
                bars.DrawBorder(viewportW, viewportH, p.x, p.y, p.w, p.h, dim);
            }
        }
        Row(Movement, st.Movement, "Move");
        Row(Attack, st.Attack, "Atk");
        Row(Targeting, st.Targeting, "Tgt");

        // Select-all / Disband / Follow.
        foreach (var b in Buttons)
        {
            var p = Px(b.R, s, originX);
            bool follow = b.A == Action.ToggleFollow && st.Follow;
            bars.DrawRect(viewportW, viewportH, p.x, p.y, p.w, p.h,
                follow ? new Vector4(0.20f, 0.18f, 0.10f, 1f) : new Vector4(0.10f, 0.10f, 0.12f, 1f));
            bars.DrawBorder(viewportW, viewportH, p.x, p.y, p.w, p.h, follow ? onCol : dim);
            int lw = text.MeasureWidth(b.Label);
            text.DrawString(viewportW, viewportH, b.Label,
                p.x + (p.w - lw) / 2, p.y + p.h / 3, ink);
        }
    }
}

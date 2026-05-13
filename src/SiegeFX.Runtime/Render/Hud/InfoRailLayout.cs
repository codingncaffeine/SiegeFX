using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 22-INFORAIL-A — gas-cited layout constants for DS1's I-key
/// "information rail" (the paperdoll + inventory + spellbook trio
/// that chains rightward, edge-to-edge). Every rect, uv, and texture
/// name below is pasted verbatim from the shipped DS1 backend gas
/// under <c>/ui/interfaces/backend/</c>. Source files in this code-
/// base's research copies live at:
/// <list type="bullet">
///   <item><c>hud_character.gas</c> — paperdoll info panel</item>
///   <item><c>hud_inventory.gas</c> — inventory grid + transfer/sort/X</item>
///   <item><c>hud_spell.gas</c> — spellbook + active spell slots + X</item>
///   <item><c>hud_character_awp.gas</c> — AWP widget + close-arrow</item>
/// </list>
/// All coordinates use the gas-authored 640×480 reference; consumers
/// multiply by <c>viewportH / 480f</c> per
/// feedback_siegefx_authentic_scalable.md (uniform-scale-only rule).
/// V-flip rule from project_siegefx_raw_bottomup.md applies at draw
/// sites — these constants store gas-native bottom-up uvcoords.
/// </summary>
public static class InfoRailLayout
{
    public const int RefRes = 480;
    /// <summary>Uniform info-rail scale. Native DS1 ran at 800×600 /
    /// 1024×768 where viewportH/480 was 1.25..1.6 and the 3 panels
    /// tiled across most of the screen height naturally. On 1080p+
    /// the raw viewportH/480 produces oversize panels that crowd the
    /// world view, so we clamp the scale at 1.5× to keep the rail
    /// at a usable height while still respecting the gas-authored
    /// aspect ratios (feedback_siegefx_authentic_scalable.md
    /// scale-only deviation rule).</summary>
    public const float MaxScale = 1.5f;
    public static float Scale(int viewportH)
    {
        float raw = viewportH / (float)RefRes;
        return raw < MaxScale ? raw : MaxScale;
    }

    // ============================================================
    // PAPERDOLL / INFO PANEL — hud_character.gas
    // ============================================================
    // Three stacked chrome panes share x=87..254 (w=167):

    /// <summary>Top header pane (name, class, vital bars, stats).
    /// hud_character.gas line 175: rect=87,0,254,116, tex
    /// b_gui_ig_mnu_cp_top_01, uv 0,0.09375,0.652344,1.</summary>
    public static readonly Rect Pane1     = new(87,   0, 254, 116);
    public const string Pane1Texture     = "b_gui_ig_mnu_cp_top_01";
    public static readonly Uv     Pane1Uv = new(0f, 0.09375f, 0.652344f, 1f);

    /// <summary>Middle pane (level, 8-row skills + damages + armor).
    /// hud_character.gas line 187.</summary>
    public static readonly Rect Pane2     = new(87, 116, 254, 228);
    public const string Pane2Texture     = "b_gui_ig_mnu_cp_mid_01";
    public static readonly Uv     Pane2Uv = new(0f, 0.125f, 0.652344f, 1f);

    /// <summary>Bottom paperdoll pane (equipment + ghosts + buttons).
    /// hud_character.gas line 199.</summary>
    public static readonly Rect Pane3     = new(87, 228, 254, 449);
    public const string Pane3Texture     = "b_gui_ig_mnu_cp_bot_01";
    public static readonly Uv     Pane3Uv = new(0f, 0.136719f, 0.652344f, 1f);

    // Header text — character name & class (hud_character.gas 152, 136):
    public static readonly Rect CharacterName  = new(91,  5, 248, 17);
    public static readonly Rect CharacterClass = new(91, 23, 249, 36);

    // Vertical Health bar (LEFT side of pane 1) — hud_character.gas 879:
    public static readonly Rect VertHealthBar  = new( 88, 44, 101, 114);
    public static readonly Uv   VertHealthUv   = new(0.714844f, 0.460938f, 0.765625f, 1.007813f);
    public const string         VertHealthTex  = "b_gui_ig_mnu_cp_top_01";
    // Vertical Mana bar (RIGHT side of pane 1) — hud_character.gas 892:
    public static readonly Rect VertManaBar    = new(238, 44, 251, 114);
    public static readonly Uv   VertManaUv     = new(0.765625f, 0.460938f, 0.816406f, 1.007813f);

    // Labels between the vertical bars (hud_character.gas 947/993/921/952 + Str text TBD):
    public static readonly Rect TextHealth     = new(102, 45, 168, 58);
    public static readonly Rect TextMana       = new(171, 44, 237, 58);
    public static readonly Rect ValHealth      = new(102, 58, 168, 72); // "9999/9999"
    public static readonly Rect ValMana        = new(171, 58, 237, 72);
    public static readonly Rect TextDexterity    = new(104, 87, 211, 100);
    public static readonly Rect TextIntelligence = new(104, 101, 211, 114);

    // STR/DEX/INT bars (102,Y,213,Y+13) + values (214,Y,236,Y+13):
    public static readonly Rect BarStrength    = new(102, 73, 213,  86);
    public static readonly Rect ValStrength    = new(214, 73, 236,  86);
    public static readonly Rect BarDexterity   = new(102, 87, 213, 100);
    public static readonly Rect ValDexterity   = new(214, 87, 236, 100);
    public static readonly Rect BarIntelligence = new(102,101, 213, 114);
    public static readonly Rect ValIntelligence = new(214,101, 236, 114);

    // Shared horizontal stat-bar atlas region (hud_character.gas 240+):
    public const string         StatBarTex     = "b_gui_ig_mnu_cp_mid_01";
    public static readonly Uv   StatBarUv      = new(0f, -0.007813f, 0.433594f, 0.085938f);

    // Middle-pane (skills, damages, armor) — text labels + values:
    public static readonly Rect TextLevel        = new(201,117, 250,130);

    public static readonly Rect BarMelee       = new( 89, 130, 200, 143);
    public static readonly Rect ValMelee       = new(200, 130, 250, 143);
    public static readonly Rect BarRanged      = new( 89, 144, 200, 157);
    public static readonly Rect ValRanged      = new(200, 144, 250, 157);
    public static readonly Rect BarNatureMagic = new( 89, 158, 200, 171);
    public static readonly Rect ValNatureMagic = new(200, 158, 250, 171);
    public static readonly Rect BarCombatMagic = new( 89, 172, 200, 185);
    public static readonly Rect ValCombatMagic = new(200, 172, 250, 185);

    public static readonly Rect TextMeleeDamage  = new( 90, 186, 199, 199);
    public static readonly Rect ValMeleeDamage   = new(200, 186, 250, 199);
    public static readonly Rect TextRangedDamage = new( 90, 200, 199, 213);
    public static readonly Rect ValRangedDamage  = new(200, 200, 250, 213);
    public static readonly Rect TextArmorRating  = new( 90, 214, 199, 227);
    public static readonly Rect ValArmorRating   = new(200, 214, 250, 227);

    // Spellbook show/hide toggle (top-right of paperdoll bottom pane).
    // hud_character.gas 78..99 + 54..77. Same rect for both; two states
    // packed vertically in b_gui_ig_mnu_minimize-book-up atlas:
    //   show (initial)  uv 0,0,        0.65625,0.484375
    //   hide (active)   uv 0,0.5,      0.65625,0.984375
    public static readonly Rect SpellbookToggle    = new(229,238,250,269);
    public const string         SpellbookToggleTex = "b_gui_ig_mnu_minimize-book-up";
    public static readonly Uv   SpellbookToggleShowUv = new(0f, 0f,   0.65625f, 0.484375f);
    public static readonly Uv   SpellbookToggleHideUv = new(0f, 0.5f, 0.65625f, 0.984375f);

    // View button (bottom center, hud_character.gas 107):
    public static readonly Rect ViewButton     = new(140, 430, 200, 446);

    // ============================================================
    // INVENTORY — hud_inventory.gas
    // ============================================================
    // MAX mode (paperdoll open, info-rail layout): inventory shifted
    // RIGHT to touch paperdoll's right edge (x=254).
    // MIN mode (paperdoll closed, inventory only): inventory shifted
    // LEFT, occupying x=89..474.
    // MAX: dialog_box_inv_bg rect=253,0,387,449 (hud_inventory.gas:94).
    // MIN: pack_mule_dialog_box_inv_bg rect=87,0,477,449
    //   (hud_inventory.gas:230). 22-INFORAIL-G audit fold —
    //   originally had 89,2,474,447 cherry-picked from gridbox inner
    //   rect, which violates the verbatim-from-gas rule.
    public static readonly Rect InventoryMax   = new(253,  0, 387, 449);
    public static readonly Rect InventoryMin   = new( 87,  0, 477, 449);

    /// <summary>Inventory close X button in MAX mode.
    /// hud_inventory.gas 76 + 83 (onbuttonpress=notify(character_exit)).</summary>
    public static readonly Rect InventoryCloseMax = new(369,  2, 385, 18);
    /// <summary>Inventory close X button in MIN mode (different x).
    /// hud_inventory.gas 212 + 219.</summary>
    public static readonly Rect InventoryCloseMin = new(459,  2, 475, 18);

    // ============================================================
    // SPELLBOOK — hud_spell.gas
    // ============================================================
    /// <summary>Spellbook full panel extent, x 387..542. This is the
    /// UNION of gas widgets: title 387,0,542,32 + body 387,31,542,128 +
    /// lower 387,126,542,449 (hud_spell.gas 38/27/49). No single gas
    /// widget has rect 387,0,542,449 — but the union IS the correct
    /// bounding extent for positioning the panel anchor.</summary>
    public static readonly Rect Spellbook         = new(387,  0, 542, 449);

    /// <summary>Spellbook close X (top-right corner). hud_spell.gas
    /// 10 + 17 (onbuttonpress=notify(spell_close)).</summary>
    public static readonly Rect SpellbookClose    = new(524,  2, 540, 18);

    // ============================================================
    // AWP CLOSE-ARROW — hud_character_awp.gas
    // ============================================================
    // The "swap in when info-rail is open" close button sits below
    // the FIRST skill slot only (skill slot 1 is at rect 68,6,84,38;
    // the close arrow lives directly under that strip at y=40+).
    // Per hud_character_awp.gas: awp_button_inventory_small_1 at
    // rect 64,40,87,56 has uv 0.820313,0,1,1 in the awp_buttons
    // atlas — that's the "min mode" inventory button (a ◄-arrow
    // appearance per the atlas region). In our model we treat it
    // as "close the info-rail" when the rail is open.
    public static readonly Rect AwpCloseArrow     = new(64, 40,  87, 56);
    public const string         AwpCloseArrowTex  = "b_gui_ig_mnu_awp_buttons";
    public static readonly Uv   AwpCloseArrowUv   = new(0.820313f, 0f, 1f, 1f);

    // ============================================================
    // Scaled-rect helpers — multiply by Scale(viewportH).
    // ============================================================
    public readonly record struct Rect(int X0, int Y0, int X1, int Y1)
    {
        public int W => X1 - X0;
        public int H => Y1 - Y0;
        public (int x, int y, int w, int h) Scaled(float s) => (
            (int)System.Math.Round(X0 * s),
            (int)System.Math.Round(Y0 * s),
            (int)System.Math.Round(W  * s),
            (int)System.Math.Round(H  * s));
        public bool Contains(int x, int y, float s)
        {
            var (sx, sy, sw, sh) = Scaled(s);
            return x >= sx && y >= sy && x < sx + sw && y < sy + sh;
        }
    }

    public readonly record struct Uv(float U0, float V0, float U1, float V1)
    {
        /// <summary>Convert gas-native (bottom-up) uv to screen-space
        /// (top-down) uv for DrawIcon(...) which samples top-down.
        /// Per project_siegefx_raw_bottomup.md.</summary>
        public (float u0, float v0, float u1, float v1) Screen() =>
            (U0, 1f - V1, U1, 1f - V0);
    }
}

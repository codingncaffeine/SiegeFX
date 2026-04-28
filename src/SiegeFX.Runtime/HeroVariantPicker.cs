using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime;

internal enum HeroGender { Boy, Girl }

/// <summary>21d-2a-viii — env-var driven character creator picks. Slice viii-a
/// ships this CLI/launch interface; slice viii-b will replace the env-var
/// reads with in-game widgets while keeping <see cref="BuildOverride"/> as
/// the resolver-side entry point.
///
/// <para>Recognised vars (all optional; missing = template default):</para>
/// <list type="bullet">
/// <item><c>SIEGEFX_HERO_GENDER</c> = boy | girl. Picks farmboy / farmgirl
///   base template.</item>
/// <item><c>SIEGEFX_HERO_BODY</c> = 1..7. Replaces aspect.model's pos_a1
///   suffix with pos_aN, exercising the variant pipeline.</item>
/// <item><c>SIEGEFX_HERO_SKIN</c> = NN (e.g. 03). Composes
///   b_c_&lt;armor_version&gt;_skin_NN as the slot-0 override.</item>
/// <item><c>SIEGEFX_HERO_PANTS</c> = NNN (e.g. 015). Composes
///   b_c_pos_aN_NNN against the picked body type as the slot-1 override.</item>
/// </list>
///
/// <para>The picker doesn't validate that the resulting names exist on disk;
/// the renderer's texture cache and the spawner's mesh cache report MISS in
/// the launch console for unknown variants, so a typo is visible immediately
/// rather than crashing the spawn.</para></summary>
internal sealed class HeroVariantPicker
{
    public HeroGender Gender { get; init; }
    /// <summary>0..6 corresponding to pos_a1..pos_a7. -1 = no override (use
    /// template default — usually a1).</summary>
    public int BodyTypeIdx { get; init; } = -1;
    /// <summary>Two-digit zero-padded suffix (e.g. "03"). Null = no override.</summary>
    public string? SkinSuffix { get; init; }
    /// <summary>Three-digit zero-padded suffix (e.g. "001") into
    /// <c>b_c_gah_*_hair_NNN.raw</c>. Null = no hair override.</summary>
    public string? HairSuffix { get; init; }
    /// <summary>Shirt-stride index (0..N) folded into the pants atlas suffix.
    /// DS1 packs shirt + pants into a single <c>b_c_pos_aN_NNN.raw</c> atlas
    /// with no separate shirt file, so the shirt button rolls the atlas
    /// suffix forward by a coprime stride to walk distinct shirt/pant
    /// combos as the user clicks.</summary>
    public int ShirtIdx { get; init; }
    /// <summary>Three-digit zero-padded suffix (e.g. "015"). Null = no override.</summary>
    public string? PantsSuffix { get; init; }

    public static HeroVariantPicker FromEnv()
    {
        var genderRaw = Environment.GetEnvironmentVariable("SIEGEFX_HERO_GENDER");
        var gender = string.Equals(genderRaw, "girl", StringComparison.OrdinalIgnoreCase)
            ? HeroGender.Girl
            : HeroGender.Boy;

        int bodyIdx = -1;
        var bodyRaw = Environment.GetEnvironmentVariable("SIEGEFX_HERO_BODY");
        if (int.TryParse(bodyRaw, out var n) && n >= 1 && n <= 7) bodyIdx = n - 1;

        var skin  = NormalisePadded(Environment.GetEnvironmentVariable("SIEGEFX_HERO_SKIN"),  width: 2);
        var pants = NormalisePadded(Environment.GetEnvironmentVariable("SIEGEFX_HERO_PANTS"), width: 3);

        return new HeroVariantPicker
        {
            Gender = gender,
            BodyTypeIdx = bodyIdx,
            SkinSuffix = skin,
            PantsSuffix = pants,
        };
    }

    /// <summary>Pads a numeric string to <paramref name="width"/> digits if it
    /// parses as an integer, leaves it null otherwise. Tolerates "3" → "03"
    /// and "15" → "015" so users don't have to remember which axis is two vs
    /// three digits.</summary>
    private static string? NormalisePadded(string? raw, int width)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw.Trim(), out var n) || n < 0) return null;
        return n.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    /// <summary>Composes a <see cref="TemplateOverride"/> for the picked variant.
    /// Reads <c>body.armor_version</c> from the player template (e.g. <c>gah_fb</c>
    /// for farmboy, <c>gah_fg</c> for farmgirl) so the boy/girl skin/mesh stems
    /// aren't hardcoded — the resolver's existing equipment path uses the same
    /// attribute, keeping a single source of truth.</summary>
    public TemplateOverride? BuildOverride(TemplateStore store, Template template)
    {
        if (BodyTypeIdx < 0 && SkinSuffix is null && HairSuffix is null
            && PantsSuffix is null && ShirtIdx == 0) return null;

        var armorVersion = store.GetAttribute(template, "body", "armor_version");
        if (string.IsNullOrEmpty(armorVersion)) return null;

        // pos_aN body type — the suffix the user picked, or fall back to a1
        // because that's what the shipped templates author. We need an explicit
        // bodyChar for both the model name AND the pants texture key (b_c_pos_aN_NNN).
        char bodyChar = BodyTypeIdx < 0 ? '1' : (char)('1' + BodyTypeIdx);

        string? modelOverride = null;
        if (BodyTypeIdx >= 0)
            modelOverride = $"m_c_{armorVersion}_pos_a{bodyChar}";

        // Skin atlas folds Face + Hair together. Each shipped
        // b_c_gah_*_skin_NN.raw is a baked face+hair+arms atlas, so face and
        // hair colour vary together file-to-file. Face walks the atlas at
        // stride 1; Hair walks at a coprime stride of 7 so clicking Hair
        // jumps to a distant variant (different hair colour) without
        // trampling Face's neighbour. Both buttons therefore drive a visible
        // change without runtime atlas composition (which broke face
        // details when we tried it).
        string? skinOverride = null;
        if (SkinSuffix is not null)
        {
            int faceIdx = 1;
            int.TryParse(SkinSuffix, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out faceIdx);
            int hairIdx = 1;
            if (HairSuffix is not null)
                int.TryParse(HairSuffix, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out hairIdx);
            const int SkinRange = 32;
            const int HairStride = 7;
            int folded = ((faceIdx - 1) + (hairIdx - 1) * HairStride) % SkinRange;
            if (folded < 0) folded += SkinRange;
            int finalIdx = folded + 1;
            string finalSuffix = finalIdx.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(2, '0');
            skinOverride = $"b_c_{armorVersion}_skin_{finalSuffix}";
        }

        // Pants atlas folds Shirt and Pants identically — DS1 packs both into
        // one b_c_pos_aN_NNN.raw with no separate shirt file.
        string? pantsOverride = null;
        if (PantsSuffix is not null)
        {
            int basePants = 1;
            int.TryParse(PantsSuffix, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out basePants);
            const int PantsRange = 41;
            const int ShirtStride = 7;
            int folded = ((basePants - 1) + ShirtIdx * ShirtStride) % PantsRange;
            if (folded < 0) folded += PantsRange;
            int finalIdx = folded + 1;
            string finalSuffix = finalIdx.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(3, '0');
            pantsOverride = $"b_c_pos_a{bodyChar}_{finalSuffix}";
        }

        return new TemplateOverride
        {
            ModelName = modelOverride,
            SkinTextureName = skinOverride,
            ClothingTextureName = pantsOverride,
        };
    }
}

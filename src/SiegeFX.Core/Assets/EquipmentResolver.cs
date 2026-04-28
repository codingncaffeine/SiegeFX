namespace SiegeFX.Core.Assets;

/// <summary>
/// Phase 21d-2a-vii — derives the visible mesh + texture for each
/// <c>[inventory][equipment]</c> slot on a player template. DS1 layers each
/// equipped item as a separate mesh on the body via bone attachments — boots,
/// helms, gauntlets are skinned to the same biped skeleton as the body and
/// share its current animation pose, while weapons/shields rigidly attach to
/// the <c>weapon_grip</c>/<c>shield_grip</c> bones. Body chest "armor" is a
/// pure texture-swap on body subset 1; the spellbook is UI-only and not
/// rendered in 3D.
///
/// Boot/helm/gauntlet meshes are not stored on the item template (e.g.
/// <c>bo_bo_le_light</c> has no <c>aspect.model</c>). They live next to the
/// hero's body mesh under <c>/art/meshes/characters/.../armor/</c> with the
/// per-hero <c>body.armor_version</c> prefix:
///
///   m_c_&lt;armor_version&gt;_boot_&lt;armor_type&gt;_&lt;subtype&gt;.asp
///   m_c_&lt;armor_version&gt;_hlmt_&lt;armor_type&gt;.asp
///   m_c_&lt;armor_version&gt;_gntl_&lt;armor_type&gt;_&lt;subtype&gt;.asp
///
/// where the boot/gauntlet subtype comes from
/// <c>/world/global/armor_lookup.gas</c>'s <c>armor_subtype_lookup</c> table
/// keyed by the body type suffix (a1..a7) of the hero's
/// <c>aspect.model</c>. Equipment textures are <c>b_a_&lt;slot&gt;_&lt;style&gt;.raw</c>
/// where the style number is the item template's <c>defend.armor_style</c>.
/// </summary>
public sealed class EquipmentResolver
{
    public enum Strategy
    {
        /// <summary>No render — slot has no visible mesh (spellbook, amulet, ring).</summary>
        None,
        /// <summary>Rigid attach to a single bone via <c>body.bone_translator</c>
        /// (weapons → <c>weapon_bone</c>, shields → <c>shield_bone</c>). The
        /// existing weapon-attach pass in RenderHost handles these.</summary>
        AttachBone,
        /// <summary>Skinned mesh layered on the body using the SAME bone names
        /// the body uses. Body's per-frame skin matrices reuse via
        /// <see cref="AnimationRuntime.ComputeSkinMatrices"/> (name-keyed bone map).
        /// Used for boots, helms, gauntlets.</summary>
        SkinnedLayer,
        /// <summary>No separate mesh — slot's appearance is a texture-swap on a
        /// body subset (body chest armor overrides the body's clothing-strip
        /// subset). RenderHost binds <see cref="OverrideBaseName"/> instead of
        /// the template's default for <see cref="OverrideTextureSlot"/>.</summary>
        ChestTexture,
    }

    public sealed record EquipmentLayer(
        Strategy Strategy,
        string SlotName,
        string ItemRef,
        string? MeshBaseName,
        string? TextureBaseName,
        string? AttachBoneName,
        int OverrideTextureSlot,
        string? OverrideBaseName);

    private static readonly Dictionary<string, (string Boot, string Gauntlet)> s_lookupCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_lookupLock = new();
    private static bool s_lookupLoaded;

    /// <summary>Parse <c>/world/global/armor_lookup.gas</c> on first use; cache for the
    /// life of the process. Body-type → (boot subtype, gauntlet subtype). Ships seven
    /// entries: a1=b,b a2=b,b a3=a,a a4=a,a a5=a,a a6=a,a a7=c,a — derived once and
    /// memoised because every PC spawn re-asks the same questions.</summary>
    public static bool TryGetArmorSubtypes(AssetResolver resolver, string bodyType,
        out string bootSubtype, out string gauntletSubtype)
    {
        EnsureLookupLoaded(resolver);
        lock (s_lookupLock)
        {
            if (s_lookupCache.TryGetValue(bodyType, out var hit))
            {
                bootSubtype = hit.Boot; gauntletSubtype = hit.Gauntlet; return true;
            }
        }
        bootSubtype = ""; gauntletSubtype = ""; return false;
    }

    private static void EnsureLookupLoaded(AssetResolver resolver)
    {
        lock (s_lookupLock)
        {
            if (s_lookupLoaded) return;
            s_lookupLoaded = true;
            if (!resolver.TryLoadByBasename("armor_lookup.gas", out var bytes)) return;
            GasDocument doc;
            try { doc = GasDocument.Load(bytes); } catch { return; }
            foreach (var root in doc.Roots)
            {
                if (!string.Equals(root.Header, "armor_subtype_lookup",
                        StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var attr in root.Attributes)
                {
                    var v = attr.Value;
                    if (string.IsNullOrEmpty(v)) continue;
                    var comma = v.IndexOf(',');
                    if (comma < 0) continue;
                    var boot = v[..comma].Trim();
                    var gnt = v[(comma + 1)..].Trim();
                    s_lookupCache[attr.Name.Trim()] = (boot, gnt);
                }
            }
        }
    }

    /// <summary>Resolves every equipment slot on <paramref name="playerTemplate"/>'s
    /// <c>[inventory][equipment]</c> block. When <paramref name="liveSlots"/> is
    /// supplied (e.g. the host's runtime equipment dict), iterate that instead of
    /// the template — picked-up items override / extend the template's authored
    /// loadout. Slots that fail to resolve (missing item template, no mesh on
    /// disk) are returned with <see cref="Strategy.None"/> so the caller can log
    /// them without crashing the spawn path.</summary>
    public static List<EquipmentLayer> Resolve(
        TemplateStore templates, AssetResolver resolver, Template playerTemplate,
        IReadOnlyDictionary<string, string>? liveSlots = null)
    {
        var layers = new List<EquipmentLayer>();
        var armorVersion = templates.GetAttribute(playerTemplate, "body", "armor_version");
        var bodyModel = templates.GetAttribute(playerTemplate, "aspect", "model");
        var bodyType = ExtractBodyType(bodyModel); // e.g. m_c_gah_fb_pos_a1 → a1
        var weaponBone = templates.GetAttribute(playerTemplate, "body", "bone_translator", "weapon_bone");
        var shieldBone = templates.GetAttribute(playerTemplate, "body", "bone_translator", "shield_bone");

        IEnumerable<KeyValuePair<string, string>> source;
        if (liveSlots is not null)
        {
            source = liveSlots;
        }
        else
        {
            var equip = templates.GetSection(playerTemplate, "inventory", "equipment");
            if (equip is null) return layers;
            var fromTemplate = new List<KeyValuePair<string, string>>(equip.Attributes.Count);
            foreach (var attr in equip.Attributes)
                fromTemplate.Add(new KeyValuePair<string, string>(attr.Name, attr.Value ?? ""));
            source = fromTemplate;
        }

        foreach (var pair in source)
        {
            if (!pair.Key.StartsWith("es_", StringComparison.OrdinalIgnoreCase)) continue;
            var slot = pair.Key.Trim();
            var itemRef = pair.Value?.Trim();
            if (string.IsNullOrEmpty(itemRef)) continue;
            layers.Add(ResolveSlot(templates, resolver,
                slot, itemRef, armorVersion, bodyType, weaponBone, shieldBone));
        }
        return layers;
    }

    private static EquipmentLayer ResolveSlot(
        TemplateStore templates, AssetResolver resolver,
        string slotName, string itemRef,
        string? armorVersion, string? bodyType,
        string? weaponBone, string? shieldBone)
    {
        if (!templates.TryGet(itemRef, out var item))
            return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);

        switch (slotName.ToLowerInvariant())
        {
            case "es_weapon_hand":
            {
                var mesh = templates.GetAttribute(item, "aspect", "model");
                if (string.IsNullOrEmpty(mesh) || string.IsNullOrEmpty(weaponBone))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                return new(Strategy.AttachBone, slotName, itemRef, mesh, null, weaponBone, -1, null);
            }
            case "es_shield_hand":
            {
                var mesh = templates.GetAttribute(item, "aspect", "model");
                if (string.IsNullOrEmpty(mesh) || string.IsNullOrEmpty(shieldBone))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                return new(Strategy.AttachBone, slotName, itemRef, mesh, null, shieldBone, -1, null);
            }
            case "es_feet":
            {
                if (string.IsNullOrEmpty(armorVersion) || string.IsNullOrEmpty(bodyType))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                if (!TryGetArmorSubtypes(resolver, bodyType, out var bootSub, out _))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                var armorType = templates.GetAttribute(item, "defend", "armor_type");
                var armorStyle = templates.GetAttribute(item, "defend", "armor_style");
                if (string.IsNullOrEmpty(armorType) || string.IsNullOrEmpty(armorStyle))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                var meshName = $"m_c_{armorVersion}_boot_{armorType}_{bootSub}";
                var texName = $"b_a_boot_{armorStyle}";
                return new(Strategy.SkinnedLayer, slotName, itemRef, meshName, texName, null, -1, null);
            }
            case "es_head":
            {
                if (string.IsNullOrEmpty(armorVersion))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                var armorType = templates.GetAttribute(item, "defend", "armor_type");
                var armorStyle = templates.GetAttribute(item, "defend", "armor_style");
                if (string.IsNullOrEmpty(armorType) || string.IsNullOrEmpty(armorStyle))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                // Helm has no subtype in armor_lookup — single mesh per (armorVersion, armorType).
                var meshName = $"m_c_{armorVersion}_hlmt_{armorType}";
                var texName = $"b_a_hlmt_{armorStyle}";
                return new(Strategy.SkinnedLayer, slotName, itemRef, meshName, texName, null, -1, null);
            }
            case "es_forearms":
            {
                if (string.IsNullOrEmpty(armorVersion) || string.IsNullOrEmpty(bodyType))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                if (!TryGetArmorSubtypes(resolver, bodyType, out _, out var gntSub))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                var armorType = templates.GetAttribute(item, "defend", "armor_type");
                var armorStyle = templates.GetAttribute(item, "defend", "armor_style");
                if (string.IsNullOrEmpty(armorType) || string.IsNullOrEmpty(armorStyle))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                var meshName = $"m_c_{armorVersion}_gntl_{armorType}_{gntSub}";
                var texName = $"b_a_gntl_{armorStyle}";
                return new(Strategy.SkinnedLayer, slotName, itemRef, meshName, texName, null, -1, null);
            }
            case "es_chest":
            {
                if (string.IsNullOrEmpty(bodyType))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                var armorStyle = templates.GetAttribute(item, "defend", "armor_style");
                if (string.IsNullOrEmpty(armorStyle))
                    return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
                // Body chest armor is a texture override on body subset 1 (the
                // pos_a1 clothing strip). No layered mesh — same skeleton, same
                // submesh; binding b_c_pos_<bodyType>_<armorStyle>.raw as slot 1
                // turns farmboy's white shirt into chain mail.
                var texName = $"b_c_pos_{bodyType}_{armorStyle}";
                return new(Strategy.ChestTexture, slotName, itemRef,
                    null, null, null, 1, texName);
            }
            default:
                // es_spellbook, es_amulet, es_ring_*, etc. — no 3D representation.
                return new(Strategy.None, slotName, itemRef, null, null, null, -1, null);
        }
    }

    /// <summary>Pulls the trailing <c>aN</c> body-type token off a hero's
    /// <c>aspect.model</c> string. <c>m_c_gah_fb_pos_a1</c> → <c>a1</c>;
    /// returns null when the model name doesn't carry one (most monsters).</summary>
    public static string? ExtractBodyType(string? bodyModel)
    {
        if (string.IsNullOrEmpty(bodyModel)) return null;
        var underscore = bodyModel.LastIndexOf('_');
        if (underscore < 0 || underscore == bodyModel.Length - 1) return null;
        var tail = bodyModel[(underscore + 1)..];
        if (tail.Length < 2 || tail[0] != 'a') return null;
        for (int i = 1; i < tail.Length; i++)
            if (!char.IsDigit(tail[i])) return null;
        return tail;
    }
}

namespace SiegeFX.Core.Actors;

/// <summary>21d-2a-viii — runtime override of an actor template's variant
/// attributes. The character creator picks a body / skin / pants combination
/// before the player template is consumed, then asks the spawner to substitute
/// those values without mutating the parsed <c>TemplateStore</c>.
///
/// <para>Only the variant axes that DS1's character_select.gas exposes are
/// modelled here. Other template fields (skills, equipment, brain, chore
/// dictionary) keep flowing through the normal specializes chain.</para>
///
/// <para>Texture overrides are surfaced separately rather than threaded
/// through the spawner because RenderHost.ResolveActorTexture is the one
/// place that resolves textures per-actor, and it already has a slot-1
/// override path (see <c>_chestTexOverrideName</c> from the layered
/// equipment slice). The CharacterCreator host code copies
/// <see cref="SkinTextureName"/> / <see cref="ClothingTextureName"/> into
/// the renderer's per-slot override fields at spawn time.</para></summary>
public sealed record TemplateOverride
{
    /// <summary>Replacement for <c>[aspect][model]</c> — e.g. <c>m_c_gah_fb_pos_a3</c>
    /// when the user picks body type 3. Null = keep the template's authored model.</summary>
    public string? ModelName { get; init; }

    /// <summary>Replacement for <c>[aspect][textures] { 0 = ... }</c> — the skin
    /// texture (face / hair / arms region of the body .raw). Null = keep template
    /// default (e.g. <c>b_c_gah_fb_skin_04</c> for stock farmboy).</summary>
    public string? SkinTextureName { get; init; }

    /// <summary>Replacement for <c>[aspect][textures] { 1 = ... }</c> — the
    /// clothing/pants texture. Null = keep template default (e.g.
    /// <c>b_c_pos_a1_008</c> for stock farmboy).</summary>
    public string? ClothingTextureName { get; init; }
}

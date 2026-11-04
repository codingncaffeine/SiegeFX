using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

public class NpcArmorTests
{
    // Trimmed copy of the shipped Elddim Legionnaire: the skin is the only
    // [aspect][textures] entry, so the look has to come from [inventory][equipment].
    const string Gas = """
        [t:template,n:actor] { [aspect] { model = m_c_gah_fb_pos_a1; } [body] { armor_version = gah_fb; } }
        [t:template,n:mp_guard]
        {
            specializes = actor;
            [aspect] { [textures] { 0 = b_c_gah_fb_skin_02; } }
            [inventory]
            {
                [equipment]
                {
                    es_chest = bd_un_pl_f_g_c_fun;
                    es_head = he_op_pl_eyes;
                    es_weapon_hand = #sd_g_c_st_1h_avg:o_avg;
                }
            }
        }
        [t:template,n:bd_un_pl_f_g_c_fun] { [defend] { armor_style = 014; armor_type = a3; } }
        [t:template,n:he_op_pl_eyes] { [defend] { armor_style = 020; armor_type = type20; } }
        [t:template,n:he_op_pl_roman] { [defend] { armor_style = 021; armor_type = type21; } }
        """;

    static (TemplateStore Store, Template Guard) Load()
    {
        var store = new TemplateStore();
        store.RegisterFromGasText(Gas);
        Assert.True(store.TryGet("mp_guard", out var guard));
        return (store, guard);
    }

    [Fact]
    public void GuardWearsPlateChestAndHelm()
    {
        var (store, guard) = Load();
        var slots = EquipmentResolver.CollectActorSlots(store, guard, instance: null);
        var layers = EquipmentResolver.Resolve(store, new AssetResolver(), guard, slots);

        var chest = Assert.Single(layers, l => l.SlotName == "es_chest");
        Assert.Equal(EquipmentResolver.Strategy.ChestTexture, chest.Strategy);
        Assert.Equal(1, chest.OverrideTextureSlot);
        Assert.Equal("b_c_pos_a1_014", chest.OverrideBaseName);

        var helm = Assert.Single(layers, l => l.SlotName == "es_head");
        Assert.Equal(EquipmentResolver.Strategy.SkinnedLayer, helm.Strategy);
        Assert.Equal("m_c_gah_fb_hlmt_type20", helm.MeshBaseName);
        Assert.Equal("b_a_hlmt_020", helm.TextureBaseName);
    }

    [Fact]
    public void InstanceEquipmentOverridesTheTemplate()
    {
        var (store, guard) = Load();
        var inst = GasDocument.Load("""
            [t:mp_guard,n:0x01]
            {
                [inventory] { [equipment] { es_head = he_op_pl_roman; } }
            }
            """u8.ToArray()).Roots[0];
        var slots = EquipmentResolver.CollectActorSlots(store, guard, inst);
        Assert.Equal("he_op_pl_roman", slots["es_head"]);
        Assert.Equal("bd_un_pl_f_g_c_fun", slots["es_chest"]);
        Assert.Equal(3, slots.Count);
    }
}

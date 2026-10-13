using System.Buffers.Binary;
using System.Text;
using SiegeFX.Core.Assets;
using SiegeFX.OriginalSaves;
using SiegeFX.Core.Tank;

namespace SiegeFX.Tests;

/// <summary>Builds world.xidx/world.xdat pairs from a small tree, laid out
/// the way the original writes them: entries in the given order, children
/// numbered depth-first, the index listing values then children per node.</summary>
static class XferBuilder
{
    public abstract record Entry(string Name);
    public sealed record Val(string Name, byte[] Bytes) : Entry(Name);
    public sealed record Node(string Name, params Entry[] Entries) : Entry(Name);

    public static Val Bool(string n, bool b) => new(n, [(byte)(b ? 1 : 0)]);
    public static Val Int(string n, int i) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, i); return new(n, b); }
    public static Val Float(string n, float f) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, f); return new(n, b); }
    public static Val Text(string n, string s) => new(n, [.. Encoding.Latin1.GetBytes(s), 0]);
    public static Val Wide(string n, string s) => new(n, [.. Encoding.Unicode.GetBytes(s), 0, 0]);

    public static (byte[] Index, byte[] Data) Build(Node root)
    {
        var data = new List<byte>("DSigDXfr"u8.ToArray()) { 0, 0, 1, 0, 0, 0, 0, 0 };
        var records = new List<(List<uint> Vals, List<(uint, uint)> Kids)>();
        Walk(root);
        var w = new List<uint> { 0x10000, 0, (uint)records.Count };
        foreach (var (vals, kids) in records)
        {
            w.Add((uint)vals.Count); w.AddRange(vals);
            w.Add((uint)kids.Count); foreach (var (o, n) in kids) { w.Add(o); w.Add(n); }
        }
        var index = new byte[8 + w.Count * 4];
        "DSigIXfr"u8.CopyTo(index);
        for (int i = 0; i < w.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(8 + i * 4), w[i]);
        return (index, data.ToArray());

        uint Walk(Node n)
        {
            uint id = (uint)records.Count;
            var rec = (new List<uint>(), new List<(uint, uint)>());
            records.Add(rec);
            foreach (var e in n.Entries)
            {
                uint at = (uint)data.Count;
                data.AddRange(Encoding.Latin1.GetBytes(e.Name)); data.Add(0);
                if (e is Val v) { data.AddRange(v.Bytes); rec.Item1.Add(at); }
                else rec.Item2.Add((at, Walk((Node)e)));
            }
            return id;
        }
    }
}

public class XferTreeTests
{
    static XferTree Sample() => Load(new XferBuilder.Node("",
        new XferBuilder.Node("uigame", XferBuilder.Bool("tutorial_tips", false), XferBuilder.Int("data_bar_x", 12)),
        new XferBuilder.Node("world",
            XferBuilder.Text("before", "a"),
            new XferBuilder.Node("m_pvictory",
                new XferBuilder.Node("m_quests",
                    new XferBuilder.Node("0", new XferBuilder.Node("_value",
                        XferBuilder.Text("sname", "quest_one"), XferBuilder.Wide("sscreenname", "Do the Thing"),
                        XferBuilder.Bool("bactive", true), XferBuilder.Bool("bcompleted", false), XferBuilder.Bool("bviewed", true))))),
            XferBuilder.Text("after", "b"),
            new XferBuilder.Node("m_pgameauditor", new XferBuilder.Node("m_entries",
                new XferBuilder.Node("0", XferBuilder.Text("m_key", "talked_once"), XferBuilder.Int("m_int", 0),
                    XferBuilder.Bool("m_bool", true), XferBuilder.Float("m_float", 0), XferBuilder.Text("m_string", "")),
                new XferBuilder.Node("1", XferBuilder.Text("m_key", "ge_player_kill_krug"), XferBuilder.Int("m_int", 7),
                    XferBuilder.Bool("m_bool", false), XferBuilder.Float("m_float", 0), XferBuilder.Text("m_string", ""))))),
        new XferBuilder.Node("godb", new XferBuilder.Node("m_globaldb",
            new XferBuilder.Node("0", XferBuilder.Text("m_datatemplate", "krug")),
            new XferBuilder.Node("1", XferBuilder.Text("m_datatemplate", "edgaar"))))));

    static XferTree Load(XferBuilder.Node root)
    {
        var (i, d) = XferBuilder.Build(root);
        return XferTree.Load(i, d);
    }

    [Fact]
    public void Values_and_children_keep_the_order_they_were_written_in()
    {
        var world = Sample().Find("/world")!;
        Assert.Equal(["before", "m_pvictory", "after", "m_pgameauditor"], world.Entries.Select(e => e.Name));
        Assert.Equal("/world/m_pvictory/m_quests", Sample().Find("world/m_pvictory/m_quests")!.Path);
    }

    [Fact]
    public void Values_read_by_size()
    {
        var ui = Sample().Find("/uigame")!;
        Assert.False(ui.Value("tutorial_tips")!.AsBool);
        Assert.Equal(12, ui.Value("data_bar_x")!.AsInt32);
        Assert.Null(ui.Value("data_bar_x")!.AsBool);
        var q = Sample().Find("/world/m_pvictory/m_quests/0/_value")!;
        Assert.Equal("quest_one", q.Value("sname")!.AsText);
        Assert.Equal("Do the Thing", q.Value("sscreenname")!.AsWideText);
        Assert.Equal("L\"Do the Thing\"", q.Value("sscreenname")!.Display());
    }

    [Fact]
    public void The_world_facts_come_from_their_paths()
    {
        var t = Sample();
        Assert.Equal([new OriginalQuest("quest_one", "Do the Thing", "", true, false, true)], OriginalWorldFacts.Quests(t));
        Assert.Equal([new OriginalFlag("talked_once", "true"), new OriginalFlag("ge_player_kill_krug", "7")], OriginalWorldFacts.Flags(t));
        Assert.False(OriginalWorldFacts.TutorialTips(t));
        Assert.Equal(2, OriginalWorldFacts.ObjectCount(t));
    }

    [Fact]
    public void Loaded_nodes_saved_objects_and_retired_scids_come_from_their_paths()
    {
        static XferBuilder.Node Owned(string name, params uint[] guids) => new(name,
            [new XferBuilder.Node("m_prevownednodes", [.. guids.Select((g, i) =>
                new XferBuilder.Node(i.ToString(), new XferBuilder.Val("_value", Guid(g))))])]);
        static byte[] Guid(uint g) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, g); return b; }
        var t = Load(new XferBuilder.Node("",
            new XferBuilder.Node("m_pworldterrain", new XferBuilder.Node("siegeengine", new XferBuilder.Node("m_frustumcoll",
                Owned("0", 0xb803530c, 0x5353287c), Owned("1", 0x5353287c, 0x0c5303b8)))),
            new XferBuilder.Node("godb", new XferBuilder.Node("m_globaldb",
                new XferBuilder.Node("0", XferBuilder.Int("m_scid", 0x01c00009)),
                new XferBuilder.Node("1", XferBuilder.Int("m_scid", 0x01c0001d))),
                new XferBuilder.Node("m_retiredscidcoll",
                    new XferBuilder.Node("0", XferBuilder.Int("_value", 0x01c00008)),
                    new XferBuilder.Node("1", XferBuilder.Int("_value", 0x01c00427))))));
        Assert.Equal(new HashSet<uint> { 0xb803530c, 0x5353287c, 0x0c5303b8 }, OriginalWorldFacts.LoadedNodes(t));
        Assert.Equal(new HashSet<uint> { 0x01c00009, 0x01c0001d }, OriginalWorldFacts.ObjectScids(t));
        Assert.Equal(new HashSet<uint> { 0x01c00008, 0x01c00427 }, OriginalWorldFacts.RetiredScids(t));
        Assert.Empty(OriginalWorldFacts.LoadedNodes(Sample()));
        Assert.Empty(OriginalWorldFacts.RetiredScids(Sample()));
    }

    [Fact]
    public void A_position_reads_its_node_id_as_info_gas_prints_it()
    {
        var b = new byte[16];
        BinaryPrimitives.WriteSingleLittleEndian(b, 1.5f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4), -2f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), 0f);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), 0x5353287c);
        var v = Load(new XferBuilder.Node("", new XferBuilder.Val("m_cameraposition", b))).Root.Value("m_cameraposition")!;
        Assert.Equal("[16] ~pos (1.5, -2, 0) node 0x5353287c", v.Display());
    }

    [Fact]
    public void A_small_number_is_not_read_as_wide_text()
    {
        var v = Load(new XferBuilder.Node("", XferBuilder.Int("m_magic", 0x08d9))).Root.Value("m_magic")!;
        Assert.StartsWith("2265 ", v.Display());
    }

    public static TheoryData<string, Func<byte[], byte[], (byte[], byte[])>> Malformed => new()
    {
        { "bad index magic", (i, d) => { i[0] = (byte)'X'; return (i, d); } },
        { "bad data magic", (i, d) => { d[0] = (byte)'X'; return (i, d); } },
        { "truncated index", (i, d) => (i[..^4], d) },
        { "trailing word", (i, d) => ([.. i, 0, 0, 0, 0], d) },
        { "offset past the data", (i, d) => (i, d[..20]) },
        { "node count too large", (i, d) => { BinaryPrimitives.WriteUInt32LittleEndian(i.AsSpan(16), 9999); return (i, d); } },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Malformed_input_is_rejected(string what, Func<byte[], byte[], (byte[], byte[])> spoil)
    {
        var (i, d) = XferBuilder.Build(new XferBuilder.Node("",
            new XferBuilder.Node("a", XferBuilder.Int("x", 1)), new XferBuilder.Node("b", XferBuilder.Int("y", 2))));
        var (bi, bd) = spoil(i.ToArray(), d.ToArray());
        Assert.Throws<InvalidDataException>(() => XferTree.Load(bi, bd));
        Assert.NotNull(what);
    }

    [Fact]
    public void A_node_referenced_twice_is_rejected()
    {
        var (i, d) = XferBuilder.Build(new XferBuilder.Node("",
            new XferBuilder.Node("a", XferBuilder.Int("x", 1)), new XferBuilder.Node("b", XferBuilder.Int("y", 2))));
        // Root record: vc=0, cc=2, (off a, 1), (off b, 2) → point b at node 1 too.
        BinaryPrimitives.WriteUInt32LittleEndian(i.AsSpan(8 + 4 * 8), 1);
        Assert.Throws<InvalidDataException>(() => XferTree.Load(i, d));
    }
}

public class OriginalSaveTests
{
    const string Info = """
        [info]
        {
            hero_name = "D";
            is_auto_save = false;
            is_dsx = true;
            is_quick_save = false;
            map_name = map_expansion;
            map_screen_name = "Legends of Aranna";
            player_name = "D";
            product_version = 1.50.0.0056 (msqa:2003.10.1601);
            region = a1_r1_town;
            save_time = 134363309084250000;
            screen_name = "D (0-02-25)";
            single_player = true;
            teleport = map_expansion:n:1.82,-1.25,-0.71,0x695bf9a8;
            time_elapsed = 145.5;
            [tank_manifest]
            {
                [0]
                {
                    creator_id = 'GPG!';
                    data_crc32 = 0x12345678;
                    file_name = Logic.dsres;
                    tank_type = tank_resource;
                    version = 0.00.0.0000 (msqa:02.08.0701);
                }
            }
        }
        """;

    const string Party = """
        [party]
        {
            [members]
            {
                [0]
                {
                    [member]
                    {
                        actor_class = "Farmer";
                        combat_orders = co_hold;
                        disposition_orders = ad_defensive;
                        focus_orders = fo_weakest;
                        gold = 36;
                        movement_orders = mo_free;
                        screen_name = "D";
                        selected_active_location = il_active_melee_weapon;
                        template_name = farmgirl;
                        [inventory]
                        {
                            [0] { equip_slot = es_feet; gold_value = 2; inv_location = il_main; template_name = bo_bo_le_light; }
                            [1] { gold_value = 8; grid_x = 255; grid_y = 30; inv_location = il_main; template_name = spell_fireshot; }
                        }
                        [skills]
                        {
                            melee_level = 1.00195;
                            melee_xp = 187.9;
                            uber_level = 1.08;
                            uber_xp = 200;
                        }
                        [skins] { 0 = b_skin,b_hair; 1 = b_pants,b_shirt; }
                    }
                }
            }
            [party] { gold = 36; hero_index = 0; }
        }
        """;

    [Fact]
    public void Info_gas_reads_into_the_load_menu_fields()
    {
        var i = OriginalSaveInfo.Parse(GasDocument.Parse(Info));
        Assert.Equal(("D (0-02-25)", "D", "a1_r1_town"), (i.ScreenName, i.HeroName, i.Region));
        Assert.True(i.IsExpansion);
        Assert.False(i.IsAutoSave);
        Assert.Equal(new OriginalPosition("map_expansion", 1.82f, -1.25f, -0.71f, 0x695bf9a8), i.Position);
        Assert.Equal(new DateTime(2026, 10, 13, 2, 8, 28, DateTimeKind.Utc), i.SavedAtUtc!.Value.AddTicks(-(i.SavedAtUtc.Value.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal("Logic.dsres", Assert.Single(i.TankManifest).FileName);
    }

    [Fact]
    public void Party_gas_reads_members_orders_skills_and_inventory()
    {
        var p = OriginalParty.Parse(GasDocument.Parse(Party));
        Assert.Equal((36L, 0), (p.Gold, p.HeroIndex));
        var hero = p.Hero!;
        Assert.Equal(("D", "farmgirl", "Farmer"), (hero.ScreenName, hero.TemplateName, hero.ActorClass));
        Assert.Equal((0, 2, 2), (hero.Orders.MoveOrder, hero.Orders.AtkOrder, hero.Orders.TgtOrder));
        Assert.Equal("Move Freely / Hold Fire / Weakest", hero.Orders.Describe());
        Assert.Equal(1.08, hero.Level);
        Assert.Equal((1.00195, 187.9), hero.Skills["melee"]);
        Assert.Equal(["b_skin,b_hair", "b_pants,b_shirt"], hero.Skins);
        Assert.Equal(new OriginalItem("spell_fireshot", "il_main", "", 255, 30, 8), hero.Inventory[1]);
        Assert.Equal("es_feet", hero.Inventory[0].EquipSlot);
    }

    [Theory]
    [InlineData("mo_free", "co_free", "fo_closest", 0, 0, 0)]
    [InlineData("mo_limited", "co_limited", "fo_strongest", 1, 1, 1)]
    [InlineData("mo_hold", "co_hold", "fo_weakest", 2, 2, 2)]
    [InlineData("mo_new", "", "fo_?", -1, -1, -1)]
    public void Orders_map_to_the_siegefx_encoding(string mo, string co, string fo, int move, int atk, int tgt)
    {
        var o = new OriginalOrders(mo, co, fo, "ad_defensive");
        Assert.Equal((move, atk, tgt), (o.MoveOrder, o.AtkOrder, o.TgtOrder));
    }

    [Theory]
    [InlineData(0x707d3u, 0x20002u, "2003.07.0202")]
    [InlineData(0xa07d3u, 0x10010u, "2003.10.1601")]
    public void The_tank_build_stamp_unpacks_year_month_day_build(uint v2, uint v3, string stamp)
    {
        var h = new TankHeader { ProductVersion = new ProductVersion(0x50000, v2, v3) };
        Assert.Equal(stamp, OriginalSave.BuildStamp(h));
    }

    [Theory]
    [InlineData("")]
    [InlineData("map_world:n:1,2,3")]
    [InlineData("map_world:x:1,2,3,0x1")]
    [InlineData("map_world:n:a,2,3,0x1")]
    public void A_malformed_position_is_null(string text) => Assert.Null(OriginalPosition.TryParse(text));

    [Fact]
    public void A_whole_save_opens_from_its_tank()
    {
        var (idx, dat) = XferBuilder.Build(new XferBuilder.Node("",
            new XferBuilder.Node("uigame", XferBuilder.Bool("tutorial_tips", true))));
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-{Guid.NewGuid():N}.dsasave");
        var w = new TankWriter();
        w.Add("/info.gas", Encoding.ASCII.GetBytes(Info.Replace("is_auto_save = false", "is_auto_save = true")));
        w.Add("/party.gas", Encoding.ASCII.GetBytes(Party));
        w.Add("/world.xidx", idx);
        w.Add("/world.xdat", dat);
        try
        {
            w.Write(path, DateTime.UtcNow);
            var s = OriginalSave.Open(path);
            Assert.Equal(OriginalSaveKind.AutoSave, s.Kind);
            Assert.Equal("D", s.Party.Hero!.ScreenName);
            Assert.True(OriginalWorldFacts.TutorialTips(s.World!));
            Assert.Null(s.Portrait);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_tank_without_info_gas_is_not_an_original_save()
    {
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-{Guid.NewGuid():N}.dssave");
        var w = new TankWriter();
        w.Add("/party.gas", Encoding.ASCII.GetBytes(Party));
        try
        {
            w.Write(path, DateTime.UtcNow);
            Assert.Throws<InvalidDataException>(() => OriginalSave.Open(path));
        }
        finally { File.Delete(path); }
    }
}

public class MapPlacementsTests
{
    [Fact]
    public void Placements_are_found_by_scid_or_template_in_any_region()
    {
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-{Guid.NewGuid():N}.dsmap");
        var w = new TankWriter();
        w.Add("/world/maps/map_world/regions/fh_r1/objects/actor.gas",
              Encoding.ASCII.GetBytes("[t:edgaar,n:0x01C00B13] { }\n[t:krug_scout,n:0x01C00002] { }"));
        w.Add("/world/maps/map_world/regions/fh_r2/objects/non_interactive.gas",
              Encoding.ASCII.GetBytes("[t:edgaar_hut,n:0x01D00001] { }"));
        w.Add("/world/maps/map_world/regions/fh_r2/terrain_nodes/nodes.gas", Encoding.ASCII.GetBytes("[t:x,n:0x01C00B13] { }"));
        try
        {
            w.Write(path, DateTime.UtcNow);
            using var tank = TankFile.Open(path);
            var map = new TankReader(tank);

            var byScid = MapPlacements.Find(map, "0x01c00b13");
            Assert.Equal(new MapPlacement("fh_r1", "actor.gas", "edgaar", 0x01C00B13), Assert.Single(byScid.Matches));
            Assert.Equal((2, 2), (byScid.FilesScanned, byScid.RegionsScanned));

            Assert.Equal(["edgaar", "edgaar_hut"], MapPlacements.Find(map, "edgaar").Matches.Select(m => m.Template));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Placements_are_found_by_a_set_of_scids_and_carry_their_node()
    {
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-{Guid.NewGuid():N}.dsmap");
        var w = new TankWriter();
        w.Add("/world/maps/map_world/regions/fh_r1/objects/non_interactive.gas", Encoding.ASCII.GetBytes(
            "[t:barrel_glb_fh_r1,n:0x01C00009] { [placement] { position = 1.5,0,-2.25,0xb803530c; } }\n" +
            "[t:barrel_glb_fh_r1,n:0x01C0000A] { [placement] { position = 0,0,0,0x5353287c; } }\n" +
            "[t:crate_glb_fh_r1,n:0x01C0001D] { [placement] { position = 0,0,0,0xB803530C; } }\n" +
            "[t:trigger_generic,n:0x01C00480] { }"));
        try
        {
            w.Write(path, DateTime.UtcNow);
            using var tank = TankFile.Open(path);
            var r = MapPlacements.WithScids(new TankReader(tank), new HashSet<uint> { 0x01C00009, 0x01C0001D, 0x01C00480, 0x02000001 });
            Assert.Equal([
                new MapPlacement("fh_r1", "non_interactive.gas", "barrel_glb_fh_r1", 0x01C00009, 0xb803530c),
                new MapPlacement("fh_r1", "non_interactive.gas", "crate_glb_fh_r1", 0x01C0001D, 0xb803530c),
                new MapPlacement("fh_r1", "non_interactive.gas", "trigger_generic", 0x01C00480),
            ], r.Matches);
        }
        finally { File.Delete(path); }
    }
}

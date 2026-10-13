using System.Globalization;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.OriginalSaves;

/// <summary>What kind of file the original game wrote.</summary>
public enum OriginalSaveKind { Save, AutoSave, QuickSave, Party }

/// <summary>
/// A save written by the original game: a tank archive (<c>.dssave</c>,
/// <c>.dsasave</c>, or the multiplayer character file <c>.dsparty</c>) holding
/// <c>info.gas</c> (what the load menu shows), <c>party.gas</c> (every party
/// member: stats, orders, inventory), <c>portrait-0.bmp</c>, <c>thumb.bmp</c>,
/// and — not in .dsparty — the world as <c>world.xidx</c>/<c>world.xdat</c>.
/// Read-only. Layout and evidence: docs/original-save-format.md.
/// </summary>
public sealed class OriginalSave
{
    public string Path { get; }
    public OriginalSaveKind Kind { get; }
    public TankHeader TankHeader { get; }
    public IReadOnlyList<string> Files { get; }
    public OriginalSaveInfo Info { get; }
    public OriginalParty Party { get; }
    public byte[]? Portrait { get; }
    public byte[]? Thumbnail { get; }

    readonly Lazy<XferTree?> _world;
    /// <summary>The world tree; null for a .dsparty (no world in it). Parsed
    /// on first use — it is most of the file.</summary>
    public XferTree? World => _world.Value;

    OriginalSave(string path, TankHeader header, IReadOnlyList<string> files, OriginalSaveInfo info,
                 OriginalParty party, byte[]? portrait, byte[]? thumb, Func<XferTree?> world)
    {
        Path = path;
        TankHeader = header;
        Files = files;
        Info = info;
        Party = party;
        Portrait = portrait;
        Thumbnail = thumb;
        _world = new Lazy<XferTree?>(world);
        Kind = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".dsparty" => OriginalSaveKind.Party,
            _ when info.IsAutoSave => OriginalSaveKind.AutoSave,
            _ when info.IsQuickSave => OriginalSaveKind.QuickSave,
            _ => OriginalSaveKind.Save,
        };
    }

    public static OriginalSave Open(string path)
    {
        using var tank = TankFile.Open(path);
        var reader = new TankReader(tank);
        var files = reader.ListFiles().OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        byte[]? Get(string name) =>
            reader.TryGetFile("/" + name, out _) ? reader.ExtractToMemory("/" + name) : null;

        var infoBytes = Get("info.gas") ?? throw new InvalidDataException($"{path}: no info.gas — not an original-game save");
        var partyBytes = Get("party.gas") ?? throw new InvalidDataException($"{path}: no party.gas — not an original-game save");
        var info = OriginalSaveInfo.Parse(GasDocument.Load(infoBytes));
        var party = OriginalParty.Parse(GasDocument.Load(partyBytes));
        var idx = Get("world.xidx");
        var dat = Get("world.xdat");
        if ((idx is null) != (dat is null))
            throw new InvalidDataException($"{path}: has only one of world.xidx / world.xdat");
        return new OriginalSave(path, tank.Header, files, info, party, Get("portrait-0.bmp"), Get("thumb.bmp"),
            () => idx is null ? null : XferTree.Load(idx, dat!));
    }

    /// <summary>The build stamp the tank header carries in its "product
    /// version" words: 16-bit halves holding year, month, day and build, the
    /// same stamp info.gas shows as <c>msqa:YYYY.MM.DDBB</c>.</summary>
    public static string BuildStamp(TankHeader h)
    {
        uint year = h.ProductVersion.V2 & 0xFFFF, month = h.ProductVersion.V2 >> 16;
        uint day = h.ProductVersion.V3 & 0xFFFF, build = h.ProductVersion.V3 >> 16;
        return $"{year:0000}.{month:00}.{day:00}{build:00}";
    }
}

/// <summary>info.gas — what the original's load menu shows.</summary>
public sealed record OriginalSaveInfo(
    string ScreenName, string HeroName, string PlayerName,
    string MapName, string MapScreenName, string Region, OriginalPosition? Position,
    double TimeElapsedSeconds, DateTime? SavedAtUtc,
    bool IsAutoSave, bool IsQuickSave, bool IsExpansion, bool SinglePlayer,
    string ProductVersion, IReadOnlyList<OriginalTankManifestEntry> TankManifest,
    IReadOnlyDictionary<string, string> Raw)
{
    public static OriginalSaveInfo Parse(GasDocument doc)
    {
        var info = doc.Roots.FirstOrDefault(n => n.Header.Equals("info", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidDataException("info.gas has no [info] block");
        var a = Gas.Attributes(info);
        DateTime? saved = long.TryParse(a.Get("save_time"), out var ft) && ft > 0
            ? DateTime.FromFileTimeUtc(ft) : null;
        var manifest = info.Children
            .FirstOrDefault(c => c.Header.Equals("tank_manifest", StringComparison.OrdinalIgnoreCase))?
            .Children.Select(c => Gas.Attributes(c))
            .Select(m => new OriginalTankManifestEntry(
                m.Get("file_name"), m.Get("tank_type"), m.Get("version"),
                m.Get("guid"), m.Get("data_crc32"), m.Get("index_crc32"), m.Get("utc_build_time")))
            .ToList() ?? [];
        return new OriginalSaveInfo(
            a.Get("screen_name"), a.Get("hero_name"), a.Get("player_name"),
            a.Get("map_name"), a.Get("map_screen_name"), a.Get("region"),
            OriginalPosition.TryParse(a.Get("teleport")),
            Gas.Double(a.Get("time_elapsed")), saved,
            Gas.Bool(a.Get("is_auto_save")), Gas.Bool(a.Get("is_quick_save")),
            Gas.Bool(a.Get("is_dsx")), Gas.Bool(a.Get("single_player")),
            a.Get("product_version"), manifest, a);
    }
}

public sealed record OriginalTankManifestEntry(
    string FileName, string TankType, string Version, string Guid,
    string DataCrc32, string IndexCrc32, string UtcBuildTime);

/// <summary>A position as the original stores it: an offset inside one
/// terrain node, e.g. <c>map_world:n:-0.98,0.91,-0.09,0x5353287c</c>.</summary>
public sealed record OriginalPosition(string Map, float X, float Y, float Z, uint Node)
{
    public static OriginalPosition? TryParse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 3 || parts[1] != "n") return null;
        var f = parts[2].Split(',');
        if (f.Length != 4) return null;
        var ci = CultureInfo.InvariantCulture;
        if (!float.TryParse(f[0], NumberStyles.Float, ci, out var x) ||
            !float.TryParse(f[1], NumberStyles.Float, ci, out var y) ||
            !float.TryParse(f[2], NumberStyles.Float, ci, out var z)) return null;
        var hex = f[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? f[3][2..] : f[3];
        if (!uint.TryParse(hex, NumberStyles.HexNumber, ci, out var node)) return null;
        return new OriginalPosition(parts[0], x, y, z, node);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Map} node 0x{Node:x8} ({X:0.##}, {Y:0.##}, {Z:0.##})");
}

/// <summary>party.gas — the whole party.</summary>
public sealed record OriginalParty(long Gold, int HeroIndex, IReadOnlyList<OriginalPartyMember> Members)
{
    public OriginalPartyMember? Hero => HeroIndex >= 0 && HeroIndex < Members.Count ? Members[HeroIndex] : null;

    public static OriginalParty Parse(GasDocument doc)
    {
        var top = doc.Roots.FirstOrDefault(n => n.Header.Equals("party", StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidDataException("party.gas has no [party] block");
        var members = top.Children
            .FirstOrDefault(c => c.Header.Equals("members", StringComparison.OrdinalIgnoreCase))?
            .Children
            .Select(slot => slot.Children.FirstOrDefault(c => c.Header.Equals("member", StringComparison.OrdinalIgnoreCase)))
            .Where(m => m is not null)
            .Select(m => OriginalPartyMember.Parse(m!))
            .ToList() ?? [];
        var summary = Gas.Attributes(top.Children.FirstOrDefault(c => c.Header.Equals("party", StringComparison.OrdinalIgnoreCase)));
        return new OriginalParty(
            long.TryParse(summary.Get("gold"), out var g) ? g : 0,
            int.TryParse(summary.Get("hero_index"), out var h) ? h : 0,
            members);
    }
}

public sealed record OriginalPartyMember(
    string ScreenName, string TemplateName, string ActorClass, long Gold,
    OriginalOrders Orders, string SelectedActiveLocation,
    IReadOnlyDictionary<string, (double Level, double Xp)> Skills,
    IReadOnlyList<string> Skins, IReadOnlyList<OriginalItem> Inventory)
{
    /// <summary>The overall character level DS1 shows ("uber" skill).</summary>
    public double Level => Skills.TryGetValue("uber", out var s) ? s.Level : 0;

    internal static OriginalPartyMember Parse(GasNode m)
    {
        var a = Gas.Attributes(m);
        var skills = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        var sk = Gas.Attributes(m.Children.FirstOrDefault(c => c.Header.Equals("skills", StringComparison.OrdinalIgnoreCase)));
        foreach (var key in sk.Keys.Where(k => k.EndsWith("_level", StringComparison.OrdinalIgnoreCase)))
        {
            var name = key[..^"_level".Length];
            skills[name] = (Gas.Double(sk.Get(key)), Gas.Double(sk.Get(name + "_xp")));
        }
        var skins = Gas.Attributes(m.Children.FirstOrDefault(c => c.Header.Equals("skins", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
        var items = m.Children.FirstOrDefault(c => c.Header.Equals("inventory", StringComparison.OrdinalIgnoreCase))?
            .Children.Select(i => OriginalItem.Parse(Gas.Attributes(i))).ToList() ?? [];
        return new OriginalPartyMember(
            a.Get("screen_name"), a.Get("template_name"), a.Get("actor_class"),
            long.TryParse(a.Get("gold"), out var g) ? g : 0,
            new OriginalOrders(a.Get("movement_orders"), a.Get("combat_orders"),
                               a.Get("focus_orders"), a.Get("disposition_orders")),
            a.Get("selected_active_location"), skills, skins, items);
    }
}

/// <summary>One inventory row. Grid positions are the inventory window's
/// pixel coordinates in 32-pixel cells (first cell at 255,30); equipped
/// items carry an equip slot instead.</summary>
public sealed record OriginalItem(string TemplateName, string InvLocation, string EquipSlot,
                                  int? GridX, int? GridY, long GoldValue)
{
    internal static OriginalItem Parse(IReadOnlyDictionary<string, string> a) => new(
        a.Get("template_name"), a.Get("inv_location"), a.Get("equip_slot"),
        int.TryParse(a.Get("grid_x"), out var x) ? x : null,
        int.TryParse(a.Get("grid_y"), out var y) ? y : null,
        long.TryParse(a.Get("gold_value"), out var g) ? g : 0);
}

/// <summary>A member's Field Commands orders as the original stores them,
/// with the mapping to SiegeFX's 0/1/2 encoding (the same one
/// <see cref="SiegeFX.Core.Save.CompanionSnapshot.MoveOrder"/> uses). Every value below was
/// observed in a save written after setting that button in the original.</summary>
public sealed record OriginalOrders(string Movement, string Combat, string Focus, string Disposition)
{
    /// <summary>mo_free / mo_limited / mo_hold → Move Freely / Engage / Hold Ground.</summary>
    public int MoveOrder => Movement switch { "mo_free" => 0, "mo_limited" => 1, "mo_hold" => 2, _ => -1 };
    /// <summary>co_free / co_limited / co_hold → Attack Freely / Defend / Hold Fire.</summary>
    public int AtkOrder => Combat switch { "co_free" => 0, "co_limited" => 1, "co_hold" => 2, _ => -1 };
    /// <summary>fo_closest / fo_strongest / fo_weakest → Closest / Strongest / Weakest.</summary>
    public int TgtOrder => Focus switch { "fo_closest" => 0, "fo_strongest" => 1, "fo_weakest" => 2, _ => -1 };

    public string Describe() =>
        $"{MoveLabel(MoveOrder) ?? Movement} / {AtkLabel(AtkOrder) ?? Combat} / {TgtLabel(TgtOrder) ?? Focus}";

    static string? MoveLabel(int v) => v switch { 0 => "Move Freely", 1 => "Engage", 2 => "Hold Ground", _ => null };
    static string? AtkLabel(int v) => v switch { 0 => "Attack Freely", 1 => "Defend", 2 => "Hold Fire", _ => null };
    static string? TgtLabel(int v) => v switch { 0 => "Closest", 1 => "Strongest", 2 => "Weakest", _ => null };
}

/// <summary>Small helpers over parsed GAS blocks.</summary>
static class Gas
{
    public static IReadOnlyDictionary<string, string> Attributes(GasNode? node)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node is null) return d;
        foreach (var a in node.Attributes) d[a.Name] = Unquote(a.Value);
        return d;
    }

    public static string Get(this IReadOnlyDictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) ? v : "";

    public static double Double(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static bool Bool(string s) => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";

    static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
}

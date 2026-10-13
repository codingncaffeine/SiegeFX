using System.Globalization;
using SiegeFX.OriginalSaves;

/// <summary><c>siegefx save</c> — read saves written by the original game
/// (.dssave, .dsasave, .dsparty). Read-only. Format notes and the evidence
/// behind every mapping: docs/original-save-format.md.</summary>
static class SaveCommands
{
    const string Usage =
        "usage: siegefx save inspect <file> [--all]                  summary: save info, party, quests, flags\n" +
        "       siegefx save tree    <file> [--path=/world] [--depth=N] [--find=TEXT]\n" +
        "                                                            the world tree (world.xidx + world.xdat)\n" +
        "       siegefx save diff    <a> <b> [--all] [--ignore=n1,n2] what changed between two saves\n" +
        "       siegefx save locate  <World.dsmap> <0xSCID | template>  where a save object was placed in the map\n" +
        "       siegefx save retired <file> <World.dsmap> [--all]   map objects the game will not create again\n" +
        "                                                            (smashed, killed, used up)";

    public static int Dispatch(string[] a)
    {
        if (a.Length < 2) { Console.Error.WriteLine(Usage); return 1; }
        var files = a.Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList();
        var opts = a.Skip(1).Where(x => x.StartsWith("--", StringComparison.Ordinal)).ToList();
        string? Opt(string name) => opts.FirstOrDefault(o => o.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];
        bool Flag(string name) => opts.Contains($"--{name}");
        return a[0].ToLowerInvariant() switch
        {
            "inspect" when files.Count == 1 => Inspect(OriginalSave.Open(files[0]), Flag("all")),
            "tree" when files.Count == 1 => Tree(OriginalSave.Open(files[0]), Opt("path") ?? "/",
                                                 int.TryParse(Opt("depth"), out var d) ? d : int.MaxValue, Opt("find")),
            "locate" when files.Count == 2 => Locate(files[0], files[1]),
            "retired" when files.Count == 2 => Retired(OriginalSave.Open(files[0]), files[1], Flag("all")),
            "diff" when files.Count == 2 => Diff(OriginalSave.Open(files[0]), OriginalSave.Open(files[1]), Flag("all"),
                                                 (Opt("ignore") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)),
            _ => Fail(),
        };
        static int Fail() { Console.Error.WriteLine(Usage); return 1; }
    }

    // ── inspect ────────────────────────────────────────────────────────────
    static int Inspect(OriginalSave s, bool all)
    {
        var i = s.Info;
        Console.WriteLine($"{Path.GetFileName(s.Path)} — original-game {s.Kind}");
        Console.WriteLine($"  name        : {i.ScreenName}   hero: {i.HeroName}   player: {i.PlayerName}");
        Console.WriteLine($"  map         : {i.MapName} \"{i.MapScreenName}\"{(i.IsExpansion ? " (Legends of Aranna)" : "")}   region: {i.Region}");
        Console.WriteLine($"  position    : {i.Position?.ToString() ?? "(none)"}");
        Console.WriteLine($"  played      : {TimeSpan.FromSeconds(i.TimeElapsedSeconds):h\\:mm\\:ss}   saved: {i.SavedAtUtc?.ToString("u") ?? "?"}   {(i.SinglePlayer ? "single player" : "multiplayer")}");
        Console.WriteLine($"  game version: {i.ProductVersion}   tank build stamp: {OriginalSave.BuildStamp(s.TankHeader)}");
        Console.WriteLine($"  files       : {string.Join(", ", s.Files)}");
        Console.WriteLine($"  game data   : {string.Join(", ", i.TankManifest.Select(t => $"{t.FileName} {t.DataCrc32}"))}");

        var p = s.Party;
        Console.WriteLine();
        Console.WriteLine($"Party: {p.Members.Count} member(s), gold {p.Gold:N0}, hero = #{p.HeroIndex}");
        for (int m = 0; m < p.Members.Count; m++)
        {
            var mem = p.Members[m];
            Console.WriteLine($"  #{m} {mem.ScreenName} ({mem.TemplateName}{(mem.ActorClass.Length > 0 ? ", " + mem.ActorClass : "")})  level {mem.Level:0.##}");
            Console.WriteLine($"     orders  : {mem.Orders.Describe()}   [{mem.Orders.Movement} {mem.Orders.Combat} {mem.Orders.Focus} {mem.Orders.Disposition}]");
            Console.WriteLine($"     skills  : {string.Join("  ", mem.Skills.Where(k => k.Key != "uber").Select(k => string.Create(CultureInfo.InvariantCulture, $"{k.Key} {k.Value.Level:0.##} ({k.Value.Xp:0.##}xp)")))}");
            var equipped = mem.Inventory.Where(it => it.EquipSlot.Length > 0 || it.InvLocation.StartsWith("il_active", StringComparison.Ordinal)).ToList();
            Console.WriteLine($"     equipped: {string.Join(", ", equipped.Select(it => $"{it.TemplateName} [{(it.EquipSlot.Length > 0 ? it.EquipSlot : it.InvLocation)}]"))}");
            var pack = mem.Inventory.Except(equipped).ToList();
            var shown = all ? pack : pack.Take(12).ToList();
            Console.WriteLine($"     pack    : {pack.Count} item(s){(shown.Count > 0 ? ": " + string.Join(", ", shown.Select(it => it.TemplateName)) : "")}{(shown.Count < pack.Count ? ", …" : "")}");
        }

        var w = s.World;
        Console.WriteLine();
        if (w is null) { Console.WriteLine("World: none (a .dsparty carries only the party)"); return 0; }
        Console.WriteLine($"World: {w.NodeCount:N0} nodes, {OriginalWorldFacts.ObjectCount(w)} game object(s), {OriginalWorldFacts.RetiredScids(w).Count} retired, {OriginalWorldFacts.LoadedNodes(w).Count} terrain nodes loaded, tips {(OriginalWorldFacts.TutorialTips(w) switch { true => "on", false => "off", null => "?" })}");
        var quests = OriginalWorldFacts.Quests(w);
        Console.WriteLine($"  quests: {quests.Count(q => q.Active)} active, {quests.Count(q => q.Completed)} completed, {quests.Count} known");
        foreach (var q in quests.Where(q => all || q.Active || q.Completed))
            Console.WriteLine($"    {(q.Completed ? "done  " : q.Active ? "active" : "      ")} {q.Name} \"{q.ScreenName}\"{(q.Speaker.Length > 0 ? " (" + q.Speaker + ")" : "")}");
        var flags = OriginalWorldFacts.Flags(w);
        Console.WriteLine($"  script flags: {flags.Count}");
        foreach (var f in all ? flags : flags.Take(20)) Console.WriteLine($"    {f.Key} = {f.Value}");
        if (!all && flags.Count > 20) Console.WriteLine("    … (--all for the rest)");
        return 0;
    }

    // ── locate ─────────────────────────────────────────────────────────────
    static int Locate(string mapPath, string query)
    {
        using var tank = SiegeFX.Core.Tank.TankFile.Open(mapPath);
        var r = MapPlacements.Find(new SiegeFX.Core.Tank.TankReader(tank), query);
        foreach (var m in r.Matches)
            Console.WriteLine($"{m.Region,-24} {m.File,-22} [t:{m.Template},n:0x{m.Scid:x8}]");
        Console.WriteLine($"{r.Matches.Count} match(es) in {r.FilesScanned} object file(s) across {r.RegionsScanned} region(s)" +
                          (r.Errors.Count > 0 ? $"; {r.Errors.Count} file(s) unreadable:" : ""));
        foreach (var e in r.Errors.Take(5)) Console.WriteLine($"  {e}");
        return r.FilesScanned == 0 ? 3 : 0;
    }

    // ── retired ────────────────────────────────────────────────────────────
    // /godb/m_retiredscidcoll lists the map placements the game must not
    // create again. Absence from m_globaldb says nothing on its own: decor is
    // never saved and untouched props drop out once their area unloads.
    static int Retired(OriginalSave s, string mapPath, bool all)
    {
        if (s.World is null) { Console.Error.WriteLine("no world in this file (a .dsparty)"); return 1; }
        var retired = OriginalWorldFacts.RetiredScids(s.World);
        var saved = OriginalWorldFacts.ObjectScids(s.World);
        using var tank = SiegeFX.Core.Tank.TankFile.Open(mapPath);
        var r = MapPlacements.WithScids(new SiegeFX.Core.Tank.TankReader(tank), retired);
        Console.WriteLine($"{retired.Count} retired SCID(s), {r.Matches.Count} found in the map" +
                          $"{(r.Matches.Count < retired.Count ? $" ({retired.Count - r.Matches.Count} not in this map)" : "")}; " +
                          "* = still saved (a corpse, or an object kept with its new state)");
        foreach (var g in r.Matches.GroupBy(p => p.Template).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var list = g.ToList();
            var shown = all ? list : list.Take(6).ToList();
            Console.WriteLine($"  {g.Key,-28} {list.Count,3}  {string.Join(" ", shown.Select(p => $"0x{p.Scid:x8}{(saved.Contains(p.Scid) ? "*" : "")}"))}{(shown.Count < list.Count ? " …" : "")}");
        }
        if (r.Errors.Count > 0) Console.WriteLine($"{r.Errors.Count} map file(s) unreadable, first: {r.Errors[0]}");
        return r.FilesScanned == 0 ? 3 : 0;
    }

    // ── tree ───────────────────────────────────────────────────────────────
    static int Tree(OriginalSave s, string path, int depth, string? find)
    {
        if (s.World is null) { Console.Error.WriteLine("no world in this file (a .dsparty)"); return 1; }
        var start = s.World.Find(path);
        if (start is null) { Console.Error.WriteLine($"no node at {path}"); return 1; }
        if (find is not null)
        {
            foreach (var n in start.DescendantsAndSelf())
            {
                if (n.Name.Contains(find, StringComparison.OrdinalIgnoreCase)) Console.WriteLine($"{n.Path}/");
                foreach (var v in n.Values)
                    if (v.Name.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                        (v.AsText?.Contains(find, StringComparison.OrdinalIgnoreCase) ?? false))
                        Console.WriteLine($"{n.Path}  {v.Name} = {v.Display()}");
            }
            return 0;
        }
        Print(start, 0);
        return 0;

        void Print(XferNode n, int level)
        {
            // --depth=N: values of the start node and N levels of children.
            Console.WriteLine($"{new string(' ', level * 2)}{(level == 0 ? n.Path : n.Name)}/");
            foreach (var e in n.Entries)
            {
                if (e is XferValue v) Console.WriteLine($"{new string(' ', level * 2 + 2)}{v.Name} = {v.Display()}");
                else if (level < depth) Print((XferNode)e, level + 1);
                else Console.WriteLine($"{new string(' ', level * 2 + 2)}{e.Name}/ …");
            }
        }
    }

    // ── diff ───────────────────────────────────────────────────────────────
    /// <summary>World values that change on their own between any two saves
    /// (animation clocks, timers, poll state). Measured from two saves taken
    /// seconds apart with only a few steps walked; hidden unless --all.</summary>
    static readonly HashSet<string> Volatile = new(StringComparer.Ordinal)
    {
        "m_currentframe", "m_currenttime", "m_currentpoll", "m_nexttimerid", "m_randomseed", "m_time",
        "m_starttime", "m_lastupdatetime", "m_elapsedtime", "m_scaledtime$", "m_timescale$", "m_fadeintime$",
        "m_fadeouttime$", "m_randomscale$", "m_attackduration$", "m_requestedattackduration$",
        "m_projectileanimationhaselevation$", "m_weightedtime", "m_weightedduration", "m_weightedtimescale",
        "m_currentinterval", "m_totaltime", "m_intervaltime", "m_laggingtime", "m_leadingtime", "m_deltatime",
    };
    static readonly HashSet<string> InfoVolatile = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_name", "save_time", "save_time_text", "save_duration", "screen_name",
    };

    static int Diff(OriginalSave a, OriginalSave b, bool all, string[] ignore)
    {
        var skip = new HashSet<string>(ignore, StringComparer.Ordinal);
        if (!all) skip.UnionWith(Volatile);
        Console.WriteLine($"{Path.GetFileName(a.Path)}  →  {Path.GetFileName(b.Path)}");

        Console.WriteLine("info.gas:");
        foreach (var key in a.Info.Raw.Keys.Union(b.Info.Raw.Keys, StringComparer.OrdinalIgnoreCase).Order())
        {
            if (!all && InfoVolatile.Contains(key)) continue;
            var va = a.Info.Raw.GetValueOrDefault(key, "(none)");
            var vb = b.Info.Raw.GetValueOrDefault(key, "(none)");
            if (va != vb) Console.WriteLine($"  {key}: {va}  →  {vb}");
        }

        Console.WriteLine("party.gas:");
        if (a.Party.Gold != b.Party.Gold) Console.WriteLine($"  party gold: {a.Party.Gold:N0}  →  {b.Party.Gold:N0}");
        for (int m = 0; m < Math.Max(a.Party.Members.Count, b.Party.Members.Count); m++)
        {
            var ma = m < a.Party.Members.Count ? a.Party.Members[m] : null;
            var mb = m < b.Party.Members.Count ? b.Party.Members[m] : null;
            if (ma is null || mb is null)
            {
                Console.WriteLine($"  #{m}: {(ma is null ? "joined: " + mb!.ScreenName : "left: " + ma.ScreenName)}");
                continue;
            }
            string who = $"#{m} {mb.ScreenName}";
            if (ma.Gold != mb.Gold) Console.WriteLine($"  {who} gold: {ma.Gold:N0}  →  {mb.Gold:N0}");
            if (ma.Orders != mb.Orders) Console.WriteLine($"  {who} orders: {ma.Orders.Describe()}  →  {mb.Orders.Describe()}");
            foreach (var k in ma.Skills.Keys.Union(mb.Skills.Keys))
            {
                var sa = ma.Skills.GetValueOrDefault(k);
                var sb = mb.Skills.GetValueOrDefault(k);
                if (sa != sb)
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  {who} {k}: level {sa.Level:0.####} ({sa.Xp:0.##}xp)  →  {sb.Level:0.####} ({sb.Xp:0.##}xp)"));
            }
            // Row order in party.gas is not meaningful; compare as multisets.
            var ia = ma.Inventory.Select(ItemKey).ToList();
            var ib = mb.Inventory.Select(ItemKey).ToList();
            foreach (var gone in Minus(ia, ib)) Console.WriteLine($"  {who} item removed: {gone}");
            foreach (var added in Minus(ib, ia)) Console.WriteLine($"  {who} item added  : {added}");
        }

        if (a.World is null || b.World is null) return 0;
        Console.WriteLine("world:");
        var tipsA = OriginalWorldFacts.TutorialTips(a.World);
        var tipsB = OriginalWorldFacts.TutorialTips(b.World);
        if (tipsA != tipsB) Console.WriteLine($"  tips: {OnOff(tipsA)}  →  {OnOff(tipsB)}");
        int objA = OriginalWorldFacts.ObjectCount(a.World), objB = OriginalWorldFacts.ObjectCount(b.World);
        if (objA != objB) Console.WriteLine($"  game objects saved: {objA}  →  {objB}");
        var qa = OriginalWorldFacts.Quests(a.World).ToDictionary(q => q.Name);
        foreach (var q in OriginalWorldFacts.Quests(b.World))
            if (!qa.TryGetValue(q.Name, out var old) || old != q)
                Console.WriteLine($"  quest {q.Name}: {QuestState(old)}  →  {QuestState(q)}");
        var fa = OriginalWorldFacts.Flags(a.World).GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First().Value);
        foreach (var f in OriginalWorldFacts.Flags(b.World))
            if (!fa.TryGetValue(f.Key, out var old) || old != f.Value)
                Console.WriteLine($"  flag {f.Key}: {old ?? "(none)"}  →  {f.Value}");

        // Generic value diff by path. Script instances and game objects are
        // positional lists, so one new entry shifts every later index: those
        // two sections churn on any save and are listed last.
        var va2 = Flatten(a.World, skip);
        var vb2 = Flatten(b.World, skip);
        var lines = new List<(string Section, char Kind, string Text)>();
        foreach (var (k, v) in vb2)
        {
            if (!va2.TryGetValue(k, out var old)) lines.Add((Section(k), '+', $"  + {k} = {v}"));
            else if (old != v) lines.Add((Section(k), '~', $"  ~ {k}: {old}  →  {v}"));
        }
        foreach (var (k, v) in va2) if (!vb2.ContainsKey(k)) lines.Add((Section(k), '-', $"  - {k} = {v}"));
        Console.WriteLine($"  values by section{(all ? "" : " (clocks and timers hidden; --all shows them)")}:");
        foreach (var g in lines.GroupBy(l => l.Section).OrderBy(g => Churny(g.Key)).ThenBy(g => g.Key))
            Console.WriteLine($"    {g.Key,-14} {g.Count(l => l.Kind == '~'),7:N0} changed {g.Count(l => l.Kind == '+'),7:N0} added {g.Count(l => l.Kind == '-'),7:N0} removed");
        var ordered = lines.OrderBy(l => Churny(l.Section)).Select(l => l.Text).ToList();
        foreach (var l in all ? ordered : ordered.Take(60)) Console.WriteLine(l);
        if (!all && ordered.Count > 60) Console.WriteLine($"  … {ordered.Count - 60:N0} more (--all)");
        return 0;

        static string Section(string key) { var p = key.Split('/', 3); return p.Length > 1 ? p[1].Split(' ')[0] : "/"; }
        static int Churny(string section) => section is "skritengine" or "godb" ? 1 : 0;
        static string OnOff(bool? b) => b switch { true => "on", false => "off", null => "?" };

        static string ItemKey(OriginalItem it) =>
            it.TemplateName + (it.EquipSlot.Length > 0 ? $" [{it.EquipSlot}]" : it.InvLocation != "il_main" ? $" [{it.InvLocation}]" : "");
        static IEnumerable<string> Minus(List<string> x, List<string> y)
        {
            var left = y.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
            foreach (var s in x)
                if (left.TryGetValue(s, out var n) && n > 0) left[s] = n - 1;
                else yield return s;
        }
        static string QuestState(OriginalQuest? q) =>
            q is null ? "(none)" : q.Completed ? "completed" : q.Active ? "active" : "inactive";
    }

    static Dictionary<string, string> Flatten(XferTree t, HashSet<string> skip)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in t.Root.DescendantsAndSelf())
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var v in n.Values)
            {
                if (skip.Contains(v.Name)) continue;
                int k = seen[v.Name] = seen.GetValueOrDefault(v.Name) + 1;
                d[$"{n.Path}  {v.Name}{(k > 1 ? "#" + k : "")}"] = v.Display();
            }
        }
        return d;
    }
}

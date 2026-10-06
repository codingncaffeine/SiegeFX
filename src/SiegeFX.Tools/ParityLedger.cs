using System.Diagnostics;
using System.Text;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Parity;
using SiegeFX.Core.Skrit;
using SiegeFX.Core.Tank;

/// <summary><c>siegefx parity ledger</c> — everything the original game
/// ships, kind by kind, against what the engine handles. Each section states
/// its rule for "handled", and every rule reads the engine itself: the command
/// catalog the dispatcher consults, the effect engine's live-probed keys, or
/// the names in the compiled engine assemblies (<see cref="EngineVocabulary"/>).
/// Nothing here is a hand-kept list of what works. A baseline file makes it a
/// gate: a section that handles fewer items than the baseline fails the run.</summary>
static class ParityLedger
{
    const string Usage =
        "usage: siegefx parity ledger <Dungeon Siege install> [--engine=DIR] [--out=DIR]\n" +
        "                             [--baseline=FILE] [--write-baseline=FILE]\n" +
        "  --engine  folder holding the built SiegeFX.dll (default: the repo's Runtime build)\n" +
        "  --out     write the summary and one gap list per section there\n" +
        "  --baseline        fail (exit 1) when a section handles fewer items than the file says\n" +
        "  --write-baseline  record this run's counts as the new baseline";

    public static int Dispatch(string[] a)
    {
        if (a.Length >= 1 && a[0].Equals("ledger", StringComparison.OrdinalIgnoreCase)) return Run(a[1..]);
        Console.Error.WriteLine(Usage);
        return 1;
    }

    sealed class Item
    {
        public required string Key;
        public bool Handled;
        public int Uses;
        public string Example = "";
    }

    sealed class Section(string name, string title, string rule, string caveat = "")
    {
        public string Name { get; } = name;
        public string Title { get; } = title;
        public string Rule { get; } = rule;
        /// <summary>Why the rule may undercount here (shown with the section).</summary>
        public string Caveat { get; } = caveat;
        public Dictionary<string, Item> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Authored => Items.Count;
        public int Handled => Items.Values.Count(i => i.Handled);
        public int Uses => Items.Values.Sum(i => i.Uses);
        public int HandledUses => Items.Values.Where(i => i.Handled).Sum(i => i.Uses);

        public void Add(string key, bool handled, string example, int uses = 1)
        {
            if (!Items.TryGetValue(key, out var it))
                Items[key] = it = new Item { Key = key, Handled = handled, Example = example };
            it.Uses += uses;
        }
    }

    static int Run(string[] a)
    {
        string? install = null, engineDir = null, outDir = null, baseline = null, writeBaseline = null;
        foreach (var arg in a)
        {
            if (arg.StartsWith("--engine=", StringComparison.Ordinal)) engineDir = arg["--engine=".Length..];
            else if (arg.StartsWith("--out=", StringComparison.Ordinal)) outDir = arg["--out=".Length..];
            else if (arg.StartsWith("--baseline=", StringComparison.Ordinal)) baseline = arg["--baseline=".Length..];
            else if (arg.StartsWith("--write-baseline=", StringComparison.Ordinal)) writeBaseline = arg["--write-baseline=".Length..];
            else if (!arg.StartsWith("--", StringComparison.Ordinal) && install is null) install = arg;
            else { Console.Error.WriteLine($"unknown argument {arg}\n{Usage}"); return 1; }
        }
        if (install is null) { Console.Error.WriteLine(Usage); return 1; }

        string logicPath = Path.Combine(install, "Resources", "Logic.dsres");
        string worldPath = Path.Combine(install, "Maps", "World.dsmap");
        if (!File.Exists(logicPath) || !File.Exists(worldPath))
        {
            Console.Error.WriteLine($"no Resources/Logic.dsres and Maps/World.dsmap under {install}");
            return 1;
        }
        engineDir ??= FindEngineDir();
        if (engineDir is null || !File.Exists(Path.Combine(engineDir, "SiegeFX.dll")))
        {
            Console.Error.WriteLine("no built engine found (SiegeFX.dll); build src/SiegeFX.Runtime or pass --engine=DIR");
            return 1;
        }
        var vocab = EngineVocabulary.Load(
            new[] { "SiegeFX.dll", "SiegeFX.Core.dll", "SiegeFX.Audio.dll" }
                .Select(f => Path.Combine(engineDir, f)).Where(File.Exists));

        using var logicTank = TankFile.Open(logicPath);
        using var worldTank = TankFile.Open(worldPath);
        var logic = new TankReader(logicTank);
        var world = new TankReader(worldTank);

        var sections = new List<Section>
        {
            Commands(world),
            TriggerVerbs(logic, world, vocab),
            Quests(world),
        };
        var templateSectionUses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var (templateSections, templateFields) = Templates(logic, vocab, templateSectionUses);
        sections.Add(SkritFiles(logic, vocab, "skrit-components", "Skrit components",
            "/world/contentdb/components/", templateSectionUses, ""));
        sections.Add(SkritFiles(logic, vocab, "ai-jobs", "AI jobs", "/world/ai/jobs/", null,
            "the native AI reimplements behaviours without naming the original job scripts, " +
            "so a job it covers still shows as a gap until it is mapped"));
        sections.Add(templateSections);
        sections.Add(templateFields);
        sections.Add(MoodFields(logic, vocab));
        sections.Add(UiInterfaces(logic, vocab));
        sections.Add(EffectKeys(logic));
        sections.Add(AnimationScriptApi(logic, vocab));

        string engineVersion = FileVersionInfo.GetVersionInfo(Path.Combine(engineDir, "SiegeFX.dll")).ProductVersion ?? "?";
        var summary = Summary(sections, install, engineVersion, vocab);
        Console.Write(summary);

        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "ledger.txt"), summary);
            foreach (var s in sections)
                File.WriteAllText(Path.Combine(outDir, $"{s.Name}.txt"), Detail(s));
            Console.WriteLine($"gap lists: {outDir}/<section>.txt");
        }
        if (writeBaseline is not null)
        {
            File.WriteAllText(writeBaseline, BaselineText(sections));
            Console.WriteLine($"baseline written: {writeBaseline}");
        }
        return baseline is null ? 0 : CompareWithBaseline(sections, baseline);
    }

    // ---- sections ----------------------------------------------------------

    static Section Commands(TankReader world)
    {
        var s = new Section("commands", "Scripted commands (command.gas)",
            "the command catalog the engine's dispatcher and NIS indexer consult");
        foreach (var rp in RegionPaths(world))
        {
            var (placements, _) = RegionObjects.LoadPlacements(world, rp, "command.gas");
            foreach (var p in placements)
                s.Add(p.TemplateName, CommandCatalog.Classify(p.TemplateName) != CommandHandling.None, RegionName(rp));
        }
        return s;
    }

    static Section Quests(TankReader world)
    {
        var s = new Section("quests", "Quests the dialogue grants (activate_quest)",
            "the engine's quest catalog defines the quest (journal text and objective)");
        foreach (var rp in RegionPaths(world))
        {
            var (convs, _) = ConversationStore.Load(world, rp);
            foreach (var (convKey, conv) in convs)
                foreach (var node in conv.Nodes)
                    foreach (var key in DialogueNode.SplitQuestKeys(node.ActivateQuest))
                        s.Add(key, SiegeFX.Core.Actors.QuestCatalog.All.ContainsKey(key), $"{RegionName(rp)} {convKey}");
        }
        return s;
    }

    static Section TriggerVerbs(TankReader logic, TankReader world, EngineVocabulary vocab)
    {
        var s = new Section("trigger-verbs", "Trigger conditions and actions",
            "the verb is named by the engine (TriggerRuntime dispatches by name)");
        void Visit(GasNode node, string label)
        {
            var header = PlainHeader(node.Header);
            if (header is "instance_triggers" or "template_triggers")
            {
                foreach (var row in node.Children)
                {
                    TriggerRow r;
                    try { r = TriggerRow.Parse(row, label, null); }
                    catch { continue; }
                    foreach (var c in r.Conditions) s.Add("if " + c.Verb, vocab.Names(c.Verb), label);
                    foreach (var act in r.Actions) s.Add("do " + act.Verb, vocab.Names(act.Verb), label);
                }
                return;
            }
            foreach (var child in node.Children) Visit(child, label);
        }
        foreach (var (reader, path) in GasFiles(world, "/world/maps/").Concat(GasFiles(logic, "/world/contentdb/templates/")))
            foreach (var root in Load(reader, path)) Visit(root, ShortLabel(path));
        return s;
    }

    static (Section Sections, Section Fields) Templates(TankReader logic, EngineVocabulary vocab,
        Dictionary<string, int> sectionUses)
    {
        var sections = new Section("template-sections", "Template components (sections the templates use)",
            "the section name is named by the engine");
        var fields = new Section("template-fields", "Template fields (component.field the templates set)",
            "the field name is named by the engine");
        foreach (var (reader, path) in GasFiles(logic, "/world/contentdb/templates/"))
            foreach (var root in Load(reader, path))
            {
                if (!TemplateStore.TryParseHeader(root.Header, out var typeTag, out var name) ||
                    !typeTag.Equals("template", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var at in root.Attributes)
                {
                    var (key, check) = NormalizeField(at.Name);
                    fields.Add("(template)." + key, vocab.Names(check), name);
                }
                foreach (var comp in root.Children)
                {
                    var header = PlainHeader(comp.Header);
                    sections.Add(header, vocab.Names(header), name);
                    sectionUses[header] = sectionUses.GetValueOrDefault(header) + 1;
                    foreach (var (field, _) in FieldsUnder(comp))
                    {
                        var (key, check) = NormalizeField(field);
                        fields.Add($"{header}.{key}", vocab.Names(check), name);
                    }
                }
            }
        return (sections, fields);
    }

    static Section SkritFiles(TankReader logic, EngineVocabulary vocab, string key, string title, string root,
        Dictionary<string, int>? templateUses, string caveat)
    {
        var s = new Section(key, title, "the script's name is named by the engine (a native replacement or the VM)", caveat);
        foreach (var path in logic.ListFiles())
        {
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".skrit", StringComparison.OrdinalIgnoreCase)) continue;
            var stem = Path.GetFileNameWithoutExtension(path);
            int uses = templateUses is null ? 1 : templateUses.GetValueOrDefault(stem);
            s.Add(stem, vocab.Names(stem), ShortLabel(path), uses);
        }
        return s;
    }

    static Section MoodFields(TankReader logic, EngineVocabulary vocab)
    {
        var s = new Section("mood-fields", "Mood fields (section.field the moods set)",
            "the field name is named by the engine");
        foreach (var (reader, path) in GasFiles(logic, "/world/global/moods/"))
            foreach (var root in Load(reader, path))
                foreach (var (field, section) in FieldsUnder(root))
                {
                    var (key, check) = NormalizeField(field);
                    s.Add($"{section}.{key}", vocab.Names(check), ShortLabel(path));
                }
        return s;
    }

    static Section UiInterfaces(TankReader logic, EngineVocabulary vocab)
    {
        var s = new Section("ui-interfaces", "UI interfaces (/ui/interfaces)",
            "the interface name is named by the engine",
            "most screens are native panels drawn with the original art, which do not name the " +
            "interface file, so a screen they cover still shows as a gap until it is mapped");
        foreach (var (reader, path) in GasFiles(logic, "/ui/interfaces/"))
            foreach (var root in Load(reader, path))
            {
                var name = TemplateStore.TryParseHeader(root.Header, out _, out var n) ? n : PlainHeader(root.Header);
                if (string.IsNullOrEmpty(name)) continue;
                s.Add(name, vocab.Names(name), ShortLabel(path));
            }
        return s;
    }

    static Section EffectKeys(TankReader logic)
    {
        var s = new Section("effect-keys", "Effect-script parameter keys (spell cast effects)",
            "the effect engine reports the key as consumed (live probe)");
        var (templates, _) = TemplateStore.LoadFromTank(logic);
        var spells = SpellCatalog.Build(templates);
        var sfx = SfxScriptStore.LoadFromTank(logic);
        var consumed = SiegeFX.Core.Sfx.SfxRuntime.CollectConsumedParamKeys();
        var gameplay = SiegeFX.Core.Sfx.SfxRuntime.GameplayParamKeys;
        foreach (var spell in spells.All)
        {
            if (string.IsNullOrEmpty(spell.CastSfxScript) || !sfx.TryGet(spell.CastSfxScript, out var script)) continue;
            var keys = new List<(string Key, string Kind)>();
            int strings = 0;
            SiegeFX.Core.Sfx.SfxParamInventory.WalkParamStrings(script.Name, script.Body, sfx,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), keys, ref strings);
            foreach (var (k, _) in keys)
                s.Add(k, consumed.Contains(k) || gameplay.Contains(k), spell.Name);
        }
        return s;
    }

    static Section AnimationScriptApi(TankReader logic, EngineVocabulary vocab)
    {
        var s = new Section("animation-skrit-api", "Engine API the animation scripts call (run by the Skrit VM)",
            "the extern is named by the engine (bridge member or literal)");
        foreach (var path in logic.ListFiles())
        {
            if (!path.StartsWith("/art/animations/skrits/", StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".skrit", StringComparison.OrdinalIgnoreCase)) continue;
            SkritBindResult bind;
            try { bind = new SkritBinder(SkritParser.Parse(Encoding.UTF8.GetString(logic.ExtractToMemory(path)))).Bind(); }
            catch { continue; }
            foreach (var ext in bind.Externs)
                s.Add(ext, vocab.Names(ext) || vocab.NamesMember(ext), Path.GetFileNameWithoutExtension(path));
        }
        return s;
    }

    // ---- output ------------------------------------------------------------

    static string Summary(List<Section> sections, string install, string engineVersion, EngineVocabulary vocab)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PARITY LEDGER — what the original game ships vs what SiegeFX handles");
        sb.AppendLine($"  data   : {install}");
        sb.AppendLine($"  engine : SiegeFX {engineVersion} ({vocab.Count:N0} literal names, {vocab.MemberCount:N0} member names)");
        sb.AppendLine();
        sb.AppendLine($"  {"section",-22} {"authored",8} {"handled",8} {"gap",6} {"by use",8}  rule");
        foreach (var s in sections)
        {
            string byUse = s.Uses == 0 ? "-" : $"{100.0 * s.HandledUses / s.Uses:F1}%";
            string mark = s.Caveat.Length > 0 ? "*" : " ";
            sb.AppendLine($"  {s.Name,-22} {s.Authored,8} {s.Handled,8} {s.Authored - s.Handled,6}{mark}{byUse,8}  {s.Rule}");
        }
        sb.AppendLine();
        sb.AppendLine("  'handled' means some engine path can act on it by name; it is a ceiling, not a");
        sb.AppendLine("  promise of faithful behaviour. A gap row is something the engine cannot read at all.");
        foreach (var s in sections.Where(s => s.Caveat.Length > 0))
            sb.AppendLine($"  * {s.Name}: {s.Caveat}.");
        return sb.ToString();
    }

    static string Detail(Section s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{s.Title} — {s.Handled}/{s.Authored} handled; rule: {s.Rule}");
        if (s.Caveat.Length > 0) sb.AppendLine($"Caveat: {s.Caveat}.");
        sb.AppendLine();
        sb.AppendLine("NOT HANDLED (by uses):");
        foreach (var it in s.Items.Values.Where(i => !i.Handled).OrderByDescending(i => i.Uses).ThenBy(i => i.Key, StringComparer.Ordinal))
            sb.AppendLine($"  {it.Uses,6}  {it.Key}  (e.g. {it.Example})");
        sb.AppendLine();
        sb.AppendLine("HANDLED (by uses):");
        foreach (var it in s.Items.Values.Where(i => i.Handled).OrderByDescending(i => i.Uses).ThenBy(i => i.Key, StringComparer.Ordinal))
            sb.AppendLine($"  {it.Uses,6}  {it.Key}");
        return sb.ToString();
    }

    static string BaselineText(List<Section> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# siegefx parity ledger baseline: section, authored, handled (a run that handles fewer fails)");
        foreach (var s in sections) sb.AppendLine($"{s.Name}\t{s.Authored}\t{s.Handled}");
        return sb.ToString();
    }

    static int CompareWithBaseline(List<Section> sections, string baselinePath)
    {
        if (!File.Exists(baselinePath)) { Console.Error.WriteLine($"no baseline at {baselinePath}"); return 1; }
        var expected = File.ReadAllLines(baselinePath)
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('\t'))
            .Where(p => p.Length == 3)
            .ToDictionary(p => p[0], p => (Authored: int.Parse(p[1]), Handled: int.Parse(p[2])));
        int regressions = 0;
        Console.WriteLine();
        Console.WriteLine($"against baseline {baselinePath}:");
        foreach (var s in sections)
        {
            if (!expected.TryGetValue(s.Name, out var e)) { Console.WriteLine($"  {s.Name}: new section"); continue; }
            if (s.Handled < e.Handled) { regressions++; Console.WriteLine($"  REGRESSION {s.Name}: handled {e.Handled} -> {s.Handled}"); }
            else if (s.Handled > e.Handled) Console.WriteLine($"  better {s.Name}: handled {e.Handled} -> {s.Handled}");
            if (s.Authored != e.Authored) Console.WriteLine($"  note {s.Name}: authored {e.Authored} -> {s.Authored} (different data or inventory)");
        }
        foreach (var name in expected.Keys.Where(k => sections.All(s => s.Name != k)))
        {
            regressions++;
            Console.WriteLine($"  REGRESSION {name}: section missing from this run");
        }
        Console.WriteLine(regressions == 0 ? "  no regressions" : $"  {regressions} regression(s)");
        return regressions == 0 ? 0 : 1;
    }

    // ---- helpers -----------------------------------------------------------

    static string? FindEngineDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var bin = Path.Combine(dir.FullName, "src", "SiegeFX.Runtime", "bin");
            if (!Directory.Exists(bin)) continue;
            foreach (var cfg in new[] { "Release", "Debug" })
                foreach (var tfm in new[] { "net10.0", "net10.0-windows10.0.22621.0" })
                {
                    var d = Path.Combine(bin, cfg, tfm);
                    if (File.Exists(Path.Combine(d, "SiegeFX.dll"))) return d;
                }
        }
        return null;
    }

    static IEnumerable<string> RegionPaths(TankReader map)
    {
        var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in map.ListFiles())
        {
            var idx = path.IndexOf("/regions/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var rest = path[(idx + "/regions/".Length)..];
            var slash = rest.IndexOf('/');
            if (slash > 0) seen.Add(path[..(idx + "/regions/".Length + slash)]);
        }
        return seen;
    }

    static string RegionName(string regionPath) => regionPath[(regionPath.LastIndexOf('/') + 1)..];

    static IEnumerable<(TankReader Reader, string Path)> GasFiles(TankReader reader, string root) =>
        reader.ListFiles()
            .Where(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                        p.EndsWith(".gas", StringComparison.OrdinalIgnoreCase))
            .Select(p => (reader, p));

    static IReadOnlyList<GasNode> Load(TankReader reader, string path)
    {
        try { return GasDocument.Load(reader.ExtractToMemory(path)).Roots; }
        catch { return Array.Empty<GasNode>(); }
    }

    /// <summary>A section header without type tags: "[aspect]" → aspect,
    /// "[t:template,n:x]" → template.</summary>
    static string PlainHeader(string header)
    {
        if (TemplateStore.TryParseHeader(header, out var typeTag, out _) && typeTag.Length > 0) return typeTag.ToLowerInvariant();
        return header.Trim().ToLowerInvariant();
    }

    /// <summary>A GAS field name as a ledger key and the name the engine would
    /// read it by. DS1 writes indexed and shorthand fields with colons
    /// (textures:0, aspect:model at template level, anim_files: 00, the sun
    /// table's 08h00m:color): numbers fold to '#' so one field is one item, and
    /// the name to look for is the last part that is a word, not a number or a
    /// clock time.</summary>
    static (string Key, string Check) NormalizeField(string raw)
    {
        var parts = raw.Trim().ToLowerInvariant().Split(':').Select(p => p.Trim()).ToArray();
        var key = string.Join(':', parts.Select(p => System.Text.RegularExpressions.Regex.Replace(p, @"\d+", "#")));
        var check = parts.LastOrDefault(p =>
                        p.Any(char.IsLetter) &&
                        !System.Text.RegularExpressions.Regex.IsMatch(p, @"^\d+h\d+m$"))
                    ?? parts[0];
        return (key, check);
    }

    /// <summary>Every attribute under a node, at any depth, with the header of
    /// the section it sits in.</summary>
    static IEnumerable<(string Field, string Section)> FieldsUnder(GasNode node)
    {
        foreach (var at in node.Attributes) yield return (at.Name, PlainHeader(node.Header));
        foreach (var child in node.Children)
            foreach (var f in FieldsUnder(child)) yield return f;
    }

    static string ShortLabel(string path)
    {
        var i = path.IndexOf("/regions/", StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? path[(i + "/regions/".Length)..] : path.TrimStart('/');
    }
}

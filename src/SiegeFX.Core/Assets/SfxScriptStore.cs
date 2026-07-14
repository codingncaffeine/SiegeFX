using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>One <c>[effect_script*] { name=X; script=[[ ... ]]; }</c> block,
/// stored as the raw script body. The body is a stack-based mini-DSL —
/// <see cref="SfxScriptStore"/> only holds it as text; the interpreter that
/// actually runs the verbs (sfx create / start / target, sound play, pause,
/// call, if/else) lives in the runtime layer (Phase 17-SC-F).</summary>
public sealed class SfxScript
{
    public string Name { get; }
    public string Body { get; }
    public string SourcePath { get; }

    public SfxScript(string name, string body, string sourcePath)
    {
        Name = name;
        Body = body;
        SourcePath = sourcePath;
    }
}

/// <summary>
/// Index of every <c>[effect_script*]</c> block parsed out of
/// <c>/world/global/effects/*.gas</c>. DS1 ships ~14 effect-gas files
/// (offensive, defensive, environmental, monstereffects, monsterspells,
/// itemeffects, chargeups, dragon, traps, physics, indicators, ui,
/// utility, plus the small <c>emitters.gas</c> + <c>effectscripts.gas</c>
/// primitives) — every <c>call_sfx_script("foo")</c> trigger looks the
/// name up here.
///
/// Phase 17-SC-D is read-only: load once at world start, query by name.
/// Running the script body is a separate concern (SC-F).
/// </summary>
public sealed class SfxScriptStore
{
    /// <summary>The DS1 directory holding every shipped effect script.</summary>
    public const string EffectsDir = "/world/global/effects";

    private readonly Dictionary<string, SfxScript> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byName.Count;
    public IEnumerable<SfxScript> All => _byName.Values;

    public bool TryGet(string name, out SfxScript script)
    {
        if (_byName.TryGetValue(name, out var s)) { script = s; return true; }
        script = null!;
        return false;
    }

    /// <summary>Walk every <c>.gas</c> under <see cref="EffectsDir"/> in the
    /// supplied tank, parse each as a GAS document, and harvest every
    /// <c>[effect_script*]</c> root block. Later occurrences of the same
    /// name win — DS1 ships a handful of duplicate names where a more
    /// specific override sits in a separate file (e.g. <c>fireball</c>
    /// in offensive.gas vs an older copy in chargeups.gas).</summary>
    public static SfxScriptStore LoadFromTank(TankReader tank)
    {
        var store = new SfxScriptStore();
        store.MergeTank(tank);
        return store;
    }

    /// <summary>Multi-tank load: tanks are merged in argument order, so a
    /// later tank's script overrides an earlier one's on a name collision.
    /// Used to let a map tank ship custom effect scripts over the stock
    /// Logic.dsres set — inert for stock maps, which bundle no
    /// <c>/world/global/effects</c> tree at all.</summary>
    public static SfxScriptStore LoadFromTank(params TankReader[] tanks)
    {
        var store = new SfxScriptStore();
        foreach (var tank in tanks)
            if (tank is not null) store.MergeTank(tank);
        return store;
    }

    void MergeTank(TankReader tank)
    {
        var dir = EffectsDir + "/";

        foreach (var path in tank.ListFiles())
        {
            if (!path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;

            byte[] bytes;
            try { bytes = tank.ExtractToMemory(path); }
            catch { continue; }

            GasDocument doc;
            try { doc = GasDocument.Load(bytes); }
            catch { continue; }

            HarvestDocument(doc, path);
        }
    }

    /// <summary>Harvest <c>[effect_script*]</c> blocks out of loose GAS text
    /// (a file on disk rather than a tank entry), overriding any existing
    /// script of the same name. This is the hot-reload path: the Effects
    /// Lab and the filmstrip's <c>--effects-dir</c> override feed edited
    /// script files through here over the stock store.</summary>
    public int AddFromGasText(string gasText, string sourcePath)
    {
        GasDocument doc;
        try { doc = GasDocument.Parse(gasText); }
        catch { return 0; }
        return HarvestDocument(doc, sourcePath);
    }

    /// <summary>Insert or override a single script by name.</summary>
    public void AddOrReplace(SfxScript script) => _byName[script.Name] = script;

    /// <summary>Shallow copy — scripts are immutable, so a clone is a cheap
    /// independent overlay target (stock store stays pristine while a lab
    /// session layers edits on the copy).</summary>
    public SfxScriptStore Clone()
    {
        var copy = new SfxScriptStore();
        foreach (var kv in _byName) copy._byName[kv.Key] = kv.Value;
        return copy;
    }

    int HarvestDocument(GasDocument doc, string path)
    {
        int added = 0;
        foreach (var node in doc.Roots)
        {
            if (!IsEffectScriptHeader(node.Header)) continue;

            string? name = null;
            string? body = null;
            foreach (var attr in node.Attributes)
            {
                if (string.Equals(attr.Name, "name",   StringComparison.OrdinalIgnoreCase))
                    name = attr.Value.Trim();
                else if (string.Equals(attr.Name, "script", StringComparison.OrdinalIgnoreCase))
                    body = attr.Value;
            }
            if (string.IsNullOrEmpty(name) || body is null) continue;

            _byName[name] = new SfxScript(name, body, path);
            added++;
        }
        return added;
    }

    /// <summary>DS1 spells-style headers can take the form
    /// <c>[effect_script*]</c>, <c>[effect_script]</c>, or with extra
    /// attributes after the asterisk (<c>[effect_script*] // comment</c>
    /// is already stripped by the parser). Match leniently.</summary>
    static bool IsEffectScriptHeader(string header)
    {
        var h = header.AsSpan().Trim();
        if (h.EndsWith("*")) h = h[..^1].TrimEnd();
        return h.Equals("effect_script", StringComparison.OrdinalIgnoreCase);
    }
}

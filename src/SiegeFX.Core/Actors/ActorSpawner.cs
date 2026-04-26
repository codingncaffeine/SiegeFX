using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Skrit;

namespace SiegeFX.Core.Actors;

/// <summary>Builds <see cref="Actor"/>s from a region's <see cref="ActorInstance"/>
/// records. Each call owns:
///   • template lookup via <see cref="TemplateStore"/>
///   • asset resolution (mesh, clip, skrit) via <see cref="AssetResolver"/>
///   • skrit compile — parse/bind/compile shared across actors that use the same skrit
///   • mesh + clip parsing — shared across actors that use the same model/anim
///   • per-instance <see cref="SkritInstance"/> with its own <see cref="ActorHostBridge"/>
///   • world-transform composition from <see cref="RegionLayout"/>
///
/// The spawner is deliberately single-region: the caller loops regions if they want a
/// whole world. Its <see cref="SkritRuntime"/> is shared across all actors it builds so
/// the render loop drives them with one <see cref="SkritRuntime.Tick"/> call.</summary>
public sealed class ActorSpawner
{
    readonly TemplateStore _store;
    readonly AssetResolver _resolver;
    RegionLayout? _layout;
    readonly SkritRuntime _runtime;
    readonly WorldMessageBus _bus;

    readonly Dictionary<string, AspMesh?> _meshCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, PrsAnimation?> _clipCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SkritProgram?> _skritCache = new(StringComparer.OrdinalIgnoreCase);

    public SkritRuntime Runtime => _runtime;
    public WorldMessageBus MessageBus => _bus;

    /// <summary>Phase 21a-3 — re-anchor support. When the world layout is rebuilt
    /// (rolling preload after the player crosses into a new region), the spawner
    /// needs the new transform table for actors authored in newly-loaded regions.
    /// Re-pointing is safe because the spawner's caches (mesh/clip/skrit) are
    /// keyed by name, not by layout, so a layout swap doesn't invalidate them.</summary>
    public RegionLayout? Layout
    {
        get => _layout;
        set => _layout = value;
    }

    /// <summary>Reasons individual actors were skipped — malformed templates, missing
    /// assets, failed skrit compile. Inspect after <see cref="Spawn"/> returns.</summary>
    public List<string> Diagnostics { get; } = new();

    public ActorSpawner(
        TemplateStore store,
        AssetResolver resolver,
        RegionLayout? layout = null,
        SkritRuntime? runtime = null,
        WorldMessageBus? bus = null)
    {
        _store = store;
        _resolver = resolver;
        _layout = layout;
        _runtime = runtime ?? new SkritRuntime();
        _bus = bus ?? new WorldMessageBus();
    }

    public IReadOnlyList<Actor> Spawn(IEnumerable<ActorInstance> instances)
    {
        var spawned = new List<Actor>();
        foreach (var inst in instances)
        {
            var actor = TrySpawnOne(inst);
            if (actor is not null) spawned.Add(actor);
        }
        return spawned;
    }

    Actor? TrySpawnOne(ActorInstance inst)
    {
        if (!_store.TryGet(inst.TemplateName, out var template))
        {
            Diagnostics.Add($"{inst}: template not in store");
            return null;
        }

        var modelName = _store.GetAttribute(template, "aspect", "model");
        if (modelName is null)
        {
            Diagnostics.Add($"{inst}: aspect.model missing along specializes chain");
            return null;
        }

        var mesh = GetOrLoadMesh(modelName);
        if (mesh is null)
        {
            Diagnostics.Add($"{inst}: model '{modelName}.asp' not in any indexed tank");
            return null;
        }

        // chore_default is every actor's idle. Its {prefix, stance list, suffix} triple
        // composes one PRS clip; the skrit lives alongside under `skrit = NAME`.
        var chorePrefix = _store.GetAttribute(template, "body", "chore_dictionary", "chore_prefix");
        var defaultSection = _store.GetSection(template, "body", "chore_dictionary", "chore_default");
        if (chorePrefix is null || defaultSection is null)
        {
            Diagnostics.Add($"{inst}: chore_default section missing");
            return null;
        }

        var skritName = TemplateStore.FindAttr(defaultSection, "skrit");
        if (skritName is null)
        {
            Diagnostics.Add($"{inst}: chore_default.skrit missing");
            return null;
        }

        var stances = ParseChoreStances(TemplateStore.FindAttr(defaultSection, "chore_stances"));
        var animFiles = TemplateStore.FindChild(defaultSection, "anim_files");
        var animSuffix = animFiles?.Attributes.FirstOrDefault().Value;
        if (animSuffix is null)
        {
            Diagnostics.Add($"{inst}: chore_default.anim_files missing or empty");
            return null;
        }

        // Clip is best-effort in Phase 10c. Not every shipped .prs version parses yet
        // (0x0202 vs the 0x0003 our loader targets); if the clip fails to load, the
        // actor still spawns and falls back to rest pose. The skrit still runs so its
        // blender log reflects what *would* have been selected once the loader catches
        // up. Missing-file vs version-miss is distinguished in diagnostics.
        var clip = TryLoadAnyStanceClip(chorePrefix, stances, animSuffix, inst);

        // Phase 21c-4 — load chore_walk alongside chore_default so the renderer can
        // swap to the walk cycle while the brain is moving the actor along nav paths.
        // Falls back to the idle clip if the walk PRS is missing or version-misses.
        PrsAnimation? walkClip = null;
        var walkSection = _store.GetSection(template, "body", "chore_dictionary", "chore_walk");
        if (walkSection is not null)
        {
            var walkAnimFiles = TemplateStore.FindChild(walkSection, "anim_files");
            var walkSuffix = walkAnimFiles?.Attributes.FirstOrDefault().Value;
            if (walkSuffix is not null)
            {
                var walkStances = ParseChoreStances(TemplateStore.FindAttr(walkSection, "chore_stances"));
                walkClip = TryLoadAnyStanceClip(chorePrefix, walkStances, walkSuffix, inst);
            }
        }

        // Clips[0] = idle (chore_default), Clips[1] = walk if loaded. Order is the
        // contract Actor.WalkClipIndex publishes to the renderer.
        var clipList = new List<PrsAnimation>();
        int walkIdx = -1;
        if (clip is not null) clipList.Add(clip);
        if (walkClip is not null)
        {
            walkIdx = clipList.Count;
            clipList.Add(walkClip);
        }
        var clips = clipList.ToArray();

        var program = GetOrCompileSkrit(skritName);
        if (program is null)
        {
            Diagnostics.Add($"{inst}: skrit '{skritName}' failed to compile");
            return null;
        }

        var host = new ActorHostBridge(rngSeed: (int)inst.Scid)
        {
            NumSubAnims = 1,
            OwnerScid = inst.Scid,
            MessageBus = _bus,
        };
        var skrit = new SkritInstance(program, host);
        host.Instance = skrit;
        _runtime.Add(skrit);
        _bus.Register(inst.Scid, skrit);

        skrit.Start();
        skrit.Dispatch("OnStartChore$", SkritValue.FromInt(0), SkritValue.FromInt(0));

        var world = ComposeWorldTransform(inst.Placement);
        var stats = ActorStats.FromTemplate(_store, template);
        return new Actor(inst, template, world, mesh, clips, skrit, host, stats, walkIdx);
    }

    Matrix4x4 ComposeWorldTransform(NodePlacement p)
    {
        // Local pose: rotate by the stored quaternion, then translate by the node-local
        // position. Composing as R * T means the translation is applied AFTER rotation in
        // world-vector terms, i.e. treating the quaternion as the actor's facing and the
        // position as an offset inside the node's frame — which is how DS1 authors it.
        var local = Matrix4x4.CreateFromQuaternion(p.Orientation) *
                    Matrix4x4.CreateTranslation(p.LocalPosition);
        if (_layout is null) return local;
        if (!_layout.TryGetTransform(p.NodeGuid, out var nodeWorld)) return local;
        return local * nodeWorld;
    }

    AspMesh? GetOrLoadMesh(string modelName)
    {
        if (_meshCache.TryGetValue(modelName, out var cached)) return cached;
        if (!_resolver.TryLoadModel(modelName, out var bytes)) { _meshCache[modelName] = null; return null; }
        try
        {
            var mesh = AspMesh.Load(bytes);
            _meshCache[modelName] = mesh;
            return mesh;
        }
        catch (Exception ex)
        {
            Diagnostics.Add($"mesh '{modelName}.asp' load failed: {ex.Message}");
            _meshCache[modelName] = null;
            return null;
        }
    }

    PrsAnimation? TryLoadAnyStanceClip(string prefix, int[] stances, string suffix, ActorInstance inst)
    {
        foreach (var s in stances)
        {
            var filename = $"{prefix}{s}_{suffix}.prs";
            if (_clipCache.TryGetValue(filename, out var cached))
            {
                if (cached is not null) return cached;
                continue; // cached failure — try next stance
            }
            if (!_resolver.TryLoadByBasename(filename, out var bytes)) continue;
            try
            {
                var clip = PrsAnimation.Load(bytes);
                _clipCache[filename] = clip;
                return clip;
            }
            catch (Exception ex)
            {
                Diagnostics.Add($"{inst}: clip '{filename}' load failed: {ex.Message}");
                _clipCache[filename] = null;
            }
        }
        return null;
    }

    SkritProgram? GetOrCompileSkrit(string skritName)
    {
        if (_skritCache.TryGetValue(skritName, out var cached)) return cached;
        if (!_resolver.TryLoadSkrit(skritName, out var bytes))
        {
            _skritCache[skritName] = null;
            return null;
        }
        try
        {
            var src = System.Text.Encoding.UTF8.GetString(bytes);
            var script = SkritParser.Parse(src);
            var bind = new SkritBinder(script).Bind();
            var program = new SkritCompiler(script, bind).Compile();
            _skritCache[skritName] = program;
            return program;
        }
        catch (Exception ex)
        {
            Diagnostics.Add($"skrit '{skritName}' compile error: {ex.Message}");
            _skritCache[skritName] = null;
            return null;
        }
    }

    // Shared with the spawn-probe tooling. Keeping the parser local so Actors doesn't
    // acquire a runtime dependency on the Tools project.
    static int[] ParseChoreStances(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new[] { 0 };
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>(parts.Length);
        foreach (var p in parts) if (int.TryParse(p, out var n)) list.Add(n);
        return list.Count == 0 ? new[] { 0 } : list.ToArray();
    }
}

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
    readonly TriggerRuntime _triggerRuntime;

    readonly Dictionary<string, AspMesh?> _meshCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, PrsAnimation?> _clipCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SkritProgram?> _skritCache = new(StringComparer.OrdinalIgnoreCase);

    public SkritRuntime Runtime => _runtime;
    public WorldMessageBus MessageBus => _bus;
    public TriggerRuntime TriggerRuntime => _triggerRuntime;

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
        WorldMessageBus? bus = null,
        TriggerRuntime? triggerRuntime = null)
    {
        _store = store;
        _resolver = resolver;
        _layout = layout;
        _runtime = runtime ?? new SkritRuntime();
        _bus = bus ?? new WorldMessageBus();
        _triggerRuntime = triggerRuntime ?? new TriggerRuntime();
    }

    /// <summary>Phase 10-SC-1 — spawn every <c>[instance_triggers]</c>-bearing placement
    /// in <paramref name="placements"/> into the trigger runtime. Each placement gets
    /// its world transform composed (so condition radii operate in world space) and
    /// its matrix parsed from the per-instance node first, then the template chain.
    /// Skips placements with no matrix authored anywhere along that chain — many
    /// special.gas entries are pure markers (mood boxes, generators) and don't carry
    /// triggers.</summary>
    public IReadOnlyList<TriggerInstance> SpawnTriggers(IEnumerable<ActorInstance> placements)
    {
        var spawned = new List<TriggerInstance>();
        foreach (var p in placements)
        {
            if (!_store.TryGet(p.TemplateName, out var template))
            {
                Diagnostics.Add($"trigger {p}: template not in store");
                continue;
            }
            var matrix = TriggerMatrix.FromInstanceOrTemplate(p, template, _store, Diagnostics);
            if (matrix is null) continue;

            var world = ComposeWorldTransform(p.Placement);
            var pos = world.Translation;

            // start_active is per-row in the GAS but for the instance we honor the
            // first row's flag — DS1's editor only ever writes one [*] row that
            // wants the shared flag, and rows that share an instance share its
            // active state through flip_flop semantics. Defaults to active when
            // unauthored.
            bool startActive = matrix.Rows.Count == 0 || matrix.Rows[0].StartActive;

            var trigger = new TriggerInstance(p.Scid, p.Placement.NodeGuid, pos, matrix, startActive);
            _triggerRuntime.Register(trigger);
            spawned.Add(trigger);
        }
        return spawned;
    }

    public IReadOnlyList<Actor> Spawn(IEnumerable<ActorInstance> instances)
    {
        var spawned = new List<Actor>();
        foreach (var inst in instances)
        {
            var actor = TrySpawnOne(inst, preferredStance: null);
            if (actor is not null) spawned.Add(actor);
        }
        return spawned;
    }

    /// <summary>21d-2a-vi — preferred-stance overload. DS1's chore_default lists
    /// chore_stances=0..8 and the FIRST one that loads wins; for the player that's
    /// stance 0 (unarmed) regardless of equipment, so a dagger-equipped farmboy
    /// played the unarmed idle and the wrist sat in the wrong place. Callers that
    /// know the equipped weapon's class (1H melee → stance 1, ranged → 5, etc.)
    /// pass the matching stance number; the loader tries it first and falls back
    /// to the authored order if it's missing.</summary>
    public IReadOnlyList<Actor> Spawn(IEnumerable<ActorInstance> instances, int? preferredStance)
    {
        return Spawn(instances, preferredStance, overrides: null);
    }

    /// <summary>21d-2a-viii — variant override overload. The character creator
    /// builds a <see cref="TemplateOverride"/> from the player's body / skin /
    /// pants picks; this overload feeds those into the per-instance template
    /// lookup so the spawner picks the chosen <c>pos_aN</c> mesh while every
    /// other actor in the same Spawn batch uses its authored aspect.model.
    /// The overrides are dictionary-keyed by template name and apply only to
    /// matching instances. Pass null to keep the legacy (template-only) path.</summary>
    public IReadOnlyList<Actor> Spawn(
        IEnumerable<ActorInstance> instances,
        int? preferredStance,
        IReadOnlyDictionary<string, TemplateOverride>? overrides)
    {
        var spawned = new List<Actor>();
        foreach (var inst in instances)
        {
            TemplateOverride? ov = null;
            overrides?.TryGetValue(inst.TemplateName, out ov);
            var actor = TrySpawnOne(inst, preferredStance, ov);
            if (actor is not null) spawned.Add(actor);
        }
        return spawned;
    }

    Actor? TrySpawnOne(ActorInstance inst, int? preferredStance, TemplateOverride? overrides = null)
    {
        if (!_store.TryGet(inst.TemplateName, out var template))
        {
            Diagnostics.Add($"{inst}: template not in store");
            return null;
        }

        // 21d-2a-viii — character creator overrides aspect.model so the picked
        // pos_aN body mesh wins over the template's authored a1 default. Texture
        // overrides (skin, pants) flow through the renderer's ResolveActorTexture,
        // not here — the spawner only cares about the .asp.
        var modelName = overrides?.ModelName
            ?? _store.GetAttribute(template, "aspect", "model");
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
        var clip = TryLoadAnyStanceClip(chorePrefix, stances, animSuffix, inst, preferredStance);

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
                walkClip = TryLoadAnyStanceClip(chorePrefix, walkStances, walkSuffix, inst, preferredStance);
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

    /// <summary>Phase 9-SC-10 — re-pick chore_default + chore_walk against a new
    /// preferred stance and swap them into <paramref name="actor"/>'s clip array
    /// in place. Used by the player path when equipment changes mid-game (e.g.
    /// picking up a shield should switch the idle from fs1 to fs2). Leaves
    /// Clips[0] / Clips[WalkClipIndex] untouched if the new lookup fails so the
    /// actor never goes clipless.</summary>
    public void RefreshMotionClips(Actor actor, int? preferredStance)
    {
        var template = actor.Template;
        var chorePrefix = _store.GetAttribute(template, "body", "chore_dictionary", "chore_prefix");
        var defaultSection = _store.GetSection(template, "body", "chore_dictionary", "chore_default");
        if (chorePrefix is null || defaultSection is null) return;

        var animFiles = TemplateStore.FindChild(defaultSection, "anim_files");
        var animSuffix = animFiles?.Attributes.FirstOrDefault().Value;
        if (animSuffix is null) return;

        var stances = ParseChoreStances(TemplateStore.FindAttr(defaultSection, "chore_stances"));
        var newIdle = TryLoadAnyStanceClip(chorePrefix, stances, animSuffix, actor.Instance, preferredStance);
        if (newIdle is not null && actor.Clips.Length > 0)
            actor.Clips[0] = newIdle;

        if (actor.WalkClipIndex >= 0 && actor.WalkClipIndex < actor.Clips.Length)
        {
            var walkSection = _store.GetSection(template, "body", "chore_dictionary", "chore_walk");
            if (walkSection is not null)
            {
                var walkFiles = TemplateStore.FindChild(walkSection, "anim_files");
                var walkSuffix = walkFiles?.Attributes.FirstOrDefault().Value;
                if (walkSuffix is not null)
                {
                    var walkStances = ParseChoreStances(TemplateStore.FindAttr(walkSection, "chore_stances"));
                    var newWalk = TryLoadAnyStanceClip(chorePrefix, walkStances, walkSuffix, actor.Instance, preferredStance);
                    if (newWalk is not null)
                        actor.Clips[actor.WalkClipIndex] = newWalk;
                }
            }
        }
    }

    PrsAnimation? TryLoadAnyStanceClip(string prefix, int[] stances, string suffix, ActorInstance inst, int? preferredStance = null)
    {
        // 21d-2a-vi: try preferredStance first when caller knows the equipped weapon's class.
        // chore_default lists 0..8 and DS1 takes the first that loads — that's stance 0
        // (unarmed) for the player regardless of equipment, so a dagger-equipped farmboy
        // played the unarmed idle and the wrist sat in the wrong place. We try the
        // preferred stance even when it's NOT in the authored list because chore_walk
        // often only authors stance 0 yet the fs1 walk PRS still ships on disk — the
        // template's `chore_stances` is what DS1 *expects to find*, not what's available.
        IEnumerable<int> order = stances;
        if (preferredStance is int p)
        {
            order = new[] { p }.Concat(stances.Where(s => s != p));
        }
        foreach (var s in order)
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
                Diagnostics.Add($"loaded clip: {filename} (stance {s})");
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

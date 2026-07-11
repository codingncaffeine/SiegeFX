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
            // ALPHA-2 ORIENTED-BOX — carry the composed world rotation (node
            // rotation × placement quaternion) so box conditions test in the
            // authored frame. DS1's threshold strips rotate 90° routinely.
            var orient = System.Numerics.Quaternion.CreateFromRotationMatrix(world);

            // start_active is per-row in the GAS but for the instance we honor the
            // first row's flag — DS1's editor only ever writes one [*] row that
            // wants the shared flag, and rows that share an instance share its
            // active state through flip_flop semantics. Defaults to active when
            // unauthored.
            bool startActive = matrix.Rows.Count == 0 || matrix.Rows[0].StartActive;

            var trigger = new TriggerInstance(p.Scid, p.Placement.NodeGuid, pos, matrix, startActive, p.TemplateName, orient);
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
        var dictionary = _store.GetSection(template, "body", "chore_dictionary");
        var defaultSection = dictionary is null
            ? null
            : TemplateStore.FindChild(dictionary, "chore_default");
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

        // Clip catalogue is best-effort in Phase 10. Not every shipped .prs version parses
        // yet (0x0202 vs the 0x0003 our loader targets); if a clip fails to load the chore
        // simply doesn't appear in ClipIndexByName and callers branch to a bind-pose
        // fallback. The skrit still runs so its blender log reflects what *would* have
        // been selected once the loader catches up.
        //
        // Phase 10-SC-2: walk every chore_* child of the dictionary, not just default + walk.
        // chore_default lands at index 0 (renderer's bind-time fallback), chore_walk gets
        // tracked separately for WalkClipIndex; everything else is addressable by name via
        // Actor.GetClipIndex("chore_die") / "chore_attack" / etc.
        var clipList = new List<PrsAnimation>();
        var clipIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int walkIdx = -1;

        var defaultClip = TryLoadChoreClip(chorePrefix!, defaultSection, inst, preferredStance);
        if (defaultClip is not null)
        {
            // Index 0 is the renderer's idle fallback (CurrentClipIndex returns 0 when the
            // skrit hasn't dispatched yet). The contract is "Clips[0] = chore_default" — if
            // chore_default fails to load (typically the v0x202 PRS issue addressed by
            // Phase 10-SC-3), we leave the catalogue empty rather than promote chore_attack
            // or chore_die into slot 0 where the renderer would loop it as the idle.
            clipList.Add(defaultClip);
            clipIndexByName["chore_default"] = 0;

            // Iterate every chore_* section the template authors. The dictionary's other
            // children (chore_prefix is a flat attribute, not a child) are all chore sections.
            foreach (var section in dictionary!.Children)
            {
                var name = section.Header;
                if (name is null) continue;
                if (!name.StartsWith("chore_", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("chore_default", StringComparison.OrdinalIgnoreCase)) continue;

                var sectionClip = TryLoadChoreClip(chorePrefix!, section, inst, preferredStance);
                if (sectionClip is null) continue;

                int idx = clipList.Count;
                clipList.Add(sectionClip);
                clipIndexByName[name] = idx;
                if (name.Equals("chore_walk", StringComparison.OrdinalIgnoreCase))
                    walkIdx = idx;
            }
            // SC-NIS — chore_misc (chore_stances=ignore) maps NAMED anims to full-name
            // PRS clips; the loop above only kept its first entry (as "chore_misc").
            // Also expose each by its own key ("fall", "dead", "hurt", …) so NIS drivers
            // and cmd_animation_command can play them by name — Norick's bridge death
            // uses fall→dsf-03 (collapse) and dead→di-02 (final pose).
            var miscSection = TemplateStore.FindChild(dictionary!, "chore_misc");
            var miscAnims = miscSection is null ? null : TemplateStore.FindChild(miscSection, "anim_files");
            if (miscAnims is not null)
            {
                foreach (var attr in miscAnims.Attributes)
                {
                    if (string.IsNullOrWhiteSpace(attr.Name) || string.IsNullOrWhiteSpace(attr.Value)) continue;
                    if (clipIndexByName.ContainsKey(attr.Name)) continue;
                    var miscClip = TryLoadFullNameClip(attr.Value, inst);
                    if (miscClip is null) continue;
                    clipIndexByName[attr.Name] = clipList.Count;
                    clipList.Add(miscClip);
                }
            }
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
        // SC-INSTANCE-OVERRIDES — the placement's own [aspect] block (life /
        // max_life / scale_multiplier) wins over the template chain.
        var stats = ActorStats.FromTemplate(_store, template, inst.Node);
        var actor = new Actor(inst, template, world, mesh, clips, skrit, host, stats, walkIdx, clipIndexByName);
        // Phase 18 — resolve the animation stance from the AUTHORED equipment
        // when the caller didn't supply one (NPCs): an axe-armed krug idles,
        // walks, and swings in fs1/fs3 instead of the unarmed fs0 the plain
        // "first stance that loads" pick landed on. Also populates the
        // attack-variant set + qffg pad + authored base duration for every
        // spawned combatant (clip cache makes the second pass cheap).
        RefreshMotionClips(actor, preferredStance ?? DeriveStanceFromEquipment(template));
        return actor;
    }

    /// <summary>Phase 18 — stance from the template's authored
    /// [inventory][equipment] block: es_weapon_hand's attack_class +
    /// is_two_handed, and es_shield_hand only when the item really is a
    /// shield (krug_throw carries a throwing rock there). Null when the
    /// template authors no weapon and no shield — the authored
    /// chore_stances order then leads, which is DS1's unarmed default.</summary>
    int? DeriveStanceFromEquipment(Assets.Template template)
    {
        var weaponRef = _store.GetAttribute(template, "inventory", "equipment", "es_weapon_hand")?.Trim();
        var shieldRef = _store.GetAttribute(template, "inventory", "equipment", "es_shield_hand")?.Trim();
        Assets.Template? weapon = null;
        if (!string.IsNullOrEmpty(weaponRef)) _store.TryGet(weaponRef, out weapon);
        bool shield = false;
        if (!string.IsNullOrEmpty(shieldRef) && _store.TryGet(shieldRef, out var sh))
            shield = WeaponStance.IsShield(sh);
        if (weapon is null && !shield) return null;
        return WeaponStance.Resolve(_store, weapon, shield);
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

    /// <summary>Phase 9-SC-10 / Phase 12-SC-5 — re-pick every chore clip against a
    /// new preferred stance and swap them into <paramref name="actor"/>'s clip
    /// array in place. Used by the player path when equipment changes mid-game.
    /// SC-10 originally refreshed only chore_default + chore_walk so picking up a
    /// shield switched the idle stance, but chore_attack stayed on the unarmed
    /// fist clip — so a dagger-equipped farmboy played the punch animation on
    /// every swing. SC-5 walks the whole chore_dictionary so attack/magic/die/
    /// get_hit/fidget all rebind to the equipped weapon's stance. Leaves an
    /// existing clip in place if the new lookup fails so the actor never goes
    /// clipless.</summary>
    public void RefreshMotionClips(Actor actor, int? preferredStance)
    {
        var template = actor.Template;
        var chorePrefix = _store.GetAttribute(template, "body", "chore_dictionary", "chore_prefix");
        var dict = _store.GetSection(template, "body", "chore_dictionary");
        if (chorePrefix is null || dict is null) return;

        foreach (var section in dict.Children)
        {
            var name = section.Header;
            if (string.IsNullOrEmpty(name) || !name.StartsWith("chore_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!actor.ClipIndexByName.TryGetValue(name, out var clipIdx)) continue;
            if (clipIdx < 0 || clipIdx >= actor.Clips.Length) continue;

            var newClip = TryLoadChoreClip(chorePrefix, section, actor.Instance, preferredStance);
            if (newClip is not null)
                actor.Clips[clipIdx] = newClip;

            // Phase 18 — for chore_attack, load the full DS1 attack set:
            // every non-qffg sub-anim as a swing variant (the shipped
            // select_attack picks uniformly at random among them), the qffg
            // entry as the between-swings pad fidget, and the authored
            // [anim_durations] value for the resolved stance as the base
            // attack duration (GetBaseDuration's data source).
            if (name.Equals("chore_attack", StringComparison.OrdinalIgnoreCase))
                LoadAttackSet(chorePrefix, section, actor, preferredStance);
        }
    }

    /// <summary>Phase 18 — resolve one stance for the attack chore (preferred
    /// first, then the authored chore_stances order), then load EVERY
    /// [anim_files] sub-anim at that single stance: non-qffg entries become
    /// <see cref="Actor.AttackVariants"/>, the qffg entry becomes
    /// <see cref="Actor.AttackPadClip"/>. Also reads the optional
    /// [anim_durations] fsN table (heroes author it; monsters don't) into
    /// <see cref="Actor.AttackBaseDuration"/>. Resolving the stance ONCE
    /// keeps the whole set coherent — mixing an fs1 slash with an fs0 punch
    /// (the old per-suffix fallback) read as a glitch.</summary>
    void LoadAttackSet(string prefix, GasNode section, Actor actor, int? preferredStance)
    {
        var animFiles = TemplateStore.FindChild(section, "anim_files");
        if (animFiles is null || animFiles.Attributes.Count == 0) return;

        var stancesRaw = TemplateStore.FindAttr(section, "chore_stances");
        bool ignoreStance = stancesRaw is not null
            && stancesRaw.Trim().Equals("ignore", StringComparison.OrdinalIgnoreCase);

        var variants = new List<PrsAnimation>();
        PrsAnimation? pad = null;
        int resolvedStance = -1;

        if (ignoreStance)
        {
            foreach (var attr in animFiles.Attributes)
            {
                if (string.IsNullOrWhiteSpace(attr.Value)) continue;
                var clip = TryLoadFullNameClip(attr.Value, actor.Instance);
                if (clip is null) continue;
                if (IsQffg(attr.Name)) pad ??= clip; else variants.Add(clip);
            }
        }
        else
        {
            var stances = ParseChoreStances(stancesRaw);
            IEnumerable<int> order = stances;
            if (preferredStance is int p)
                order = new[] { p }.Concat(stances.Where(s => s != p));
            foreach (var s in order)
            {
                foreach (var attr in animFiles.Attributes)
                {
                    var suffix = attr.Value;
                    if (string.IsNullOrWhiteSpace(suffix)) continue;
                    var clip = TryLoadAnyStanceClip(prefix, new[] { s }, suffix, actor.Instance);
                    if (clip is null) continue;
                    if (IsQffg(attr.Name)) pad ??= clip; else variants.Add(clip);
                }
                if (variants.Count > 0) { resolvedStance = s; break; }
                pad = null; // stance had only a qffg — keep looking
            }
        }

        if (variants.Count == 0) return;
        actor.AttackVariants = variants.ToArray();
        actor.AttackPadClip = pad;
        actor.AttackStance = resolvedStance;
        actor.AttackBaseDuration = 0f;
        if (resolvedStance >= 0 &&
            TemplateStore.FindChild(section, "anim_durations") is { } durations)
        {
            foreach (var attr in durations.Attributes)
            {
                if (!string.Equals(attr.Name, $"fs{resolvedStance}", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (float.TryParse(attr.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0f)
                    actor.AttackBaseDuration = d;
                break;
            }
        }
    }

    static bool IsQffg(string? key) =>
        string.Equals(key, "qffg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Phase 10-SC-2 — load a representative PRS clip for one chore_* section.
    /// Walks every <c>[anim_files]</c> entry and stops at the first that resolves; returns
    /// null if none load. Two filename strategies depending on the section's
    /// <c>chore_stances</c> attribute:
    ///
    /// • Numeric list (the common case, e.g. <c>0,1,2,3,4,5,6,7,8</c>) — stance values
    ///   compose with prefix + suffix to <c>{prefix}{stance}_{suffix}.prs</c>. Mirrors the
    ///   classic <see cref="TryLoadAnyStanceClip"/> path so behavior for chore_default and
    ///   chore_walk is identical to before SC-2 landed.
    ///
    /// • <c>ignore</c> (chore_misc only across shipped DS1) — the anim_files VALUE is the
    ///   full PRS basename already (e.g. <c>a_c_gah_fb_fs1_dk</c>); we just append .prs.
    ///   No stance/prefix composition.
    ///
    /// Sub-anim selection: the chore section often lists multiple sub-anims (chore_attack
    /// has 5: 0mid/high/loww/extr/qffg). Phase 10-SC-2 just loads the first that resolves;
    /// the per-sub-anim catalogue (so combat can pick "high" vs "extr") is a future
    /// splinter — for now combat just needs *some* attack clip rather than falling back
    /// to the idle.</summary>
    PrsAnimation? TryLoadChoreClip(string prefix, GasNode section, ActorInstance inst, int? preferredStance)
    {
        var animFiles = TemplateStore.FindChild(section, "anim_files");
        if (animFiles is null || animFiles.Attributes.Count == 0) return null;

        var stancesRaw = TemplateStore.FindAttr(section, "chore_stances");
        bool ignoreStance = stancesRaw is not null
            && stancesRaw.Trim().Equals("ignore", StringComparison.OrdinalIgnoreCase);

        if (ignoreStance)
        {
            foreach (var attr in animFiles.Attributes)
            {
                var basename = attr.Value;
                if (string.IsNullOrWhiteSpace(basename)) continue;
                var clip = TryLoadFullNameClip(basename, inst);
                if (clip is not null) return clip;
            }
            return null;
        }

        var stances = ParseChoreStances(stancesRaw);
        foreach (var attr in animFiles.Attributes)
        {
            var suffix = attr.Value;
            if (string.IsNullOrWhiteSpace(suffix)) continue;
            var clip = TryLoadAnyStanceClip(prefix, stances, suffix, inst, preferredStance);
            if (clip is not null) return clip;
        }
        return null;
    }

    /// <summary>Phase 10-SC-2 helper — load a PRS by full basename (no stance composition).
    /// Used for chore_misc whose <c>chore_stances=ignore</c> means each anim_files value
    /// is itself a complete PRS basename (e.g. <c>a_c_gah_fb_fs0_dsf-04</c>).</summary>
    PrsAnimation? TryLoadFullNameClip(string basename, ActorInstance inst)
    {
        var filename = $"{basename}.prs";
        if (_clipCache.TryGetValue(filename, out var cached)) return cached;
        if (!_resolver.TryLoadByBasename(filename, out var bytes))
        {
            _clipCache[filename] = null;
            return null;
        }
        try
        {
            var clip = PrsAnimation.Load(bytes);
            _clipCache[filename] = clip;
            Diagnostics.Add($"loaded clip: {filename} (chore_misc full name)");
            return clip;
        }
        catch (Exception ex)
        {
            Diagnostics.Add($"{inst}: clip '{filename}' load failed: {ex.Message}");
            _clipCache[filename] = null;
            return null;
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

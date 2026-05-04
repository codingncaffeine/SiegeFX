using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Sfx;
using SiegeFX.Core.Skrit;
using SiegeFX.Core.Tank;
using SiegeFX.Runtime.Render.Hud;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// Owns the window, GL context, input binding, camera, and render loop.
/// Phase 3 just draws a reference grid so WASD + mouse-look can be verified;
/// later phases will hang real scene objects off this.
/// </summary>
public sealed class RenderHost : IDisposable
{
    private readonly IWindow _window;
    private readonly string? _meshPath;
    private readonly string? _texturePath;
    private readonly string? _regionMapTankPath;
    private readonly string? _regionTerrainTankPath;
    private readonly string? _regionPath;
    private readonly string? _worldMapTankPath;
    private readonly string? _worldTerrainTankPath;
    private readonly string? _worldRootHint;
    private readonly string? _animAspPath;
    private readonly string? _animPrsPath;
    private readonly string? _animTexturePath;
    private readonly string? _skritPath;
    private readonly IReadOnlyList<string>? _skritClipPaths;
    private readonly string? _playLogicTankPath;
    private readonly string? _playObjectsTankPath;
    // Phase 24-MAINMENU — boot-mode state. _bootMode=true when the user
    // launched siegefx.exe with no positional args; OnLoad opens the standard
    // DS1 tanks under _ds1ResourcesDir, builds a frontend resolver, and
    // drives the splash → main menu state machine via _frontendScene.
    // _noVideo (DS1's nointro=true) skips the splash sequence entirely.
    private readonly bool _bootMode;
    private readonly string? _ds1ResourcesDir;
    private readonly bool _noVideo;

    // Phase 10e — play-region mode. Populated by LoadPlayActors from a spawned ActorSpawner;
    // OnUpdate ticks the runtime + drains the bus each 20 Hz step, OnRender issues a skinned
    // draw per actor with its own animTime + skrit-chosen clip. The region-layout stash
    // below keeps LoadRegion's node transforms available so actor node-anchored positions
    // compose against the same world frame as the terrain.
    private RegionLayout? _regionLayout;
    // Phase 21a-2 — neighbor-aware loading state. LoadNeighborTerrain populates
    // these so LoadPlayActors can build a unified RegionGraph + RegionLayout
    // spanning player + first-ring neighbors without re-walking stitch helpers
    // or re-composing the WorldLayout. Each entry's Path is the neighbor's
    // tank path so RegionObjects + ConversationStore can be re-loaded against
    // the right scope. Empty when LoadRegion ran without a stitch helper
    // (standalone test regions); LoadPlayActors then falls back to single-region
    // behavior.
    private readonly List<(string Path, RegionGraph Graph)> _worldRegionGraphs = new();
    private WorldLayout? _worldLayout;
    // Phase 21a-3 — rolling preload state. _loadedRegions caches every
    // (graph, layout, stitches) entry the loader has parsed so far so the
    // re-anchor path (PreloadAroundRegion) can rebuild WorldLayout without
    // re-parsing already-loaded regions. _worldRootRegion stays pinned at the
    // launch region forever so re-anchor never shifts world coordinates.
    // _currentPlayerRegion tracks the player's containing region (updated
    // every ~0.5s in OnUpdate); when it changes, PreloadAroundRegion fires
    // to extend the loaded ring. _snodeRegionLookup is a flat array of
    // (snode XZ origin, region path) used for fast nearest-snode region
    // detection from the player's current world position.
    private readonly Dictionary<string, WorldLayout.RegionEntry> _loadedRegions =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _worldRootRegion;
    private string? _currentPlayerRegion;
    // Phase 21d-2a-xi — region-default mood lookup. Loaded once during audio
    // init from /world/global/moods/<map>/moods*.gas; used by
    // ApplyAmbientForRegion to pick the looping ambient bed each time the
    // player's containing region changes. Map name (e.g. "world") is derived
    // off the launch region path.
    private IReadOnlyDictionary<string, SiegeFX.Core.Assets.MoodSetting>? _moodStore;
    private string? _moodMapName;
    private string? _activeBedRegion;
    // Phase 21d-2a-xii — DS1 sound effect descriptors. Loaded once at audio
    // init from Sound.dsres; consulted by TryRegisterSfx so every wired clip
    // picks up the per-fire pitch jitter the SED authored. Null when audio
    // init failed or Sound.dsres was absent — TryRegisterSfx no-ops the
    // pitch lookup in that case.
    private IReadOnlyDictionary<string, SiegeFX.Core.Assets.SedDescriptor>? _sedStore;
    private (Vector3 OriginXZ, string RegionPath)[] _snodeRegionLookup =
        Array.Empty<(Vector3, string)>();
    private float _regionCheckAccumulator;
    private const float RegionCheckIntervalSec = 0.5f;
    // Phase 21a-3 — kept past LoadPlayActors so PreloadAroundRegion can
    // call Spawn() again with new neighbor instances. The spawner's caches
    // (mesh/clip/skrit) are reused across calls; only the RegionLayout
    // gets re-pointed via the public setter when world layout rebuilds.
    private SiegeFX.Core.Actors.ActorSpawner? _actorSpawner;
    // Phase 21a-3 — mutable backing for the IReadOnlyDictionary the
    // dialogue UI consumes via _conversations. Lets PreloadAroundRegion
    // append newly-loaded regions' dialogue trees without rebuilding the
    // dictionary (and without invalidating the IReadOnly view, which
    // points at this same dict instance).
    private Dictionary<string, SiegeFX.Core.Assets.ConversationDef>? _conversationsMutable;
    private readonly List<ActorRenderState> _actors = new();
    private readonly Dictionary<AspMesh, SkinnedMesh> _actorMeshCache = new();
    // Bind-pose bone array cached per unique mesh — reused every frame for zero-clip actors
    // so the hot render loop doesn't allocate 181× Matrix4x4[BoneCount] under GC.
    private readonly Dictionary<AspMesh, Matrix4x4[]> _actorIdentityBones = new();
    // Phase 21b-2 — reusable skin-matrix scratch shared across every actor in the
    // OnRender draw loop. SetMatrix4Array uploads (or queues) the bytes synchronously
    // so the next actor can clobber the buffer immediately. Sized lazily to the
    // largest BoneCount we've seen; zero per-frame allocation once warm.
    private Matrix4x4[] _skinScratch = Array.Empty<Matrix4x4>();
    // Same idea for the player's weapon-attach path, which needs animated bone worlds
    // (not skin matrices) to find the grip bone's frame each render.
    private Matrix4x4[] _boneWorldsScratch = Array.Empty<Matrix4x4>();
    // Phase 21c — albedo texture cached per AspMesh (NPCs + props share the same cache
    // because the lookup key is the mesh's own TextureNames[0]). null entries memoize
    // misses so we don't retry resolution every frame for assets with no texture.
    private readonly Dictionary<AspMesh, GlTexture?> _aspTextureCache = new();
    // Phase 21c-4 — actors apply per-template texset overrides (`[aspect][textures] { 0 = b_c_eam_kg; }`)
    // so two templates sharing the same mesh (krug_grunt vs krug_scout vs ... all on
    // m_c_eam_kg_pos_1.asp) render with different skins. Keyed by template+mesh+slot
    // so identical template instances reuse one GL upload, and multi-subset characters
    // (farmboy = skin slot + clothing slot) cache each subset's texture independently.
    private readonly Dictionary<(SiegeFX.Core.Assets.Template, AspMesh, int), GlTexture?> _actorTextureCache = new();
    // Phase 21c — static props streamed from non_interactive/container/inventory/
    // interactive/emitter .gas. No skrit, no animation, no nav — just transform +
    // mesh + (optional) texture, drawn through the static-mesh pipeline.
    private readonly List<StaticPropInstance> _staticProps = new();
    private readonly Dictionary<string, AspMesh?> _propAspCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AspMesh, StaticMesh> _propGlMeshCache = new();

    // Phase 21-SC-BARREL-C — frag debris from `[physics][break_particulate]`.
    // Each shipped breakable lists per-material frag template names + counts
    // (frag_glb_wood_01..06 for wood barrels, frag_glb_metal_* for the metal-
    // bound variants, frag_glb_pot_clay_* for pots, etc.). On shatter we
    // spawn a ballistic instance per count with a random outward velocity
    // and spin; the per-tick integrator pulls them down under gravity to
    // the prop's authored Y, lets them settle, and despawns after the
    // lifetime. Asset cache survives region changes — frag meshes are
    // tiny (a handful of tris each) and reused across many barrels.
    private readonly List<FragDebris> _fragDebris = new();
    private readonly Dictionary<string, FragAsset?> _fragAssets =
        new(StringComparer.OrdinalIgnoreCase);
    private sealed class FragAsset
    {
        public StaticMesh Mesh = null!;
        public GlTexture? Texture;
    }
    private sealed class FragDebris
    {
        public FragAsset Asset = null!;
        public Vector3 Pos;
        public Vector3 Vel;
        public Vector3 SpinAxis;
        public float SpinRadPerSec;
        public float SpinAngle;
        public float Age;
        public float Lifetime;
        public float RestY;
        public bool Settled;
    }
    // Phase 21c-1 — barrel investigation: emit a corner-by-corner dump (UV/normal/color)
    // for the first placement of selected debug templates so we can confirm UVs cover
    // the band rows and normals aren't pointing the lit faces inward. One-shot per
    // template name to keep the load log readable.
    private readonly HashSet<string> _dumpedPropDiagnostics = new(StringComparer.OrdinalIgnoreCase);
    // Phase 21c-3 — region directional lighting. Replaces the hardcoded white sun
    // in MeshFragmentSource with the player region's own [t:directional] lights
    // from lights/lights.gas. fh_r1 ships a white key + cool-blue fill; without
    // both, prop tints read flat-warm (the "barrels look slightly off" complaint).
    // Capped at MaxDirectionalLights — enough for every shipped DS1 region's
    // outdoor count (regions ship 1–2 directionals; the rest are point torches).
    private const int MaxDirectionalLights = 4;
    private readonly Vector3[] _dirLightDirs   = new Vector3[MaxDirectionalLights];
    private readonly Vector3[] _dirLightColors = new Vector3[MaxDirectionalLights];
    private int _dirLightCount;
    private float _ambientLevel = 0.25f;
    private SkritRuntime? _actorRuntime;
    private SiegeFX.Core.Actors.WorldMessageBus? _actorBus;
    private SiegeFX.Core.Actors.TriggerRuntime? _triggerRuntime;
    private RenderHostTriggerContext? _triggerCtx;
    // Phase 17-SC-D / SC-F — sfx_script catalogue + interpreter. Loaded once
    // at LoadPlayActors; the trigger runtime hits OnTriggerCallSfxScript which
    // forwards to _sfxRuntime.Spawn so emitter.gas placements + spell casts
    // share the same particle backend.
    private SiegeFX.Core.Assets.SfxScriptStore? _sfxStore;
    private SiegeFX.Core.Sfx.SfxRuntime? _sfxRuntime;
    // Template index for the active scene; used everywhere we need to walk a
    // template's specializes chain (texture resolution, weapon equip, loot
    // tables, voice cues). Loaded from Logic.dsres at LoadPlayActors.
    private SiegeFX.Core.Assets.TemplateStore? _templateStore;
    // Phase 9-SC-7 (Phase A pcontent roller) — resolves drop/equip specs
    // like "#club/2-3" to a concrete weapon template at render time. Built
    // alongside _templateStore. Honors weapon class only; tier/rarity
    // arguments are ignored until SC-16 widens the resolver.
    private SiegeFX.Core.Actors.PcontentResolver? _pcontentResolver;
    private readonly Random _pcontentRng = new(0x5165DEF1);
    private SiegeFX.Core.Assets.FormulasStore? _formulas;
    // Phase 16d — player XP/level state. Created right after the PC spawns once
    // _formulas is also live; null-guarded everywhere because viewer modes that
    // don't TrySpawnPlayer leave both at null and we still want the world to load.
    private SiegeFX.Core.Actors.PlayerProgression? _progression;
    // Phase 16d — seconds the "LEVEL UP!" banner stays on screen after a
    // threshold crossing. Counts down each render frame; >0 means banner
    // is currently being drawn near the top of the HUD.
    private float _levelUpToastRemaining;
    private int _levelUpToastLevel;
    private const float LevelUpToastDuration = 3f;
    // Phase 17a — instant-hit spell catalog (parsed from spl_spell.gas via the
    // template store) and the player's single-slot spellbook. Built after the
    // template store is populated and the player has spawned. Both are null in
    // viewer modes that bypass play-region loading.
    private SiegeFX.Core.Assets.SpellCatalog? _spellCatalog;
    private SiegeFX.Core.Actors.PlayerSpellbook? _playerSpellbook;
    // Phase 17-SC-H-DBG — index into the catalog for the [ ] cycle keys.
    // Lazy: -1 means "not yet aligned with current Primary"; the first cycle
    // input snaps it to whichever slot the active spell already occupies.
    private int _spellCycleIdx = -1;
    // 9-SC-3: DS1 spellbooks are pcontent-rolled containers (book_glb_magic_01
    // has pcontent_level=0, no authored spell names). These two names are a
    // SiegeFX stand-in so 'Q'/'W' have something to fire while pcontent
    // (9-SC-16) is unimplemented. Once pcontent lands, the spellbook walked
    // off es_spellbook by 9-SC-12 will provide real spells and these go away.
    private const string DefaultPrimarySpellName   = "spell_zap";
    private const string DefaultSecondarySpellName = "spell_healing_wind";
    // Phase 17a — short-lived floating world-anchored text (cast feedback like
    // "ZAP -7", "no mana", "out of range"). One render-frame timer per entry;
    // entries with Remaining<=0 are pruned at the start of OnRender.
    private readonly List<FloatingText> _floatingTexts = new();
    private const float FloatingTextDuration = 1.6f;
    private sealed class FloatingText
    {
        public string Text = "";
        public Vector3 WorldPos;
        public Vector4 Color;
        public float Remaining;
        public float Total;
    }
    // Phase 17-SC-B — element → projectile/impact tint. DS1 ships per-spell
    // sfx_scripts (call_sfx_script("fireball") etc.) that pick the actual
    // particle system; until the script runtime lands we lean on the name
    // taxonomy (SpellElementClassifier) so a fireball reads orange instead
    // of the same blue zap as every other offensive spell.
    private static Vector4 SpellElementColor(SiegeFX.Core.Assets.SpellElement e) => e switch
    {
        SiegeFX.Core.Assets.SpellElement.Fire      => new Vector4(1.00f, 0.55f, 0.20f, 1f),
        SiegeFX.Core.Assets.SpellElement.Ice       => new Vector4(0.65f, 0.90f, 1.00f, 1f),
        SiegeFX.Core.Assets.SpellElement.Lightning => new Vector4(0.55f, 0.85f, 1.00f, 1f),
        SiegeFX.Core.Assets.SpellElement.Acid      => new Vector4(0.55f, 0.95f, 0.40f, 1f),
        SiegeFX.Core.Assets.SpellElement.Death     => new Vector4(0.70f, 0.40f, 0.85f, 1f),
        SiegeFX.Core.Assets.SpellElement.Holy      => new Vector4(1.00f, 0.95f, 0.60f, 1f),
        _                                           => new Vector4(0.55f, 0.85f, 1.00f, 1f),
    };

    // Phase 21-SC-SPELL-VISUAL-F — caster-side bone resolver handed to
    // SfxContext. Maps DS1 logical bone names to skeleton bone names per
    // heroes.gas's [bone_translator] block (weapon_bone → weapon_grip,
    // kill_bone → bip01_spine2, body_anterior → bip01_head, …) and
    // returns the live world position by combining the cached bone-world
    // matrix with the player's current transform. Returns null when the
    // bone is unknown so SfxContext.ResolveBone falls back to its
    // approximation table (lerps body bones along Y from feet).
    //
    // _boneWorldsScratch is repopulated each render frame; at cast time
    // it's at most one frame stale (~16ms), well within shipped DS1 sfx
    // timing tolerances. No need to recompute on the cast thread.
    private Vector3? ResolvePlayerBone(string logicalName)
    {
        if (_player is null) return null;
        var pcMesh = _player.Actor.Mesh;
        if (pcMesh is null) return null;
        // DS1 logical name → skeleton bone name. Heroes (and most actors)
        // share the canonical mapping below; rare bone_translator overrides
        // would need a per-template lookup, which we'll fold into a future
        // slice if any shipped spell trips on it.
        string skeletalName = logicalName.ToLowerInvariant() switch
        {
            "weapon_bone"     => "weapon_grip",
            "shield_bone"     => "shield_grip",
            "kill_bone"       => "bip01_spine2",
            "kill"            => "bip01_spine2",
            "body_anterior"   => "bip01_head",
            "head"            => "bip01_head",
            "body_mid"        => "bip01_spine2",
            "body_posterior"  => "bip01_pelvis",
            _                 => logicalName,
        };
        int boneIdx = -1;
        for (int bi = 0; bi < pcMesh.BoneNames.Count; bi++)
        {
            if (string.Equals(pcMesh.BoneNames[bi], skeletalName, StringComparison.OrdinalIgnoreCase))
            { boneIdx = bi; break; }
        }
        if (boneIdx < 0 || boneIdx >= _boneWorldsScratch.Length) return null;
        // Bone world matrix (mesh-local) × player transform → world.
        // Translation only; SfxContext returns a Vector3 and we don't yet
        // honor bone orientation (no shipped spell needs a per-bone basis).
        var world = _boneWorldsScratch[boneIdx] * _player.CurrentTransform;
        return world.Translation;
    }

    // Phase 21-SC-SPELL-VFX — element-aware 3D cast visual. Always emits a
    // primary primitive (beam or projectile) so every one of the 69 shipped
    // OffensiveInstantHit spells has a clear cast read at gameplay distance,
    // independent of how complete the sfx_script VM is for that script's
    // verbs. Lightning-class → instant beam (DS1 zap shape), Fire-class →
    // homing fireball with fire+ember trail (DS1 trackball shape), everything
    // else → tinted beam in the element's color.
    private void SpawnSpellVisual(Vector3 src, Vector3 dst,
                                  SiegeFX.Core.Assets.SpellElement element,
                                  Vector4 color)
    {
        if (_particles is null) return;
        switch (element)
        {
            case SiegeFX.Core.Assets.SpellElement.Lightning:
                _particles.SpawnLightning(src, dst, color, 0.40f);
                // Layer a warm-white second beam (DS1 zap is two passes —
                // cool-white over warm-white). Slightly different seed gives
                // the jaggy stack a parallax read.
                _particles.SpawnLightning(src, dst,
                    new Vector4(1f, 0.95f, 0.85f, 1f), 0.30f);
                break;
            case SiegeFX.Core.Assets.SpellElement.Fire:
                _particles.SpawnProjectile(src, dst, color, 0.55f, 16f, 0);
                break;
            case SiegeFX.Core.Assets.SpellElement.Ice:
                _particles.SpawnProjectile(src, dst, color, 0.55f, 18f, 1);
                break;
            case SiegeFX.Core.Assets.SpellElement.Acid:
                _particles.SpawnProjectile(src, dst, color, 0.50f, 14f, 3);
                break;
            case SiegeFX.Core.Assets.SpellElement.Death:
                _particles.SpawnLightning(src, dst, color, 0.35f);
                _particles.SpawnSmoke(dst,
                    new Vector4(0.45f, 0.20f, 0.55f, 0.7f), 0.6f, 1.0f, 8);
                break;
            case SiegeFX.Core.Assets.SpellElement.Holy:
                _particles.SpawnLightning(src, dst, color, 0.30f);
                _particles.SpawnSpark(dst,
                    new Vector4(1f, 0.95f, 0.65f, 1f), 0.6f, 0.45f, 24);
                break;
            default:
                _particles.SpawnLightning(src, dst, color, 0.30f);
                break;
        }
    }
    // Phase 12e — one entry per dead actor that produced a non-empty loot roll.
    // Drawn as a small untextured cube at Pile.Position using the mesh shader's
    // default beige tint. Phase 14a upgraded the bare Vector3 to a LootPile record
    // so the pickup path can transfer the actual rolled items into PC inventory.
    private readonly List<LootPile> _lootPiles = new();
    private DebugCubeMesh? _lootCube;
    // Phase 9-SC-LL — frame-local cache: world position + display name for
    // each loot pile that resolved to a real item this frame. Built during
    // the loot mesh-draw loop, consumed by the text-overlay pass below.
    // Stays a field (not a method local) so we don't reallocate every frame.
    private readonly List<(Vector3 World, string Label)> _frameLootLabels = new();
    // Phase 14a — PC inventory backing store; HUD's InventoryPanel lays it out
    // as the DS1-authentic 8×5 grid. Populated by auto-pickup when the Farmboy
    // walks within PickupRadius of a pile during the 20 Hz tick.
    private readonly List<SiegeFX.Core.Actors.LootEntry> _playerInventory = new();
    private const float PickupRadius = 1.8f;
    // Phase 14b — PC equipment slots keyed by DS1 slot tag (es_weapon_hand,
    // es_feet, es_spellbook, etc.). Populated at spawn by walking the specializes
    // chain for `[inventory][equipment]`. 14c reads damage_min/max off the
    // weapon template to replace HeroBaselineStats damage.
    private readonly Dictionary<string, string> _playerEquipment = new(StringComparer.OrdinalIgnoreCase);
    // Phase 14d — resolver kept alive past LoadPlayActors so weapon swaps at pickup
    // time can load the new item's ASP + texture on demand. Null outside play-region mode.
    // The resolver holds TankReader handles that wrap the FileStreams below, so the
    // tanks must outlive the resolver — hence they're fields here rather than `using
    // var` locals inside LoadPlayActors (initial try died with ObjectDisposedException
    // the first time a goblin dropped something).
    private SiegeFX.Core.Assets.AssetResolver? _playResolver;
    private SiegeFX.Core.Tank.TankFile? _playMapTank;
    private SiegeFX.Core.Tank.TankFile? _playLogicTank;
    private SiegeFX.Core.Tank.TankFile? _playObjectsTank;
    // Phase 21c-5 — Terrain.dsres ships the b_t_*.raw bitmaps that interior
    // props (bookshelves, doors, walls) reference even though they aren't
    // terrain meshes themselves. Without this in the play resolver, those
    // props rendered with the neutral fallback color.
    private SiegeFX.Core.Tank.TankFile? _playTerrainTank;
    // Phase 18a — Sound.dsres pinned for the audio engine's clip lifetime.
    // We only read .wav blobs out at LoadPlayActors time, but keeping the
    // tank open is harmless and lets later sub-phases lazy-load extra SFX
    // (hit, death, level-up) without re-opening the file.
    private SiegeFX.Core.Tank.TankFile? _playSoundTank;
    private SiegeFX.Audio.AudioEngine? _audio;
    // Phase 22-SC-MUSIC-B — single-channel streaming mp3 player for the
    // game's music track. Constructed alongside _audio (shares the
    // OpenAL device + context) and ticked once per frame to refill the
    // streaming buffer queue. Track changes go through PlayMusicTrack
    // (extract from _playSoundTank, hand bytes to MusicPlayer.Play).
    private SiegeFX.Audio.MusicPlayer? _music;
    // Phase 22-SC-MUSIC-B — last-loaded music track basename so a redundant
    // PlayMusicTrack with the same name (region re-entry, save-load) is a
    // no-op rather than restarting the clip. Empty until first Play.
    private string _currentMusicTrack = "";
    // Phase 22-SC-MUSIC-C/D — active region mood. ApplyMoodMusic stores
    // this so a later combat-state transition can reach back into the
    // mood's battle_track / standard_track without re-resolving the
    // region→mood lookup. Null when no mood has been applied (viewer
    // modes, headless smoke runs).
    private SiegeFX.Core.Assets.MoodSetting? _activeMood;
    // Phase 22-SC-MUSIC-D — combat-music state. _inCombat flips the
    // moment any hostile NPC enters Chase/Attack against the player;
    // _combatExitTimer accumulates wall time since the LAST hostile left
    // combat state and only after CombatExitDelay does the music revert
    // to standard_track. Without the delay, transient encounters
    // (single-skirmish in fh_r1) would flicker the music inside ~3s.
    private bool _inCombat;
    private float _combatExitTimer;
    private const float CombatExitDelay = 3f;
    // Clip-id constants — tying RenderHost call sites to the AudioEngine
    // dictionary keys through symbols rather than magic strings keeps a typo
    // in one place from silently disabling a sound.
    const string SfxZapCast         = "spell_zap_cast";
    const string SfxHealingWindCast = "spell_healing_wind_cast";

    // Phase 21-SC-SPELL-VFX-3a — per-spell cast-sound resolution cache.
    // Key = SpellTemplate.Name. Value = the audio-engine clip-id we should
    // Play() at cast time, derived from the first `sound play <clip>`
    // statement in the spell's compiled cast sfx_script. Empty string means
    // "we already looked, the script has no sound play, use the fallback."
    // Populated lazily on first cast of each spell.
    private readonly Dictionary<string, string> _spellCastSoundCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Phase 21-SC-SPELL-VFX-3c — per-spell static-coverage decision cache.
    // Key = SpellTemplate.Name. Value = true iff every `sfx create` in the
    // compiled cast script names a kind in SfxRuntime.SupportedCreateKinds.
    // The compile + walk runs once per spell; subsequent casts hit the dict.
    private readonly Dictionary<string, bool> _spellCoverageCache =
        new(StringComparer.OrdinalIgnoreCase);
    // Phase 18b — combat-feedback SFX. Singles + per-group ids. The
    // group ids are what RenderHost calls Play() with; the singles are
    // the underlying variants (registered first, then grouped).
    const string SfxMeleeSwingGroup = "melee_swing";
    // Phase 9-SC-2/SC-8 — hit group is now per weapon material, lazy-registered.
    // Group ids are formed at runtime as "melee_hit_<material>" (e.g. melee_hit_steelsword).
    const string SfxMeleeHitGroupPrefix = "melee_hit_";
    const string SfxMeleeMiss       = "melee_miss";
    const string SfxLevelUp         = "level_up";
    // Phase 21d-2a-ix — GUI feedback. Surgical wires for the three call sites
    // where DS1 plays a UI cue today: inventory open/close, loot pickup, and
    // a failed cast for lack of mana. Picked from `audio coverage` audit's
    // [gui] orphan category — the rest of the put_down_<item> family needs a
    // real drag/drop inventory before it has a trigger to fire on.
    const string SfxGuiInventory   = "gui_inventory_sheet";
    const string SfxGuiPickup      = "gui_pick_up";
    // Phase 21-SC-SCROLL-C-2 — DS1 ships per-category drop sounds
    // (s_e_gui_put_down_<category>.wav) for armor_chain/leather/metal/
    // plate, book, boots, gloves, helmet, jewelry, mace, potion, robe,
    // scroll, shield, staff, sword, gold, swish, ...). The full mapping
    // (item template → category → cue) lands in SC-SCROLL-AUDIO. For
    // now, scroll-drop uses the dedicated scroll cue; other categories
    // still fall back to the generic SfxGuiInventory.
    const string SfxGuiPutDownScroll = "gui_put_down_scroll";
    const string SfxGuiOutOfMana   = "gui_out_of_mana";
    // Phase 24-POLISH-C — frontend menu click cue. DS1 ships
    // s_e_frontend_big_button.wav (79 KB) as the main-menu button click;
    // tiny_button is the spinner arrow. Hover SFX is a follow-up — the
    // Sound.dsres inventory doesn't list a dedicated rollover cue, so a
    // softer pitched-down version of the click would be a synthesizer
    // job rather than a "play this clip" wire-up.
    const string SfxFrontendBigButton  = "frontend_big_button";
    const string SfxFrontendArrowButton = "frontend_arrow_button";
    // Phase 25-CHROME — logo "fly-in" + "fly-out" cues. DS1 plays these
    // during the splash → main menu sequence: flyin = sword drop into
    // log (the heavy thunk you hear); flyout = sword withdraws + the
    // logo flies up off-screen.
    const string SfxFrontendLogoFlyin  = "frontend_logo_flyin";
    const string SfxFrontendLogoFlyout = "frontend_logo_flyout";
    // Phase 9-SC-2 — death cues are derived from the actor's template's
    // [aspect][voice][die] `*` attribute (universal DS1 pattern). Cache the
    // cue stems we've already pulled out of Sound.dsres so the per-kill
    // path isn't re-registering the same WAV blob over and over.
    private readonly HashSet<string> _registeredDeathCues = new(StringComparer.OrdinalIgnoreCase);
    // Phase 9-SC-8 — track which (material) hit-group ids we've already registered so
    // EnsureHitGroupRegistered doesn't reload the same WAVs every swing. The actual
    // group id is "melee_hit_<material>" and contains every shipped flesh-impact
    // variant for that weapon material.
    private readonly HashSet<string> _registeredHitGroups = new(StringComparer.OrdinalIgnoreCase);
    // Phase 9-SC-8 — material from the equipped weapon's [aspect][material]; drives
    // hit-cue selection. Empty string until TryLoadPlayerWeapon resolves it.
    private string _playerWeaponMaterial = "steelsword";
    // Phase 14d — currently-rendered weapon mesh pinned to the PC's weapon_grip bone.
    // Null until TrySpawnPlayer resolves the first equipped weapon. Swapped on every
    // es_weapon_hand change so a pickup upgrade shows up visually.
    private StaticMesh? _weaponMesh;
    private GlTexture? _weaponTexture;
    private int _weaponGripBoneIdx = -1;
    // Phase 14d — inverse bind-pose of the weapon ASP's grip bone (bone 0 for DS1
    // weapons; every corner weights to it). Pre-multiplied into uModel so the
    // weapon's grip bone origin snaps to the player's hand bone. Without this
    // the dagger draws at the weapon ASP's mesh origin, which is nowhere near
    // the grip — blade tip usually, judging by how DS1 weapons are authored.
    private Matrix4x4 _weaponBindInv = Matrix4x4.Identity;
    // Phase 9-SC-7 — item-mesh cache for ground loot. Keyed by item template name
    // (e.g. "dg_g_d_1h_fun"). Each entry is the loaded ASP mesh + optional first-
    // texture binding; tracked for sentinel "no mesh authored" so we don't keep
    // retrying templates that have no [aspect][model] (gold piles, scrolls).
    private readonly Dictionary<string, ItemMesh?> _itemMeshCache =
        new(StringComparer.OrdinalIgnoreCase);
    // Phase 9-SC-7 follow-up — log the cube-fallback reason once per unique
    // ref so the user can read the cause from the diag output without spam.
    private readonly HashSet<string> _loggedItemRefMisses =
        new(StringComparer.OrdinalIgnoreCase);
    private sealed record ItemMesh(StaticMesh Mesh, GlTexture? Texture, string? DisplayName);

    // 21d-2a-vi → 9-SC-1: weapon_grip prerotation. SiegeMax ASPImport.ms applies
    // an `angleAxis 90 [1,0,0]` to weapon_grip / shield_grip on import; the PRS
    // keys for those bones are authored against that prerotated frame. Without
    // the same rotation at draw time the dagger sits 90° off (icepick grip
    // instead of thrust grip).
    //
    // X 180° rotation + (+0.02, +0.04, 0) translation in bone-local space snaps
    // the fun dagger's blade into the palm — visually signed off in 21d-2a-vi
    // after F1-F4 A/B cycling. The cycling has been removed now; if other weapon
    // classes need different offsets they should be authored on the weapon item
    // template, not knobbed at runtime.
    private static readonly Quaternion s_gripPreRot =
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
    private static readonly Vector3 s_gripPreTrans = new(0.02f, 0.04f, 0f);

    // Phase 9-SC-10 — extra prerotation for the shield slot, applied on top
    // of the shared s_gripPreRot. The dagger's X 180° doesn't quite fit a
    // shield's bind orientation; +85° around X (visually signed off) tilts the
    // shield face out from the forearm into the standard "behind the buckler"
    // ready pose. Authored as a constant — if a future shield mesh disagrees
    // it should ship its own [aspect] offset rather than re-introducing the
    // runtime knob removed at the same time as the dagger's F1-F4 cycling.
    private static readonly Quaternion s_shieldExtraRot =
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, 85f * MathF.PI / 180f);
    private static readonly Vector3 s_shieldExtraTrans = Vector3.Zero;

    // Phase 21d-2a-vii — layered equipment composition. Each entry is a skinned
    // ASP that re-uses the body's biped skeleton (boots/helms/gauntlets) and
    // gets posed against the body's current animation clip, then drawn through
    // the same _skinShader. The entry's AspMesh holds the per-vertex weights;
    // SkinnedMesh holds the GL VAO/VBO/EBO; Texture is the resolved albedo.
    // Body chest "armor" doesn't sit here — it's a slot-1 texture override on
    // the body subset and lives in _chestTexOverrideName instead. Spawned by
    // TrySpawnPlayer; rebuilt on every equipment change.
    private readonly List<EquippedLayer> _equippedLayers = new();
    private string? _chestTexOverrideName;
    // 21d-2a-viii — character creator picks resolved at spawn. Skin = body
    // subset 0 (face/hair/arms); pants = body subset 1 (clothing strip). Both
    // are nulled out for NPCs and for player templates without a matching env
    // var override; the renderer's ResolveActorTexture falls through to the
    // template's authored aspect.textures{0,1} when these are null.
    private string? _skinTexOverrideName;
    private string? _pantsTexOverrideName;
    private readonly Dictionary<(string Mesh, string Tex), GlTexture?> _equipTexCache = new();

    private sealed class EquippedLayer
    {
        public required SiegeFX.Core.Assets.AspMesh Asp { get; init; }
        public required SkinnedMesh Mesh { get; init; }
        public required GlTexture? Texture { get; init; }
        public required string SlotName { get; init; }
        public required string ItemRef { get; init; }
        public required string MeshBaseName { get; init; }
    }

    // Phase 9-SC-10 — bone-attached props other than the primary weapon.
    // Shields are the only shipped DS1 example (es_shield_hand → shield_grip),
    // but the same pattern handles any future single-bone attachment (quiver,
    // off-hand torch, lantern). The primary weapon stays in its dedicated
    // _weaponMesh fields because hit-cue selection and material lookup are
    // entangled with es_weapon_hand specifically; this list is everything else.
    private sealed class AttachedItem
    {
        public required string SlotName { get; init; }
        public required string ItemRef { get; init; }
        public required StaticMesh Mesh { get; init; }
        public required GlTexture? Texture { get; init; }
        public required Matrix4x4 BindInv { get; init; }
        public required int BoneIdx { get; init; }
    }
    private readonly List<AttachedItem> _attachedItems = new();

    // Phase 9-SC-9 — pile holds an optional throw state for items the player
    // just dropped. While Throw is non-null, the pile lerps from source to
    // target with a parabolic Y arc (mimics DS1's drop-toss animation), and
    // auto-pickup skips it so the player can't grab it mid-flight. Cleared
    // when Elapsed reaches Duration; pile is then a normal pickup target.
    private sealed class LootPile
    {
        public Vector3 Position;
        public float RotationY; // radians; non-zero only while a throw is in flight
        // Phase 21-SC-SCROLL-F — X-axis tumble. Set during a throw alongside
        // RotationY so dropped items rotate-and-twist mid-flight (more
        // DS1-faithful than a pure yaw spin). Reset to 0 on landing; the
        // RestPitch flow below then governs the resting pitch.
        public float RotationX;
        // Phase 21-SC-SCROLL-GLITTER — DS1's spell-on-ground "pixie dust"
        // sparkle. The shipped combat_spell_sparkle / nature_spell_sparkle
        // scripts fire a `sfx create sparkles` burst on `we_dropped`; in
        // DS1 the sparkles primitive without a dur() parameter is a
        // CONTINUOUS emitter, so the pile glitters until picked up. Our
        // VM treats sparkles as one-shot — easier to fake the continuous
        // look at the pile-tick layer with a re-fire timer than to refactor
        // the SfxRuntime emitter model. Carries are per-pile so each pile
        // glitters independently.
        public float GlitterCarry;
        // Phase 9-SC-10 — per-pile rest pitch so items that aren't naturally
        // upright (shields modeled vertically along the forearm) lie flat on
        // the ground after landing. Authored at pile creation by inspecting
        // the item template chain; default 0 keeps weapons / generic loot
        // standing as they are now.
        public float RestPitch;
        public readonly List<SiegeFX.Core.Actors.LootEntry> Items;
        public LootThrow? Throw;
        public LootPile(Vector3 position, List<SiegeFX.Core.Actors.LootEntry> items)
        { Position = position; Items = items; }
    }
    private sealed class LootThrow
    {
        public Vector3 Source;
        public Vector3 Target;
        public float Duration;
        public float Elapsed;
        public float ArcHeight;
        public float Spins;        // total Y-axis turns over the duration
        public float StartRotation; // initial yaw so PC vs enemy drops don't all face the same way
        // Phase 21-SC-SCROLL-F — X-axis tumble. Lower count than Y so the
        // result reads as "twist + flop" not a chaotic spin. 0 keeps
        // legacy enemy-drop look (pure yaw); player drops set ~1.0 for
        // a single end-over-end during flight.
        public float XSpins;
        public float StartRotationX;
    }
    // Phase 13a — the one player-controlled actor's render state. Null until
    // TrySpawnPlayer succeeds. Also lives inside _actors (rendered + ticked with
    // the NPCs); this field is the named handle for the input layer.
    private ActorRenderState? _player;
    // Phase 13c — NavFollower driving the PC's click-to-move. Separate from any
    // ActorFollower (wander AI) because the player's targets come from user clicks,
    // not random sampling. Null until the first LMB click routes through the nav
    // mesh; once created, ticked alongside NPC followers in the logic-step loop.
    private SiegeFX.Core.Nav.NavFollower? _playerFollower;
    // Region-scope nav mesh stashed from LoadPlayActors so the LMB raycaster can
    // hit-test against it. Null when the region has no nav-floor SNOs (shouldn't
    // happen for playable regions; guarded anyway).
    private SiegeFX.Core.Nav.NavMesh? _navMesh;
    // Persistent XZ unit facing for the PC. Updated only when the player moves,
    // so an idle PC keeps his last heading instead of snapping to +Z each tick.
    private Vector3 _playerFacing = Vector3.UnitZ;
    private double _actorTickAccumulator;

    // Phase 9-SC-10b — render-state interpolation for the PC. The actor runtime
    // ticks at 20 Hz (50 ms steps) but render runs at 60+ fps; without smoothing
    // the body's CurrentTransform jumped in 50 ms increments and the chase
    // camera dragged the whole world with it (visible as walk jitter). We
    // double-buffer pos/facing each fixed tick and lerp into CurrentTransform
    // every render frame using the leftover accumulator. Render lags one tick
    // (50 ms) behind sim, which is invisible at this cadence.
    private Vector3 _playerRenderPosPrev;
    private Vector3 _playerRenderPosNext;
    private Vector3 _playerRenderFacingPrev = Vector3.UnitZ;
    private Vector3 _playerRenderFacingNext = Vector3.UnitZ;
    private bool _playerRenderInit;

    // Phase 21b-1 — `--diag` performance instrumentation. _diagMode is set
    // from the CLI flag and gates every block in this file that prints
    // perf data, so a non-diag run pays nothing beyond a few branch
    // predictions. Stage timings live as a list of (label, ms) pairs so
    // the OnLoad summary can be one consolidated table at the end. The
    // frame-time ring is fixed-size so the histogram cost is O(N) over
    // a known buffer, not O(frames-since-launch).
    private readonly bool _diagMode;
    // Phase 21d-2a-v — env-var-driven flag; when true, the actor loop writes a
    // per-subset solid color into uSubsetTint and flips uSubsetTintActive=1 for
    // the duration of the draw, so each ASP subset shows up as a distinct color
    // on the rendered model.
    private bool _subsetTintActive;
    private readonly List<(string Label, double Ms)> _diagStageTimings = new();
    private readonly System.Diagnostics.Stopwatch _diagBootStopwatch = new();
    private const int FrameRingSize = 240;     // 4 sec at 60 Hz
    private readonly double[] _diagFrameMs = new double[FrameRingSize];
    private int _diagFrameRingHead;
    private int _diagFrameRingFill;
    private double _diagFrameAccumulator;
    private const double FrameReportIntervalSec = 1.0;

    private sealed class ActorRenderState
    {
        public Actor Actor = null!;
        public SkinnedMesh GlMesh = null!;
        public double AnimTime;
        public int LastClipIndex;

        // Phase 11d — actors that spawn over the nav mesh get a brain (wander + aggro)
        // and roam/chase. Those that land off-mesh (pens inside buildings, props) have
        // Brain=null and just render at their authored spawn pose via CurrentTransform.
        // The brain wraps the wander follower; aggro / chase / swing on top of pure
        // wander landed in Phase 16c.
        public SiegeFX.Core.Actors.ActorBrain? Brain;
        public Matrix4x4 CurrentTransform;

        // Phase 12c — once the actor dies (CurrentLife hits 0), the follower is
        // nulled, the anim accumulator stops advancing, and the last skin matrices
        // effectively freeze the body mid-pose. Phase 12d will swap to chore_die.
        public bool IsDead;

        // Phase 13a — set for the single player character. Distinct from NPCs so
        // the input layer (LMB move, RMB attack — 13c/d) can find and drive this
        // one actor without scanning all 181. Also gates off the random-wander
        // follower: the player stands still until the user clicks somewhere.
        public bool IsPlayer;

        // Phase 21c-4 — last frame's XZ translation, used to decide whether the
        // actor is walking (swap to chore_walk clip) or idle (chore_default).
        // Initialized from the spawn position the first time the renderer sees
        // the actor; a frame's velocity over a small threshold flips to walk.
        public Vector3 LastPositionXZ;
        public bool HasLastPosition;
        public bool IsMoving;
    }

    // Phase 10-SC-1 — trigger-runtime queries route through these helpers so the
    // trigger context never has to reach into ActorRenderState (which is private to
    // this class). Each one is a thin pass-through; we keep them grouped here so
    // future trigger verbs (line-of-sight checks, occupants groups) can sit alongside.
    internal IEnumerable<(uint Scid, Vector3 Pos)> EnumerateActorPositionsForTriggers()
    {
        for (int i = 0; i < _actors.Count; i++)
        {
            var s = _actors[i];
            yield return (s.Actor.Instance.Scid, s.CurrentTransform.Translation);
        }
    }

    internal Vector3? PlayerWorldPositionForTriggers()
    {
        if (_player is null) return null;
        return _player.CurrentTransform.Translation;
    }

    internal void PostTriggerWorldMessage(string name, uint fromScid, uint toScid)
    {
        // Skrit-side actors hear it through the bus; trigger-side rows hear it via
        // the runtime's per-instance inbox stamp. Phase 10-SC-1 ships both halves so
        // a `send_world_message` action posted by trigger A can satisfy a
        // `receive_world_message` condition on trigger B in the same region.
        _actorBus?.Post(name, fromScid, toScid, 0, 0);
        _triggerRuntime?.PostInboundMessage(toScid, name);
    }

    private string? _lastLoggedMoodChange;
    internal void OnTriggerMoodChange(string moodName)
    {
        // Mood swap is Phase 18 territory — the trigger runtime fires the request
        // and the mood subsystem consumes it. For now we log so audits can confirm
        // the action is reaching dispatch even when the mood pipeline doesn't yet
        // accept arbitrary names. Some fh_r1 triggers re-fire per tick; only log
        // on actual change so the console stays readable.
        if (string.Equals(_lastLoggedMoodChange, moodName, StringComparison.OrdinalIgnoreCase))
            return;
        _lastLoggedMoodChange = moodName;
        Console.WriteLine($"[trigger] mood_change → '{moodName}'");
    }

    internal void OnTriggerCallSfxScript(string scriptName, IReadOnlyList<string>? args, Vector3 origin)
    {
        // Phase 17-SC-G — region emitters route here via TriggerRuntime's
        // call_sfx_script action. The SfxRuntime spawns a coroutine + maintains
        // any persistent fire/smoke/steam columns the script kicks off.
        _sfxRuntime?.Spawn(scriptName, origin, args);
    }

    // Phase 21c — one placed prop (tree, barrel, fence, crop, candle, etc.).
    // Drawn via the static-mesh pipeline; no per-frame state besides the bake.
    private sealed class StaticPropInstance
    {
        public StaticMesh Mesh = null!;
        public GlTexture? Texture;
        public Matrix4x4 World;
        public string Template = "";
        // Phase 17-SC-I-2 — DS1's chore_default = rotatex?rpm=N skrit drives the
        // mill waterwheel + a few other rotating props. Rather than running
        // skrit per static prop (props aren't actors), we recognise the pattern
        // at spawn time and bake the angular velocity here. Axis 0/1/2 = X/Y/Z;
        // SpinRadPerSec = 0 means no spin. The rotation is applied around the
        // model's local origin before the placement transform — the asp origin
        // sits at the wheel spindle, so the wheel spins in place rather than
        // orbiting the region origin.
        public int   SpinAxis;
        public float SpinRadPerSec;

        // Phase 17-SC-K — breakable container/prop state. DS1 wires
        // `[aspect][break_particulate]` on barrels/crates/jugs that the
        // player can shatter with a click; non-breakable variants ship
        // `aspect:is_invincible = true`. We promote those placements out
        // of pure-static rendering: they keep getting drawn the same way
        // until life reaches zero, at which point IsDestroyed flips and
        // the renderer skips them. Click-attack picks them when no live
        // combatant is closer to the cursor.
        public bool  IsBreakable;
        public float Life;
        public float MaxLife;
        public bool  IsDestroyed;
    }

    // Phase 9a — skrit-driven animation. The runtime ticks a SkritInstance every logic
    // frame; OnStartChore$ sets the blender's "current anim" index, which we watch via
    // ActorHostBridge.CurrentAnimIndex to pick the PRS clip to play. Replaces the hardcoded
    // single-clip _anim pipeline when --skrit-anim is used.
    private SkritRuntime? _skritRuntime;
    private SkritInstance? _skritInstance;
    private ActorHostBridge? _skritHost;
    private PrsAnimation[]? _skritClips;
    private int _skritCurrentClip = -1;
    private double _skritTickAccumulator;
    private GL? _gl;
    private IInputContext? _input;
    private Shader? _gridShader;
    private Shader? _meshShader;
    private Shader? _skinShader;
    private GridMesh? _grid;
    // Phase 15a — 2D text overlay. Constructed in OnLoad, font wired in
    // LoadPlayActors once _playResolver can serve fonts.gas + the .raw atlas.
    // Drawn after the 3D scene each frame inside its own depth-disabled,
    // alpha-blended pass.
    private TextRenderer? _textRenderer;
    private BarRenderer? _barRenderer;
    // Phase 9-SC-13 — textured-rect HUD primitive used to draw inventory
    // icons (b_gui_ig_*.raw) inside InventoryPanel cells. Same blend pass
    // as the other HUD renderers; lifetime parallels them.
    private IconRenderer? _iconRenderer;

    // Phase 21-SC-BARREL-A1 — DS1 sprite cursor state machine. Cursors.gas
    // ships a 6-state set under /art/bitmaps/gui/cursors/: pointer (sword)
    // is the default, attack1 (red sword) is enemy hover, smash1.flm is
    // the animated hammer for breakable props, grab1.flm is the animated
    // hand for loot piles, talk is the NPC marker. We hide the OS cursor
    // and draw our own sprite at _currentMousePos minus the authored
    // hotspot — sethotspot(21,13) for 64x64, (9,5) for 32x32. State picks
    // happen per render frame via a ground-plane raycast through the
    // mouse, mirroring TryClickToAttack / TryClickToBreakProp / TryClickToTalk
    // so cursor visual tracks 1:1 with what a click actually picks.
    private enum CursorState { Pointer, Attack, CastAttack, Smash, Grab, Talk }
    private CursorState _cursorState = CursorState.Pointer;
    private GlTexture? _cursorPointer;          // sword (default)
    private GlTexture? _cursorAttack;           // red sword (enemy under cursor in melee/ranged)
    private GlTexture? _cursorCastAttack;       // blue-glow sword (enemy under cursor in spell mode)
    private GlTexture? _cursorTalk;             // talk marker
    private GlTexture[]? _cursorSmash;          // animated hammer (smash1.flm)
    private GlTexture[]? _cursorGrab;           // animated hand (grab1.flm)
    private bool _cursorTexturesAttempted;
    private bool _osCursorHidden;
    // Phase 17-SC-E — billboard particle backend (fire, smoke, sparks,
    // lightning bolts). Built once GL is up; LoadPlayActors fills its
    // sprite atlas off Objects.dsres. Tick + Draw run inside OnRender's
    // world pass before the HUD overlay starts.
    private ParticleSystem? _particles;
    // Per-template icon cache. Keyed by the same itemRef that TryGetItemMesh
    // uses, so pcontent specs and direct template names share one entry.
    // null sentinel = no [gui][inventory_icon] authored / .raw missing.
    private readonly Dictionary<string, GlTexture?> _itemIconCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedItemIconMisses =
        new(StringComparer.OrdinalIgnoreCase);
    // Phase 9-SC-14 — per-template (gui_grid_w, gui_grid_h) cache. Defaults
    // to (1,1) when the template omits the attributes; helms / robes / two-
    // handers ship 2x2 / 2x1 etc. and the panel's first-fit packer needs
    // these to reserve adjacent cells.
    private readonly Dictionary<string, (int W, int H)> _itemGridCache =
        new(StringComparer.OrdinalIgnoreCase);
    // Phase 9-SC-13 — single source of truth for "this pcontent spec rolls
    // to that template name." Without this, the mesh path and the icon path
    // each call PcontentResolver.TryResolve independently and roll different
    // templates from the bucket — the loot pile would render one weapon, the
    // inventory cell a different one, and screen_name / inventory_icon would
    // disagree. Cache lookup must precede any TryResolve call in either path.
    private readonly Dictionary<string, string> _resolvedSpecCache =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _inventoryOpen; // 'I' toggles; rendered above the HUD bars
    private readonly InventoryPanel _inventoryPanel = new(); // owns drag state
    // Phase 21-SC-INV-A — DS1's three top-docked panels live alongside the
    // grid inventory: character pane on 'C', spell book on 'B'. Each toggles
    // independently so the player can park any combination open. Origins
    // are assigned in OnRender so they tile across the dock from left→right.
    private bool _charPanelOpen;
    private bool _spellBookOpen;
    private readonly CharacterPanel _characterPanel = new();
    private readonly SpellBookPanel _spellBookPanel = new();
    // Phase 21-SC-INV-B (round 2) — basename of the player's portrait icon
    // (e.g. b_gui_ig_i_ic_c_fb_01 for farmboy) pulled off the actor template's
    // [actor]portrait_icon attribute at spawn. Empty string when the chosen
    // template doesn't ship one; in that case the panel falls back to a dim
    // placeholder cell.
    private string _playerPortraitIconName = "";
    // Phase 21-SC-INV-A2 — currently selected combat-ability slot for the
    // mini-HUD's 4-cell ability bar. The cell that's selected gets a thicker
    // golden ring; the other three are dim. 0=melee, 1=ranged, 2=spell-1
    // (Q/Primary), 3=spell-2 (W/Secondary). Phase 21-SC-ABIL-RMB wires this
    // into the RMB tap dispatch: clicking a target with cell 2/3 active casts
    // the slotted spell instead of melee-attacking. Q/W keys still cast their
    // own slots so keyboard-only players aren't forced through the cell ring.
    private int _activeAbilityIdx; // 0..3
    // Hit-rects published by the last DrawMiniStatusHud so the mouse-down
    // handler can route clicks to ability-bar selection / open-arrows toggle.
    private (int X, int Y, int W, int H)[] _abilitySlotRects = new (int, int, int, int)[4];
    private (int X, int Y, int W, int H) _openArrowsRect;
    // Phase 21-SC-INV-A2 (round 7) — right edge of the always-on HUD so the
    // dock-layout pass can plant the three large panels immediately to its
    // right instead of below it. Updated each DrawMiniStatusHud frame.
    private int _miniHudRightX;
    // Phase 9-SC-9 — put_down cues lazy-registered the same way death cues are
    // (template's [aspect][voice][put_down] *). Cache hits per cue stem; many
    // templates share the same put_down cue (every steel sword fires the same
    // s_e_gui_put_down_steelsword) so this keeps the per-drop path one cache hit.
    private readonly HashSet<string> _registeredPutDownCues = new(StringComparer.OrdinalIgnoreCase);
    private bool _questLogOpen;  // 'L' toggles; sibling overlay to inventory
    private readonly PauseMenu _pauseMenu = new(); // Esc toggles; click "Resume" or "Quit"
    // Phase 23-SC-OPTIONS-A — F10 (and pause menu's "Options" button in
    // a future slice once we wire it through) opens the modal Options
    // dialog with four tabs: Video, Audio, Input, Game. Skeleton lands
    // chrome + state machine; per-tab controls follow in slices B–F.
    private readonly OptionsMenuPanel _optionsMenu = new();
    bool _optionsAudioHookWired;
    // Phase 24-MAINMENU step 5+6 — main menu panel. Active only while the
    // FrontendScene is in the MainMenu state (boot path); click events
    // surface through ConsumeAction in OnUpdate.
    private readonly MainMenuPanel _mainMenu = new();
    // Phase 27-SP-FLYOUT — Single Player sub-menu panel. Active only while
    // the FrontendScene is in the SinglePlayer state. Two buttons (NEW
    // GAME / LOAD GAME) + Back. Replaces _mainMenu's input ownership for
    // the duration of the SP submenu.
    private readonly SinglePlayerMenuPanel _spMenu = new();
    // Phase 28-CD-FLYOUT — Character Creator nav panel (Previous / Next).
    // Active only while FrontendScene is in CharacterSelect state. The
    // spinner-axis buttons live on the existing _creator panel; this
    // panel only owns the two backbutton.asp-driven nav buttons.
    private readonly CharacterSelectMenuPanel _csMenu = new();
    // Phase 24-MAINMENU step 6 — About sub-screen overlay. Toggle from main
    // menu's About button; Esc / clicking outside dismisses.
    private bool _aboutOpen;
    // Phase 23-SC-OPTIONS-FOLD2 — Game-tab "Show Framerate" toggle. ApplyOptionsRuntime
    // sets it on commit; OnRender draws a small string at top-right when true.
    bool _showFps;
    double _fpsEmaMs = 16.6667; // exponential moving average of frame ms (60fps default)
    // Phase 21d-2a-viii-b — pre-spawn character creator. Opens by default when
    // a play-region path runs and SIEGEFX_CREATOR != "0"; closes on Begin (then
    // TrySpawnPlayerWithPicker fires) or Cancel (falls through to env-var-only
    // pick). Layout sourced from /ui/interfaces/frontend/character_select/character_select.gas.
    private readonly CharacterCreatorPanel _creator = new();
    // Phase 21d-2a-viii-FE — full DS1 frontend scene composer. Owns all 8
    // shipped frontend ASPs (backdrop / leftside / rightside / logo /
    // mainmenu / menubars / backbutton / heromenu) and animates them into
    // the character_select (cd) pose by playing the authored PRS clips.
    // Lazy-loaded once Objects.dsres is open (LoadPlayActors stashes
    // _playResolver) and disposed on shutdown. Replaces the viii-d
    // BarRenderer scaffolding + viii-e single-mesh CreatorChrome.
    private FrontendScene? _frontendScene;
    // Phase 21d-2a-viii-FE-2 — live 3D hero preview rendered into the
    // character_select listener rect. Owns its own ActorSpawner +
    // SkritRuntime + WorldMessageBus so the preview actor never registers
    // with the play-region bus. Lazy-built on first cd-state frame because
    // it needs _templateStore + _playResolver + _skinShader, all of which
    // come online during LoadPlayActors.
    private HeroPreviewRenderer? _heroPreview;
    // Args captured on the first frame the creator is open so Begin can re-enter
    // the spawn path with the panel-mutated picker.
    private ActorSpawner? _pendingSpawner;
    private SiegeFX.Core.Nav.NavMesh? _pendingNavMesh;
    // 21d-2a-viii-c — saved across F5/F9 so the player's chosen name and
    // variant survive a quicksave roundtrip. Empty / null when the creator
    // was bypassed (env-var spawn). Persisted into PlayerSnapshot.HeroName /
    // .Variant; restored from save into these fields by ApplySave.
    private string _heroName = "";
    private HeroVariantPicker? _heroVariant;
    // Phase 20a — dialogue overlay. Per-region conversation pool loaded with the
    // actor list; RMB on a talkable NPC opens the panel against that NPC's first
    // conversation key. Phase 20b wired dialogue.ConsumePendingQuestActivation()
    // → PlayerProgression.Journal.AddActive() and the 'L' journal overlay.
    private readonly DialoguePanel _dialogue = new();
    private IReadOnlyDictionary<string, SiegeFX.Core.Assets.ConversationDef>? _conversations;

    // Phase 20d — vendor trade overlay. Opens when the player closes a
    // dialogue with an NPC whose template is in the VendorCatalog. Esc
    // closes; LMB on row buttons drives buy/sell. Tracked alongside
    // _lastTalkedTemplate so the close edge knows which vendor to open.
    private readonly VendorPanel _vendor = new();
    private string? _lastTalkedTemplate;

    // Phase 20a (follow-up) — authored player spawn. info/start_positions.gas
    // names a default start group ("farmhouse" in the shipped main map); we
    // resolve its first slot through _regionLayout so the PC drops in next
    // to Norick instead of at the centroid of every NPC in fh_r1.
    private Vector3? _authoredSpawn;
    private StaticMesh? _mesh;
    private SnoMesh? _sno;
    private SkinnedMesh? _skinnedMesh;
    private AspMesh? _skinnedAsp;
    private PrsAnimation? _anim;
    private GlTexture? _animTexture;
    private GlTexture? _texture;
    private double _animTime;
    // Phase 17-SC-I — wallclock seconds since the renderer opened, ticked
    // every OnUpdate regardless of whether _anim is loaded. Drives the
    // waterfall UV scroll (and any future TSD-driven texture animation)
    // independent of the skinned-anim time loop above.
    private double _terrainTime;
    private readonly Dictionary<string, GlTexture> _snoTextures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, SnoMesh> _regionMeshes = new();
    private readonly List<RegionInstance> _regionInstances = new();
    // Phase 21b-2 — pre-resolved subset texture names per (mesh, texsetAbbr).
    // ResolveTexName previously allocated a new string per subset per region
    // instance per frame (ResolveTexName does string.Concat when the basename
    // contains "_xxx_"). With ~2k region instances × ~3 subsets, that was
    // ~6k strings/frame; cached here so it's O(unique mesh+texset pairs)
    // string-allocs at region load and zero per frame thereafter.
    private readonly Dictionary<(SnoMesh, string), string[]> _resolvedTexNameCache = new();
    private readonly Camera _camera = new();
    private bool _mouseLookActive;
    private Vector2? _lastMousePos;

    // Phase 21-SC-SCROLL-PRE-1/PRE-2 — cursor drag-state machine for the
    // spell-scroll UI. _currentMousePos is the always-up-to-date pointer
    // location (separate from _lastMousePos which the RMB camera nulls on
    // release). _cursorScroll holds the spell whose scroll icon is "on"
    // the cursor when the player is mid-drag — null = no drag in progress.
    // Source provenance lets a cancelled drag put the spell back where it
    // came from.
    private Vector2 _currentMousePos = Vector2.Zero;
    /// <summary>Phase 21-SC-SCROLL-C-1 — names parsed from SIEGEFX_DEBUG_SPELLS
    /// beyond the first two (primary/secondary). Seeded into
    /// PlayerSpellbook.Placed[] right after the spellbook is constructed.
    /// Null = no env-var seed in effect.</summary>
    private string[]? _pendingPlacedSeeds;
    /// <summary>Phase 21-SC-SCROLL-D follow-up — names beyond the first 12
    /// (2 active + 10 placed) overflow into the player's inventory as
    /// scroll items so testing the drag-from-inventory path (E) doesn't
    /// require world drops + pickup. Seeded into _playerInventory after
    /// the spellbook is built. Null = no inventory overflow in effect.</summary>
    private string[]? _pendingInventoryScrollSeeds;
    private SiegeFX.Core.Assets.SpellTemplate? _cursorScroll;
    private CursorScrollSource _cursorScrollSource;

    /// <summary>Where the spell currently being dragged came from. Used so
    /// a Cancel (ESC / right-click) can restore it to the source slot
    /// rather than dropping it. <see cref="None"/> means no drag in flight.</summary>
    private enum CursorScrollSource
    {
        None,
        SpellbookActive1,    // primary (Q) slot
        SpellbookActive2,    // secondary (W) slot
        SpellbookPlaced,     // one of the 10 inactive rows below the actives
        Inventory,           // a scroll item in the regular inventory grid
    }
    /// <summary>Index within the source list — meaningful for SpellbookPlaced
    /// (which row 0..9) and Inventory (which grid cell index). Ignored for
    /// the Active1/Active2 sources which are singletons.</summary>
    private int _cursorScrollSourceIndex;
    // Phase 13d — RMB does double duty: drag to orbit, tap-click to attack. We
    // latch the screen position at RMB-down and measure pixel drift on MouseUp;
    // below _rmbClickDriftPx it's a click (→ attack), otherwise just end orbit.
    private Vector2 _rmbDownPos;
    private float _rmbDrift;
    private const float RmbClickDriftPx = 4f;

    // Phase 13b — camera modes. Fly is the free RMB+WASD debug cam from earlier
    // phases; Chase locks the camera behind the player with RMB-drag to orbit the
    // yaw around him. Default is Fly so viewer modes without a PC (mesh/region
    // probes) keep their old behavior; TrySpawnPlayer flips to Chase.
    private enum CameraMode { Fly, Chase }
    private CameraMode _cameraMode = CameraMode.Fly;
    // Yaw of the camera *around* the player in Chase mode. Independent from
    // Camera.Yaw because we overwrite Camera.Yaw/Pitch every frame to make
    // Forward point at the player.
    private float _chaseYaw;
    private float _chaseDistance = 9f;
    private float _chaseHeight   = 7f;
    // Phase 21-SC-ZOOM — scroll-wheel zoom bounds. DS1 manual lists scroll-wheel
    // and Minus/Equals as canonical zoom controls; forward = zoom in (closer),
    // back = zoom out (farther). Height tracks distance via ChasePitchSlope so
    // zooming dollies along the view ray instead of arcing flatter as you pull
    // back. Slope is the default 7f/9f ratio so existing camera framing is
    // unchanged at the default distance.
    private const float ChaseDistanceMin = 3f;
    private const float ChaseDistanceMax = 18f;
    private const float ChaseZoomStep    = 0.9f;
    private const float ChasePitchSlope  = 7f / 9f;
    // Head-height offset so the camera looks at the torso/head of the ~5-foot
    // Farmboy model instead of his feet. Rough guess; tune when the real PC
    // camera appears in Phase 14+.
    private const float ChaseLookTargetY = 1.5f;

    private readonly record struct RegionInstance(Matrix4x4 World, SnoMesh Mesh, string TexsetAbbr);

    /// <summary>DS1 SNO surfaces often hold a <c>_xxx_</c> placeholder that per-snode
    /// <c>texset</c> from nodes.gas fills in at load time (e.g. <c>t_xxx_flr_04x04-a</c>
    /// + texset <c>grs01</c> → <c>t_grs01_flr_04x04-a</c>). Matches OpenSiege's
    /// <c>ReaderWriterSNO.cpp</c> behavior — the one missing piece that was leaving
    /// most terrain subsets untextured.</summary>
    private static string ResolveTexName(string raw, string texsetAbbr)
    {
        if (string.IsNullOrEmpty(texsetAbbr)) return raw;
        var i = raw.IndexOf("_xxx_", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? raw : string.Concat(raw.AsSpan(0, i + 1), texsetAbbr, raw.AsSpan(i + 4));
    }

    /// <summary>Phase 21b-2 — return the cached array of resolved subset texture
    /// names for <paramref name="mesh"/> rebound to <paramref name="texsetAbbr"/>.
    /// Building it costs <c>mesh.Subsets.Count</c> string allocations the first
    /// time we see this (mesh, abbr) pair; subsequent calls return the cached
    /// array. Cleared alongside <see cref="_regionInstances"/> on region unload.</summary>
    private string[] GetResolvedSubsetTexNames(SnoMesh mesh, string texsetAbbr)
    {
        var key = (mesh, texsetAbbr);
        if (_resolvedTexNameCache.TryGetValue(key, out var arr)) return arr;
        arr = new string[mesh.Subsets.Count];
        for (var i = 0; i < arr.Length; i++)
            arr[i] = ResolveTexName(mesh.Subsets[i].TextureName, texsetAbbr);
        _resolvedTexNameCache[key] = arr;
        return arr;
    }

    /// <summary>Phase 17-SC-I-2 — parse a DS1 chore_default skrit reference of
    /// the form <c>rotateX?rpm=-8.0</c>/<c>rotatey?rpm=...</c>/<c>rotatez?...</c>
    /// into (axis, radPerSec). Returns false for any other skrit (most actor
    /// chores) so the caller can fall back to non-rotating placement. Used
    /// once per static prop at spawn — no per-frame parsing.</summary>
    private static bool TryParseRotateSkrit(string? skrit, out int axis, out float radPerSec)
    {
        axis = 0;
        radPerSec = 0f;
        if (string.IsNullOrEmpty(skrit)) return false;
        var lower = skrit.Trim();
        var qm = lower.IndexOf('?');
        if (qm < 0) return false;
        var name = lower[..qm].Trim();
        if (!name.StartsWith("rotate", StringComparison.OrdinalIgnoreCase)) return false;
        var axisCh = char.ToLowerInvariant(name[^1]);
        axis = axisCh switch { 'x' => 0, 'y' => 1, 'z' => 2, _ => -1 };
        if (axis < 0) return false;
        var rest = lower[(qm + 1)..];
        foreach (var tok in rest.Split('&'))
        {
            var eq = tok.IndexOf('=');
            if (eq < 0) continue;
            var key = tok[..eq].Trim();
            var val = tok[(eq + 1)..].Trim();
            if (key.Equals("rpm", StringComparison.OrdinalIgnoreCase)
                && float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rpm))
            {
                radPerSec = rpm * (MathF.PI * 2f / 60f);
                return true;
            }
        }
        return false;
    }

    /// <summary>Phase 17-SC-J followup — DS1 stores per-texture scroll under
    /// <c>layer1ushiftpersecond</c> / <c>layer1vshiftpersecond</c> in each
    /// texture's TSD <c>.gas</c> sidecar. SC-I shipped a name heuristic
    /// (<c>fall</c> on / <c>static</c> off) which was wrong on both sides:
    /// it scrolled autumn-grass land textures whose name contains "fall",
    /// and it skipped the wheelfall waterfall whose layer-1 ships static
    /// (the visible motion is a separate <c>b_t_grs01_rvr_dynamic</c> layer-2
    /// overlay blended <c>modulate2x</c>, which a future slice will land).
    /// For now: layer-1 never scrolls — every shipped <c>rvr_*</c> sidecar
    /// authors layer1 vshift = 0. Until the overlay layer ships, water reads
    /// stationary; the prior heuristic's wrong-texture scroll is gone.</summary>
    private static Vector2 ComputeTexUvOffset(string textureName, double time) => Vector2.Zero;

    /// <summary>Phase 17-SC-J — pull <c>aspect.scale_multiplier</c> for a placed
    /// prop. DS1 lets the *instance* override the template (fh_r1's breakable
    /// farmhouse door is <c>scale_multiplier=1.5</c> on the placement so the
    /// destroyable variant reads visibly larger than the everyday door using
    /// the same mesh). Reads instance-level <c>[aspect]</c> first, then falls
    /// through to the template chain via <see cref="TemplateStore.GetAttribute"/>.
    /// Returns 1.0 when nothing is declared.</summary>
    private float ResolveScaleMultiplier(Template template, GasNode instanceNode)
    {
        var instanceAspect = TemplateStore.FindChild(instanceNode, "aspect");
        if (instanceAspect is not null)
        {
            var v = TemplateStore.FindAttr(instanceAspect, "scale_multiplier");
            if (v is not null && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
                return s;
        }
        var fromTemplate = _templateStore!.GetAttribute(template, "aspect", "scale_multiplier");
        if (fromTemplate is not null
            && float.TryParse(fromTemplate, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var st))
            return st;
        return 1f;
    }

    /// <summary>Region paths look like <c>/world/maps/&lt;map&gt;/regions/&lt;region&gt;</c>.
    /// Strip the trailing <c>regions/&lt;region&gt;</c> and append <c>info</c> so callers
    /// can locate map-scoped files like <c>start_positions.gas</c>. Returns null on a
    /// shape we don't recognise so the caller can fall back gracefully.</summary>
    private static string? DeriveMapInfoPath(string regionPath)
    {
        var norm = regionPath.Trim().TrimEnd('/');
        var idx = norm.IndexOf("/regions/", StringComparison.OrdinalIgnoreCase);
        if (idx <= 0) return null;
        return norm[..idx] + "/info";
    }

    private const string GridVertexSource = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;
uniform mat4 uViewProj;
out vec3 vColor;
void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vColor = aColor;
}";

    private const string GridFragmentSource = @"#version 330 core
in  vec3 vColor;
out vec4 FragColor;
void main() { FragColor = vec4(vColor, 1.0); }";

    private const string MeshVertexSource = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aUv;
uniform mat4 uViewProj;
uniform mat4 uModel;
out vec3 vNormal;
out vec2 vUv;
void main()
{
    gl_Position = uViewProj * uModel * vec4(aPos, 1.0);
    vNormal = mat3(uModel) * aNormal;
    vUv = aUv;
}";

    // Skinning vertex shader. Same per-vertex output contract as MeshVertexSource (vNormal,
    // vUv) so it pairs with MeshFragmentSource unchanged. Bone uniforms are uploaded by the
    // host once per frame from CPU-composed skin matrices (AnimationRuntime.ComputeSkinMatrices).
    // .NET row-vector matrices uploaded with transpose=false flip to column-vector in GL,
    // so `uBones[i] * vec4(aPos, 1.0)` here matches `Vector3.Transform(aPos, skin[i])` on
    // the CPU side. The linear combination of mat4s before applying to aPos is mathematically
    // identical to summing the per-bone-transformed positions weighted by the same weights —
    // one matmul instead of four, which the GPU prefers.
    private const string SkinnedVertexSource = @"#version 330 core
layout (location = 0) in vec3  aPos;
layout (location = 1) in vec3  aNormal;
layout (location = 2) in vec2  aUv;
layout (location = 3) in vec4  aWeights;
layout (location = 4) in uvec4 aBones;
uniform mat4 uViewProj;
uniform mat4 uModel;
uniform mat4 uBones[64];
out vec3 vNormal;
out vec2 vUv;
void main()
{
    mat4 skin = aWeights.x * uBones[aBones.x]
              + aWeights.y * uBones[aBones.y]
              + aWeights.z * uBones[aBones.z]
              + aWeights.w * uBones[aBones.w];
    vec4 sp = skin * vec4(aPos, 1.0);
    gl_Position = uViewProj * uModel * sp;
    // DS1 rigs are rigid (rotation + translation, no non-uniform scale), so mat3(skin) is
    // the correct normal transform. Don't rewrite this as a transpose-inverse: it would be
    // slower, identical for these inputs, and would mask corruption if a future skin ever did
    // carry scale.
    vec3 sn = mat3(skin) * aNormal;
    vNormal  = mat3(uModel) * sn;
    vUv = aUv;
}";

    // Cheap N·L lambert plus a constant ambient. Samples uAlbedo when uHasTexture != 0;
    // otherwise falls back to a neutral sand colour so untextured meshes still read as solids.
    // DS1 textures were authored for D3D (V=0 at top), so we flip V on sample for GL.
    // Vertex colors are parsed (DS1 bakes radiosity into them) but not wired here yet —
    // trying texture*vertexColor made the visible picture darker overall, suggesting the
    // color range isn't a straight [0..1] sRGB tint. Needs a data-look before hooking up.
    // Alpha test (discard < 0.5) is essential for foliage. DS1 trees, shrubs,
    // and grass clumps are rendered as textured quad clusters with the leaf
    // shape masked into the alpha channel — without the discard, every leaf
    // card draws as a solid opaque rectangle and trees look like the blocky
    // sprite-billboards of pre-2000 games. Threshold 0.5 matches the original
    // engine's hard-edged cutout (no premultiplied alpha blending in DS1).
    private const string MeshFragmentSource = @"#version 330 core
in  vec3 vNormal;
in  vec2 vUv;
out vec4 FragColor;
uniform sampler2D uAlbedo;
uniform int       uHasTexture;
// Phase 21d-2a-v debug — when set, replaces the no-texture fallback color
// with bright magenta so we can spot subsets that fall through to the
// untextured path. Off by default (preserves the sand fallback).
uniform int       uDebugFallback;
// Phase 21d-2a-v debug — when uSubsetTintActive=1, output uSubsetTint as the
// final fragment color (skipping texture sampling and lighting). Used to map
// each ASP subset to the body region it covers; the host sets uSubsetTint to
// a palette color per draw before issuing DrawSubset.
uniform int       uSubsetTintActive;
uniform vec4      uSubsetTint;
// Phase 21c-1 — V-flip is now a per-pass switch. NPCs/weapons (skinned pipeline)
// were authored with UVs that need the D3D→GL flip we used to do unconditionally.
// Static props (barrels, jugs, fences, foliage) author UVs in the same space the
// .raw is stored in — flipping makes their bands and lid views land on the wrong
// faces. Default = flip (preserves the working NPC look); the static-prop pass
// sets uFlipV=0 before drawing.
uniform int       uFlipV;
// Phase 17-SC-I — per-draw UV scroll for animated terrain textures
// (waterfalls vshift 0.5/sec in DS1's TSD sidecars). Defaults to (0,0)
// for everything else, which the host resets between water and non-water
// passes so the offset doesn't leak into actors/weapons/static props.
uniform vec2      uUvOffset;
// Phase 21c-3 — region directional lighting. Replaces the prior single-white-sun
// constant. uDirCount may be 0 (no lights file → fall back to ambient-only),
// 1 (single key sun), or up to 4. uDirColor is pre-multiplied by intensity so
// the sum is just an unweighted addition.
uniform int       uDirCount;
uniform vec3      uDirDir[4];
uniform vec3      uDirColor[4];
uniform float     uAmbient;
void main()
{
    vec2 uv = (uFlipV != 0) ? vec2(vUv.x, 1.0 - vUv.y) : vUv;
    uv += uUvOffset;
    vec4 fallback = (uDebugFallback != 0)
        ? vec4(1.0, 0.0, 1.0, 1.0)
        : vec4(0.85, 0.78, 0.62, 1.0);
    vec4 sampled = (uHasTexture != 0)
        ? texture(uAlbedo, uv)
        : fallback;
    if (uHasTexture != 0 && sampled.a < 0.5) discard;

    vec3 N = normalize(vNormal);
    vec3 lighting = vec3(uAmbient);
    for (int i = 0; i < uDirCount; ++i) {
        float ndl = max(dot(N, uDirDir[i]), 0.0);
        lighting += uDirColor[i] * ndl;
    }
    // Phase 21c-4 — clamp combined irradiance to 1.0. fh_r1 ships two
    // directionals (warm-white key + cool-blue fill) plus 0.20 ambient;
    // unclamped, the lit side of an actor reaches ~1.7 and washes out the
    // texture. Cap at 1.0 so the most-lit fragments equal the texture's
    // sampled color (no over-bright washout) while shadowed fragments still
    // honor ambient.
    lighting = min(lighting, vec3(1.0));
    if (uSubsetTintActive != 0) {
        FragColor = uSubsetTint;
    } else {
        FragColor = vec4(sampled.rgb * lighting, 1.0);
    }
}";

    public RenderHost(string title = "SiegeFX", int width = 1280, int height = 720,
        string? meshPath = null, string? texturePath = null,
        string? regionMapTankPath = null, string? regionTerrainTankPath = null, string? regionPath = null,
        string? worldMapTankPath = null, string? worldTerrainTankPath = null, string? worldRootHint = null,
        string? animAspPath = null, string? animPrsPath = null, string? animTexturePath = null,
        string? skritPath = null, IReadOnlyList<string>? skritClipPaths = null,
        string? playLogicTankPath = null, string? playObjectsTankPath = null,
        bool diagMode = false,
        // Phase 24-MAINMENU step 1+2 — when bootMode is set, OnLoad opens the
        // standard DS1 tanks under ds1ResourcesDir and the splash → main menu
        // sequence drives the scene instead of any per-CLI-flag region/anim
        // entry. noVideo skips the splash sequence and jumps straight to the
        // main menu state (DS1's `nointro=true`).
        bool bootMode = false,
        string? ds1ResourcesDir = null,
        bool noVideo = false)
    {
        _meshPath = meshPath;
        _texturePath = texturePath;
        _regionMapTankPath = regionMapTankPath;
        _regionTerrainTankPath = regionTerrainTankPath;
        _regionPath = regionPath;
        _worldMapTankPath = worldMapTankPath;
        _worldTerrainTankPath = worldTerrainTankPath;
        _worldRootHint = worldRootHint;
        _animAspPath = animAspPath;
        _animPrsPath = animPrsPath;
        _animTexturePath = animTexturePath;
        _skritPath = skritPath;
        _skritClipPaths = skritClipPaths;
        _playLogicTankPath = playLogicTankPath;
        _playObjectsTankPath = playObjectsTankPath;
        _diagMode = diagMode;
        _bootMode = bootMode;
        _ds1ResourcesDir = ds1ResourcesDir;
        _noVideo = noVideo;
        var opts = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            VSync = true,
        };
        _window = Window.Create(opts);
        _window.Load    += OnLoad;
        _window.Update  += OnUpdate;
        _window.Render  += OnRender;
        _window.Resize  += OnResize;
        // Phase 16b — release GL resources while the context is still alive.
        // Silk.NET's Run() returns after the GLFW window is destroyed, so the
        // outer Dispose() runs with no current context and DeleteTextures
        // throws "NoContext". Closing fires while the window is still valid.
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        if (_diagMode) _diagBootStopwatch.Start();
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();

        foreach (var kb in _input.Keyboards)
        {
            // Phase 21d-2a-viii-b — typed characters feed the creator's name
            // edit box. Silk's KeyChar fires once per logical char with shift
            // already applied, which is what we want — KeyDown gives raw
            // keycodes that don't disambiguate "a" from "A".
            kb.KeyChar += (_, c) =>
            {
                if (_creator.IsOpen) _creator.OnChar(c);
            };
            kb.KeyDown += (_, key, _) =>
            {
                // Backspace is not a printable char, so KeyChar gets the
                // platform-specific ASCII 0x08 on Windows but not on all
                // backends — route it through KeyDown explicitly so creator
                // name editing always works.
                if (_creator.IsOpen && key == Key.Backspace)
                {
                    _creator.OnChar('\b');
                    return;
                }
                // Phase 21d-2a-viii-b — while the creator is up, world hotkeys
                // (F, F1-F4, C, I, L, H, Q, W, F5/F9) are meaningless and
                // shouldn't fire on a stray keystroke during name typing.
                // Esc isn't trapped — gives the user a back-out path even
                // though there's no formal "creator pause" state.
                if (_creator.IsOpen) return;
                // Phase 23-SC-OPTIONS-FOLD — same suppression while
                // the Options dialog is up: F5 quicksave / F9 quickload
                // / F11 / B / I / L / H / Q / W / [ / ] / \ all fire
                // off this lambda and would mutate world state during
                // an in-progress options edit. Esc still flows through
                // (handled above by the early Options branch) so the
                // user's back-out path works.
                if (_optionsMenu.IsOpen && key != Key.F10) return;
                // Phase 15d: Esc opens/closes the pause menu instead of slamming the
                // window shut. Quit-from-menu still routes through _window.Close, so
                // there's a one-button-press exit (Esc, click Quit).
                if (key == Key.Escape)
                {
                    // Phase 21-SC-SCROLL-B-2 — Esc cancels a spell-scroll drag
                    // and restores the spell to its source slot. Cheap to do
                    // first because canceling a half-finished drag should be
                    // free regardless of what other UI is open.
                    if (_cursorScroll is not null)
                    {
                        CancelScrollDrag();
                        return;
                    }
                    // Phase 24-MAINMENU step 1+2-FOLD — Esc during the splash
                    // sequence skips straight to the main menu state (DS1's
                    // canonical "skip the intro" behavior). Without this gate,
                    // Esc fell through to _pauseMenu.Toggle() which opened a
                    // Save/Load/Resume menu over a half-faded splash with no
                    // _player to act on.
                    if (_bootMode && _frontendScene is not null
                        && (_frontendScene.State == Hud.FrontendScene.ScreenState.IntroMicrosoft
                         || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroGaspowered
                         || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroBink
                         || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroLogoDrop
                         || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroLogoHold
                         || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroLogoExit
                         || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroMenuFlyIn))
                    {
                        _frontendScene.SetState(Hud.FrontendScene.ScreenState.MainMenu);
                        return;
                    }
                    // Phase 24-MAINMENU step 6 — About overlay closes on
                    // Esc before any other handler. Gameplay-stack pause
                    // menu has no business in boot mode anyway since
                    // there's no game state to pause.
                    if (_aboutOpen) { _aboutOpen = false; return; }
                    // Phase 24-MAINMENU step 5+6 — Esc on main menu = quit
                    // (DS1's behavior). No pause menu to fall through to.
                    if (_bootMode && _frontendScene is not null
                        && _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenu
                        && !_optionsMenu.IsOpen)
                    {
                        _window.Close();
                        return;
                    }
                    // Phase 23-SC-OPTIONS-A — Esc inside the Options dialog
                    // closes it as Cancel (matches DS1's onescape →
                    // notify(cancel_options) on the Cancel button) before
                    // the pause-menu / dialogue / vendor handlers see it.
                    if (_optionsMenu.IsOpen) _optionsMenu.OnEscape();
                    // Phase 20a/d: dialogue + vendor swallow Esc first so closing
                    // a chat or trade doesn't double up into the pause menu.
                    else if (_vendor.IsOpen) _vendor.Close();
                    else if (_dialogue.IsOpen) _dialogue.Close();
                    // Phase 24-MAINMENU step 5+6-FOLD — pause menu is a
                    // gameplay-stack overlay with Save / Load / Resume; in
                    // boot mode there's no loaded world to act on, so Esc
                    // falling through to a Save-against-null-state would
                    // either NRE or write a corrupt save. Quit the window
                    // instead, matching DS1's "Esc on the menu = exit."
                    else if (_bootMode) _window.Close();
                    else _pauseMenu.Toggle();
                }
                // Phase 21-SC-INV-A: 'C' toggles the DS1 character pane. The
                // chase↔fly camera flip moved to F8 so the muscle-memory C key
                // matches the original game.
                else if (key == Key.C) { _charPanelOpen = !_charPanelOpen; _audio?.Play(SfxGuiInventory); }
                // Phase 21-SC-SPELL-A: 'B' toggles the spell book pane.
                else if (key == Key.B) { _spellBookOpen = !_spellBookOpen; _audio?.Play(SfxGuiInventory); }
                // Phase 13b → relocated: F8 flips between chase cam (follows
                // the PC) and fly cam (free WASD+RMB). No-op if there's no
                // player. C used to do this; freed for the character pane.
                else if (key == Key.F8 && _player is not null)
                {
                    _cameraMode = _cameraMode == CameraMode.Chase ? CameraMode.Fly : CameraMode.Chase;
                    Console.WriteLine($"camera: {_cameraMode}");
                }
                // Phase 15c: 'I' toggles the grid inventory panel.
                else if (key == Key.I) { _inventoryOpen = !_inventoryOpen; _audio?.Play(SfxGuiInventory); }
                // Phase 20b: 'L' toggles the quest log overlay.
                else if (key == Key.L) _questLogOpen = !_questLogOpen;
                // Phase 16b: 'H' takes 5 HP and 5 MP off the player (debug only —
                // until enemy aggro lands in a later phase, this is the only way
                // to drain the bars to verify regen is ticking).
                else if (key == Key.H && _player is not null && !_player.IsDead)
                {
                    _player.Actor.Combat.ApplyDamage(5f);
                    // Mana drain has no helper yet; nudge MP via heal-negative isn't
                    // supported (Heal clamps), so we'll just call the formula path
                    // when spells land. For now drain via a direct method.
                    _player.Actor.Combat.SpendMana(5f);
                }
                // Phase 17a — Q casts the slotted primary spell at whatever the
                // cursor is currently pointing at. Picks the same way RMB does:
                // unproject, find the closest combatant in XZ to the ground hit.
                // The spellbook owns the gating (range, mana, cooldown) — the
                // render layer just supplies the candidate target + distance.
                else if (key == Key.Q && _player is not null && !_player.IsDead)
                {
                    TryClickToCast(SiegeFX.Core.Actors.SpellSlot.Primary);
                }
                // Phase 17c — W casts the secondary slot. Defaults to a self
                // heal so a single key keeps the loop simple; when a real
                // hotbar lands the user picks per-slot bindings.
                else if (key == Key.W && _player is not null && !_player.IsDead)
                {
                    TryClickToCast(SiegeFX.Core.Actors.SpellSlot.Secondary);
                }
                // Phase 17-SC-H-DBG — [ and ] cycle the Primary spell slot
                // through SpellCatalog.All so SC-H's sfx_script wiring can be
                // verified per element (zap, fireball, freeze, charm, …)
                // without an inventory/learn UI. Off-element heals stay on W;
                // self-heal spells are skipped by the cycle since Primary's
                // click-target flow expects a live target. The full spell
                // book panel is Phase 21-SC-SPELL.
                else if ((key == Key.LeftBracket || key == Key.RightBracket) &&
                         _playerSpellbook is not null && _spellCatalog is not null)
                {
                    CyclePrimarySpell(forward: key == Key.RightBracket);
                }
                // Phase 21-SC-SPELL-VFX-debug — \ cycles which DS1 streak
                // texture the lightning-bolt renderer samples (lightray_01,
                // _02, _04, streaks, legacy lightray01, sparkle01). Lets
                // the user A/B candidates live to pick the authentic look.
                else if (key == Key.BackSlash && _particles is not null)
                {
                    var name = _particles.CycleBoltTexture();
                    Console.WriteLine($"  bolt-tex: {name} (slot {_particles.BoltTexSlot})");
                }
                // Phase 19c — F5 quicksaves to a single slot under the user
                // profile; F9 reloads the same slot. No confirmation prompt
                // and no multi-slot UI yet — that's a save-screen job that
                // lands when the rest of the menu system does. F5 is silent
                // on a viewer-mode boot (no player, no region) since
                // CaptureSave still produces a file but ApplySave's region
                // check would refuse it on the next F9 anyway.
                else if (key == Key.F5)
                {
                    var path = SiegeFX.Core.Save.SaveStore.QuicksavePath();
                    try
                    {
                        var save = CaptureSave();
                        SiegeFX.Core.Save.SaveStore.Save(path, save);
                        Console.WriteLine($"  save: wrote {save.Actors.Count} actor(s) + " +
                                          $"{save.LootPiles.Count} pile(s) -> {path}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  save: failed -- {ex.Message}");
                    }
                }
                // Phase 17-SC-E — debug particle receipt. F11 spawns a
                // burst of fire + smoke + sparks at the player's feet so
                // the standalone backend has a visible test before the
                // sfx_script interpreter (SC-F) and emitter wiring (SC-G)
                // call into it from data. F10 fires a lightning bolt
                // straight up to verify SpawnLightning + bolt quad path.
                else if (key == Key.F11 && _particles is not null && _player is not null)
                {
                    var p = _player.CurrentTransform.Translation + new Vector3(0f, 0.5f, 0f);
                    _particles.SpawnFire (p, new Vector4(1.00f, 0.55f, 0.20f, 1f), 0.8f, 1.4f, 28);
                    _particles.SpawnSmoke(p + new Vector3(0f, 0.6f, 0f), new Vector4(0.4f, 0.4f, 0.42f, 0.6f), 0.9f, 3.0f, 16);
                    _particles.SpawnSpark(p, new Vector4(1.00f, 0.85f, 0.30f, 1f), 1.0f, 0.8f, 24);
                }
                else if (key == Key.F10 && _player is not null)
                {
                    // Phase 23-SC-OPTIONS-A — F10 opens the Options Menu.
                    // Matches DS1's `[game_options] input = key_f10` in
                    // /config/input_bindings.gas. Toggle: a second F10
                    // closes the menu (treated as Cancel).
                    if (_optionsMenu.IsOpen) _optionsMenu.OnEscape();
                    else _optionsMenu.Open();
                }
                else if (key == Key.F9)
                {
                    // Phase 21-SC-SCROLL-C-2 — drop a half-finished scroll
                    // drag before reloading; the post-load spellbook has
                    // different slot contents so restoring to a stale
                    // source index would put the spell in the wrong place.
                    // Pure clear (no restore) — the spell wasn't in any
                    // slot when the save was captured anyway since drag
                    // state isn't persisted.
                    if (_cursorScroll is not null) ClearScrollDrag();
                    var path = SiegeFX.Core.Save.SaveStore.QuicksavePath();
                    try
                    {
                        if (!File.Exists(path))
                        {
                            Console.WriteLine($"  load: no quicksave at {path}");
                        }
                        else
                        {
                            var save = SiegeFX.Core.Save.SaveStore.Load(path);
                            ApplySave(save);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  load: failed -- {ex.Message}");
                    }
                }
            };
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += (m, btn) =>
            {
                // Phase 21d-2a-viii-b — character creator owns the screen until
                // Begin/Cancel; LMB lands on its buttons and never falls through
                // to click-to-move. RMB swallowed too so a stray right-click
                // doesn't engage mouse-look behind the modal panel.
                if (_creator.IsOpen && (btn == MouseButton.Left || btn == MouseButton.Right))
                {
                    if (btn == MouseButton.Left)
                    {
                        // 21d-2a-viii-FE-2 — preview-rect drag check first.
                        // If the click landed inside the listener rect, latch
                        // the preview's drag state and skip the panel button
                        // dispatch (otherwise the click also fires axis cycle
                        // on press, which we don't want).
                        if (_heroPreview is not null
                            && _heroPreview.TryStartDrag((int)m.Position.X, (int)m.Position.Y,
                                _window.Size.X, _window.Size.Y))
                        {
                            // drag started — eat the click
                        }
                        else
                        {
                            var sz = _window.FramebufferSize;
                            _creator.OnMouseDown((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                        }
                    }
                    return;
                }
                // Phase 23-SC-OPTIONS-A — Options dialog is modal and
                // sits above pause menu / dialogue / vendor (in case
                // they're somehow stacked). Its press handler latches
                // OK / Cancel / Defaults so the click registers only on
                // a press-and-release-on-the-same-button stroke.
                if (_optionsMenu.IsOpen && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    _optionsMenu.OnMouseDown((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                // Phase 24-MAINMENU step 5+6 — main menu eats LMB while
                // active. About sub-screen click-outside-to-close also
                // swallows LMB regardless of where it lands.
                if (_aboutOpen && btn == MouseButton.Left)
                {
                    _aboutOpen = false;
                    return;
                }
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenu
                    && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    _mainMenu.OnMouseDown((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayer
                    && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    _spMenu.OnMouseDown((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect
                    && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    // Phase 29-CD-CREATOR-FIX3 — route to BOTH panels.
                    // _creator owns the 12 spinner-arrow rects on the
                    // left panel; _csMenu owns the bottom-nav
                    // Previous/Next rects. They don't overlap (top vs
                    // bottom of screen) so both can fire OnMouseDown
                    // safely; whichever rect contains the click sets
                    // its own _pressed and the matching OnMouseUp
                    // commits the action.
                    _creator.OnMouseDown((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    _csMenu.OnMouseDown((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                // Phase 23-SC-OPTIONS-D — RMB on a cycle widget steps
                // backward (DS1 lets the cycle buttons go either way
                // via onrbuttondown). Also swallow RMB camera-look
                // while the menu is open.
                if (_optionsMenu.IsOpen && btn == MouseButton.Right)
                {
                    var sz = _window.FramebufferSize;
                    _optionsMenu.OnRightClickWidget((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                // Phase 15d — pause menu eats LMB while it's open so a click on
                // a button doesn't also retarget the follower behind the panel.
                if (_pauseMenu.IsOpen && btn == MouseButton.Left)
                {
                    _pauseMenu.OnMouseDown((int)m.Position.X, (int)m.Position.Y);
                    return;
                }
                // Phase 20d — vendor trade overlay; same modal rules as dialogue.
                // Latches a Buy/Sell press on LMB-down; RMB is swallowed so a
                // stray right-click behind the panel can't retarget the camera
                // mid-trade.
                if (_vendor.IsOpen && (btn == MouseButton.Left || btn == MouseButton.Right))
                {
                    if (btn == MouseButton.Left)
                        _vendor.OnMouseDown((int)m.Position.X, (int)m.Position.Y,
                                            _playerInventory.Count, _window.Size.X, _window.Size.Y);
                    return;
                }
                // Phase 20a — dialogue panel ranks above everything except pause.
                // While open, LMB lands on its buttons and never falls through to
                // click-to-move; RMB is also swallowed so a "talk again" RMB-tap
                // doesn't attack the friendly NPC behind the panel.
                if (_dialogue.IsOpen && (btn == MouseButton.Left || btn == MouseButton.Right))
                {
                    if (btn == MouseButton.Left)
                        _dialogue.OnMouseDown((int)m.Position.X, (int)m.Position.Y);
                    return;
                }
                // Phase 21-SC-INV-A2 — mini-HUD ability bar + open-arrows.
                // Ability cells select the active combat mode (visual ring
                // for now; LMB routing lands with -B). The open-arrows strip
                // toggles all three large panels in unison so a mouse-only
                // player has a one-click "show me everything" affordance.
                if (btn == MouseButton.Left && _player is not null)
                {
                    int mx = (int)m.Position.X, my = (int)m.Position.Y;
                    for (int i = 0; i < _abilitySlotRects.Length; i++)
                    {
                        var r = _abilitySlotRects[i];
                        if (mx >= r.X && my >= r.Y && mx < r.X + r.W && my < r.Y + r.H)
                        {
                            _activeAbilityIdx = i;
                            _audio?.Play(SfxGuiInventory);
                            return;
                        }
                    }
                    var ar = _openArrowsRect;
                    if (ar.W > 0 && mx >= ar.X && my >= ar.Y && mx < ar.X + ar.W && my < ar.Y + ar.H)
                    {
                        bool anyOpen = _charPanelOpen || _inventoryOpen || _spellBookOpen;
                        _charPanelOpen = !anyOpen;
                        _inventoryOpen = !anyOpen;
                        _spellBookOpen = !anyOpen;
                        _audio?.Play(SfxGuiInventory);
                        return;
                    }
                }
                // Phase 21-SC-INV-A — character + spell book panes are read-only
                // in this slice (their drag/drop wiring lands with -B / -SPELL-B);
                // any LMB that lands on their rect is swallowed so the world
                // below doesn't see a click-to-move on the panel chrome. Close
                // X on inventory / spell book panels resolves here too.
                if (btn == MouseButton.Left)
                {
                    int mx = (int)m.Position.X, my = (int)m.Position.Y;
                    if (_spellBookOpen && _spellBookPanel.IsPointInClose(mx, my))
                    {
                        _spellBookOpen = false;
                        _audio?.Play(SfxGuiInventory);
                        return;
                    }
                    if (_inventoryOpen && _inventoryPanel.IsPointInClose(mx, my))
                    {
                        _inventoryOpen = false;
                        _audio?.Play(SfxGuiInventory);
                        return;
                    }
                    if (_charPanelOpen && _characterPanel.IsPointInPanel(mx, my))
                        return;
                    // Phase 21-SC-SCROLL-B-2 — LMB on a spellbook spell row
                    // starts a scroll drag (when no drag is already in
                    // flight). Active1/Active2/placed slots all qualify;
                    // clicking an empty placed row is a no-op. The drop
                    // side (LMB on another slot while dragging = swap)
                    // lands in SC-SCROLL-C-2 alongside the placed-list
                    // backing storage. For now, ANY click while dragging
                    // ends the drag at the cursor position (cancel
                    // semantics) so the cursor doesn't get stuck holding
                    // a scroll.
                    if (_spellBookOpen && _spellBookPanel.IsPointInPanel(mx, my))
                    {
                        var hit = _spellBookPanel.HitTestSlot(mx, my);
                        if (hit.Kind != Hud.SpellBookPanel.SlotKind.None && _playerSpellbook is not null)
                        {
                            if (_cursorScroll is null)
                            {
                                // Phase 21-SC-SCROLL-B-2 / C-1 — LMB on a
                                // populated row picks up its scroll onto the
                                // cursor. Source slot is cleared on pickup;
                                // RMB / ESC restores via CancelScrollDrag.
                                SiegeFX.Core.Assets.SpellTemplate? src = hit.Kind switch
                                {
                                    Hud.SpellBookPanel.SlotKind.Active1 => _playerSpellbook.Primary,
                                    Hud.SpellBookPanel.SlotKind.Active2 => _playerSpellbook.Secondary,
                                    Hud.SpellBookPanel.SlotKind.Placed  => _playerSpellbook.Placed[hit.Index],
                                    _ => null,
                                };
                                if (src is not null)
                                {
                                    var srcKind = hit.Kind switch
                                    {
                                        Hud.SpellBookPanel.SlotKind.Active1 => CursorScrollSource.SpellbookActive1,
                                        Hud.SpellBookPanel.SlotKind.Active2 => CursorScrollSource.SpellbookActive2,
                                        _ => CursorScrollSource.SpellbookPlaced,
                                    };
                                    BeginScrollDrag(src, srcKind, hit.Index);
                                    ClearSpellbookSlot(hit.Kind, hit.Index);
                                    _audio?.Play(SfxGuiPickup);
                                    Console.WriteLine($"  scroll drag: pickup {src.Name} from {srcKind}[{hit.Index}]");
                                    return;
                                }
                                // Empty slot — fall through to swallow.
                            }
                            else
                            {
                                // Phase 21-SC-SCROLL-C-2 — LMB on any slot
                                // while dragging drops the cursor scroll
                                // there. If destination is occupied, the
                                // existing spell goes to the source slot
                                // (a swap). Empty destination is a plain
                                // move. Either way the drag ends.
                                //
                                // SELF-DROP GUARD (review fold): clicking
                                // the source slot you just picked up from
                                // would clear-then-write-then-clear-again
                                // and lose the spell. Treat as cancel —
                                // restore in place.
                                var dropping = _cursorScroll;
                                var dropSlotKind = hit.Kind switch
                                {
                                    Hud.SpellBookPanel.SlotKind.Active1 => CursorScrollSource.SpellbookActive1,
                                    Hud.SpellBookPanel.SlotKind.Active2 => CursorScrollSource.SpellbookActive2,
                                    _ => CursorScrollSource.SpellbookPlaced,
                                };
                                if (dropSlotKind == _cursorScrollSource
                                    && hit.Index == _cursorScrollSourceIndex)
                                {
                                    RestoreToSource(dropping);
                                    _audio?.Play(SfxGuiPutDownScroll);
                                    Console.WriteLine($"  scroll drag: self-drop -> restored {dropping.Name} in place");
                                    ClearScrollDrag();
                                    return;
                                }
                                var displaced = ReadSpellbookSlot(hit.Kind, hit.Index);
                                WriteSpellbookSlot(hit.Kind, hit.Index, dropping);
                                if (displaced is not null)
                                    RestoreToSource(displaced);
                                else
                                    ClearSpellbookSlot(_cursorScrollSource, _cursorScrollSourceIndex);
                                _audio?.Play(SfxGuiPutDownScroll);
                                Console.WriteLine($"  scroll drag: drop {dropping.Name} into {hit.Kind}[{hit.Index}]" +
                                                  (displaced is not null ? $" (swap with {displaced.Name})" : ""));
                                ClearScrollDrag();
                                return;
                            }
                        }
                        // No-slot hit — swallow click so the world below
                        // doesn't see a click-to-move on the panel chrome.
                        return;
                    }
                }
                // Phase 9-SC-9 — inventory panel owns LMB while open. Latch a
                // drag if the click lands on an item rect; clicks on empty cells
                // or the panel chrome are still swallowed so the world below
                // doesn't see a click-to-move on an inventory backdrop hit.
                if (_inventoryOpen && btn == MouseButton.Left)
                {
                    int imx = (int)m.Position.X, imy = (int)m.Position.Y;
                    // Phase 21-SC-SCROLL-D — LMB-while-dragging-a-scroll on
                    // the inventory panel drops the cursor scroll into the
                    // inventory as a scroll item. Reference is the spell
                    // template name; TryGetItemIcon already routes spell_*
                    // refs through the [gui][inventory_icon] attribute (A-1
                    // pre-cached every catalog spell), so the new entry
                    // renders with its DS1 b_gui_ig_i_ic_sp_*_inv art.
                    // The source slot was cleared on pickup so the move
                    // is just an Add here; ClearScrollDrag ends the drag.
                    if (_cursorScroll is not null
                        && _inventoryPanel.IsPointInPanel(imx, imy, _window.Size.X, _window.Size.Y))
                    {
                        var spell = _cursorScroll;
                        _playerInventory.Add(new SiegeFX.Core.Actors.LootEntry(
                            Slot: "", Reference: spell.Name));
                        // Phase 21-SC-SCROLL-D fold — keep the panel's _placements
                        // in sync with the inventory list. Without this, the new
                        // scroll renders at the next free cell via
                        // EnsurePlacements' defensive padding rather than at the
                        // panel's authored layout, and a user-arranged grid loses
                        // placement determinism for the new entry.
                        _inventoryPanel.NotifyItemAdded();
                        _audio?.Play(SfxGuiPutDownScroll);
                        Console.WriteLine($"  scroll drag: drop {spell.Name} into inventory grid");
                        ClearScrollDrag();
                        return;
                    }
                    // Phase 21-SC-SCROLL-E-2 — LMB-without-cursor on a SCROLL
                    // item in the inventory grid picks it up onto the cursor.
                    // We hit-test before the inventory's own OnMouseDown
                    // latch so the intra-grid drag doesn't shadow the
                    // scroll-pickup intent. Non-scroll items still go
                    // through the existing intra-grid drag path below.
                    if (_cursorScroll is null && _spellCatalog is not null
                        && _inventoryPanel.IsPointInPanel(imx, imy, _window.Size.X, _window.Size.Y))
                    {
                        int idx = _inventoryPanel.TryHitTestItem(imx, imy,
                            _window.Size.X, _window.Size.Y, _playerInventory, TryGetItemGridSize);
                        if (idx >= 0)
                        {
                            var clicked = _playerInventory[idx];
                            // A scroll item is identifiable by its Reference
                            // resolving via ResolveSlottableSpell — same
                            // path as SIEGEFX_DEBUG_SPELLS, so it works for
                            // catalog + synthesized (summon/charm) templates.
                            var spell = ResolveSlottableSpell(clicked.Reference, debugSpellsEnv: null);
                            if (spell is not null && string.IsNullOrEmpty(clicked.Slot))
                            {
                                BeginScrollDrag(spell, CursorScrollSource.Inventory, idx);
                                _playerInventory.RemoveAt(idx);
                                _inventoryPanel.NotifyItemRemoved(idx);
                                _audio?.Play(SfxGuiPickup);
                                Console.WriteLine($"  scroll drag: pickup {spell.Name} from inventory[{idx}]");
                                return;
                            }
                            // Not a scroll — fall through to the intra-grid
                            // drag path below. Existing behavior preserved.
                        }
                    }
                    _inventoryPanel.OnMouseDown(imx, imy,
                        _window.Size.X, _window.Size.Y, _playerInventory, TryGetItemGridSize);
                    if (_inventoryPanel.IsPointInPanel(imx, imy,
                            _window.Size.X, _window.Size.Y))
                        return;
                    // LMB outside the panel with inventory still open: skip the
                    // panel and fall through so a click-to-move still works while
                    // the grid is up. (No drop-on-LMB-down — drop happens on
                    // LMB-up so the user has to actively pull an item out.)
                }
                if (btn == MouseButton.Right)
                {
                    // Phase 21-SC-SCROLL-B-2 — RMB cancels an in-flight
                    // scroll drag (DS1 convention), restoring the spell to
                    // its source. Done before the camera-look latch so an
                    // RMB-while-dragging doesn't simultaneously enter
                    // mouselook AND drop the cursor scroll.
                    if (_cursorScroll is not null)
                    {
                        CancelScrollDrag();
                        return;
                    }
                    _mouseLookActive = true;
                    _lastMousePos = null;
                    _rmbDownPos = m.Position;
                    _rmbDrift = 0f;
                    m.Cursor.CursorMode = CursorMode.Raw;
                }
                // Phase 21-SC-SCROLL-F-1 — LMB outside any UI WITH a scroll
                // on cursor = world drop. Spawn a loot pile at the player's
                // feet that arcs to a target ~1.5u out, with the Phase
                // 9-SC-9 throw-tumble + the new XSpins (-F flavor: end-
                // over-end). Pickup of the resulting pile auto-routes the
                // scroll back to the spellbook via F-2 in TryAutoPickup.
                else if (btn == MouseButton.Left && _cursorScroll is not null)
                {
                    DropScrollToWorld(_cursorScroll);
                    return;
                }
                // Phase 13c — LMB click-to-move. Unproject the cursor onto the
                // player's current Y plane, confirm the point lands on a nav
                // triangle, retarget the follower. Silent no-op on off-mesh clicks
                // (clicks past the nav edge) so the user can rotate-drag + click
                // without stray warnings.
                else if (btn == MouseButton.Left)
                    TryClickToMove(m.Position);
            };
            mouse.MouseUp += (m, btn) =>
            {
                // Phase 21d-2a-viii-b — character creator. Begin/Cancel resolve
                // here; the post-modal world spawn is driven by FlushCreator()
                // on the next render frame so we don't reenter spawn from inside
                // an input callback.
                if (_creator.IsOpen && (btn == MouseButton.Left || btn == MouseButton.Right))
                {
                    if (btn == MouseButton.Left)
                    {
                        // 21d-2a-viii-FE-2 — drag release is its own path so
                        // a "drag the hero, release outside the rect" stroke
                        // doesn't accidentally trip an axis arrow.
                        if (_heroPreview is not null && _heroPreview.IsDragging)
                            _heroPreview.EndDrag();
                        else
                        {
                            var sz = _window.FramebufferSize;
                            _creator.OnMouseUp((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                        }
                    }
                    return;
                }
                // Phase 23-SC-OPTIONS-A — same modal-priority order as
                // OnMouseDown. Click-up on a tab swaps the active tab;
                // click-up on OK / Cancel / Defaults fires the matching
                // edge flag (consumed in OnUpdate's FlushOptionsMenu).
                if (_optionsMenu.IsOpen && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    _optionsMenu.OnMouseUp((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                // Phase 24-MAINMENU step 5+6 — main menu click-up commits
                // the action; OnUpdate's HandleMainMenuActions drains
                // _mainMenu.ConsumeAction() the same frame.
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenu
                    && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    _mainMenu.OnMouseUp((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayer
                    && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    _spMenu.OnMouseUp((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect
                    && btn == MouseButton.Left)
                {
                    var sz = _window.FramebufferSize;
                    _creator.OnMouseUp((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    _csMenu.OnMouseUp((int)m.Position.X, (int)m.Position.Y, sz.X, sz.Y);
                    return;
                }
                if (_pauseMenu.IsOpen && btn == MouseButton.Left)
                {
                    _pauseMenu.OnMouseUp((int)m.Position.X, (int)m.Position.Y);
                    if (_pauseMenu.QuitRequested) _window.Close();
                    return;
                }
                // Phase 20d — vendor LMB-up. Resolve the press to a Buy/Sell
                // action and apply gold + inventory mutations here so the
                // trade authority lives in one place; the panel is purely
                // display+intent.
                if (_vendor.IsOpen && (btn == MouseButton.Left || btn == MouseButton.Right))
                {
                    if (btn == MouseButton.Left)
                    {
                        var act = _vendor.OnMouseUp((int)m.Position.X, (int)m.Position.Y,
                                                    _playerInventory.Count, _window.Size.X, _window.Size.Y);
                        if (!act.IsNone) ApplyVendorAction(act);
                    }
                    return;
                }
                if (_dialogue.IsOpen && (btn == MouseButton.Left || btn == MouseButton.Right))
                {
                    if (btn == MouseButton.Left)
                    {
                        _dialogue.OnMouseUp((int)m.Position.X, (int)m.Position.Y);
                        var quest = _dialogue.ConsumePendingQuestActivation();
                        if (quest is not null)
                        {
                            // Phase 20b — fold the activation into the journal.
                            // AddActive is idempotent so re-pitches don't reset
                            // a completed entry; the bool tells us whether this
                            // is the first acceptance for the log line.
                            bool added = _progression?.Journal.AddActive(quest) ?? false;
                            Console.WriteLine(added
                                ? $"[dialogue] quest activated: {quest}"
                                : $"[dialogue] quest re-pitched (already in journal): {quest}");
                        }
                        // Phase 20d — if dialogue just closed and the talked actor
                        // is a vendor, surface the trade panel automatically. No-op
                        // if the panel is still open or the NPC isn't in the catalog.
                        TryOpenVendorAfterTalk();
                    }
                    return;
                }
                // Phase 9-SC-9 — resolve a drag release. If the user moved the
                // item to a different cell inside the panel, the panel updates
                // its own placement state silently; if the release landed
                // outside the panel, pop the item out and spawn a loot pile at
                // the player's feet (then fire the put_down SFX).
                if (_inventoryOpen && btn == MouseButton.Left)
                {
                    var drag = _inventoryPanel.OnMouseUp((int)m.Position.X, (int)m.Position.Y,
                        _window.Size.X, _window.Size.Y, _playerInventory, TryGetItemGridSize);
                    if (drag.Kind == InventoryPanel.ActionKind.DropToWorld)
                        DropInventoryItem(drag.ItemIndex);
                    if (_inventoryPanel.IsPointInPanel((int)m.Position.X, (int)m.Position.Y,
                            _window.Size.X, _window.Size.Y))
                        return;
                }
                if (btn == MouseButton.Right)
                {
                    _mouseLookActive = false;
                    _lastMousePos = null;
                    // Phase 21-SC-BARREL-A1 — keep the OS cursor hidden in
                    // play mode so the sprite cursor isn't doubled up on
                    // RMB release. Viewer modes (no _player) restore the
                    // OS pointer as before.
                    m.Cursor.CursorMode = _player is not null
                        ? CursorMode.Hidden
                        : CursorMode.Normal;
                    // Phase 13d — tap-click discrimination. In Raw cursor mode
                    // m.Position is unreliable on mouse-up (driver snaps it back),
                    // so we use the drift accumulated while RMB was held instead.
                    // A nearly-zero drift means the user tapped without dragging;
                    // anything past the threshold was an orbit gesture.
                    if (_rmbDrift <= RmbClickDriftPx)
                    {
                        // Phase 20a — talk before attack. If the click landed on
                        // a talkable NPC, open dialogue; otherwise fall through
                        // to the combat path. Hostile actors don't get talkable
                        // even if they happen to have a [conversation] block.
                        if (!TryClickToTalk(_rmbDownPos))
                        {
                            // Phase 21-SC-ABIL-RMB — RMB respects the selected
                            // ability cell. Cells 0/1 swing the equipped weapon
                            // (no dedicated ranged-projectile path yet — the
                            // swing chore plus weapon-class stance is what
                            // distinguishes the two visually). Cells 2/3 cast
                            // the Primary/Secondary spell slot at whatever the
                            // cursor is over, mirroring Q/W keyboard casts.
                            switch (_activeAbilityIdx)
                            {
                                case 2:
                                    TryClickToCast(SiegeFX.Core.Actors.SpellSlot.Primary);
                                    break;
                                case 3:
                                    TryClickToCast(SiegeFX.Core.Actors.SpellSlot.Secondary);
                                    break;
                                default:
                                    TryClickToAttack(_rmbDownPos);
                                    break;
                            }
                        }
                    }
                    _rmbDrift = 0f;
                }
            };
            mouse.MouseMove += (_, pos) =>
            {
                // Phase 21-SC-SCROLL-PRE — track the latest mouse position for
                // any UI that needs cursor-anchored rendering (the spell-scroll
                // drag follows the cursor across panels). _lastMousePos below
                // is RMB-camera-specific and gets nulled on RMB release; a
                // separate _currentMousePos stays valid for the whole session.
                _currentMousePos = new Vector2(pos.X, pos.Y);
                // Phase 21d-2a-viii-b — creator hover updates so ◄► buttons
                // highlight under the cursor.
                if (_creator.IsOpen)
                {
                    var csz = _window.FramebufferSize;
                    _creator.OnMouseMove((int)pos.X, (int)pos.Y, csz.X, csz.Y);
                    // 21d-2a-viii-FE-2 — feed drag delta to the live preview
                    // when the user is mid-drag (LMB held inside the listener
                    // rect). Bidirectional yaw so dragging either way spins
                    // the model the matching way.
                    if (_heroPreview is not null && _heroPreview.IsDragging)
                        _heroPreview.OnDragMove((int)pos.X);
                }
                // Phase 15d — feed the pause menu so its buttons can light up on
                // hover. Cheap rect tests; no-op when the menu is closed.
                if (_pauseMenu.IsOpen)
                    _pauseMenu.OnMouseMove((int)pos.X, (int)pos.Y);
                if (_dialogue.IsOpen)
                    _dialogue.OnMouseMove((int)pos.X, (int)pos.Y);
                if (_vendor.IsOpen)
                    _vendor.OnMouseMove((int)pos.X, (int)pos.Y,
                                        _playerInventory.Count, _window.Size.X, _window.Size.Y);
                if (_inventoryOpen)
                    _inventoryPanel.OnMouseMove((int)pos.X, (int)pos.Y);
                // Phase 23-SC-OPTIONS-A — hover state for tab + bottom
                // buttons so the options dialog highlights under the cursor.
                if (_optionsMenu.IsOpen)
                {
                    var sz = _window.FramebufferSize;
                    _optionsMenu.OnMouseMove((int)pos.X, (int)pos.Y, sz.X, sz.Y);
                }
                // Phase 24-MAINMENU step 5+6 — main menu hover updates so
                // buttons highlight under the cursor while it's active.
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenu)
                {
                    var sz = _window.FramebufferSize;
                    _mainMenu.OnMouseMove((int)pos.X, (int)pos.Y, sz.X, sz.Y);
                }
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayer)
                {
                    var sz = _window.FramebufferSize;
                    _spMenu.OnMouseMove((int)pos.X, (int)pos.Y, sz.X, sz.Y);
                }
                if (_bootMode && _frontendScene is not null
                    && _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect)
                {
                    var sz = _window.FramebufferSize;
                    _creator.OnMouseMove((int)pos.X, (int)pos.Y, sz.X, sz.Y);
                    _csMenu.OnMouseMove((int)pos.X, (int)pos.Y, sz.X, sz.Y);
                }
                if (!_mouseLookActive) return;
                if (_lastMousePos is { } last)
                {
                    float dx = pos.X - last.X;
                    float dy = pos.Y - last.Y;
                    // Phase 13d — accumulate L1 pixel drift so MouseUp can tell a
                    // tap from a drag. L1 (abs sum) instead of L2 so a dead-slow
                    // diagonal scrub still accumulates past the click threshold.
                    _rmbDrift += MathF.Abs(dx) + MathF.Abs(dy);
                    if (_cameraMode == CameraMode.Chase)
                    {
                        // Orbit only (no pitch change) — chase pitch is derived from the
                        // look-at target each frame so mouse-Y would fight that logic.
                        // Phase 23-SC-OPTIONS-FOLD2-FOLD — route through Camera.YawIncrement
                        // so chase mode and first-person mode can never drift on the
                        // sensitivity/invert formula (the duplicate-formula version was a
                        // splinter waiting to happen the first time someone tweaked it).
                        _chaseYaw += _camera.YawIncrement(dx);
                    }
                    else
                    {
                        _camera.LookDelta(dx, dy);
                    }
                }
                _lastMousePos = pos;
            };
            // Phase 21-SC-ZOOM — scroll-wheel zoom for the chase camera. DS1
            // manual: forward = zoom in (decrease distance), back = zoom out.
            // Suppressed when a UI panel that scrolls its own content is open
            // so wheel events feel right inside the inventory/spellbook/vendor.
            mouse.Scroll += (_, wheel) =>
            {
                if (_cameraMode != CameraMode.Chase) return;
                if (_inventoryOpen || _vendor.IsOpen || _dialogue.IsOpen ||
                    _pauseMenu.IsOpen || _creator.IsOpen || _optionsMenu.IsOpen) return;
                if (wheel.Y == 0f) return;
                _chaseDistance = Math.Clamp(
                    _chaseDistance - wheel.Y * ChaseZoomStep,
                    ChaseDistanceMin, ChaseDistanceMax);
            };
        }

        _gl.Enable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.CullFace);
        _gl.CullFace(GLEnum.Back);
        _gl.FrontFace(GLEnum.Ccw);
        _gl.ClearColor(0.08f, 0.09f, 0.11f, 1f);

        _gridShader = new Shader(_gl, GridVertexSource, GridFragmentSource);
        _meshShader = new Shader(_gl, MeshVertexSource, MeshFragmentSource);
        _skinShader = new Shader(_gl, SkinnedVertexSource, MeshFragmentSource);
        // Phase 21d-2a-v — one-shot debug-fallback wiring. The fragment shader
        // tints uHasTexture=0 fragments bright magenta when this is on, so we
        // can spot subsets that fail to bind a texture vs. ones that bind a
        // sand-toned albedo and merely look like skin.
        int debugFallback = Environment.GetEnvironmentVariable("SIEGEFX_DEBUG_FALLBACK") == "1" ? 1 : 0;
        _meshShader.Use(); _meshShader.SetInt("uDebugFallback", debugFallback);
        _skinShader.Use(); _skinShader.SetInt("uDebugFallback", debugFallback);
        // Phase 21d-2a-v — subset-tint diagnostic. When SIEGEFX_SUBSET_TINT=1, the
        // skin pipeline overrides each subset's fragment color with a unique solid
        // hue (palette below) so we can map subset index → body region by eye and
        // check that the BSMM (textureIndex, faceSpan) carve matches actual geometry.
        // Off resets uniforms; the actor loop only writes them when the env is on.
        _subsetTintActive = Environment.GetEnvironmentVariable("SIEGEFX_SUBSET_TINT") == "1";
        _meshShader.Use(); _meshShader.SetInt("uSubsetTintActive", 0);
        _skinShader.Use(); _skinShader.SetInt("uSubsetTintActive", 0);
        // Default lighting matches the prior single-white-sun constants so viewer
        // modes (boot.asp, anim viewer, single-mesh) and any pre-LoadStaticProps
        // pass keep working. LoadStaticProps overwrites these from the player
        // region's lights/lights.gas before drawing.
        SetDefaultLighting();
        _grid       = new GridMesh(_gl);
        _lootCube   = new DebugCubeMesh(_gl);
        _textRenderer = new TextRenderer(_gl);
        _barRenderer  = new BarRenderer(_gl);
        _iconRenderer = new IconRenderer(_gl);
        // Phase 17-SC-E — particle backend lives next to the other GL
        // renderers; sprite atlas loads later off Objects.dsres inside
        // LoadPlayActors when the play-mode tank is open.
        _particles = new ParticleSystem(_gl);

        if (_meshPath is not null)
        {
            var bytes = File.ReadAllBytes(_meshPath);
            var ext = Path.GetExtension(_meshPath).ToLowerInvariant();
            Vector3 center;
            float radius;

            if (ext == ".sno")
            {
                var sno = SnoModel.Load(bytes);
                _sno = new SnoMesh(_gl, sno);
                center = _sno.Center;
                radius = MathF.Max(_sno.Radius, 1f);
                Console.WriteLine($"loaded SNO v{sno.Version} ({sno.Corners.Length} corners, {sno.TotalTriangleCount} tris across {sno.Surfaces.Length} surfaces, bounds {_sno.Min} .. {_sno.Max})");
            }
            else
            {
                var asp = AspMesh.Load(bytes);
                _mesh = new StaticMesh(_gl, asp);
                center = _mesh.Center;
                radius = MathF.Max(_mesh.Radius, 0.5f);
                Console.WriteLine($"loaded mesh '{asp.MeshName}' ({asp.Positions.Length} v, {asp.TriangleCount} tris, bounds {_mesh.Min} .. {_mesh.Max})");
            }

            // Frame whatever we loaded: put the camera radius*3 back along +Z, looking
            // at the node's center, so something is visible on first paint.
            _camera.Position = center + new Vector3(0, 0, radius * 3f);
            _camera.Yaw = 0;
            _camera.Pitch = 0;
        }

        if (_texturePath is not null)
        {
            var ext = Path.GetExtension(_texturePath).ToLowerInvariant();
            if (ext is ".dsres" or ".dsmap")
            {
                if (_sno is not null) LoadSnoTexturesFromTank(_texturePath);
                else Console.WriteLine($"tank '{_texturePath}' ignored — texture-from-tank resolution only runs for SNO meshes");
            }
            else
            {
                var texBytes = File.ReadAllBytes(_texturePath);
                var raw = RawImage.Load(texBytes);
                _texture = new GlTexture(_gl, raw);
                Console.WriteLine($"loaded texture '{_texturePath}' ({raw.Width}x{raw.Height}, {raw.SurfaceCount} surface(s))");
            }
        }

        if (_regionMapTankPath is not null && _regionTerrainTankPath is not null && _regionPath is not null)
        {
            DiagTime("region", () => LoadRegion(_regionMapTankPath, _regionTerrainTankPath, _regionPath));
            // Phase 21a-1/2/3 — pre-load every door-graph neighbor's terrain
            // right after the player region. Pure visual additive on the
            // initial call; LoadPlayActors then unifies graph + nav mesh +
            // actors + dialogue across the ring. As the player walks into a
            // new region, OnPlayerRegionChanged calls back here with the new
            // center to extend the loaded ring without shifting world coords.
            DiagTime("neighbor preload", () => PreloadAroundRegion(_regionPath));
        }

        if (_worldMapTankPath is not null && _worldTerrainTankPath is not null)
            DiagTime("world", () => LoadWorld(_worldMapTankPath, _worldTerrainTankPath, _worldRootHint));

        if (_regionMapTankPath is not null && _playLogicTankPath is not null
            && _playObjectsTankPath is not null && _regionPath is not null)
            DiagTime("play actors", () => LoadPlayActors(_regionMapTankPath, _playLogicTankPath, _playObjectsTankPath, _regionPath));

        // Phase 24-MAINMENU step 1+2 — no-args boot path. Opens the DS1 tanks
        // resolved by Program.cs, builds the frontend asset resolver, and
        // kicks off the splash sequence (or jumps straight to main menu when
        // --noVideo was passed). The hosting flow shares OnRender + OnUpdate
        // with every other mode, so the splash is just another HUD layer
        // that runs while the world isn't loaded.
        if (_bootMode && _ds1ResourcesDir is not null)
            DiagTime("boot mode", () => LoadBootMode(_ds1ResourcesDir, _noVideo));

        if (_animAspPath is not null && _animPrsPath is not null)
            DiagTime("anim", () => LoadAnim(_animAspPath, _animPrsPath, _animTexturePath));

        if (_animAspPath is not null && _skritPath is not null && _skritClipPaths is not null)
            DiagTime("skrit", () => LoadSkrit(_animAspPath, _skritPath, _skritClipPaths, _animTexturePath));

        if (_diagMode)
        {
            _diagBootStopwatch.Stop();
            Console.WriteLine();
            Console.WriteLine("--- diag: startup timings ---");
            double total = 0;
            foreach (var (label, ms) in _diagStageTimings)
            {
                Console.WriteLine($"  {label,-20} {ms,8:F1} ms");
                total += ms;
            }
            Console.WriteLine($"  {"(measured stages)",-20} {total,8:F1} ms");
            Console.WriteLine($"  {"OnLoad wall clock",-20} {_diagBootStopwatch.Elapsed.TotalMilliseconds,8:F1} ms");
            Console.WriteLine("-----------------------------");
        }
    }

    /// <summary>Phase 21b-1 — wrap a startup stage in Stopwatch timing when
    /// diag mode is on, otherwise run it inline with no overhead. Recorded
    /// timings are summed + dumped at the end of OnLoad.</summary>
    private void DiagTime(string label, Action body)
    {
        if (!_diagMode) { body(); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        body();
        sw.Stop();
        _diagStageTimings.Add((label, sw.Elapsed.TotalMilliseconds));
    }

    /// <summary>Phase 21b-1 — record one frame's wall-clock dt into the ring
    /// buffer and, every <see cref="FrameReportIntervalSec"/>, print a
    /// one-line histogram (avg / p50 / p99 / max) plus current actor count.
    /// The buffer is fixed-size so report cost stays O(FrameRingSize)
    /// regardless of session length. Only fires when <see cref="_diagMode"/>
    /// is on, so non-diag runs pay nothing.</summary>
    private void DiagRecordFrame(double dt)
    {
        _diagFrameMs[_diagFrameRingHead] = dt * 1000.0;
        _diagFrameRingHead = (_diagFrameRingHead + 1) % FrameRingSize;
        if (_diagFrameRingFill < FrameRingSize) _diagFrameRingFill++;

        _diagFrameAccumulator += dt;
        if (_diagFrameAccumulator < FrameReportIntervalSec) return;
        _diagFrameAccumulator = 0;
        if (_diagFrameRingFill < 8) return; // need a meaningful sample size

        // Copy + sort the populated portion. ~240 doubles, sub-microsecond.
        Span<double> sample = stackalloc double[_diagFrameRingFill];
        for (int i = 0; i < _diagFrameRingFill; i++) sample[i] = _diagFrameMs[i];
        sample.Sort();

        double sum = 0;
        for (int i = 0; i < sample.Length; i++) sum += sample[i];
        double avg = sum / sample.Length;
        double p50 = sample[sample.Length / 2];
        double p99 = sample[Math.Min(sample.Length - 1, (int)(sample.Length * 0.99))];
        double max = sample[^1];
        double fps = avg > 0 ? 1000.0 / avg : 0;

        Console.WriteLine($"diag: frame avg={avg:F2}ms p50={p50:F2} p99={p99:F2} max={max:F2}  ({fps:F0} fps, " +
                          $"actors={_actors.Count}, regions={_loadedRegions.Count})");
    }

    /// <summary>Phase 9a entry: load a rigged ASP + a skrit + N PRS clips, then let the
    /// skrit pick which clip plays. The skrit's <c>OnStartChore$</c> handler calls
    /// <c>owner.blender.AddAnimToBlendGroup(idx, w)</c>; we capture idx via
    /// <see cref="ActorHostBridge.CurrentAnimIndex"/> and swap <see cref="_anim"/> to
    /// <paramref name="clipPaths"/>[idx]. Only the first blend slot is honored — proper
    /// multi-slot blending is Phase 10+.</summary>
    private void LoadSkrit(string aspPath, string skritPath, IReadOnlyList<string> clipPaths, string? texturePath)
    {
        if (_gl is null) return;
        if (!File.Exists(skritPath)) { Console.Error.WriteLine($"skrit '{skritPath}' not found"); return; }
        if (clipPaths.Count == 0) { Console.Error.WriteLine("--skrit-anim requires at least one clip"); return; }

        // If LoadAnim didn't already run, stand up the skinned mesh ourselves.
        if (_skinnedAsp is null || _skinnedMesh is null)
        {
            _skinnedAsp = AspMesh.Load(File.ReadAllBytes(aspPath));
            if (!_skinnedAsp.HasSkin) { Console.Error.WriteLine($"asp '{aspPath}' has no WCRN skin data"); _skinnedAsp = null; return; }
            _skinnedMesh = new SkinnedMesh(_gl, _skinnedAsp);
            if (texturePath is not null && File.Exists(texturePath))
                _animTexture = new GlTexture(_gl, RawImage.Load(File.ReadAllBytes(texturePath)));
            _camera.Position = _skinnedMesh.Center + new Vector3(0, 0, MathF.Max(_skinnedMesh.Radius, 0.5f) * 3f);
            _camera.Yaw = 0;
            _camera.Pitch = 0;
        }

        _skritClips = new PrsAnimation[clipPaths.Count];
        for (int i = 0; i < clipPaths.Count; i++)
        {
            if (!File.Exists(clipPaths[i]))
            {
                Console.Error.WriteLine($"clip '{clipPaths[i]}' not found — anim index {i} will fall back to clip 0");
                continue;
            }
            _skritClips[i] = PrsAnimation.Load(File.ReadAllBytes(clipPaths[i]));
        }
        // Guarantee clip 0 is present so CurrentAnimIndex == -1 / unresolved defaults cleanly.
        if (_skritClips[0] is null)
        {
            Console.Error.WriteLine("clip 0 must exist");
            _skritClips = null;
            return;
        }

        // Compile the skrit. Shipped content carries real binder diagnostics (see project
        // memory); surface them but don't abort — the VM can still run the handlers we need.
        var src = File.ReadAllText(skritPath);
        var script = SkritParser.Parse(src);
        var bind = new SkritBinder(script).Bind();
        if (bind.Diagnostics.Count > 0)
        {
            Console.WriteLine($"skrit '{skritPath}': {bind.Diagnostics.Count} bind diagnostic(s):");
            foreach (var d in bind.Diagnostics) Console.WriteLine($"  !! {d}");
        }
        var program = new SkritCompiler(script, bind).Compile();

        _skritHost = new ActorHostBridge(rngSeed: 1) { NumSubAnims = clipPaths.Count };
        _skritRuntime = new SkritRuntime();
        _skritInstance = _skritRuntime.Add(new SkritInstance(program, _skritHost));
        _skritHost.Instance = _skritInstance;
        _skritInstance.Start();
        // Anim skrits conventionally enter through OnStartChore$( subanim, flags ).
        _skritInstance.Dispatch("OnStartChore$", SkritValue.FromInt(0), SkritValue.FromInt(0));

        _skritCurrentClip = Math.Max(0, _skritHost.CurrentAnimIndex);
        _anim = _skritClips[_skritCurrentClip] ?? _skritClips[0];
        _animTime = 0;

        Console.WriteLine($"skrit '{skritPath}' driving animation: state={_skritInstance.CurrentState}, " +
                          $"clip={_skritCurrentClip} ({clipPaths[_skritCurrentClip]}), " +
                          $"blender-log={_skritHost.BlenderLog.Count} call(s)");
    }

    /// <summary>Loads a rigged ASP and a PRS clip, builds a SkinnedMesh, frames the camera,
    /// and (optionally) loads an albedo .raw to bind during draw. The clip just loops at
    /// its native length — this is a viewer, not a state machine.</summary>
    private void LoadAnim(string aspPath, string prsPath, string? texturePath)
    {
        if (_gl is null) return;

        if (!File.Exists(aspPath))
        {
            Console.Error.WriteLine($"asp '{aspPath}' not found");
            return;
        }
        if (!File.Exists(prsPath))
        {
            Console.Error.WriteLine($"prs '{prsPath}' not found");
            return;
        }

        _skinnedAsp = AspMesh.Load(File.ReadAllBytes(aspPath));
        if (!_skinnedAsp.HasSkin)
        {
            Console.Error.WriteLine($"asp '{aspPath}' has no WCRN skin data; --anim requires a rigged mesh");
            _skinnedAsp = null;
            return;
        }
        _anim = PrsAnimation.Load(File.ReadAllBytes(prsPath));
        _skinnedMesh = new SkinnedMesh(_gl, _skinnedAsp);

        if (texturePath is not null)
        {
            if (!File.Exists(texturePath))
            {
                Console.Error.WriteLine($"texture '{texturePath}' not found; rendering untextured");
            }
            else
            {
                var raw = RawImage.Load(File.ReadAllBytes(texturePath));
                _animTexture = new GlTexture(_gl, raw);
            }
        }

        var matched = 0;
        var prsByName = new HashSet<string>(_anim.BoneNames);
        foreach (var n in _skinnedAsp.BoneNames) if (prsByName.Contains(n)) matched++;

        Console.WriteLine($"loaded skinned mesh '{_skinnedAsp.MeshName}' " +
                          $"({_skinnedAsp.Positions.Length} v, {_skinnedAsp.TriangleCount} tris, " +
                          $"{_skinnedAsp.BoneCount} bones, bounds {_skinnedMesh.Min} .. {_skinnedMesh.Max})");
        Console.WriteLine($"loaded clip '{prsPath}' ({_anim.NumBones} bones, length={_anim.AnimLength:F3}s, " +
                          $"{matched}/{_anim.NumBones} bones map to mesh)");

        // Frame the rigged mesh the same way we frame static meshes — bounds×3 back along +Z.
        var center = _skinnedMesh.Center;
        var radius = MathF.Max(_skinnedMesh.Radius, 0.5f);
        _camera.Position = center + new Vector3(0, 0, radius * 3f);
        _camera.Yaw = 0;
        _camera.Pitch = 0;
    }

    /// <summary>Phase 24-MAINMENU step 1+2 — bring up the minimum surface needed for
    /// the splash → main menu sequence. Opens Logic.dsres + Objects.dsres under
    /// <paramref name="ds1Resources"/> (which Program.cs already verified contains
    /// Logic.dsres) and stashes them on the RenderHost fields that gameplay
    /// later reuses, builds the same <c>AssetResolver</c> shape <c>LoadPlayActors</c>
    /// uses (Objects last → Logic patches over Objects), spins the audio engine
    /// + music player so the frontend mp3 can stream during the menu, and seeds
    /// the <c>FrontendScene</c> at the splash entry state. <paramref name="noVideo"/>
    /// (DS1's <c>nointro=true</c> equivalent) skips the splash entirely and jumps
    /// straight to the main menu state.
    /// <para>Doesn't load a region or actor list — those only spawn after New
    /// Game / Continue / Load Game commits the player to gameplay (Phase 24
    /// step 5+).</para></summary>
    private void LoadBootMode(string ds1Resources, bool noVideo)
    {
        if (_gl is null) return;
        var logicPath   = Path.Combine(ds1Resources, "Logic.dsres");
        var objectsPath = Path.Combine(ds1Resources, "Objects.dsres");
        // Phase 24-MAINMENU step 1+2-FOLD — fail loudly when the tanks
        // we resolved aren't actually openable. Pre-fold this returned
        // silently with _bootSplashActive=false and the user got a
        // black window forever; now we close the window so the
        // top-level catch can persist a crash-log entry the .exe
        // double-launcher can find.
        if (!File.Exists(logicPath) || !File.Exists(objectsPath))
        {
            var msg = $"boot: Logic.dsres or Objects.dsres missing under {ds1Resources} — set SIEGEFX_DS1 to a valid Dungeon Siege install.";
            Console.Error.WriteLine("  " + msg);
            throw new FileNotFoundException(msg);
        }
        try
        {
            _playLogicTank   = TankFile.Open(logicPath);
            _playObjectsTank = TankFile.Open(objectsPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  boot: tank open failed — {ex.Message}");
            throw;
        }
        var logicReader   = new TankReader(_playLogicTank);
        var objectsReader = new TankReader(_playObjectsTank);
        var resolver = new SiegeFX.Core.Assets.AssetResolver();
        resolver.Add(objectsReader, "Objects.dsres");
        resolver.Add(logicReader,   "Logic.dsres");
        _playResolver = resolver;
        Console.WriteLine($"  boot: tanks open ({logicPath}, {objectsPath})");

        // Phase 29-CD-CREATOR-FIX — build the template store at boot
        // (was previously only built inside LoadPlayActors → region
        // load) so HeroPreviewRenderer can spawn the live 3D farmboy /
        // farmgirl preview at the boot CharacterSelect state without
        // needing a region. EnsurePreview's only other requirement is
        // _skinShader (set in OnLoad) + _playResolver (just set above).
        // LoadPlayActors reassigns _templateStore later — harmless.
        try
        {
            var (store, storeDiags) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
            _templateStore = store;
            Console.WriteLine($"  boot: template store loaded ({store.Count} templates)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  boot: template store load failed — {ex.Message}");
        }

        // Phase 27-SP-FLYOUT-FIX3 — load the HUD font at boot so the
        // frontend menu can render TextRenderer overlays. Previously
        // SetFont was only called inside LoadPlayActors (region load),
        // so HasFont was false at the menu screen and every DrawString
        // silently returned — hence "no text on any buttons" in SP
        // state where atlas-driven labels are masked off. Idempotent
        // (LoadPlayActors's later SetFont just re-applies the same
        // font with the same atlas).
        if (_textRenderer is not null && !_textRenderer.HasFont)
        {
            try
            {
                var bootFont = SiegeFX.Core.Assets.BitmapFont.TryLoadByName(
                    resolver, "b_gui_fnt_12p_copperplate-light");
                if (bootFont is not null)
                {
                    _textRenderer.SetFont(bootFont);
                    Console.WriteLine($"  boot font: {bootFont.Name} ({bootFont.Atlas.Width}x{bootFont.Atlas.Height} atlas)");
                }
                else
                {
                    Console.Error.WriteLine("  !! boot font missing — menu overlay text disabled");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  boot font load failed: {ex.Message}");
            }
        }

        // Audio comes up so the frontend music slot (s_m_Frontend.mp3 per
        // /ui/config/frontend_music/frontend_music.gas) can stream during
        // the menu. Sound.dsres is the music + SFX tank; same path treatment
        // as LoadPlayActors so we don't drift between the two boot routes.
        try
        {
            var soundPath = Path.Combine(ds1Resources, "Sound.dsres");
            if (File.Exists(soundPath))
            {
                _playSoundTank = TankFile.Open(soundPath);
                _audio = SiegeFX.Audio.AudioEngine.TryCreate();
                _music = SiegeFX.Audio.MusicPlayer.TryCreate(_audio);
                // Phase 24-POLISH-C — register the frontend button click
                // cues. Skipped silently when the audio device is missing.
                if (_audio is not null)
                {
                    var soundReader = new TankReader(_playSoundTank);
                    TryRegisterSfx(soundReader, SfxFrontendBigButton,
                        "/sound/effects/s_e_frontend_big_button.wav");
                    TryRegisterSfx(soundReader, SfxFrontendArrowButton,
                        "/sound/effects/s_e_frontend_arrow_button.wav");
                    TryRegisterSfx(soundReader, SfxFrontendLogoFlyin,
                        "/sound/effects/s_e_frontend_logo_flyin.wav");
                    TryRegisterSfx(soundReader, SfxFrontendLogoFlyout,
                        "/sound/effects/s_e_frontend_logo_flyout.wav");
                }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"  boot: audio init failed — {ex.Message}"); }

        // Spin the frontend scene now that GL + resolver are live. EnsureFrontendScene
        // builds the 8-mesh chrome composer; SetState below picks the start screen.
        EnsureFrontendScene();
        if (_frontendScene is not null)
        {
            // --noVideo (DS1 nointro=true) skips both splashes and the logo
            // drop, jumping straight to the main menu state. Otherwise we
            // start at the Microsoft splash and let the state machine
            // advance through GPG → logo drop → main menu on its own.
            _frontendScene.SetState(noVideo
                ? SiegeFX.Runtime.Render.Hud.FrontendScene.ScreenState.MainMenu
                : SiegeFX.Runtime.Render.Hud.FrontendScene.ScreenState.IntroMicrosoft);
            _bootSplashActive = !noVideo;
        }
        // Phase 24-POLISH-B — preload the DS1 wood-button textures so
        // MainMenuPanel can render textured quads instead of the fallback
        // colored rectangles. Six textures total (3 idle/hov/down for the
        // shared wood button + 3 for exitback). Failure-tolerant — null
        // textures fall through to the fallback path.
        if (_iconRenderer is not null)
        {
            var up      = LoadBootRaw("b_gui_fe_m_mn_3d_button_wood_up");
            var hov     = LoadBootRaw("b_gui_fe_m_mn_3d_button_wood_hov");
            var down    = LoadBootRaw("b_gui_fe_m_mn_3d_button_wood_down");
            var exitUp  = LoadBootRaw("b_gui_fe_m_mn_3d_exitback-up");
            var exitHov = LoadBootRaw("b_gui_fe_m_mn_3d_exitback");      // no -hov suffix in DS1; idle reused
            var exitDn  = LoadBootRaw("b_gui_fe_m_mn_3d_exitback-down");
            // Phase 25-CHROME-FOLD12 — text-small trio carries the EXIT
            // (and BACK / NEXT / PREVIOUS / NAME / HERO / etc.) labels
            // baked in. Three states for idle / hover / pressed; user
            // confirmed via PNG inspection that "EXIT" lives in the
            // visual bottom-left of the atlas (= upper-right after the
            // RAW bottom-up storage flip).
            var smIdle  = LoadBootRaw("b_gui_fe_m_mn_3d_text-small");
            var smUp    = LoadBootRaw("b_gui_fe_m_mn_3d_text-small-up");
            var smDown  = LoadBootRaw("b_gui_fe_m_mn_3d_text-small-down");
            _mainMenu.SetButtonTextures(up, hov, down, exitUp, exitHov, exitDn,
                                        smIdle, smUp, smDown);
        }
    }

    /// <summary>Phase 24-POLISH-B — basename → cached GlTexture helper for
    /// the boot-mode UI. Reuses <see cref="_splashTexCache"/> so OnClosing
    /// disposes uniformly. Returns null on miss/parse-fail; caller falls
    /// back to a non-textured visual.</summary>
    private GlTexture? LoadBootRaw(string basename)
    {
        if (_gl is null || _playResolver is null) return null;
        if (_splashTexCache.TryGetValue(basename, out var hit)) return hit;
        try
        {
            if (!_playResolver.TryLoadByBasename(basename + ".raw", out var bytes))
            {
                Console.Error.WriteLine($"  boot: '{basename}.raw' not found");
                _splashTexCache[basename] = null;
                return null;
            }
            var raw = SiegeFX.Core.Assets.RawImage.Load(bytes);
            var tex = new GlTexture(_gl, raw);
            _splashTexCache[basename] = tex;
            return tex;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  boot: '{basename}' load failed — {ex.Message}");
            _splashTexCache[basename] = null;
            return null;
        }
    }

    // Phase 24-MAINMENU — true while the splash → main menu transition is
    // running so OnRender knows to draw the frontend layer instead of the
    // (non-existent) gameplay scene. Cleared once the menu state stabilizes.
    private bool _bootSplashActive;

    /// <summary>Loads every region in the map tank stitched into one world. Shares the
    /// per-mesh / per-texture caches so the 77k-instance MpWorld costs the same in GPU
    /// upload as one region — the bulk of the work is the CPU-side layout build.</summary>
    private void LoadWorld(string mapTankPath, string terrainTankPath, string? rootHint)
    {
        if (_gl is null) return;

        using var mapTank = TankFile.Open(mapTankPath);
        var mapReader = new TankReader(mapTank);
        using var terrainTank = TankFile.Open(terrainTankPath);
        var terrainReader = new TankReader(terrainTank);

        var meshIndex = SnoMeshIndex.Build(terrainReader);
        var modelCache = new Dictionary<uint, SnoModel?>();
        SnoModel? ResolveModel(uint meshGuid)
        {
            if (modelCache.TryGetValue(meshGuid, out var cached)) return cached;
            SnoModel? sno = null;
            if (meshIndex.TryResolve(meshGuid, out var path))
            {
                try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
                catch { sno = null; }
            }
            modelCache[meshGuid] = sno;
            return sno;
        }

        // Enumerate every region, build its graph + layout + (optional) stitch helper.
        var entries = new List<WorldLayout.RegionEntry>();
        var regionGraphs = new Dictionary<string, RegionGraph>();
        foreach (var path in mapReader.ListFiles())
        {
            if (!path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
            var regionPath = path[..^"/terrain_nodes/nodes.gas".Length];
            try
            {
                var graph = RegionGraph.Load(mapReader.ExtractToMemory(path));
                var layout = RegionLayout.Build(graph, ResolveModel);
                RegionStitchHelper? stitches = null;
                var stitchPath = regionPath + "/editor/stitch_helper.gas";
                if (mapReader.TryGetFile(stitchPath, out _))
                {
                    try { stitches = RegionStitchHelper.Load(mapReader.ExtractToMemory(stitchPath)); }
                    catch { /* a bad stitch file just makes that region un-stitchable */ }
                }
                entries.Add(new WorldLayout.RegionEntry(regionPath, graph, layout, stitches));
                regionGraphs[regionPath] = graph;
            }
            catch
            {
                // Skipping a broken region is better than failing the whole world load.
            }
        }

        var world = WorldLayout.Build(entries, ResolveModel, rootHint);

        // .raw index built once; reused for every subset resolution below.
        var rawIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in terrainReader.ListFiles())
        {
            if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(p);
            if (!rawIndex.ContainsKey(bare)) rawIndex[bare] = p;
        }

        // Materialize each placed snode as a RegionInstance. We need per-instance texsetAbbr
        // (authored per-snode in nodes.gas), so walk region graphs rather than the flat
        // world.Transforms dict directly.
        foreach (var entry in entries)
        {
            if (!world.RegionOffsets.ContainsKey(entry.Path)) continue;
            foreach (var node in entry.Graph.Nodes)
            {
                if (!world.Transforms.TryGetValue(node.Guid, out var worldXf)) continue;
                var model = ResolveModel(node.MeshGuid);
                if (model is null) continue;

                if (!_regionMeshes.TryGetValue(node.MeshGuid, out var mesh))
                {
                    mesh = new SnoMesh(_gl, model);
                    _regionMeshes[node.MeshGuid] = mesh;
                }

                foreach (var subset in mesh.Subsets)
                {
                    if (string.IsNullOrEmpty(subset.TextureName)) continue;
                    var resolved = ResolveTexName(subset.TextureName, node.TexsetAbbr);
                    if (_snoTextures.ContainsKey(resolved)) continue;
                    if (!rawIndex.TryGetValue(resolved, out var texPath)) continue;
                    try
                    {
                        var raw = RawImage.Load(terrainReader.ExtractToMemory(texPath));
                        _snoTextures[resolved] = new GlTexture(_gl, raw);
                    }
                    catch { /* skip */ }
                }

                _regionInstances.Add(new RegionInstance(worldXf, mesh, node.TexsetAbbr));
            }
        }

        // Frame the camera over the root region's origin, lifted so the player sees a
        // decent patch of ground on first paint. World extents span ±1km, so the user
        // will fly a lot — WASD+sprint is the expected traversal.
        if (world.RegionOffsets.TryGetValue(world.RootRegion, out var rootOffset))
        {
            _camera.Position = new Vector3(rootOffset.M41, rootOffset.M42 + 20f, rootOffset.M43 + 40f);
        }
        else
        {
            _camera.Position = new Vector3(0, 20f, 40f);
        }
        _camera.Yaw = 0;
        _camera.Pitch = -0.3f;

        Console.WriteLine($"world '{mapTankPath}': root={world.RootRegion}");
        Console.WriteLine($"  placed {world.PlacedRegionCount}/{entries.Count} region(s), " +
                          $"{_regionInstances.Count:N0} instance(s), " +
                          $"{_regionMeshes.Count:N0} unique SNO(s), {_snoTextures.Count:N0} texture(s)");
        Console.WriteLine($"  unresolved={world.UnresolvedStitchCount} dangling={world.DanglingStitchCount} unreachable={world.UnreachableRegionCount}");
    }

    /// <summary>Loads a full region: opens both tanks, parses the region graph, walks the
    /// door graph to get world-space transforms, resolves and uploads each unique SNO + its
    /// textures once, then enqueues one <see cref="RegionInstance"/> per placed snode.</summary>
    private void LoadRegion(string mapTankPath, string terrainTankPath, string regionPath)
    {
        if (_gl is null) return;

        var normalized = regionPath.Replace('\\', '/');
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        if (normalized.EndsWith('/')) normalized = normalized[..^1];

        using var mapTank = TankFile.Open(mapTankPath);
        var mapReader = new TankReader(mapTank);
        using var terrainTank = TankFile.Open(terrainTankPath);
        var terrainReader = new TankReader(terrainTank);

        var graph = RegionGraph.Load(mapReader.ExtractToMemory(normalized + "/terrain_nodes/nodes.gas"));
        var meshIndex = SnoMeshIndex.Build(terrainReader);

        // Two caches: SnoModel (Core-side) per mesh_guid and SnoMesh (GL-side) per mesh_guid.
        // The model cache lets RegionLayout.Build read door transforms; the mesh cache lets
        // us upload each unique SNO exactly once regardless of how many instances use it.
        var modelCache = new Dictionary<uint, SnoModel?>();
        SnoModel? ResolveModel(uint meshGuid)
        {
            if (modelCache.TryGetValue(meshGuid, out var cached)) return cached;
            SnoModel? sno = null;
            if (meshIndex.TryResolve(meshGuid, out var path))
            {
                try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
                catch { sno = null; }
            }
            modelCache[meshGuid] = sno;
            return sno;
        }

        var layout = RegionLayout.Build(graph, ResolveModel);
        _regionLayout = layout;

        // Build a bare-filename → full-path .raw index on demand (same pattern as
        // LoadSnoTexturesFromTank), used every time a subset's texture is first seen.
        var rawIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in terrainReader.ListFiles())
        {
            if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(p);
            if (!rawIndex.ContainsKey(bare)) rawIndex[bare] = p;
        }

        foreach (var node in graph.Nodes)
        {
            if (!layout.TryGetTransform(node.Guid, out var world)) continue;
            var model = ResolveModel(node.MeshGuid);
            if (model is null) continue;

            if (!_regionMeshes.TryGetValue(node.MeshGuid, out var mesh))
            {
                mesh = new SnoMesh(_gl, model);
                _regionMeshes[node.MeshGuid] = mesh;
            }

            // Load textures per (subset, node.texset). Different instances of the same
            // SNO can use different texsets, so keying by resolved name lets one mesh
            // appear under multiple palettes without reloading raws we already have.
            foreach (var subset in mesh.Subsets)
            {
                if (string.IsNullOrEmpty(subset.TextureName)) continue;
                var resolved = ResolveTexName(subset.TextureName, node.TexsetAbbr);
                if (_snoTextures.ContainsKey(resolved)) continue;
                if (!rawIndex.TryGetValue(resolved, out var texPath)) continue;
                try
                {
                    var raw = RawImage.Load(terrainReader.ExtractToMemory(texPath));
                    _snoTextures[resolved] = new GlTexture(_gl, raw);
                }
                catch { /* skip unreadable textures; subset falls back to uHasTexture=0 */ }
            }

            _regionInstances.Add(new RegionInstance(world, mesh, node.TexsetAbbr));
        }

        // Frame the camera on the anchor: pull back along +Z by ~3× the anchor SNO's
        // bounding radius and lift by ~1× so the tile is comfortably visible on first
        // paint. A full region-bounds pass is overkill here — the user flies from there.
        var anchorRadius = 8f;
        if (graph.TryGetNode(layout.AnchorGuid, out var anchorNode) &&
            _regionMeshes.TryGetValue(anchorNode.MeshGuid, out var anchorMesh))
        {
            anchorRadius = MathF.Max(anchorMesh.Radius, 1f);
        }
        _camera.Position = new Vector3(0, anchorRadius, anchorRadius * 3f);
        _camera.Yaw = 0;
        _camera.Pitch = -0.3f;

        // Diagnostic: count resolved vs missing subset texture refs across every placed
        // instance. A miss means either the subset's resolved name isn't in rawIndex, or
        // the .raw failed to decode. Shows up as untextured (sand) fallback.
        var resolvedSubsets = 0;
        var missingSubsets = 0;
        var missingSample = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in _regionInstances)
        {
            foreach (var subset in inst.Mesh.Subsets)
            {
                if (string.IsNullOrEmpty(subset.TextureName)) continue;
                var resolved = ResolveTexName(subset.TextureName, inst.TexsetAbbr);
                if (_snoTextures.ContainsKey(resolved)) resolvedSubsets++;
                else
                {
                    missingSubsets++;
                    if (missingSample.Count < 12) missingSample.Add(resolved);
                }
            }
        }
        Console.WriteLine($"region '{normalized}': placed {_regionInstances.Count} instance(s) " +
                          $"across {_regionMeshes.Count} unique SNO(s), {_snoTextures.Count} texture(s)");
        Console.WriteLine($"  subsets: {resolvedSubsets} textured, {missingSubsets} missing");
        if (missingSample.Count > 0)
            Console.WriteLine($"  missing samples: {string.Join(", ", missingSample)}");
    }

    /// <summary>Phase 21a-1 — neighbor terrain preload. Walks the player region's
    /// stitch helper to discover its door-graph neighbors, then composes a partial
    /// <see cref="WorldLayout"/> rooted at the player region (so the player keeps
    /// its identity offset and existing instances stay valid). Each neighbor's
    /// snodes are emitted as additional <see cref="RegionInstance"/>s at their
    /// composed world transforms, sharing the same <see cref="_regionMeshes"/>
    /// and <see cref="_snoTextures"/> caches the player region populated.
    ///
    /// Pure-additive: no existing field is rewritten. The player's
    /// <see cref="_regionLayout"/> is unchanged, so 21a-2 (nav + actor merge)
    /// can still index into it without confusion.
    /// </summary>
    /// <summary>Phase 21a-3 — incremental region preload. The first call (from
    /// OnLoad) seeds <see cref="_loadedRegions"/> with the launch region + its
    /// first ring and pins <see cref="_worldRootRegion"/>. Subsequent calls
    /// (from <see cref="OnPlayerRegionChanged"/> when the player walks into a
    /// new region) extend the loaded set with that new region's first ring.
    /// Already-loaded regions are preserved — no re-parse, no re-instantiate,
    /// no coordinate shift, since the world root stays pinned forever. Returns
    /// the list of newly-loaded region paths so the caller can spawn their
    /// actors and merge their conversation pools.</summary>
    private List<string> PreloadAroundRegion(string centerRegion)
    {
        var newlyLoaded = new List<string>();
        if (_gl is null || _regionMapTankPath is null || _regionTerrainTankPath is null)
            return newlyLoaded;

        var center = centerRegion.Replace('\\', '/');
        if (!center.StartsWith('/')) center = "/" + center;
        if (center.EndsWith('/')) center = center[..^1];

        bool isInitial = _worldRootRegion is null;
        if (isInitial) _worldRootRegion = center;

        using var mapTank = TankFile.Open(_regionMapTankPath);
        var mapReader = new TankReader(mapTank);
        using var terrainTank = TankFile.Open(_regionTerrainTankPath);
        var terrainReader = new TankReader(terrainTank);

        // Center's stitch helper drives the new neighbor list. No file =
        // standalone region (test fixtures) → nothing to extend with.
        var centerStitchPath = center + "/editor/stitch_helper.gas";
        if (!mapReader.TryGetFile(centerStitchPath, out _))
        {
            if (isInitial)
                Console.WriteLine("  neighbor preload: no stitch_helper.gas — standalone region");
            return newlyLoaded;
        }
        RegionStitchHelper centerStitches;
        try { centerStitches = RegionStitchHelper.Load(mapReader.ExtractToMemory(centerStitchPath)); }
        catch (Exception ex)
        {
            Console.WriteLine($"  neighbor preload: stitch parse failed for {center} ({ex.Message}) — skipping");
            return newlyLoaded;
        }
        if (centerStitches.ByDestination.Count == 0)
        {
            if (isInitial) Console.WriteLine("  neighbor preload: 0 destination region(s) declared");
            return newlyLoaded;
        }

        // Map leaf names ("fh_r2") to full tank paths.
        var pathByLeaf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in mapReader.ListFiles())
        {
            if (!p.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
            var rp = p[..^"/terrain_nodes/nodes.gas".Length];
            pathByLeaf[LeafName(rp)] = rp;
        }

        // Build the to-load list: center (only on initial call — mid-game the
        // center is already loaded by definition), plus any first-ring neighbor
        // not currently in _loadedRegions.
        var toLoad = new List<string>();
        if (!_loadedRegions.ContainsKey(center)) toLoad.Add(center);
        foreach (var destLeaf in centerStitches.ByDestination.Keys)
        {
            if (!pathByLeaf.TryGetValue(destLeaf, out var np)) continue;
            if (np == center) continue;
            if (!_loadedRegions.ContainsKey(np)) toLoad.Add(np);
        }

        // Mid-game with no new regions to add: nothing to do. (Initial call
        // always has at least the player region in toLoad — this branch is
        // reachable only on a mid-game re-anchor where the player walked
        // back into a region whose ring is fully loaded already.)
        if (toLoad.Count == 0 && !isInitial) return newlyLoaded;

        // SNO resolver shared across all the parses we're about to do, so
        // a SNO referenced by both player and neighbors only loads once.
        // Also reused below for WorldLayout.Build's door composition.
        var meshIndex = SnoMeshIndex.Build(terrainReader);
        var modelCache = new Dictionary<uint, SnoModel?>();
        SnoModel? ResolveModel(uint meshGuid)
        {
            if (modelCache.TryGetValue(meshGuid, out var cached)) return cached;
            SnoModel? sno = null;
            if (meshIndex.TryResolve(meshGuid, out var path))
            {
                try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
                catch { sno = null; }
            }
            modelCache[meshGuid] = sno;
            return sno;
        }

        bool TryBuildEntry(string rp, out WorldLayout.RegionEntry entry)
        {
            entry = default;
            try
            {
                var graph = RegionGraph.Load(mapReader.ExtractToMemory(rp + "/terrain_nodes/nodes.gas"));
                var layout = RegionLayout.Build(graph, ResolveModel);
                RegionStitchHelper? stitches = null;
                var sp = rp + "/editor/stitch_helper.gas";
                if (mapReader.TryGetFile(sp, out _))
                {
                    try { stitches = RegionStitchHelper.Load(mapReader.ExtractToMemory(sp)); }
                    catch { /* a bad stitch file just makes that region un-stitchable */ }
                }
                entry = new WorldLayout.RegionEntry(rp, graph, layout, stitches);
                return true;
            }
            catch
            {
                return false;
            }
        }

        foreach (var rp in toLoad)
        {
            if (!TryBuildEntry(rp, out var entry))
            {
                Console.WriteLine($"  neighbor preload: failed to parse {rp} — skipping");
                continue;
            }
            _loadedRegions[rp] = entry;
            newlyLoaded.Add(rp);
        }

        if (newlyLoaded.Count == 0 && !isInitial) return newlyLoaded;

        // Rebuild WorldLayout from the full loaded set so newly-loaded regions
        // get composed into the same world frame as already-loaded ones. Root
        // is pinned to _worldRootRegion (the launch region) forever, so world
        // coordinates of the player + already-spawned actors never shift on
        // a re-anchor.
        var entries = new List<WorldLayout.RegionEntry>(_loadedRegions.Count);
        foreach (var kv in _loadedRegions) entries.Add(kv.Value);
        var rootHint = (_worldRootRegion != null && _loadedRegions.ContainsKey(_worldRootRegion))
            ? _worldRootRegion : center;
        var world = WorldLayout.Build(entries, ResolveModel, rootHint);
        _worldLayout = world;

        // Refresh the legacy _worldRegionGraphs view (root region first to
        // preserve LoadPlayActors's "graphs[0] is the player region" idiom)
        // and the snode → region lookup table used by RegionLocator.
        _worldRegionGraphs.Clear();
        var lookup = new List<(Vector3, string)>(world.Transforms.Count);
        void AppendRegion(string rp, RegionGraph g)
        {
            if (!world.RegionOffsets.ContainsKey(rp)) return;
            _worldRegionGraphs.Add((rp, g));
            foreach (var n in g.Nodes)
            {
                if (world.Transforms.TryGetValue(n.Guid, out var xf))
                    lookup.Add((new Vector3(xf.M41, 0f, xf.M43), rp));
            }
        }
        if (_worldRootRegion is not null && _loadedRegions.TryGetValue(_worldRootRegion, out var rootEntry))
            AppendRegion(_worldRootRegion, rootEntry.Graph);
        foreach (var kv in _loadedRegions)
        {
            if (kv.Key == _worldRootRegion) continue;
            AppendRegion(kv.Key, kv.Value.Graph);
        }
        _snodeRegionLookup = lookup.ToArray();

        // .raw texture index for new terrain. Already-decoded textures
        // short-circuit on _snoTextures.ContainsKey.
        var rawIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in terrainReader.ListFiles())
        {
            if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(p);
            if (!rawIndex.ContainsKey(bare)) rawIndex[bare] = p;
        }

        int placedNew = 0;
        int instancesAdded = 0;
        foreach (var rp in newlyLoaded)
        {
            // Initial call: skip the player region — LoadRegion already added
            // its RegionInstances. Mid-game: every newly-loaded region is new,
            // so include them all.
            if (isInitial && rp == center) continue;

            if (!_loadedRegions.TryGetValue(rp, out var entry)) continue;
            if (!world.RegionOffsets.ContainsKey(rp)) continue;
            placedNew++;

            foreach (var node in entry.Graph.Nodes)
            {
                if (!world.Transforms.TryGetValue(node.Guid, out var worldXf)) continue;
                var model = ResolveModel(node.MeshGuid);
                if (model is null) continue;

                if (!_regionMeshes.TryGetValue(node.MeshGuid, out var mesh))
                {
                    mesh = new SnoMesh(_gl, model);
                    _regionMeshes[node.MeshGuid] = mesh;
                }

                foreach (var subset in mesh.Subsets)
                {
                    if (string.IsNullOrEmpty(subset.TextureName)) continue;
                    var resolved = ResolveTexName(subset.TextureName, node.TexsetAbbr);
                    if (_snoTextures.ContainsKey(resolved)) continue;
                    if (!rawIndex.TryGetValue(resolved, out var texPath)) continue;
                    try
                    {
                        var raw = RawImage.Load(terrainReader.ExtractToMemory(texPath));
                        _snoTextures[resolved] = new GlTexture(_gl, raw);
                    }
                    catch { /* skip unreadable textures */ }
                }

                _regionInstances.Add(new RegionInstance(worldXf, mesh, node.TexsetAbbr));
                instancesAdded++;
            }
        }

        if (isInitial)
        {
            Console.WriteLine($"  neighbor preload: {placedNew}/{toLoad.Count - 1} region(s) " +
                              $"({instancesAdded:N0} instance(s)" +
                              $", world layout: unresolved={world.UnresolvedStitchCount} dangling={world.DanglingStitchCount})");
        }
        else
        {
            Console.WriteLine($"  rolling preload: +{newlyLoaded.Count} region(s) (now {_loadedRegions.Count} total, " +
                              $"+{instancesAdded:N0} terrain instance(s))");
        }

        return newlyLoaded;
    }

    private static string LeafName(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? path : path[(i + 1)..];
    }

    /// <summary>Phase 10e — spawn every shipped actor in a region and attach each one to the
    /// skinned-mesh draw loop. Terrain already loaded by <see cref="LoadRegion"/> populated
    /// <see cref="_regionLayout"/> so actor node-anchored positions compose against the same
    /// world frame. One <see cref="SkritRuntime"/> + one <see cref="SiegeFX.Core.Actors.WorldMessageBus"/>
    /// are shared across all actors; OnUpdate drains them at the 20 Hz logical tick so actor
    /// skrits can self-swap clips the same way the single-actor <see cref="_skritRuntime"/>
    /// path does. Textures resolve through <see cref="ResolveActorTexture"/> (template
    /// chain → texset override → variant .raw) at draw time; the skinned shader's
    /// uHasTexture=0 neutral-sand path is now the safety net for templates whose
    /// chain doesn't surface any texture, not the common case.</summary>
    private void LoadPlayActors(string mapTankPath, string logicTankPath, string objectsTankPath, string regionPath)
    {
        if (_gl is null) return;
        if (_regionLayout is null)
        {
            Console.Error.WriteLine("--play-region: region did not load; cannot place actors");
            return;
        }

        // Pinned to RenderHost fields (not `using var`) so _playResolver can keep
        // reading from them during gameplay — loot-swap weapon loads fire hours after
        // LoadPlayActors returns.
        _playMapTank     = TankFile.Open(mapTankPath);
        _playLogicTank   = TankFile.Open(logicTankPath);
        _playObjectsTank = TankFile.Open(objectsTankPath);
        var mapTank     = _playMapTank;
        var logicTank   = _playLogicTank;
        var objectsTank = _playObjectsTank;
        var mapReader     = new TankReader(mapTank);
        var logicReader   = new TankReader(logicTank);
        var objectsReader = new TankReader(objectsTank);

        // Phase 17-SC-E — populate the particle atlas off Objects.dsres
        // while we have a fresh reader. Failure-tolerant: missing entries
        // leave the slot null and the shader falls back to white.
        try { _particles?.LoadTextures(objectsReader); }
        catch (Exception ex) { Console.WriteLine($"  particle atlas load failed: {ex.Message}"); }

        // Phase 17-SC-D — load every shipped effect_script* block. SC-F-2's
        // SfxRuntime walks these on demand when the trigger runtime fires
        // call_sfx_script. Failure is non-fatal; without the store, emitter
        // calls just no-op (the matrix still ticks).
        try
        {
            _sfxStore = SiegeFX.Core.Assets.SfxScriptStore.LoadFromTank(logicReader);
            if (_particles is not null)
                _sfxRuntime = new SiegeFX.Core.Sfx.SfxRuntime(_sfxStore, _particles);
            Console.WriteLine($"  sfx scripts: {_sfxStore.Count} loaded");
        }
        catch (Exception ex) { Console.WriteLine($"  sfx_script load failed: {ex.Message}"); }

        var (store, storeDiags)    = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
        var (instances, instDiags) = SiegeFX.Core.Assets.RegionObjects.LoadActors(mapReader, regionPath);

        // Phase 21a-2 — when LoadNeighborTerrain stashed a world layout +
        // per-region graphs, swap _regionLayout for a unified world-space
        // layout (so cross-boundary node guids resolve) and merge each
        // neighbor's actor instances into the spawn list. The player
        // region is at identity in WorldLayout by construction, so swapping
        // the layout doesn't shift the player's own actors. Without
        // neighbors (standalone regions, test fixtures) we fall through
        // to single-region behavior unchanged.
        var allInstances = new List<SiegeFX.Core.Assets.ActorInstance>(instances);
        var neighborInstanceCount = 0;
        if (_worldLayout is not null && _worldRegionGraphs.Count > 1)
        {
            var graphs = new List<SiegeFX.Core.Assets.RegionGraph>(_worldRegionGraphs.Count);
            foreach (var (_, g) in _worldRegionGraphs) graphs.Add(g);
            var unifiedGraph  = SiegeFX.Core.Assets.RegionGraph.Combine(graphs);
            var unifiedLayout = SiegeFX.Core.Assets.RegionLayout.FromTransforms(
                _regionLayout.AnchorGuid, _worldLayout.Transforms);
            _regionLayout = unifiedLayout;

            foreach (var (path, _) in _worldRegionGraphs)
            {
                if (path == regionPath) continue;
                try
                {
                    var (more, moreDiags) = SiegeFX.Core.Assets.RegionObjects.LoadActors(mapReader, path);
                    allInstances.AddRange(more);
                    neighborInstanceCount += more.Count;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  neighbor actors load failed for {path}: {ex.Message}");
                }
            }
            Console.WriteLine($"  unified region scope: {graphs.Count} region(s), " +
                              $"{unifiedGraph.Nodes.Count:N0} snode(s), " +
                              $"{neighborInstanceCount:N0} neighbor actor instance(s)");
        }

        // Objects.dsres carries more recent asset overrides than Logic.dsres in shipped DS1
        // content (patch-tank order), so add Logic last — the AssetResolver does last-added-wins
        // basename indexing and we want Logic's skrit/prs/asp resolution to shadow stale objects
        // copies. Phase 21c-5 — Terrain.dsres goes in first as a fallback texture source for
        // interior props (bookshelves, doors) that reference b_t_*.raw bitmaps shipped alongside
        // the terrain SNOs; Objects/Logic still win on basename collisions.
        var resolver = new SiegeFX.Core.Assets.AssetResolver();
        if (_regionTerrainTankPath is not null)
        {
            try
            {
                _playTerrainTank = TankFile.Open(_regionTerrainTankPath);
                resolver.Add(new TankReader(_playTerrainTank), "Terrain.dsres");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Terrain.dsres open failed: {ex.Message} (interior prop textures may miss)");
            }
        }
        resolver.Add(objectsReader, "Objects.dsres");
        resolver.Add(logicReader,   "Logic.dsres");

        var spawner = new ActorSpawner(store, resolver, _regionLayout);
        var actors  = spawner.Spawn(allInstances);
        _actorRuntime = spawner.Runtime;
        _actorBus     = spawner.MessageBus;
        _triggerRuntime = spawner.TriggerRuntime;
        // Phase 10-SC-1 — load every region's special.gas (player region + neighbors)
        // through the same spawner so condition radii operate against the unified
        // world layout. trigger_generic placements never have aspect.model and were
        // previously dropped by Spawn() with a "missing model" diagnostic; this is
        // their real entry point. RegionObjects.LoadPlacements quietly returns empty
        // when special.gas is absent, so regions with no triggers cost one path probe.
        var triggerPlacementCount = 0;
        var triggerRegions = new List<string> { regionPath };
        if (_worldLayout is not null && _worldRegionGraphs.Count > 1)
        {
            foreach (var (path, _) in _worldRegionGraphs)
                if (path != regionPath) triggerRegions.Add(path);
        }
        foreach (var rp in triggerRegions)
        {
            var (placements, diags) =
                SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "special.gas");
            foreach (var d in diags) Console.WriteLine("  " + d);
            triggerPlacementCount += placements.Count;
            spawner.SpawnTriggers(placements);
        }
        // Phase 17-SC-G — emitter.gas placements ride the same trigger pipeline.
        // Their templates carry [template_triggers] rows whose action is
        // call_sfx_script("smoke_emitter") and whose condition is
        // receive_world_message("we_entered_world"). LoadStaticProps already
        // skips them (no aspect.model) so this is the only path that ever
        // registers them; we kick the message below so the rows fire.
        var emitterPlacementCount = 0;
        var legacyParticleCount = 0;
        foreach (var rp in triggerRegions)
        {
            var (placements, diags) =
                SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "emitter.gas");
            foreach (var d in diags) Console.WriteLine("  " + d);
            emitterPlacementCount += placements.Count;
            spawner.SpawnTriggers(placements);

            // Phase 17-SC-J — legacy raw [particle_emitter] block emitters
            // (emt_particle, emt_generic, …). They ship no [template_triggers]
            // so SpawnTriggers skips them; we route the per-instance block
            // straight to SfxRuntime as a persistent fire/smoke/steam column.
            // fh_r1's burning farmhouse is the canary: fire+smoke at the
            // doorway, dark/light columns from the same template family.
            legacyParticleCount += RegisterLegacyParticleEmitters(placements);
        }
        Console.WriteLine($"  triggers: {_triggerRuntime.Instances.Count} active matrices " +
                          $"from {triggerPlacementCount} special.gas + " +
                          $"{emitterPlacementCount} emitter.gas placements " +
                          $"({_triggerRuntime.Instances.Sum(t => t.Matrix.Rows.Count)} rows); " +
                          $"{legacyParticleCount} legacy [particle_emitter] columns");
        // Phase 17-SC-G — boot signal. Every shipped emitter (and many trigger
        // matrices in special.gas) authors `condition*=receive_world_message
        // ("we_entered_world")` to fire on world load. Post the message to
        // every registered SCID after spawn so those rows latch on tick 1.
        // PostInboundMessage is no-op for SCIDs without matching rows, so
        // broadcasting to all is safe and one-shot.
        foreach (var inst in _triggerRuntime.Instances)
            _triggerRuntime.PostInboundMessage(inst.Scid, "we_entered_world");
        _triggerCtx = new RenderHostTriggerContext(this);
        _templateStore = store;
        _pcontentResolver = new SiegeFX.Core.Actors.PcontentResolver(store);
        _playResolver = resolver;
        // Phase 21a-3 — promote spawner so OnPlayerRegionChanged can call
        // Spawn() again with the newly-loaded regions' actors. Track the
        // player's launch region as the initial _currentPlayerRegion; the
        // OnUpdate region check fires re-anchor the moment the player walks
        // out of it.
        _actorSpawner = spawner;
        _currentPlayerRegion = regionPath;

        // Phase 16a — load formulas.gas once. Combat regen, level-up math, and
        // future spell costs all key off this. Failure is non-fatal: shipped data
        // always has the file, but a heavily-modded install might not, and the
        // engine should still render the world without leveling.
        try { _formulas = SiegeFX.Core.Assets.FormulasStore.LoadFromTank(logicReader); }
        catch (Exception ex) { Console.WriteLine($"  formulas.gas load failed: {ex.Message} (regen disabled)"); }

        // Phase 20a — load the region's conversation pool. Region-scoped (one
        // gas file per region) so the dictionary stays small. Missing file is
        // graceful-fail: the dialogue panel just never opens.
        try
        {
            var (convs, convDiags) = SiegeFX.Core.Assets.ConversationStore.Load(mapReader, regionPath);
            foreach (var d in convDiags) Console.WriteLine("  " + d);
            // Build a mutable merged pool then assign (the field is IReadOnly).
            // First-loaded wins on key collisions: matches DS1's region-local
            // priority (cross-region dialogue goes through quest_state.gas).
            // Stored on _conversationsMutable too so Phase 21a-3's rolling
            // preload can append newly-loaded regions' dialogue trees in
            // place without breaking the IReadOnly view.
            var merged = new Dictionary<string, SiegeFX.Core.Assets.ConversationDef>(convs);
            int neighborConvs = 0;
            if (_worldRegionGraphs.Count > 1)
            {
                foreach (var (path, _) in _worldRegionGraphs)
                {
                    if (path == regionPath) continue;
                    try
                    {
                        var (nconvs, ndiags) = SiegeFX.Core.Assets.ConversationStore.Load(mapReader, path);
                        foreach (var d in ndiags) Console.WriteLine("  " + d);
                        foreach (var kv in nconvs)
                            if (merged.TryAdd(kv.Key, kv.Value)) neighborConvs++;
                    }
                    catch (Exception nex)
                    {
                        Console.WriteLine($"  neighbor conversations load failed for {path}: {nex.Message}");
                    }
                }
            }
            _conversationsMutable = merged;
            _conversations = merged;
            if (_conversations.Count > 0)
                Console.WriteLine($"  conversations: {_conversations.Count} dialogue tree(s) loaded" +
                                  (neighborConvs > 0 ? $" (+{neighborConvs} from neighbors)" : ""));
        }
        catch (Exception ex) { Console.WriteLine($"  conversations load failed: {ex.Message}"); }

        // Phase 20a (follow-up) — authored start position. The path
        // /world/maps/<map>/regions/<region> shares the prefix
        // /world/maps/<map>/info with start_positions.gas, so we walk back
        // up two segments from the region path to find it.
        try
        {
            var infoPath = DeriveMapInfoPath(regionPath);
            if (infoPath is not null)
            {
                var (groups, spDiags) = SiegeFX.Core.Assets.StartPositionsStore.Load(mapReader, infoPath);
                foreach (var d in spDiags) Console.WriteLine("  " + d);
                var def = SiegeFX.Core.Assets.StartPositionsStore.FindDefault(groups);
                if (def is not null && _regionLayout is not null
                    && _regionLayout.TryGetTransform(def.NodeGuid, out var nodeWorld))
                {
                    var world = Vector3.Transform(def.LocalPosition, nodeWorld);
                    _authoredSpawn = world;
                    Console.WriteLine(
                        $"  start position: default group, node 0x{def.NodeGuid:x8}, " +
                        $"world ({world.X:F2}, {world.Y:F2}, {world.Z:F2})");
                }
                else if (def is not null)
                {
                    Console.WriteLine(
                        $"  start position: default group resolved but node 0x{def.NodeGuid:x8} " +
                        $"not in this region's layout — falling back to centroid spawn");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"  start position load failed: {ex.Message}"); }

        // Phase 17a — build the spell catalog from the same template store. Cheap
        // (~150 instant-hit templates filter out of ~7300 total) and one-shot.
        // Failure here just leaves _spellCatalog null; cast attempts then no-op
        // through the null guard in TryClickToCast.
        try
        {
            _spellCatalog = SiegeFX.Core.Assets.SpellCatalog.Build(store);
            Console.WriteLine($"  spell catalog: {_spellCatalog.Count} instant-hit spells loaded");
        }
        catch (Exception ex) { Console.WriteLine($"  spell catalog build failed: {ex.Message}"); }

        // Phase 21-SC-SCROLL-A-1 — pre-cache every catalog spell's
        // inventory-grid scroll icon so the first time a player drags a
        // spell out of the spellbook, the cursor scroll renders without
        // a frame-stutter from on-demand RAW decode + GL upload. Two
        // disjoint caches in play and BOTH need warming:
        //   - _itemIconCache (TryGetItemIcon) — keyed by template name;
        //     used by InventoryPanel render when a scroll item lands in
        //     the inventory grid (SC-SCROLL-D drop).
        //   - _guiTextureCache (TryGetGuiTexture) — keyed by RAW basename;
        //     used by ResolveSpellInventoryIcon for spellbook + cursor
        //     overlay (PRE-1 / B / C drag flows).
        // A-1's first commit only warmed the former; the cursor-overlay
        // path missed the cache. Reviewer caught it; folding here.
        // ~69 textures × ~4KB compressed × 2 paths = still trivial cost.
        if (_spellCatalog is not null)
        {
            int itemCached = 0, guiCached = 0;
            foreach (var spell in _spellCatalog.All)
            {
                if (TryGetItemIcon(spell.Name) is not null) itemCached++;
                if (ResolveSpellInventoryIcon(spell) is not null) guiCached++;
            }
            Console.WriteLine($"  spell icons: pre-cached {itemCached}/{_spellCatalog.Count} via item-cache, " +
                              $"{guiCached}/{_spellCatalog.Count} via gui-cache");
        }

        // Phase 18a — bootstrap the audio engine and pre-register the two
        // shipped cast SFX. Sound.dsres lives next to Logic.dsres in the
        // Resources folder; if it's missing (very-stripped install) we
        // silently skip — TryCreate / RegisterClip never throw upward.
        try
        {
            string soundTankPath = Path.Combine(Path.GetDirectoryName(logicTankPath) ?? "",
                                                "Sound.dsres");
            if (File.Exists(soundTankPath))
            {
                _playSoundTank = TankFile.Open(soundTankPath);
                var soundReader = new TankReader(_playSoundTank);
                _audio = SiegeFX.Audio.AudioEngine.TryCreate();
                // Phase 22-SC-MUSIC-B — share the OpenAL context with the
                // music player. Null-tolerant: TryCreate returns null when
                // _audio itself failed (no device), and every PlayMusicTrack
                // call below null-checks so the runtime stays playable
                // without music.
                _music = SiegeFX.Audio.MusicPlayer.TryCreate(_audio);
                if (_audio is not null)
                {
                    // Phase 21d-2a-xii — load DS1's SED (sound effect descriptor)
                    // registry up-front so each TryRegisterSfx call below picks
                    // up its authored playback-rate range. Failure leaves
                    // _sedStore null and TryRegisterSfx falls back to unity
                    // pitch — same behavior as pre-xii.
                    try
                    {
                        var (seds, sedDiags) = SiegeFX.Core.Assets.SedStore.Load(soundReader);
                        _sedStore = seds;
                        Console.WriteLine($"  audio: SED registry loaded — {seds.Count} descriptors");
                        foreach (var d in sedDiags) Console.Error.WriteLine($"  sed: {d}");
                    }
                    catch (Exception sex)
                    {
                        Console.Error.WriteLine($"  SED store load failed: {sex.Message}");
                    }

                    TryRegisterSfx(soundReader, SfxZapCast,
                        "/sound/effects/s_e_spell_zap_cast.wav");
                    TryRegisterSfx(soundReader, SfxHealingWindCast,
                        "/sound/effects/s_e_spell_healing_wind_cast.wav");

                    // Phase 18b — combat-feedback library. Swings + hits ship
                    // as 4-5 wav variants each so a sustained fight doesn't
                    // sound like one .wav looping; we group them and let
                    // AudioEngine pick at random per Play() call.
                    TryRegisterSfx(soundReader, "swing_01", "/sound/effects/s_e_swing_01.wav");
                    TryRegisterSfx(soundReader, "swing_02", "/sound/effects/s_e_swing_02.wav");
                    TryRegisterSfx(soundReader, "swing_03", "/sound/effects/s_e_swing_03.wav");
                    TryRegisterSfx(soundReader, "swing_04", "/sound/effects/s_e_swing_04.wav");
                    _audio.RegisterGroup(SfxMeleeSwingGroup,
                        "swing_01", "swing_02", "swing_03", "swing_04");

                    // Phase 9-SC-8 — material-keyed hit cues lazy-registered on first
                    // swing-that-lands; see EnsureHitGroupRegistered. Replaces the old
                    // unconditional steelsword-only block that masqueraded as "hit_flesh_*".
                    TryRegisterSfx(soundReader, SfxMeleeMiss, "/sound/effects/s_e_miss_melee.wav");
                    TryRegisterSfx(soundReader, SfxLevelUp,   "/sound/effects/s_e_level_up_melee.wav");

                    // Phase 21d-2a-ix — GUI cue triplet (see Sfx const block above).
                    TryRegisterSfx(soundReader, SfxGuiInventory, "/sound/effects/s_e_gui_inventory_sheet.wav");
                    TryRegisterSfx(soundReader, SfxGuiPickup,    "/sound/effects/s_e_gui_pick_up.wav");
                    TryRegisterSfx(soundReader, SfxGuiPutDownScroll, "/sound/effects/s_e_gui_put_down_scroll.wav");
                    TryRegisterSfx(soundReader, SfxGuiOutOfMana, "/sound/effects/s_e_gui_out_of_mana.wav");

                    // Phase 9-SC-2 — death cues lazy-registered on first kill,
                    // sourced from each template's [aspect][voice][die] `*`.
                    // No per-species table here.

                    // Phase 21d-2a-xi — load mood definitions, then register
                    // the looping ambient beds shipped under /sound/effects/
                    // s_e_ambient_*. We pre-register every distinct
                    // ambient_track value referenced by any mood so the
                    // SetAmbientBed swap-on-region-change doesn't need a
                    // file IO step in the playline. Skipping silently if
                    // moods didn't parse — bed stays silent rather than
                    // crashing the player out of a fresh load.
                    try
                    {
                        var (moods, moodDiags) = SiegeFX.Core.Assets.MoodStore.Load(logicReader);
                        _moodStore = moods;
                        _moodMapName = DeriveMapName(_regionPath);
                        int regBeds = 0;
                        foreach (var track in CollectDistinctAmbientTracks(moods))
                        {
                            if (TryRegisterSfx(soundReader, track, $"/sound/effects/{track}.wav"))
                                regBeds++;
                        }
                        Console.WriteLine($"  audio: mood store loaded — {moods.Count} moods, " +
                                          $"{regBeds} distinct ambient bed clip(s) registered " +
                                          $"(map='{_moodMapName ?? "<unknown>"}')");
                        foreach (var d in moodDiags) Console.Error.WriteLine($"  mood: {d}");
                    }
                    catch (Exception mex)
                    {
                        Console.Error.WriteLine($"  mood store load failed: {mex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"  audio: Sound.dsres not at {soundTankPath} — SFX disabled");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"  audio init failed: {ex.Message}"); }

        // Phase 21d-2a-xi — kick the looping ambient bed for the launch region
        // now that mood store + clip registrations have completed. Subsequent
        // region crossings get re-applied via OnPlayerRegionChanged below.
        ApplyAmbientForRegion(_regionPath);

        // Phase 15a — load DS1's small UI font and hand it to the text overlay.
        // copperplate-light is the body font DS1 uses for HP/MP readouts and
        // tooltip text; it covers ASCII 0x20..0x7F at 12px which is plenty for
        // a debug "SiegeFX [coords]" tag. Silent no-op if the font's missing
        // (modded installs that strip the GUI tank still get a working scene).
        if (_textRenderer is not null)
        {
            var font = SiegeFX.Core.Assets.BitmapFont.TryLoadByName(
                resolver, "b_gui_fnt_12p_copperplate-light");
            if (font is not null)
            {
                _textRenderer.SetFont(font);
                Console.WriteLine($"  hud font: {font.Name} ({font.Atlas.Width}x{font.Atlas.Height} atlas, " +
                                  $"glyphs {font.StartRange:X2}..{font.EndRange:X2})");
            }
            else
            {
                Console.Error.WriteLine("  !! hud font missing — overlay text disabled");
            }
        }

        // Phase 11d — build a region-scope nav mesh once and hand a follower to every
        // actor that spawns over a walkable triangle. We reuse the terrain tank already
        // opened at LoadRegion time but re-resolve SNOs into our own cache to keep the
        // nav mesh's lifetime decoupled from the render-mesh cache.
        SiegeFX.Core.Nav.NavMesh? navMesh = null;
        if (_regionTerrainTankPath is not null && _regionLayout is not null)
        {
            try
            {
                using var terrainTank = TankFile.Open(_regionTerrainTankPath);
                var terrainReader = new TankReader(terrainTank);
                var meshIdx = SnoMeshIndex.Build(terrainReader);
                var navCache = new Dictionary<uint, SnoModel?>();
                SnoModel? ResolveNav(uint meshGuid)
                {
                    if (navCache.TryGetValue(meshGuid, out var hit)) return hit;
                    SnoModel? m = null;
                    if (meshIdx.TryResolve(meshGuid, out var p))
                    {
                        try { m = SnoModel.Load(terrainReader.ExtractToMemory(p)); }
                        catch { m = null; }
                    }
                    navCache[meshGuid] = m;
                    return m;
                }
                // Phase 21a-2 — when neighbors were preloaded, use the unified
                // RegionGraph (player + neighbors) so the nav mesh spans the
                // boundary. Vertex welding inside NavMesh.BuildForRegion uses
                // world-space positions (transforms here are already world-space
                // via _worldLayout), so adjacent regions' floor edges weld
                // together where they touch.
                RegionGraph navGraph;
                if (_worldRegionGraphs.Count > 1)
                {
                    var graphList = new List<RegionGraph>(_worldRegionGraphs.Count);
                    foreach (var (_, g) in _worldRegionGraphs) graphList.Add(g);
                    navGraph = RegionGraph.Combine(graphList);
                }
                else
                {
                    navGraph = RegionGraph.Load(mapReader.ExtractToMemory(regionPath + "/terrain_nodes/nodes.gas"));
                }
                navMesh = SiegeFX.Core.Nav.NavMesh.BuildForRegion(navGraph, _regionLayout, ResolveNav);
                Console.WriteLine($"  nav mesh: {navMesh.TriangleCount} tri(s), " +
                                  $"{navMesh.Vertices.Length} welded vert(s), " +
                                  $"{navMesh.SourceSnodeCount} snode(s), " +
                                  $"{navMesh.NonManifoldEdgeCount} non-manifold edge(s)" +
                                  (_worldRegionGraphs.Count > 1
                                       ? $" — unified across {_worldRegionGraphs.Count} region(s)"
                                       : ""));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  !! nav mesh build failed: {ex.Message} — actors will stand still");
                navMesh = null;
            }
        }

        int actorsOnMesh = 0;
        int actorsOffMesh = 0;
        foreach (var actor in actors)
        {
            if (!_actorMeshCache.TryGetValue(actor.Mesh, out var gl))
            {
                gl = new SkinnedMesh(_gl, actor.Mesh);
                _actorMeshCache[actor.Mesh] = gl;
            }

            SiegeFX.Core.Actors.ActorBrain? brain = null;
            // Phase 20a (follow-up) — actors with a [conversation] block are
            // talkable NPCs (narrator, edgaar, norick). DS1 only ticks their
            // AI inside NIS cutscenes; outside that, they stand still and
            // wait for an RMB. We skip the brain entirely so they don't
            // wander up and start swinging at the player from a default
            // attack template.
            bool isTalkable = SiegeFX.Core.Assets.ConversationStore
                .KeysFromInstance(actor.Instance.Node).Count > 0;
            if (isTalkable)
            {
                actorsOffMesh++;
            }
            else if (navMesh is not null &&
                navMesh.TryFindTriangle(actor.WorldTransform.Translation, out var startTri))
            {
                // Snap the starting Y to the mesh surface so the first tick doesn't
                // lift the actor up off a ramp or drop it through a floor.
                var snapped = actor.WorldTransform.Translation with
                {
                    Y = navMesh.SampleYOnTriangle(startTri, actor.WorldTransform.Translation),
                };
                // Scid makes the per-actor RNG deterministic across runs so two launches
                // on the same region play out the same.
                //
                // Phase 13e — speed from template.body.avg_move_velocity (ActorStats.WalkSpeed).
                // Krug scouts amble at 3.1u/s, chickens scurry at ~1.9, butterflies float fast.
                // A template that didn't resolve a gait falls back to ActorStats's own 4u/s
                // default. Clamp non-positive values (dead/prop templates) up to 0.5 so the
                // follower still moves visibly when the author left a zero in by mistake.
                //
                // Authored facing = Actor.WorldTransform's local +Z direction in world
                // space (DS1 convention — characters look down +Z in their local frame).
                // Extracted with a forward-vector transform, then projected to XZ by
                // the follower ctor so actors stalled at spawn don't snap to +Z.
                var authoredFacing = Vector3.TransformNormal(Vector3.UnitZ, actor.WorldTransform);
                var gait = actor.Stats.WalkSpeed > 0.5f ? actor.Stats.WalkSpeed : 4f;
                var follower = new SiegeFX.Core.Actors.ActorFollower(
                    navMesh, snapped, speed: gait, rngSeed: (int)actor.Instance.Scid,
                    initialFacing: authoredFacing);
                // Phase 16c — wrap the wander follower in a brain so combatants
                // can chase + swing at the player. Non-combatants (chickens) keep
                // the same brain class but never receive a target during Tick, so
                // their state machine stays parked in Wander.
                brain = new SiegeFX.Core.Actors.ActorBrain(
                    follower, actor.Stats, rngSeed: (int)actor.Instance.Scid ^ unchecked((int)0xA17ACC1Eu),
                    selfActor: actor);
                actorsOnMesh++;
            }
            else
            {
                actorsOffMesh++;
            }

            _actors.Add(new ActorRenderState
            {
                Actor             = actor,
                GlMesh            = gl,
                AnimTime          = 0,
                LastClipIndex     = actor.CurrentClipIndex,
                Brain             = brain,
                CurrentTransform  = actor.WorldTransform,
            });
        }

        if (navMesh is not null)
        {
            Console.WriteLine($"  followers: {actorsOnMesh} wandering / {actorsOffMesh} pinned (off-mesh spawn)");
        }

        // Phase 13c — remember the nav mesh for the LMB raycast handler. We always
        // set it (even to null) so mid-session mode changes can't leave a stale
        // reference from a previous region.
        _navMesh = navMesh;

        // Phase 13a — spawn one Farmboy PC next to the NPC centroid so he lands on the
        // nav mesh among the goblins. AI-idle until 13c/13d wire LMB/RMB input — the
        // render loop already tolerates a null follower by falling back to CurrentTransform.
        TrySpawnPlayer(spawner, navMesh);

        // Frame the camera on the player (13a) if we spawned one, else fall back to the
        // NPC centroid. Lift + pull back by the region-anchor radius we already computed
        // in LoadRegion (camera position is set there); overwrite here so "play-region"
        // always frames the actors, not the raw terrain anchor.
        var framingTarget = _player?.CurrentTransform.Translation;
        if (framingTarget is null && _actors.Count > 0)
        {
            var centroid = Vector3.Zero;
            foreach (var s in _actors) centroid += s.Actor.WorldTransform.Translation;
            centroid /= _actors.Count;
            framingTarget = centroid;
        }
        if (framingTarget is Vector3 f)
        {
            _camera.Position = f + new Vector3(0, 15f, 25f);
            _camera.Yaw = 0;
            _camera.Pitch = -0.35f;
        }

        // Diagnostic: actor world-position bounds + how many resolved a node transform vs
        // fell back to node-local identity. A large chunk falling back means NodeGuids in
        // actor.gas aren't matching the region's snode guids — actors would then pile at
        // the scene origin while terrain sprawls outward.
        int nodeResolved = 0, nodeFallback = 0;
        Vector3 pmin = new(float.PositiveInfinity), pmax = new(float.NegativeInfinity);
        var layoutForLog = _regionLayout;
        foreach (var a in actors)
        {
            if (layoutForLog is not null && layoutForLog.TryGetTransform(a.Instance.Placement.NodeGuid, out _)) nodeResolved++;
            else nodeFallback++;
            var p = a.WorldTransform.Translation;
            pmin = Vector3.Min(pmin, p);
            pmax = Vector3.Max(pmax, p);
        }
        Console.WriteLine($"play-region '{regionPath}': {actors.Count}/{instances.Count} actor(s) live, " +
                          $"{_actorMeshCache.Count} unique mesh(es), " +
                          $"{spawner.Diagnostics.Count} diagnostic(s)");
        Console.WriteLine($"  actor node xforms: resolved {nodeResolved} / fallback-to-local {nodeFallback}");
        Console.WriteLine($"  actor world bounds: {pmin} .. {pmax}  (extent {pmax - pmin})");
        if (storeDiags.Count > 0) Console.WriteLine($"  templates: {storeDiags.Count} diagnostic(s)");
        if (instDiags.Count  > 0) Console.WriteLine($"  instances: {instDiags.Count} diagnostic(s)");
        foreach (var d in spawner.Diagnostics.Take(5)) Console.WriteLine($"  !! {d}");
        if (spawner.Diagnostics.Count > 5) Console.WriteLine($"  ... ({spawner.Diagnostics.Count - 5} more)");

        // Phase 21c — populate the static-prop layer (trees, barrels, fences,
        // chairs, candles, foliage, crops, etc.) across the player region and
        // every preloaded neighbor. Without this DS1 regions render as bare
        // terrain dotted with NPCs — the densification this pass adds is what
        // makes a region feel like the original game.
        var allLoaded = new List<string> { regionPath };
        if (_worldRegionGraphs.Count > 1)
        {
            foreach (var (path, _) in _worldRegionGraphs)
                if (path != regionPath) allLoaded.Add(path);
        }
        LoadStaticProps(allLoaded);

        // Phase 20a (follow-up) — print every talkable NPC's name + world
        // position so the visual walkthrough doesn't require hunting the
        // map. Anything with a [conversation] block whose first key resolves
        // in the conversation pool counts.
        if (_conversations is not null && _conversations.Count > 0)
        {
            var talkables = new List<(string Name, string Key, Vector3 Pos)>();
            foreach (var s in _actors)
            {
                if (s.IsPlayer) continue;
                var keys = SiegeFX.Core.Assets.ConversationStore.KeysFromInstance(s.Actor.Instance.Node);
                if (keys.Count == 0) continue;
                string? firstKey = null;
                foreach (var k in keys)
                {
                    if (_conversations.TryGetValue(k, out var hit) && hit.Nodes.Count > 0)
                    { firstKey = k; break; }
                }
                if (firstKey is null) continue;
                var name = _templateStore?.GetAttribute(s.Actor.Template, "common", "screen_name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    name = name.Trim();
                    if (name.Length >= 2 && name[0] == '"' && name[^1] == '"') name = name[1..^1];
                }
                if (string.IsNullOrWhiteSpace(name)) name = s.Actor.Template.Name;
                talkables.Add((name, firstKey, s.CurrentTransform.Translation));
            }
            if (talkables.Count > 0)
            {
                Console.WriteLine($"  talkable NPCs ({talkables.Count}):");
                foreach (var t in talkables)
                    Console.WriteLine(
                        $"    {t.Name,-18} {t.Key,-40} world ({t.Pos.X:F1}, {t.Pos.Y:F1}, {t.Pos.Z:F1})");
            }
        }
    }

    /// <summary>Phase 21a-3 — fired from <see cref="OnUpdate"/> when the player's
    /// nearest snode lies in a region different from <see cref="_currentPlayerRegion"/>.
    /// Extends the loaded ring around <paramref name="newRegion"/> via
    /// <see cref="PreloadAroundRegion"/>, then for each newly-loaded region: loads
    /// its actor instances, merges its conversation pool, and attaches its actors
    /// to the render/tick lists. Finally, rebuilds the nav mesh against the now-
    /// larger unified scope and re-points the player's NavFollower at it so the PC
    /// can walk into the freshly-streamed terrain. World coordinates of every
    /// already-spawned actor (including the player) are preserved because
    /// <see cref="_worldRootRegion"/> stays pinned across re-anchors.</summary>
    private void OnPlayerRegionChanged(string newRegion)
    {
        if (_actorSpawner is null || _gl is null) return;
        if (_regionMapTankPath is null) return;
        if (_playMapTank is null) return; // play-region mode required

        // Phase 21-SC-SCROLL-C-2 — abort any in-flight scroll drag on
        // region change. The drag's source slot was already cleared on
        // pickup; ClearScrollDrag drops the cursor scroll without
        // restoration. CancelScrollDrag would restore the spell, but a
        // region cross is rare enough that "lose the drag" is the
        // simpler-to-reason-about outcome (the player can re-pick up
        // from the source slot, which is now empty in their book).
        // Net: spell A goes back to having "no slot" — user can re-add
        // via SC-SCROLL-E once that lands. Until then, cancel-restore
        // would be more user-friendly; revisit when E ships.
        if (_cursorScroll is not null) CancelScrollDrag();

        var prev = _currentPlayerRegion;
        _currentPlayerRegion = newRegion;
        // Phase 21d-2a-xi — refresh the looping bed first so the soundscape
        // catches up with the new region even when streaming is a no-op.
        ApplyAmbientForRegion(newRegion);

        var newlyLoaded = PreloadAroundRegion(newRegion);
        if (newlyLoaded.Count == 0)
        {
            // Player walked into an already-loaded region — no streaming work to
            // do, just remember we're there so the next outward step can fire.
            Console.WriteLine($"  region change: {prev} -> {newRegion} (already loaded; no preload needed)");
            return;
        }

        Console.WriteLine($"  region change: {prev} -> {newRegion} (+{newlyLoaded.Count} region(s) streamed)");

        // Re-point the spawner + region layout at the new world transforms so
        // newly-spawned actors compose against the unified table. Spawner's
        // mesh/clip/skrit caches are keyed by name, not layout, so re-pointing
        // doesn't invalidate them.
        if (_worldLayout is not null && _regionLayout is not null)
        {
            _regionLayout = SiegeFX.Core.Assets.RegionLayout.FromTransforms(
                _regionLayout.AnchorGuid, _worldLayout.Transforms);
            _actorSpawner.Layout = _regionLayout;
        }

        // Load each new region's actors + conversations. We reuse the pinned
        // _playMapTank reader (the same tank we opened in LoadPlayActors).
        var mapReader = new TankReader(_playMapTank);
        var newInstances = new List<SiegeFX.Core.Assets.ActorInstance>();
        int convsAdded = 0;
        foreach (var rp in newlyLoaded)
        {
            try
            {
                var (more, _) = SiegeFX.Core.Assets.RegionObjects.LoadActors(mapReader, rp);
                newInstances.AddRange(more);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  rolling actors load failed for {rp}: {ex.Message}");
            }

            if (_conversationsMutable is not null)
            {
                try
                {
                    var (nconvs, _) = SiegeFX.Core.Assets.ConversationStore.Load(mapReader, rp);
                    foreach (var kv in nconvs)
                        if (_conversationsMutable.TryAdd(kv.Key, kv.Value)) convsAdded++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  rolling conversations load failed for {rp}: {ex.Message}");
                }
            }
        }

        // Rebuild the nav mesh against the full unified scope so newly-loaded
        // floor tris weld to the original ring. Existing NPC followers keep
        // their old mesh reference (they only wander locally so loss of the
        // newest tris is fine); the player follower is reseated below so the
        // PC can walk onto the new terrain.
        var newNav = RebuildNavMesh();
        if (newNav is not null) _navMesh = newNav;

        // Spawn new actors and attach them to the render/tick lists with the
        // new nav mesh so their wander followers see the freshly-streamed floor.
        var newActors = _actorSpawner.Spawn(newInstances);
        var (onMesh, offMesh) = AttachActorsToScene(newActors, newNav);

        // Re-create the player's NavFollower against the new unified mesh so
        // the next click can route across into the new terrain. Position +
        // speed are preserved from the live follower.
        if (newNav is not null && _player is not null && _playerFollower is not null)
        {
            var pos = _playerFollower.Position;
            var speed = _playerFollower.Speed;
            _playerFollower = new SiegeFX.Core.Nav.NavFollower(newNav, pos, speed);
        }

        Console.WriteLine($"  rolling spawn: {newActors.Count}/{newInstances.Count} actor(s) live " +
                          $"({onMesh} wandering / {offMesh} pinned)" +
                          (convsAdded > 0 ? $", +{convsAdded} dialogue tree(s)" : ""));

        // Phase 21c — densify the freshly-streamed regions with their static
        // props too. Existing props from previously-loaded regions stay in
        // _staticProps untouched (world coords are pinned).
        LoadStaticProps(newlyLoaded);
    }

    /// <summary>Phase 21c — resolve and cache the albedo texture for an actor or
    /// prop ASP. The mesh's first BMSH text token is a *texset base*; for many
    /// props and weapons it doubles as the literal .raw filename, but actor
    /// meshes (krug, phrak, gremal, ...) name only the base and ship a swarm of
    /// variant files (<c>b_c_eam_ksc-01.raw</c>, <c>-02.raw</c>, <c>-dk-01.raw</c>)
    /// that DS1 picks from at spawn time. We try the direct file first, then
    /// fall through canonical variant suffixes (<c>-01</c>..<c>-08</c>) so the
    /// actor renders with the first available skin instead of the untextured
    /// tan fallback. Picking is deterministic per mesh (lowest variant wins) so
    /// every instance of an archetype currently shares one skin — cheap
    /// per-spawn variety is a follow-up. Misses are memoized so a missing texset
    /// doesn't re-scan the tank index every frame.</summary>
    private GlTexture? ResolveAspTexture(AspMesh mesh)
    {
        if (_aspTextureCache.TryGetValue(mesh, out var cached)) return cached;
        var tex = LoadTexsetTexture(mesh.TextureNames.Count > 0 ? mesh.TextureNames[0] : null);
        _aspTextureCache[mesh] = tex;
        return tex;
    }

    /// <summary>Phase 21c-4 — actor-aware overload. Walks the template's specializes
    /// chain for an <c>[aspect][textures] { 0 = ...; }</c> override and uses that
    /// texset name in preference to the mesh's BMSH-stored default. krug_grunt and
    /// krug_scout both ship pose mesh <c>m_c_eam_kg_pos_1.asp</c> (whose BMSH says
    /// <c>b_c_eam_ksc</c>) but each template overrides slot 0 to its own skin
    /// (<c>b_c_eam_kg</c> / <c>b_c_eam_ksc</c>); without the override, every actor
    /// rendered with the scout texset. Cache key is (template, mesh) so two actors
    /// of the same archetype share one GL upload.</summary>
    /// <summary>Phase 21d-2a-v — subset diagnostic palette. 8 hand-picked
    /// hues kept saturated and far apart in HSV so adjacent subsets are easy
    /// to tell on the rendered model. Wraps for meshes with more than 8
    /// subsets (none in shipping DS1 — farmboy = 5, krug = 1, max observed = 7).</summary>
    private static (float R, float G, float B) SubsetTintFor(int subsetIndex)
    {
        return (subsetIndex % 8) switch
        {
            0 => (1.00f, 0.00f, 0.00f), // red
            1 => (0.00f, 1.00f, 0.00f), // green
            2 => (0.20f, 0.40f, 1.00f), // blue
            3 => (1.00f, 1.00f, 0.00f), // yellow
            4 => (1.00f, 0.00f, 1.00f), // magenta
            5 => (0.00f, 1.00f, 1.00f), // cyan
            6 => (1.00f, 0.55f, 0.00f), // orange
            _ => (0.65f, 0.20f, 0.85f), // purple
        };
    }

    private GlTexture? ResolveActorTexture(SiegeFX.Core.Actors.Actor actor, int textureIndex)
    {
        // 21d-2a-viii-FE-2 — preview hero (the live 3D char in the creator's
        // listener rect) gets its own override branch parallel to the player's.
        // Same slot semantics: 0 = skin (face/hair/arms region), 1 = clothing
        // strip. Equipment overrides aren't relevant here — the preview hero
        // never wears authored equipment, the variant pick is the whole story.
        if (_heroPreview is not null && _heroPreview.Actor is not null
            && ReferenceEquals(actor, _heroPreview.Actor))
        {
            if (textureIndex == 0 && _heroPreview.SkinOverrideName is not null)
                return LoadEquipmentTexture("__preview_skin__", _heroPreview.SkinOverrideName);
            if (textureIndex == 1 && _heroPreview.PantsOverrideName is not null)
                return LoadEquipmentTexture("__preview_pants__", _heroPreview.PantsOverrideName);
        }
        // Player overrides — checked in priority order so equipment beats the
        // creator pick that would otherwise show through. Only the player ever
        // has these fields set; NPCs fall through to the texset path below.
        if (_player is not null && ReferenceEquals(actor, _player.Actor))
        {
            // 21d-2a-vii — equipped chest armor wins over the body's authored
            // clothing strip AND the creator's pants pick.
            if (textureIndex == 1 && _chestTexOverrideName is not null)
                return LoadEquipmentTexture("__chest__", _chestTexOverrideName);

            // 21d-2a-viii — character creator picks. Slot 0 is the body's
            // face/hair/arms region; slot 1 is the clothing strip below it.
            // Routed through _equipTexCache (slot-keyed by '__skin__' /
            // '__pants__') so swapping picks mid-session re-uses prior uploads.
            if (textureIndex == 0 && _skinTexOverrideName is not null)
                return LoadEquipmentTexture("__skin__", _skinTexOverrideName);
            if (textureIndex == 1 && _pantsTexOverrideName is not null)
                return LoadEquipmentTexture("__pants__", _pantsTexOverrideName);
        }
        var key = (actor.Template, actor.Mesh, textureIndex);
        if (_actorTextureCache.TryGetValue(key, out var cached)) return cached;
        string? baseName = null;
        string? source = null;
        if (_templateStore is not null)
        {
            // Templates can override any slot — DS1 uses `[aspect][textures] { 0 = ... }`
            // for body retexturing (krug variants) and may also override slot 1+ for
            // characters like farmboy whose clothing strip is a separate subset.
            var slotKey = textureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            baseName = _templateStore.GetAttribute(actor.Template, "aspect", "textures", slotKey);
            if (!string.IsNullOrEmpty(baseName)) source = $"template[aspect][textures][{slotKey}]";
        }
        if (string.IsNullOrEmpty(baseName) && textureIndex >= 0 && textureIndex < actor.Mesh.TextureNames.Count)
        {
            baseName = actor.Mesh.TextureNames[textureIndex];
            source = $"mesh.TextureNames[{textureIndex}]";
        }
        var tex = LoadTexsetTexture(baseName);
        _actorTextureCache[key] = tex;
        // Phase 21d-2a-v — one-shot diagnostic per (template, slot). Logs the
        // resolved texset basename + whether it found a .raw, so we can see
        // for the player exactly which texture slot 1 binds to. Cache means
        // this fires once per slot per template-instance.
        if (Environment.GetEnvironmentVariable("SIEGEFX_TEX_RESOLVE_LOG") == "1")
        {
            var status = tex is null ? "MISS" : "OK";
            Console.WriteLine($"[tex-resolve {status}] tpl={actor.Template.Name} mesh={actor.Mesh.MeshName} slot={textureIndex} base={baseName ?? "<null>"} src={source ?? "<none>"}");
        }
        return tex;
    }

    private GlTexture? LoadTexsetTexture(string? baseName)
    {
        if (_gl is null || _playResolver is null || string.IsNullOrEmpty(baseName)) return null;
        byte[]? texBytes = null;
        if (_playResolver.TryLoadByBasename(baseName + ".raw", out var direct))
            texBytes = direct;
        else
        {
            for (int i = 1; i <= 8 && texBytes is null; i++)
            {
                if (_playResolver.TryLoadByBasename($"{baseName}-{i:D2}.raw", out var v))
                    texBytes = v;
            }
        }
        if (texBytes is null) return null;
        try { return new GlTexture(_gl, RawImage.Load(texBytes)); }
        catch { return null; }
    }

    // Phase 21-SC-INV-A — cache for one-off GUI textures pulled by basename.
    // Used for the per-panel close-button raws (b_gui_ig_mnu_minimize-up,
    // b_gui_ig_mnu_minimize-book-up). Survives panel toggles so we don't
    // re-decode the .raw every time the user opens a pane.
    private readonly Dictionary<string, GlTexture?> _guiTextureCache =
        new(StringComparer.OrdinalIgnoreCase);
    private GlTexture? TryGetGuiTexture(string baseName)
    {
        if (_gl is null || _playResolver is null || string.IsNullOrEmpty(baseName)) return null;
        if (_guiTextureCache.TryGetValue(baseName, out var cached)) return cached;
        GlTexture? tex = null;
        if (_playResolver.TryLoadByBasename(baseName + ".raw", out var bytes))
        {
            try { tex = new GlTexture(_gl, RawImage.Load(bytes)); }
            catch { tex = null; }
        }
        _guiTextureCache[baseName] = tex;
        return tex;
    }

    // Phase 21-SC-SPELL-A — resolver passed to SpellBookPanel.Draw so each
    // owned/active spell row can render its template's b_gui_ig_i_ic_sp_*_inv
    // icon. Returns null when SpellTemplate.InventoryIcon is empty (creator
    // / stub spells); the panel falls back to its element-tinted glyph in
    // that case.
    private GlTexture? ResolveSpellInventoryIcon(SiegeFX.Core.Assets.SpellTemplate spell)
        => string.IsNullOrEmpty(spell.InventoryIcon)
            ? null
            : TryGetGuiTexture(spell.InventoryIcon);

    /// <summary>Phase 21c — spawn the static-prop layer for the player region and
    /// every preloaded neighbor. Walks each region's <see cref="RegionObjects.StaticPropFiles"/>,
    /// looks up <c>aspect.model</c> off the template, loads the .asp +
    /// .raw albedo, and bakes a world transform via <see cref="_regionLayout"/>.
    /// Skips placements whose template carries no aspect.model (logic-only
    /// templates — triggers, generators, sound emitters that landed in
    /// non_interactive). One-shot at LoadPlayActors; re-fires from
    /// OnPlayerRegionChanged when new regions stream in.</summary>
    private void LoadStaticProps(IEnumerable<string> regionPaths)
    {
        if (_gl is null || _playResolver is null || _templateStore is null
            || _regionLayout is null || _playMapTank is null) return;

        // Phase 21c-3 — refresh global lighting from the player region's
        // lights.gas before populating props so the very first prop pass uses
        // the right colors. Re-fired by OnPlayerRegionChanged when the player
        // crosses into a neighbor with different time-of-day baking.
        var firstRegion = regionPaths.FirstOrDefault();
        if (firstRegion is not null) LoadRegionLighting(firstRegion);

        var mapReader = new TankReader(_playMapTank);
        int considered = 0, spawned = 0, missingTemplate = 0, missingModel = 0,
            missingMesh = 0, parseFail = 0, scaledPlacements = 0;
        // Group skips by template (and missing meshes by model name) so the diag
        // surfaces which content is silently absent — the user should not have
        // to spot missing trees by eye.
        var skippedNoTemplate = new SortedDictionary<string, int>();
        var skippedNoModel    = new SortedDictionary<string, int>();
        var skippedNoMesh     = new SortedDictionary<string, int>();
        // Per-template counts of placements with vs without a resolved albedo
        // texture. A row with `0/N textured` is a sure sign the texture .raw
        // didn't resolve via the play-mode tanks — the prop renders with the
        // shader's neutral-tan fallback color and looks like a featureless block.
        var texturedPerTpl = new SortedDictionary<string, (int textured, int untextured, string? texName)>();

        foreach (var rp in regionPaths)
        {
            foreach (var fileName in SiegeFX.Core.Assets.RegionObjects.StaticPropFiles)
            {
                var (placements, diags) =
                    SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, fileName);
                foreach (var d in diags) Console.WriteLine("  " + d);

                foreach (var p in placements)
                {
                    considered++;
                    if (!_templateStore.TryGet(p.TemplateName, out var template))
                    {
                        missingTemplate++;
                        skippedNoTemplate.TryGetValue(p.TemplateName, out var n);
                        skippedNoTemplate[p.TemplateName] = n + 1;
                        continue;
                    }
                    var modelName = _templateStore.GetAttribute(template, "aspect", "model");
                    if (string.IsNullOrEmpty(modelName))
                    {
                        // Many entries in non_interactive.gas / special.gas reference
                        // pure-logic templates (sound emitters, triggers) that have no
                        // mesh on purpose. Counted but not loud.
                        missingModel++;
                        skippedNoModel.TryGetValue(p.TemplateName, out var n);
                        skippedNoModel[p.TemplateName] = n + 1;
                        continue;
                    }

                    var asp = GetOrLoadPropAsp(modelName);
                    if (asp is null)
                    {
                        missingMesh++;
                        skippedNoMesh.TryGetValue(modelName, out var n);
                        skippedNoMesh[modelName] = n + 1;
                        continue;
                    }

                    StaticMesh glMesh;
                    if (!_propGlMeshCache.TryGetValue(asp, out glMesh!))
                    {
                        try { glMesh = new StaticMesh(_gl, asp); }
                        catch { parseFail++; continue; }
                        _propGlMeshCache[asp] = glMesh;
                    }

                    var tex = ResolveAspTexture(asp);
                    var world = ComposePlacementWorld(asp, p.Placement);

                    // Phase 17-SC-J — DS1 lets the placement override `aspect.scale_multiplier`
                    // (fh_r1's breakable farmhouse door bumps it to 1.5 so the destroyable
                    // variant reads visibly larger than the everyday wooden door using the
                    // same mesh). Multiply in model space so scale composes inside the
                    // rotation+translation rather than blowing up world-space placement.
                    var scale = ResolveScaleMultiplier(template, p.Node);
                    if (scale != 1f)
                    {
                        world = Matrix4x4.CreateScale(scale) * world;
                        scaledPlacements++;
                    }

                    // Phase 21c-1 — barrel-render investigation. Dump per-corner data
                    // for the first placement of any "barrel" template so we can verify
                    // UVs hit the band rows, normals point outward, and corner colors
                    // aren't multiplying the texture down to the dull tone we're seeing.
                    if (p.TemplateName.Contains("barrel", StringComparison.OrdinalIgnoreCase))
                        DumpPropDiagnostic(p.TemplateName, modelName, asp, tex);

                    // Track texture-resolution per template so the post-load summary
                    // shows whether (e.g.) every barrel placement got the right .raw
                    // bound, vs. silently falling back to the untextured tan colour.
                    texturedPerTpl.TryGetValue(p.TemplateName, out var entry);
                    var texName = asp.TextureNames.Count > 0 ? asp.TextureNames[0] : null;
                    texturedPerTpl[p.TemplateName] = (
                        entry.textured + (tex is not null ? 1 : 0),
                        entry.untextured + (tex is null ? 1 : 0),
                        texName ?? entry.texName);

                    var defaultSkrit = _templateStore.GetAttribute(template, "body", "chore_dictionary", "chore_default", "skrit");
                    TryParseRotateSkrit(defaultSkrit, out var spinAxis, out var spinRad);

                    // Phase 17-SC-K / 21-SC-BARREL-C — breakability lives in
                    // [physics][break_particulate] on every shipped breakable
                    // template (ctn_container.gas, obj_breakable.gas, the
                    // regional ctn_container_regional.gas variants). Phase
                    // 17-SC-K originally looked under [aspect] and missed
                    // every barrel; the sub-slice-C fix corrects the path.
                    // Non-breakable variants ship `aspect:is_invincible = true`
                    // (e.g. barrel_glb_inv). Default life=1 matches DS1's
                    // barrel/crate authoring; max_life off the chain covers
                    // the heavier crate variants (1500 for cav_boarded_wall).
                    bool isBreakable = false;
                    float maxLife = 1f;
                    var breakSection = _templateStore.GetSection(template, "physics", "break_particulate");
                    if (breakSection is not null)
                    {
                        var inv = _templateStore.GetAttribute(template, "aspect", "is_invincible");
                        bool invincible = inv is not null &&
                            (inv.Equals("true", StringComparison.OrdinalIgnoreCase) || inv == "1");
                        if (!invincible)
                        {
                            isBreakable = true;
                            var lifeAttr = _templateStore.GetAttribute(template, "aspect", "max_life");
                            if (lifeAttr is null)
                                lifeAttr = _templateStore.GetAttribute(template, "aspect", "life");
                            if (lifeAttr is not null &&
                                float.TryParse(lifeAttr, System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out var lv) &&
                                lv > 0f)
                                maxLife = lv;
                        }
                    }

                    _staticProps.Add(new StaticPropInstance
                    {
                        Mesh          = glMesh,
                        Texture       = tex,
                        World         = world,
                        Template      = p.TemplateName,
                        SpinAxis      = spinAxis,
                        SpinRadPerSec = spinRad,
                        IsBreakable   = isBreakable,
                        Life          = maxLife,
                        MaxLife       = maxLife,
                    });
                    spawned++;
                }
            }
        }

        Console.WriteLine($"  static props: {spawned}/{considered} placed " +
                          $"({_propGlMeshCache.Count} unique mesh(es); " +
                          $"skipped {missingTemplate} no-template, {missingModel} no-model, " +
                          $"{missingMesh} no-mesh-in-tank, {parseFail} parse-fail; " +
                          $"{scaledPlacements} with non-default scale_multiplier)");
        if (skippedNoMesh.Count > 0)
        {
            Console.WriteLine("  no-mesh-in-tank (top by count):");
            foreach (var kv in skippedNoMesh.OrderByDescending(kv => kv.Value).Take(20))
                Console.WriteLine($"    {kv.Value,4}x  model={kv.Key}");
        }
        if (skippedNoTemplate.Count > 0)
        {
            Console.WriteLine("  no-template (top by count):");
            foreach (var kv in skippedNoTemplate.OrderByDescending(kv => kv.Value).Take(20))
                Console.WriteLine($"    {kv.Value,4}x  template={kv.Key}");
        }
        // Surface untextured placements so a missing .raw shows up in the load log
        // instead of as an unexplained dark/tan blob in the world.
        var anyMissingTex = texturedPerTpl.Values.Any(v => v.untextured > 0);
        if (anyMissingTex)
        {
            Console.WriteLine("  prop texture resolution (templates with un-resolved .raw):");
            foreach (var kv in texturedPerTpl.Where(kv => kv.Value.untextured > 0)
                                            .OrderByDescending(kv => kv.Value.untextured)
                                            .Take(20))
            {
                var v = kv.Value;
                Console.WriteLine($"    {v.untextured,4}x untextured ({v.textured} textured)  " +
                                  $"template={kv.Key}  expected_tex={v.texName ?? "(none)"}");
            }
        }
    }

    /// <summary>Phase 21c-3 — restore the single-white-sun fallback that
    /// pre-region-light callers (boot.asp viewer, anim viewer, the early
    /// ticks of LoadPlayActors before LoadStaticProps fires) depend on.
    /// Numbers match the prior hardcoded shader so visuals don't drift in
    /// modes that never load a region.</summary>
    private void SetDefaultLighting()
    {
        _dirLightCount = 1;
        _dirLightDirs[0]   = Vector3.Normalize(new Vector3(0.4f, 0.9f, 0.3f));
        _dirLightColors[0] = new Vector3(0.75f, 0.75f, 0.75f);
        _ambientLevel = 0.25f;
    }

    /// <summary>Phase 21c-3 — pulls every <c>[t:directional]</c> entry out of
    /// the player region's <c>lights/lights.gas</c>, premultiplies each by its
    /// intensity, and stages it in the directional uniform arrays. Skips
    /// lights with <c>affects_actors=false</c> (those typically light terrain
    /// only). On any miss falls back to <see cref="SetDefaultLighting"/> so
    /// the world never renders pitch-black just because lights.gas was
    /// absent or malformed. Point lights are intentionally ignored here —
    /// they need world-position resolution and attenuation, a separate pass.</summary>
    private void LoadRegionLighting(string regionPath)
    {
        if (_playMapTank is null) { SetDefaultLighting(); return; }

        var (lights, diags) = SiegeFX.Core.Assets.RegionLights.Load(
            new TankReader(_playMapTank), regionPath);
        foreach (var d in diags) Console.WriteLine("  " + d);

        int count = 0;
        foreach (var l in lights)
        {
            if (l.Kind != SiegeFX.Core.Assets.RegionLightKind.Directional) continue;
            if (!l.AffectsActors) continue;
            if (count >= MaxDirectionalLights) break;
            var dir = l.DirectionOrPosition;
            if (dir.LengthSquared() < 1e-6f) continue;
            _dirLightDirs[count]   = Vector3.Normalize(dir);
            _dirLightColors[count] = l.Color * l.Intensity;
            count++;
        }

        if (count == 0) { SetDefaultLighting(); return; }

        _dirLightCount = count;
        _ambientLevel = 0.2f;  // a touch lower since multiple directionals add up
        Console.WriteLine($"  region lighting: {count} directional(s) from {regionPath}/lights/lights.gas");
    }

    /// <summary>Uploads the current directional-light state to whichever shader
    /// is currently bound. Cheap (a handful of uniform sets, all GL queries
    /// are name-cached at the driver level), so we just call it next to every
    /// existing <c>SetInt("uFlipV", …)</c> site rather than tracking dirty bits.</summary>
    private void ApplyLightingUniforms(Shader shader)
    {
        shader.SetInt("uDirCount", _dirLightCount);
        shader.SetFloat("uAmbient", _ambientLevel);
        shader.SetVec3Array("uDirDir",   _dirLightDirs.AsSpan(0, _dirLightCount));
        shader.SetVec3Array("uDirColor", _dirLightColors.AsSpan(0, _dirLightCount));
    }

    private AspMesh? GetOrLoadPropAsp(string modelName)
    {
        if (_propAspCache.TryGetValue(modelName, out var cached)) return cached;
        if (_playResolver is null) { _propAspCache[modelName] = null; return null; }
        if (!_playResolver.TryLoadModel(modelName, out var bytes))
        {
            _propAspCache[modelName] = null;
            return null;
        }
        try
        {
            var asp = AspMesh.Load(bytes);
            _propAspCache[modelName] = asp;
            return asp;
        }
        catch
        {
            _propAspCache[modelName] = null;
            return null;
        }
    }

    /// <summary>Phase 21-SC-BARREL-C — resolve a frag template (e.g.
    /// frag_glb_wood_01) into a renderable (mesh, texture) pair. Cached
    /// across all shatters in the session — frag meshes are tiny and
    /// reused across many barrels / pots / windows. Returns null if the
    /// template isn't in the store, has no aspect.model, or the asp
    /// failed to load (fragments are graceful-fail content; an unfound
    /// frag just gets dropped from the burst, not aborts the shatter).</summary>
    private FragAsset? TryResolveFragAsset(string fragTemplateName)
    {
        if (_fragAssets.TryGetValue(fragTemplateName, out var cached)) return cached;
        if (_gl is null || _templateStore is null || _playResolver is null)
        { _fragAssets[fragTemplateName] = null; return null; }
        if (!_templateStore.TryGet(fragTemplateName, out var template))
        { _fragAssets[fragTemplateName] = null; return null; }
        var modelName = _templateStore.GetAttribute(template, "aspect", "model");
        if (string.IsNullOrEmpty(modelName))
        { _fragAssets[fragTemplateName] = null; return null; }
        var asp = GetOrLoadPropAsp(modelName);
        if (asp is null) { _fragAssets[fragTemplateName] = null; return null; }
        if (!_propGlMeshCache.TryGetValue(asp, out var glMesh))
        {
            try
            {
                glMesh = new StaticMesh(_gl, asp);
                _propGlMeshCache[asp] = glMesh;
            }
            catch { _fragAssets[fragTemplateName] = null; return null; }
        }
        GlTexture? tex = null;
        if (asp.TextureNames.Count > 0 &&
            _playResolver.TryLoadByBasename(asp.TextureNames[0] + ".raw", out var texBytes))
        {
            try { tex = new GlTexture(_gl, RawImage.Load(texBytes)); }
            catch { tex = null; }
        }
        var asset = new FragAsset { Mesh = glMesh, Texture = tex };
        _fragAssets[fragTemplateName] = asset;
        return asset;
    }

    /// <summary>Phase 21-SC-BARREL-C — kick a ballistic-frag burst from the
    /// shattered prop. Walks the template's [physics][break_particulate]
    /// block (chain-resolved so leaves inherit the base list when they
    /// don't override) and spawns one <see cref="FragDebris"/> per count
    /// per frag template, with random outward velocity and spin. Frags
    /// fall under gravity to the prop's authored Y, settle, and despawn
    /// after the lifetime — no real collision, just a planar floor.</summary>
    private void SpawnPropDebris(StaticPropInstance prop)
    {
        if (_templateStore is null) return;
        if (!_templateStore.TryGet(prop.Template, out var template)) return;
        var section = _templateStore.GetSection(template, "physics", "break_particulate");
        if (section is null) return;

        // Per-shatter rng seeded off prop position + template name so two
        // stacked barrels (same X,Z, different Y, or two different templates
        // sharing the same X,Z) don't burst identical patterns from the same
        // point. Phase 21-SC-BARREL-FOLD widens a Y-collision-prone (X,Z)
        // hash to (X,Y,Z, templateNameHash).
        var origin = prop.World.Translation;
        int seed = unchecked(
            BitConverter.SingleToInt32Bits(origin.X) * 73856093 ^
            BitConverter.SingleToInt32Bits(origin.Y) * 83492791 ^
            BitConverter.SingleToInt32Bits(origin.Z) * 19349663 ^
            (prop.Template?.GetHashCode() ?? 0));
        var rng = new Random(seed);

        int spawned = 0;
        foreach (var attr in section.Attributes)
        {
            if (!int.TryParse(attr.Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var count))
                continue;
            if (count <= 0) continue;
            // DS1 sometimes ships counts in the 6-10 range — visually that's
            // a lot of frags. Cap per-frag-template to keep the burst from
            // becoming a pixel snowstorm; the total stays in the
            // visually-reasonable 20-40 range across all entries.
            count = Math.Min(count, 8);
            var asset = TryResolveFragAsset(attr.Name);
            if (asset is null) continue;
            for (int i = 0; i < count; i++)
            {
                // Outward XZ unit vector + a small horizontal speed; vertical
                // bias up so frags arc cleanly. Spin axis is unit-random
                // (Marsaglia-style cube-reject is overkill for a burst,
                // simple spherical-ish sample reads fine).
                float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
                float horiz = 2.5f + (float)(rng.NextDouble() * 3.5);
                float vertUp = 2.5f + (float)(rng.NextDouble() * 2.5);
                var vel = new Vector3(MathF.Cos(ang) * horiz, vertUp, MathF.Sin(ang) * horiz);
                var spinAxis = Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0)));
                if (!float.IsFinite(spinAxis.X)) spinAxis = Vector3.UnitY;
                float spinRate = 4f + (float)(rng.NextDouble() * 8.0);
                _fragDebris.Add(new FragDebris
                {
                    Asset = asset,
                    Pos = origin + new Vector3(0f, 0.4f, 0f),
                    Vel = vel,
                    SpinAxis = spinAxis,
                    SpinRadPerSec = spinRate,
                    Lifetime = 6f + (float)(rng.NextDouble() * 2.0),
                    RestY = origin.Y,
                });
                spawned++;
            }
        }
        if (spawned > 0)
            Console.WriteLine($"  debris: {spawned} frag instance(s) from {prop.Template}");
    }

    /// <summary>Phase 21-SC-BARREL-C — integrate frag-debris ballistic
    /// motion. Called once per logic frame from <see cref="OnUpdate"/>.
    /// Settled frags freeze in place until lifetime expires; airborne
    /// frags accelerate under gravity and settle on contact with the
    /// prop's authored ground Y. Spinning runs only while airborne so
    /// settled frags read as "lying on the ground", not "floating
    /// twitchily on the spot".</summary>
    private void TickFragDebris(float dt)
    {
        if (_fragDebris.Count == 0) return;
        const float gravity = 14f;
        for (int i = _fragDebris.Count - 1; i >= 0; i--)
        {
            var f = _fragDebris[i];
            f.Age += dt;
            if (f.Age > f.Lifetime) { _fragDebris.RemoveAt(i); continue; }
            if (!f.Settled)
            {
                f.Vel.Y -= gravity * dt;
                f.Pos += f.Vel * dt;
                f.SpinAngle += f.SpinRadPerSec * dt;
                if (f.Pos.Y <= f.RestY)
                {
                    f.Pos.Y = f.RestY;
                    f.Vel = Vector3.Zero;
                    f.Settled = true;
                }
            }
        }
    }

    /// <summary>Phase 21c-1 barrel investigation: one-shot per-template dump of the
    /// raw mesh data the GL pipeline will see. Prints UVs, normals, and corner color
    /// alongside the resolved texture name + dimensions. Also writes the texture's
    /// mip0 to a PNG under %TEMP%\siegefx_diag\ so we can confirm orientation by eye.
    /// Skipped after the first placement of a given template so we don't spam the
    /// load log.</summary>
    private void DumpPropDiagnostic(string templateName, string modelName, AspMesh asp, GlTexture? tex)
    {
        if (!_dumpedPropDiagnostics.Add(templateName)) return;

        // Texture mip0 → PNG. Re-fetch the .raw bytes via the resolver because the
        // GlTexture has uploaded to GPU and we no longer hold the CPU copy.
        if (tex is not null && _playResolver is not null && asp.TextureNames.Count > 0)
        {
            var basename = asp.TextureNames[0] + ".raw";
            if (_playResolver.TryLoadByBasename(basename, out var texBytes))
            {
                try
                {
                    var img   = SiegeFX.Core.Assets.RawImage.Load(texBytes);
                    var w     = img.GetSurfaceWidth(0);
                    var h     = img.GetSurfaceHeight(0);
                    var rgba  = img.GetSurfaceRgba(0);
                    var dir   = Path.Combine(Path.GetTempPath(), "siegefx_diag");
                    Directory.CreateDirectory(dir);
                    var safeTpl = string.Join('_', templateName.Split(Path.GetInvalidFileNameChars()));
                    var pngPath = Path.Combine(dir, $"{safeTpl}__{asp.TextureNames[0]}_mip0.png");
                    using (var fs = File.Create(pngPath))
                        SiegeFX.Core.IO.Png.EncodeRgba(fs, rgba, w, h);
                    Console.WriteLine($"         dumped mip0 -> {pngPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"         mip0 dump failed: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"  [diag] template={templateName}  model={modelName}");
        Console.WriteLine($"         asp v{asp.AspVersionMajor}.{asp.AspVersionMinor}  " +
                          $"verts={asp.Positions.Length}  corners={asp.Corners.Length}  " +
                          $"tris={asp.TriangleCount}  bones={asp.BoneCount}  hasSkin={asp.HasSkin}");
        var texName = asp.TextureNames.Count > 0 ? asp.TextureNames[0] : "(none)";
        if (tex is not null)
            Console.WriteLine($"         tex='{texName}'  size={tex.Width}x{tex.Height}  resolved=YES");
        else
            Console.WriteLine($"         tex='{texName}'  resolved=NO (shader fallback)");

        float uMin = float.PositiveInfinity, uMax = float.NegativeInfinity;
        float vMin = float.PositiveInfinity, vMax = float.NegativeInfinity;
        for (int i = 0; i < asp.Corners.Length; i++)
        {
            var c = asp.Corners[i];
            if (c.Uv.X < uMin) uMin = c.Uv.X;
            if (c.Uv.X > uMax) uMax = c.Uv.X;
            if (c.Uv.Y < vMin) vMin = c.Uv.Y;
            if (c.Uv.Y > vMax) vMax = c.Uv.Y;
        }
        Console.WriteLine($"         uv extents  U=[{uMin:F3}, {uMax:F3}]  V=[{vMin:F3}, {vMax:F3}]");

        int dumpCount = Math.Min(asp.Corners.Length, 24);
        Console.WriteLine($"         first {dumpCount} corners (idx  vIdx  pos                 normal              uv             color):");
        for (int i = 0; i < dumpCount; i++)
        {
            var c = asp.Corners[i];
            var p = asp.Positions[c.VertexIndex];
            Console.WriteLine($"           {i,3}  {c.VertexIndex,4}  " +
                              $"({p.X,7:F3},{p.Y,7:F3},{p.Z,7:F3})  " +
                              $"({c.Normal.X,6:F3},{c.Normal.Y,6:F3},{c.Normal.Z,6:F3})  " +
                              $"({c.Uv.X,6:F3},{c.Uv.Y,6:F3})  " +
                              $"0x{c.Color:X8}");
        }
    }

    /// <summary>Phase 21c — same world-transform composition ActorSpawner uses
    /// (rotate by quat, translate by node-local position, then post-multiply by
    /// the SNO node's world transform), with a critical extra step for static
    /// props: pre-multiply the mesh's bone-0 root bind pose for single-bone
    /// rigid props. DS1 V2.3/V2.4 prop ASPs (barrels, jugs, fences, crops, etc.)
    /// author vertices in 3DS Max's Z-up modeling space and ship a bone-0
    /// BindPose that rotates Z-up → Y-up; the animated actor pipeline does this
    /// implicitly through skinning, but static props skipped it before — which
    /// is why barrels rendered lying flat. Multi-bone V2.5 props (animated
    /// trees, etc.) author vertices in world-bind space already, so applying
    /// BindPose[0] would lay THEM flat — we render those at vertex-pose, no
    /// pre-multiply.</summary>
    /// <summary>Phase 17-SC-J — for each emitter.gas placement that ships an
    /// instance-scope [particle_emitter] block (fh_r1's emt_particle fire +
    /// smoke columns), translate its color/size/rate into a persistent
    /// fire/smoke/steam emitter on _sfxRuntime. Returns how many emitters
    /// were registered.</summary>
    private int RegisterLegacyParticleEmitters(IReadOnlyList<SiegeFX.Core.Assets.ActorInstance> placements)
    {
        if (_sfxRuntime is null || _regionLayout is null) return 0;
        int registered = 0;
        foreach (var inst in placements)
        {
            var pe = SiegeFX.Core.Assets.TemplateStore.FindChild(inst.Node, "particle_emitter");
            if (pe is null) continue;

            float red   = ReadFloat(pe, "red",   0.6f);
            float green = ReadFloat(pe, "green", 0.4f);
            float blue  = ReadFloat(pe, "blue",  0.2f);
            float fade  = ReadFloat(pe, "fade",  0.4f);
            int   count = (int)ReadFloat(pe, "count", 60f);
            float size  = ReadFloat(pe, "particle_size", 1.0f);
            float growth = ReadFloat(pe, "growth", 1.0f);
            bool  dark  = ReadBool(pe, "dark");

            // DS1's particle block doesn't carry an explicit kind; we
            // discriminate by `dark`+luminance. Smoke columns ship dark=true
            // with low rgb; fire columns ship red>green>blue without dark.
            // Steam (waterfall froth) shows up as bright bluish/white but
            // those live under SC-I/-L water work, not here.
            float luma = red * 0.299f + green * 0.587f + blue * 0.114f;
            ParticleKind kind = dark
                ? ParticleKind.Smoke
                : (red >= green && red >= blue && luma > 0.25f)
                    ? ParticleKind.Fire
                    : ParticleKind.Smoke;

            var local = Matrix4x4.CreateFromQuaternion(inst.Placement.Orientation) *
                        Matrix4x4.CreateTranslation(inst.Placement.LocalPosition);
            var world = _regionLayout.TryGetTransform(inst.Placement.NodeGuid, out var nw) ? local * nw : local;
            var origin = world.Translation;

            // Spawn rate: count is total particles intended at full burn,
            // fade is each particle's lifetime. count/fade yields particles
            // per second to maintain steady-state population. Clamp so a
            // pathological count=8000 emitter doesn't melt the renderer.
            float rate = (count > 0 && fade > 0f) ? Math.Min(count / fade, 60f) : 18f;
            float scale = MathF.Max(0.6f, size);

            // DS1's `dark=true` is a blend-mode flag for opacity/multiply
            // smoke, not a literal grey colour — the authored rgb=0.1 means
            // "low contribution per channel" for the dark blend, but in our
            // straight alpha-blend pipeline that paints near-black smoke. The
            // farmhouse plumes are visibly white in retail; force white tint
            // so the textured smoke billboard reads correctly until SC-L
            // wires up a real dark/multiply blend mode.
            Vector4 tint;
            if (kind == ParticleKind.Smoke)
            {
                tint = new Vector4(1f, 1f, 1f, 1f);
            }
            else
            {
                // Fire: saturate the authored colour so the additive plume
                // actually lights up. DS1 ships rgb=(0.6,0.4,0) for the
                // farmhouse fire — our texture * tint additive needs that
                // pushed up to read as flame, not "very dim ember".
                float boost = MathF.Max(red, MathF.Max(green, blue));
                float k = boost > 0f ? 1f / boost : 1f;
                tint = new Vector4(MathF.Min(red * k, 1f),
                                   MathF.Min(green * k, 1f),
                                   MathF.Min(blue  * k, 1f),
                                   1f);
            }

            _sfxRuntime.AddPersistentEmitter(kind, origin, tint, scale, rate);
            Console.WriteLine($"    legacy emitter [{inst.TemplateName} 0x{inst.Scid:x8}] -> " +
                              $"{kind} at ({origin.X:F2},{origin.Y:F2},{origin.Z:F2}) " +
                              $"rgb=({red:F2},{green:F2},{blue:F2}) dark={dark} " +
                              $"tint=({tint.X:F2},{tint.Y:F2},{tint.Z:F2}) scale={scale:F2} rate={rate:F1}");
            registered++;
        }
        return registered;
    }

    private static float ReadFloat(SiegeFX.Core.Assets.GasNode node, string name, float fallback)
    {
        foreach (var a in node.Attributes)
        {
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(a.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
        }
        return fallback;
    }

    private static bool ReadBool(SiegeFX.Core.Assets.GasNode node, string name)
    {
        foreach (var a in node.Attributes)
        {
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                var v = a.Value.Trim();
                return v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    private Matrix4x4 ComposePlacementWorld(AspMesh asp, SiegeFX.Core.Assets.NodePlacement p)
    {
        var bindRoot = ComputeRootBindPose(asp);
        var local = bindRoot *
                    Matrix4x4.CreateFromQuaternion(p.Orientation) *
                    Matrix4x4.CreateTranslation(p.LocalPosition);
        if (_regionLayout is null) return local;
        if (!_regionLayout.TryGetTransform(p.NodeGuid, out var nodeWorld)) return local;
        return local * nodeWorld;
    }

    /// <summary>Phase 21c — bone-0's world bind pose for a static prop, used to
    /// rotate Z-up authoring space into the engine's Y-up convention. Returns
    /// identity for unrigged meshes (no BindPose entries) AND for multi-bone
    /// meshes (V2.5+ skinned props that ship vertices in world-bind space).
    /// Detection by bone count is reliable: every shipped V2.3/V2.4 static prop
    /// is rigid (1 bone), every shipped V2.5 animated prop is multi-bone.</summary>
    private static Matrix4x4 ComputeRootBindPose(AspMesh asp)
    {
        if (asp.BindPose.Length != 1) return Matrix4x4.Identity;
        var bp = asp.BindPose[0];
        return Matrix4x4.CreateFromQuaternion(bp.Rotation) *
               Matrix4x4.CreateTranslation(bp.Translation);
    }

    /// <summary>Phase 21a-3 — rebuild the nav mesh against the current unified
    /// region scope (player + all preloaded neighbors). Called both at the
    /// initial play-region load (via LoadPlayActors's nav block) and after a
    /// rolling preload so freshly-streamed floor tris weld to the prior ring.
    /// Returns null if terrain tank isn't available — callers fall back to
    /// keeping the existing _navMesh.</summary>
    private SiegeFX.Core.Nav.NavMesh? RebuildNavMesh()
    {
        if (_regionTerrainTankPath is null || _regionLayout is null) return null;
        try
        {
            using var terrainTank = TankFile.Open(_regionTerrainTankPath);
            var terrainReader = new TankReader(terrainTank);
            var meshIdx = SnoMeshIndex.Build(terrainReader);
            var navCache = new Dictionary<uint, SnoModel?>();
            SnoModel? ResolveNav(uint meshGuid)
            {
                if (navCache.TryGetValue(meshGuid, out var hit)) return hit;
                SnoModel? m = null;
                if (meshIdx.TryResolve(meshGuid, out var p))
                {
                    try { m = SnoModel.Load(terrainReader.ExtractToMemory(p)); }
                    catch { m = null; }
                }
                navCache[meshGuid] = m;
                return m;
            }

            RegionGraph navGraph;
            if (_worldRegionGraphs.Count > 1)
            {
                var graphList = new List<RegionGraph>(_worldRegionGraphs.Count);
                foreach (var (_, g) in _worldRegionGraphs) graphList.Add(g);
                navGraph = RegionGraph.Combine(graphList);
            }
            else if (_worldRegionGraphs.Count == 1)
            {
                navGraph = _worldRegionGraphs[0].Graph;
            }
            else
            {
                return null;
            }

            var nav = SiegeFX.Core.Nav.NavMesh.BuildForRegion(navGraph, _regionLayout, ResolveNav);
            Console.WriteLine($"  nav mesh rebuild: {nav.TriangleCount} tri(s), " +
                              $"{nav.Vertices.Length} welded vert(s), " +
                              $"{nav.SourceSnodeCount} snode(s) across {_worldRegionGraphs.Count} region(s)");
            return nav;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  !! nav mesh rebuild failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Phase 21a-3 — attach a freshly-spawned batch of actors to the
    /// render/tick lists. Mirrors the per-actor work LoadPlayActors does in its
    /// own actor loop: GlMesh cache, talkable bypass, on-mesh snap + brain
    /// creation, _actors.Add. Returns (wandering, pinned) counts so the caller
    /// can log a one-line summary. Used by <see cref="OnPlayerRegionChanged"/>
    /// to wire up newly-streamed regions' actors without re-running the full
    /// LoadPlayActors path.</summary>
    private (int OnMesh, int OffMesh) AttachActorsToScene(
        IReadOnlyList<Actor> actors, SiegeFX.Core.Nav.NavMesh? navMesh)
    {
        if (_gl is null) return (0, 0);
        int onMesh = 0, offMesh = 0;
        foreach (var actor in actors)
        {
            if (!_actorMeshCache.TryGetValue(actor.Mesh, out var gl))
            {
                gl = new SkinnedMesh(_gl, actor.Mesh);
                _actorMeshCache[actor.Mesh] = gl;
            }

            SiegeFX.Core.Actors.ActorBrain? brain = null;
            bool isTalkable = SiegeFX.Core.Assets.ConversationStore
                .KeysFromInstance(actor.Instance.Node).Count > 0;
            if (isTalkable)
            {
                offMesh++;
            }
            else if (navMesh is not null &&
                navMesh.TryFindTriangle(actor.WorldTransform.Translation, out var startTri))
            {
                var snapped = actor.WorldTransform.Translation with
                {
                    Y = navMesh.SampleYOnTriangle(startTri, actor.WorldTransform.Translation),
                };
                var authoredFacing = Vector3.TransformNormal(Vector3.UnitZ, actor.WorldTransform);
                var gait = actor.Stats.WalkSpeed > 0.5f ? actor.Stats.WalkSpeed : 4f;
                var follower = new SiegeFX.Core.Actors.ActorFollower(
                    navMesh, snapped, speed: gait, rngSeed: (int)actor.Instance.Scid,
                    initialFacing: authoredFacing);
                brain = new SiegeFX.Core.Actors.ActorBrain(
                    follower, actor.Stats, rngSeed: (int)actor.Instance.Scid ^ unchecked((int)0xA17ACC1Eu),
                    selfActor: actor);
                onMesh++;
            }
            else
            {
                offMesh++;
            }

            _actors.Add(new ActorRenderState
            {
                Actor             = actor,
                GlMesh            = gl,
                AnimTime          = 0,
                LastClipIndex     = actor.CurrentClipIndex,
                Brain             = brain,
                CurrentTransform  = actor.WorldTransform,
            });
        }
        return (onMesh, offMesh);
    }

    /// <summary>Phase 21a-3 — find the nearest snode (in XZ) to <paramref name="worldPos"/>
    /// and return its containing region path. Linear scan over <see cref="_snodeRegionLookup"/>
    /// — DS1 regions average ~600 snodes, the loaded ring caps around ~3-5k entries,
    /// 60-iter check at 2 Hz. Cheap; no spatial index needed at this scale. Returns
    /// null if the lookup is empty (no regions loaded yet, or single-region test
    /// fixture without a stitch helper).</summary>
    private string? RegionAtWorldPos(Vector3 worldPos)
    {
        if (_snodeRegionLookup.Length == 0) return null;
        float bestSq = float.PositiveInfinity;
        string? best = null;
        for (int i = 0; i < _snodeRegionLookup.Length; i++)
        {
            var entry = _snodeRegionLookup[i];
            float dx = entry.OriginXZ.X - worldPos.X;
            float dz = entry.OriginXZ.Z - worldPos.Z;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = entry.RegionPath; }
        }
        return best;
    }

    /// <summary>
    /// Resolves each unique <see cref="SnoModel.Surface.TextureName"/> against a tank
    /// by matching bare filename (case-insensitive) against every <c>*.raw</c> entry.
    /// DS1 SNOs reference textures by basename (e.g. "t_grs01"), with the canonical
    /// location varying between terrain/logic tanks — basename matching side-steps that.
    /// </summary>
    private void LoadSnoTexturesFromTank(string tankPath)
    {
        if (_gl is null || _sno is null) return;

        using var tank = TankFile.Open(tankPath);
        var reader = new TankReader(tank);

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in reader.ListFiles())
        {
            if (!path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(path);
            // First match wins; most DS1 basenames are unique within a tank. A future pass
            // can promote collision handling if a terrain/logic merge actually shows one.
            if (!index.ContainsKey(bare)) index[bare] = path;
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _sno.Subsets) unique.Add(s.TextureName);

        var hits = 0;
        foreach (var name in unique)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!index.TryGetValue(name, out var fullPath))
            {
                Console.WriteLine($"  [miss] '{name}' not found in tank");
                continue;
            }
            var bytes = reader.ExtractToMemory(fullPath);
            var raw = RawImage.Load(bytes);
            _snoTextures[name] = new GlTexture(_gl, raw);
            hits++;
        }

        Console.WriteLine($"resolved {hits}/{unique.Count} SNO textures from tank '{tankPath}'");
    }

    private void OnUpdate(double dt)
    {
        if (_input is null) return;
        // Phase 21d-2a-viii-b — drain the creator's confirm/cancel edge here
        // so the spawn happens on the main thread (input callbacks fire from
        // the Silk dispatcher; ActorSpawner + GL resource creation expect the
        // main thread). Both branches clear the pending args.
        FlushCreator();
        FlushOptionsMenu();
        // Phase 24-MAINMENU step 1+2-FOLD — splash state machine ticks here
        // (not inside DrawBootScene) so the boot sequence keeps advancing
        // even when the render loop is paused (e.g. minimized window).
        // Phase 25-CHROME — fire the splash-stage SFX on each state edge:
        // sword "thunk" (logo_flyin) when the logo starts dropping; sword
        // withdraw (logo_flyout) when it starts rising. The state machine
        // owns the timing; this just listens at the boundary.
        if (_bootMode && _frontendScene is not null)
        {
            var prev = _frontendScene.State;
            _frontendScene.Tick((float)dt);
            var now = _frontendScene.State;
            if (prev != now)
            {
                if (now == Hud.FrontendScene.ScreenState.IntroLogoDrop)
                    _audio?.Play(SfxFrontendLogoFlyin);
                else if (now == Hud.FrontendScene.ScreenState.IntroLogoExit)
                    _audio?.Play(SfxFrontendLogoFlyout);
                // Phase 27-SP-FLYOUT — panel-morph swoosh on the Main ↔ SP
                // transitions. The big-button click already played on the
                // press; this is the layered "panel slides" cue so the
                // motion has audible weight. Reuses the logo-flyout wav
                // (wood-panel chrome swoosh) since DS1 doesn't ship a
                // dedicated sub-menu transition wav.
                else if (now == Hud.FrontendScene.ScreenState.MainMenuToSp
                      || now == Hud.FrontendScene.ScreenState.SinglePlayerToMm
                      || now == Hud.FrontendScene.ScreenState.SinglePlayerToCd
                      || now == Hud.FrontendScene.ScreenState.CharacterSelectToSp)
                    _audio?.Play(SfxFrontendLogoFlyout);
                // Phase 29-CD-CREATOR-FIX2 — one-shot creator reset on
                // ENTRY to CharacterSelect (from the SP→CD transition).
                // Previously Reset() was called every frame whenever
                // _creator.IsOpen was false, which clobbered HeroName
                // continuously while typing. Now Reset only fires on
                // the state-machine edge.
                if (now == Hud.FrontendScene.ScreenState.CharacterSelect)
                {
                    _creator.Reset();
                }
            }
        }
        // Phase 24-MAINMENU step 5+6 — drain main menu click actions one
        // per frame. Drives state transitions, opens sub-screens, fires
        // _window.Close on Exit. Stub buttons (Multiplayer / Continue /
        // Credits) currently no-op so a click is consumed without effect
        // until their splinters land.
        FlushMainMenu();
        // Phase 27-SP-FLYOUT — drain SP submenu actions while in the
        // SinglePlayer state. Activated only after the mm2sp transition
        // settles; clicks during the transition are dropped.
        FlushSinglePlayerMenu();
        // Phase 28-CD-FLYOUT — drain Character Creator nav actions.
        FlushCharacterSelectMenu();
        var forward = 0f;
        var strafe  = 0f;
        var vert    = 0f;
        var sprint  = false;

        foreach (var kb in _input.Keyboards)
        {
            if (kb.IsKeyPressed(Key.W)) forward += 1f;
            if (kb.IsKeyPressed(Key.S)) forward -= 1f;
            if (kb.IsKeyPressed(Key.D)) strafe  += 1f;
            if (kb.IsKeyPressed(Key.A)) strafe  -= 1f;
            if (kb.IsKeyPressed(Key.E) || kb.IsKeyPressed(Key.Space))      vert += 1f;
            if (kb.IsKeyPressed(Key.Q) || kb.IsKeyPressed(Key.ControlLeft)) vert -= 1f;
            if (kb.IsKeyPressed(Key.ShiftLeft)) sprint = true;
        }
        // Fly mode keeps WASD. Chase mode ignores it (PC movement is 13c) so the
        // camera can't drift off the player — cleaner than silently re-snapping
        // every frame.
        if (_cameraMode == CameraMode.Fly)
            _camera.Move(forward, strafe, vert, (float)dt, sprint);

        // Phase 13b — in chase mode, snap the camera behind the player and aim
        // at him. _chaseYaw is the orbit angle around the player's +Y axis;
        // yaw=0 puts the camera on +Z so the player is seen looking down -Z
        // (screen "forward"). Yaw/pitch of Camera are overwritten here.
        if (_cameraMode == CameraMode.Chase && _player is not null)
        {
            var target = _player.CurrentTransform.Translation + new Vector3(0, ChaseLookTargetY, 0);
            var offset = new Vector3(MathF.Sin(_chaseYaw), 0f, MathF.Cos(_chaseYaw)) * _chaseDistance;
            // Phase 21-SC-ZOOM — height tracks distance so zoom slides along
            // the view ray (dolly), not just horizontal radius. Without this
            // the camera arcs to a flatter pitch as you zoom out.
            float height = _chaseDistance * ChasePitchSlope;
            _camera.Position = target + offset + new Vector3(0, height, 0);
            var dir = Vector3.Normalize(target - _camera.Position);
            _camera.Yaw   = MathF.Atan2(dir.X, -dir.Z);
            _camera.Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.999f, 0.999f));
        }

        if (_anim is not null && _anim.AnimLength > 0f)
            _animTime += dt;
        _terrainTime += dt;

        // Phase 9a: advance the skrit runtime at a fixed 20 Hz logic tick (DS1's authoritative
        // rate for `at ( N frames )` scheduling). Render dt is variable-rate — we accumulate
        // and drain in fixed steps, matching the deterministic-loop pattern we settled on in
        // 8d. After each tick, poll the host bridge's CurrentAnimIndex; a change means the
        // skrit picked a new sub-anim and we swap clips (reset _animTime so the new clip
        // starts from its first keyframe).
        if (_skritRuntime is not null && _skritHost is not null && _skritClips is not null)
        {
            const double stepSec = 1.0 / SkritInstance.FramesPerSecond;
            // Cap backlog at 5 ticks (250 ms) so a hitched frame / window drag doesn't
            // burst-fire chores the user didn't witness real time for.
            _skritTickAccumulator = Math.Min(_skritTickAccumulator + dt, stepSec * 5);
            while (_skritTickAccumulator >= stepSec)
            {
                _skritTickAccumulator -= stepSec;
                _skritRuntime.Tick(stepSec);
            }
            int idx = _skritHost.CurrentAnimIndex;
            if (idx >= 0 && idx < _skritClips.Length && idx != _skritCurrentClip)
            {
                var nextClip = _skritClips[idx] ?? _skritClips[0];
                if (nextClip is not null)
                {
                    _anim = nextClip;
                    _skritCurrentClip = idx;
                    _animTime = 0;
                }
            }
        }

        // Phase 10e: same 20 Hz accumulator pattern, but one shared runtime ticks every actor
        // in a region at once and the bus drains after each tick so cross-actor messages
        // (broadcasts, targeted self-sends) see the updated state. Per-actor AnimTime is
        // advanced by real dt (not step*stepsDone) to keep the visible anim smooth between
        // logic ticks — the skrit state only updates at 20 Hz, but the clip plays at render rate.
        if (_actorRuntime is not null && _actorBus is not null && _actors.Count > 0)
        {
            const double stepSec = 1.0 / SkritInstance.FramesPerSecond;
            _actorTickAccumulator = Math.Min(_actorTickAccumulator + dt, stepSec * 5);
            while (_actorTickAccumulator >= stepSec)
            {
                _actorTickAccumulator -= stepSec;
                _actorRuntime.Tick(stepSec);
                _actorBus.Deliver();
                if (_triggerRuntime is not null && _triggerCtx is not null)
                    _triggerRuntime.Tick(stepSec, _triggerCtx);
                // Phase 11d/16c — drive each brain at the same fixed cadence as the
                // skrit runtime. Stepping movement inside the accumulator loop (not
                // once per render frame) keeps translation deterministic regardless
                // of framerate — an actor walks at exactly `speed` u/s wall-clock.
                // The brain wraps wander + aggro: combatants get the player as a
                // target so they chase and swing; non-combatants get null and stay
                // in Wander forever.
                Vector3? npcTargetPos = null;
                SiegeFX.Core.Actors.ActorCombatState? npcTargetCombat = null;
                SiegeFX.Core.Actors.ActorStats? npcTargetStats = null;
                if (_player is not null && !_player.IsDead)
                {
                    npcTargetPos    = _player.CurrentTransform.Translation;
                    npcTargetCombat = _player.Actor.Combat;
                    npcTargetStats  = _player.Actor.Stats;
                }
                foreach (var s in _actors)
                {
                    if (s.IsDead) continue;
                    // Phase 12-SC-2 — drain any chore_attack override pinned by
                    // the brain on the previous swing. Outside the brain branch
                    // so non-combatants and brain-less actors still tick.
                    s.Actor.Host.TickOverride((float)stepSec);
                    if (s.Brain is null) continue;
                    bool hostile = s.Actor.Stats.IsCombatant;
                    s.Brain.Tick(
                        (float)stepSec,
                        hostile ? npcTargetPos    : null,
                        hostile ? npcTargetCombat : null,
                        hostile ? npcTargetStats  : null);
                    // Compose CurrentTransform = rotate-to-facing * translate-to-pos.
                    // We drop the authored spawn orientation once the brain owns
                    // movement; the mesh's local forward in DS1 is +Z, so facing into
                    // the XZ heading means rotating around Y by atan2(dx, dz).
                    var facing = s.Brain.Facing;
                    float yaw = MathF.Atan2(facing.X, facing.Z);
                    s.CurrentTransform =
                        Matrix4x4.CreateRotationY(yaw) *
                        Matrix4x4.CreateTranslation(s.Brain.Position);
                    // Phase 21c-4 — flag walking when the brain actually translated this
                    // tick. The wander follower idles between picks (and the brain idles
                    // mid-Attack), so XZ-delta is the cheap, accurate signal — no need to
                    // distinguish chase vs wander; both translate.
                    var posXZ = new Vector3(s.Brain.Position.X, 0f, s.Brain.Position.Z);
                    if (s.HasLastPosition)
                    {
                        var dx = posXZ.X - s.LastPositionXZ.X;
                        var dz = posXZ.Z - s.LastPositionXZ.Z;
                        // ~0.05 u over a 50ms tick = 1 u/s; well below krug walk speed but
                        // above floating-point noise from idle followers.
                        s.IsMoving = (dx * dx + dz * dz) > 0.0025f;
                    }
                    s.LastPositionXZ = posXZ;
                    s.HasLastPosition = true;
                }
                // Phase 13c — tick the player follower on the same cadence. The PC
                // has no ActorFollower (no wander), so its movement lives here. When
                // the raw NavFollower actually translates, derive XZ facing from the
                // position delta and compose a new world transform; if it didn't
                // move (idle, reached goal, blocked), the last facing is kept so the
                // PC doesn't snap to +Z on arrival.
                if (_player is not null && _playerFollower is not null && !_player.IsDead)
                {
                    // Phase 12-SC-2 — drain the player's chore_attack override
                    // on the same fixed-step cadence the brains use.
                    _player.Actor.Host.TickOverride((float)stepSec);
                    var before = _playerFollower.Position;
                    _playerFollower.Tick((float)stepSec);
                    var after = _playerFollower.Position;
                    float dx = after.X - before.X;
                    float dz = after.Z - before.Z;
                    float len2 = dx * dx + dz * dz;
                    if (len2 > 1e-6f)
                    {
                        float len = MathF.Sqrt(len2);
                        _playerFacing = new Vector3(dx / len, 0f, dz / len);
                    }
                    // Phase 9-SC-10b — feed the per-frame interp buffers. The
                    // actual CurrentTransform write happens after the while loop
                    // drains, lerping prev→next by the leftover accumulator.
                    if (!_playerRenderInit)
                    {
                        _playerRenderPosPrev = before;
                        _playerRenderFacingPrev = _playerFacing;
                        _playerRenderInit = true;
                    }
                    else
                    {
                        _playerRenderPosPrev = _playerRenderPosNext;
                        _playerRenderFacingPrev = _playerRenderFacingNext;
                    }
                    _playerRenderPosNext = after;
                    _playerRenderFacingNext = _playerFacing;
                    // 21d-2a-vi: the actor draw loop swaps to WalkClipIndex while
                    // IsMoving is set; without this the PC always played chore_default
                    // even when click-to-move was translating it. Same XZ-delta
                    // threshold the NPC brains use (~1u/s minimum) so float noise
                    // from idle/arrived states doesn't trigger a phantom walk cycle.
                    _player.IsMoving = len2 > 0.0025f;
                    // Phase 21-SC-SCROLL-CLICKLOOT — DS1 has no walk-over
                    // auto-pickup. Items on the ground stay there until the
                    // player CLICKS them (gold is the lone exception, but
                    // gold is awarded directly via CreditGoldFromKill on
                    // enemy death and never enters a LootPile in the first
                    // place — see line 5967 / 7019). Removed call to
                    // TryAutoPickup; the helper is preserved for click-
                    // dispatch use in TryClickToLoot.

                    // Phase 12-SC-1 — pending click-attack drive. If the click
                    // landed beyond reach we latched _pendingAttackTarget and
                    // pointed the follower at an approach point. Each tick:
                    // re-pin the target (it may have wandered), check reach,
                    // fire the swing when in range, or drop the latch if the
                    // target died/ran away.
                    if (_pendingAttackTarget is { } pat)
                    {
                        if (pat.IsDead || pat.Actor.Combat.IsDead)
                        {
                            _pendingAttackTarget = null;
                        }
                        else
                        {
                            var atk = GetPlayerAttackStats();
                            float r = PlayerMeleeReach(atk);
                            var tp = pat.CurrentTransform.Translation;
                            float ddx = after.X - tp.X;
                            float ddz = after.Z - tp.Z;
                            float pdist = MathF.Sqrt(ddx * ddx + ddz * ddz);
                            if (pdist > MeleeChaseGiveUp)
                            {
                                Console.WriteLine(
                                    $"click-attack: gave up on {pat.Actor.Template.Name} " +
                                    $"(dist={pdist:F1}u)");
                                _pendingAttackTarget = null;
                            }
                            else if (pdist <= r)
                            {
                                PerformPlayerSwing(pat, atk);
                                _pendingAttackTarget = null;
                            }
                            else
                            {
                                _playerFollower.SetTarget(ComputeApproachPoint(after, tp, r));
                            }
                        }
                    }

                    // Phase 16b — passive HP/MP regen. Rates come from formulas.gas
                    // (lr_unit/lr_period and mr_unit/mr_period; STR/INT-scaled). At
                    // 10/10/10 a fresh hero gains 0.25 HP/sec and 0.333 MP/sec, so a
                    // full HP refill takes ~3min and a full MP refill ~90s — slow
                    // enough that you can't tank by waiting between hits in a fight,
                    // fast enough to recover between encounters without a healer.
                    if (_formulas is not null)
                    {
                        var stats  = _player.Actor.Stats;
                        var combat = _player.Actor.Combat;
                        combat.Heal       (_formulas.LifeRecoveryRate(stats.Strength)    * (float)stepSec);
                        combat.RestoreMana(_formulas.ManaRecoveryRate(stats.Intelligence) * (float)stepSec);
                    }
                    // Phase 17a — countdown the spell cooldown on the same 20Hz
                    // tick the rest of player state runs on. Trip-key checks in
                    // TryClickToCast read CooldownRemaining off the spellbook.
                    _playerSpellbook?.Tick((float)stepSec);
                }
            }
            // Phase 9-SC-10b — render-state interpolation for the PC. Lerp
            // prev→next by the leftover accumulator (clamped 0..1). Yaw is
            // composed from the lerped facing vector so the body never wraps
            // through 180° on heading reversals.
            if (_player is not null && !_player.IsDead && _playerRenderInit)
            {
                float alpha = (float)Math.Min(1.0, _actorTickAccumulator / (1.0 / SkritInstance.FramesPerSecond));
                var pos = Vector3.Lerp(_playerRenderPosPrev, _playerRenderPosNext, alpha);
                var face = Vector3.Lerp(_playerRenderFacingPrev, _playerRenderFacingNext, alpha);
                if (face.LengthSquared() > 1e-6f) face = Vector3.Normalize(face);
                else face = _playerRenderFacingNext;
                float pyaw = MathF.Atan2(face.X, face.Z);
                _player.CurrentTransform =
                    Matrix4x4.CreateRotationY(pyaw) *
                    Matrix4x4.CreateTranslation(pos);
            }
            foreach (var s in _actors)
            {
                // Phase 12-SC-4 — dead actors keep ticking so chore_die can play
                // through; the IsMoving/walk override is suppressed and the time
                // is clamped to the clip's last frame so the corpse holds its
                // final pose instead of looping the death animation.
                bool isDead = s.IsDead;
                // 21d-2a-vi: compute the EFFECTIVE clip index (same logic the draw
                // loop uses) so AnimTime resets cleanly on idle↔walk swaps. Reading
                // only CurrentClipIndex meant the walk cycle started from whatever
                // phase the idle anim happened to be at, which read as a glitch.
                int idx = s.Actor.CurrentClipIndex;
                // Phase 17-SC-C — a pinned chore override (cast / swing / die)
                // wins over the IsMoving walk swap. Otherwise a player who casts
                // while walking would never see chore_magic — the walk clip masks
                // it for its full 0.7s duration.
                if (!isDead && s.IsMoving && !s.Actor.Host.IsOverrideActive
                    && s.Actor.WalkClipIndex >= 0 && s.Actor.WalkClipIndex < s.Actor.Clips.Length)
                    idx = s.Actor.WalkClipIndex;
                if (idx != s.LastClipIndex)
                {
                    s.LastClipIndex = idx;
                    s.AnimTime = 0;
                }
                if (s.Actor.Clips.Length > 0)
                {
                    var clip = s.Actor.Clips[Math.Min(idx, s.Actor.Clips.Length - 1)];
                    if (clip.AnimLength > 0f)
                    {
                        if (isDead)
                        {
                            float endHold = clip.AnimLength - 0.01f;
                            if (s.AnimTime < endHold) s.AnimTime = Math.Min(s.AnimTime + dt, endHold);
                        }
                        else if (s.Actor.Host.IsOverrideActive)
                        {
                            // Phase 21-SC-BARREL-FOLD — chore overrides
                            // (chore_attack, chore_magic, etc.) get the
                            // same end-hold treatment dead actors do.
                            // PrepNextSwingClip pads override duration past
                            // the clip's natural length so there's a beat
                            // of follow-through; without the clamp the clip
                            // would wrap and replay during that pad window,
                            // which reads as a glitchy "swinging-twice"
                            // visual.
                            float endHold = clip.AnimLength - 0.01f;
                            if (s.AnimTime < endHold) s.AnimTime = Math.Min(s.AnimTime + dt, endHold);
                        }
                        else
                        {
                            s.AnimTime += dt;
                        }
                    }
                }
            }
        }

        // Phase 21a-3 — periodic region-membership check. We scan only every
        // RegionCheckIntervalSec because the per-snode XZ scan is O(N) over
        // every loaded snode; firing it from every render frame would burn
        // ~3-5k distance comparisons at 60Hz for a payoff that only matters
        // on a region crossing (a once-per-minute event in normal play). When
        // the player's nearest snode resolves to a different region than
        // _currentPlayerRegion, OnPlayerRegionChanged extends the loaded ring.
        if (_player is not null && _snodeRegionLookup.Length > 0)
        {
            _regionCheckAccumulator += (float)dt;
            if (_regionCheckAccumulator >= RegionCheckIntervalSec)
            {
                _regionCheckAccumulator = 0f;
                var here = RegionAtWorldPos(_player.CurrentTransform.Translation);
                if (here is not null && !string.Equals(here, _currentPlayerRegion, StringComparison.OrdinalIgnoreCase))
                {
                    OnPlayerRegionChanged(here);
                }
            }
        }
    }

    // Phase 13a — spawn a single Farmboy PC at the NPC centroid (snapped to the
    // nav mesh when present) using the existing ActorSpawner. Template 'farmboy'
    // is the canonical DS1 male-human hero archetype; aspect.model and the usual
    // chore_default skrit resolve through the same specializes chain every NPC
    // uses, so no special-casing in spawn. The actor just doesn't get a wander
    // follower — 13c will add click-to-move.
    /// <summary>21d-2a-viii-b — called from <see cref="OnUpdate"/>. When the
    /// creator panel resolves (Begin or Cancel), spawns the PC with the picked
    /// (or env-default) variant and clears the pending args. No-op otherwise.</summary>
    /// <summary>Phase 23-SC-OPTIONS-A through F — drain the options
    /// menu's edge-trigger flags. OK commits staged → live and
    /// applies the runtime hooks (audio volumes are the only fully-
    /// wired ones in this slice cluster; resolution + gamma + etc.
    /// persist into Live but require the future prefs.gas /
    /// DungeonSiege.ini writeback splinter to actually take effect
    /// on next launch). Cancel discards staged. Defaults resets the
    /// active tab from /config/options.gas defaults inline.</summary>
    /// <summary>Phase 24-MAINMENU step 5+6 — translate main menu button
    /// actions into runtime side-effects. Each branch is the click handler
    /// for one of main_menu.gas's seven notify() names.</summary>
    private void FlushMainMenu()
    {
        if (!_bootMode) return;
        if (_frontendScene is null
            || _frontendScene.State != Hud.FrontendScene.ScreenState.MainMenu) return;
        // Phase 24-POLISH-A — frontend music. /ui/config/frontend_music/
        // frontend_music.gas authors `sample = s_m_Frontend.mp3`.
        // PlayMusicTrack short-circuits when the active track basename
        // matches, so calling every frame in MainMenu state is idempotent.
        // Splash + logo-drop run silent (matches DS1's tempo where the
        // logo settle is the cue for the music to enter).
        PlayMusicTrack("Frontend");
        // The menu only takes input while no sub-screen / dialog is on top.
        _mainMenu.IsActive = !_aboutOpen && !_optionsMenu.IsOpen && !_creator.IsOpen;
        var act = _mainMenu.ConsumeAction();
        // Phase 24-POLISH-C — play the click cue on every consumed action
        // including stubs, so the user gets audible feedback for the press
        // even when the slot is "splinter pending" no-op.
        if (act != MainMenuPanel.Action.None) _audio?.Play(SfxFrontendBigButton);
        switch (act)
        {
            case MainMenuPanel.Action.None:
                break;
            case MainMenuPanel.Action.Options:
                _optionsMenu.Open();
                break;
            case MainMenuPanel.Action.About:
                _aboutOpen = true;
                break;
            case MainMenuPanel.Action.Exit:
                _window.Close();
                break;
            case MainMenuPanel.Action.SinglePlayer:
                // Phase 27-SP-FLYOUT — drive the Main → SP transition.
                // FrontendScene plays mainmenu_mm2sp / menubars_mm2sp;
                // _stateTime auto-advances to SinglePlayer at MmToSpDur.
                // _spMenu activates only after the transition settles
                // (handled in FlushSinglePlayerMenu) so clicks during
                // the fly-out don't fire the new screen.
                _frontendScene?.SetState(Hud.FrontendScene.ScreenState.MainMenuToSp);
                _mainMenu.IsActive = false;
                _mainMenu.ClearHover();
                break;
            case MainMenuPanel.Action.Continue:
            case MainMenuPanel.Action.Multiplayer:
            case MainMenuPanel.Action.Credits:
                // Stubs — SC-MAINMENU-CONTINUE / -MULTIPLAYER / -CREDITS
                // splinters track the routing for these. Each click is
                // consumed but no-op for now; clear hover so the button
                // doesn't read as "still selectable" after.
                Console.WriteLine($"  main menu: '{act}' click — splinter SC-MAINMENU-{act.ToString().ToUpperInvariant()} pending");
                _mainMenu.ClearHover();
                break;
        }
    }

    /// <summary>Phase 27-SP-FLYOUT — translate Single Player sub-menu
    /// button actions into runtime side-effects. Mirror of
    /// <see cref="FlushMainMenu"/> for the SP submenu screen.</summary>
    private void FlushSinglePlayerMenu()
    {
        if (!_bootMode) return;
        if (_frontendScene is null) return;
        var state = _frontendScene.State;
        // _spMenu is active only when the SP screen has settled. During
        // the MainMenuToSp / SinglePlayerToMm transitions input is
        // suppressed so a fast click doesn't fire on a half-flown panel.
        _spMenu.IsActive = state == Hud.FrontendScene.ScreenState.SinglePlayer
                           && !_aboutOpen && !_optionsMenu.IsOpen && !_creator.IsOpen;
        if (state != Hud.FrontendScene.ScreenState.SinglePlayer) return;
        var act = _spMenu.ConsumeAction();
        if (act != SinglePlayerMenuPanel.Action.None) _audio?.Play(SfxFrontendBigButton);
        switch (act)
        {
            case SinglePlayerMenuPanel.Action.None:
                break;
            case SinglePlayerMenuPanel.Action.Back:
                // Reverse-transition back to MainMenu. _stateTime
                // auto-advances to MainMenu at SpToMmDur.
                _frontendScene.SetState(Hud.FrontendScene.ScreenState.SinglePlayerToMm);
                _spMenu.IsActive = false;
                _spMenu.ClearHover();
                break;
            case SinglePlayerMenuPanel.Action.NewGame:
                // Phase 28-CD-FLYOUT — SP → Character Creator transition.
                // mainmenu_sng2cd / menubars_lm2cd / backbutton_b2pn /
                // heromenu_begin run together; auto-advances to
                // CharacterSelect at SpToCdDur.
                _frontendScene.SetState(Hud.FrontendScene.ScreenState.SinglePlayerToCd);
                _spMenu.IsActive = false;
                _spMenu.ClearHover();
                break;
            case SinglePlayerMenuPanel.Action.LoadGame:
                // Stub — SC-MAINMENU-LOADGAME wires the SaveStore-backed
                // load_game.gas screen.
                Console.WriteLine($"  sp menu: '{act}' click — splinter SC-MAINMENU-LOADGAME pending");
                _spMenu.ClearHover();
                break;
        }
    }

    /// <summary>Phase 28-CD-FLYOUT — translate Character Creator
    /// Previous / Next clicks into runtime side-effects. Mirror of
    /// <see cref="FlushSinglePlayerMenu"/>.</summary>
    private void FlushCharacterSelectMenu()
    {
        if (!_bootMode) return;
        if (_frontendScene is null) return;
        var state = _frontendScene.State;
        // _csMenu is active only while CharacterSelect has settled. During
        // SinglePlayerToCd / CharacterSelectToSp transitions input is
        // suppressed so a fast click doesn't fire on a half-flown panel.
        _csMenu.IsActive = state == Hud.FrontendScene.ScreenState.CharacterSelect
                           && !_aboutOpen && !_optionsMenu.IsOpen;
        if (state != Hud.FrontendScene.ScreenState.CharacterSelect) return;
        var act = _csMenu.ConsumeAction();
        if (act != CharacterSelectMenuPanel.Action.None) _audio?.Play(SfxFrontendBigButton);
        switch (act)
        {
            case CharacterSelectMenuPanel.Action.None:
                break;
            case CharacterSelectMenuPanel.Action.Previous:
                // notify(back_to_single_player) per character_select.gas.
                // Set Cancelled on the creator so FlushCreator's existing
                // env-var-fallback path picks it up if a region launch
                // ever happens via this panel.
                _creator.Cancelled = true;
                _creator.IsOpen = false;
                _frontendScene.SetState(Hud.FrontendScene.ScreenState.CharacterSelectToSp);
                _csMenu.IsActive = false;
                _csMenu.ClearHover();
                break;
            case CharacterSelectMenuPanel.Action.Next:
                // notify(on_change_map) per character_select.gas — should
                // launch the map_chooser / LoadPlayActors flow. Set the
                // creator's Confirmed flag so FlushCreator picks it up
                // on the next frame and runs TrySpawnPlayerWithPicker.
                // The actual region-launch wiring (resolving fh_r1 paths
                // from _ds1ResourcesDir without --play-region CLI args)
                // is splinter SC-CD-NEXT-LAUNCH.
                _creator.Confirmed = true;
                _creator.IsOpen = false;
                Console.WriteLine("  cs menu: 'Next' click — Confirmed=true, splinter SC-CD-NEXT-LAUNCH for region launch");
                _csMenu.ClearHover();
                break;
        }
    }

    private void FlushOptionsMenu()
    {
        // Phase 23-SC-OPTIONS-FOLD — wire the live-apply event once
        // (idempotent so a hot-reload of the panel doesn't double-
        // subscribe). Slider drags inside the Audio tab fire this so
        // the user hears the volume change during drag, not just on OK.
        if (!_optionsAudioHookWired)
        {
            _optionsMenu.AudioStagedChanged += ApplyStagedAudio;
            _optionsAudioHookWired = true;
        }
        if (_optionsMenu.ConfirmedThisFrame)
        {
            _optionsMenu.CommitStaged();
            ApplyOptionsAudio();
            ApplyOptionsRuntime();
            _optionsMenu.ClearEdgeFlags();
        }
        else if (_optionsMenu.CancelledThisFrame)
        {
            // Re-sync staged from live so a re-Open starts clean.
            _optionsMenu.SyncStagedFromLive();
            _optionsMenu.ClearEdgeFlags();
        }
        else if (_optionsMenu.DefaultsRequestedThisFrame)
        {
            _optionsMenu.ApplyDefaultsForActiveTab();
            // Audio defaults need a live re-apply since the user is
            // listening as they reset; other tabs can wait for OK.
            if (_optionsMenu.ActiveTab == OptionsMenuPanel.Tab.Audio)
                ApplyOptionsAudio();
            // Input + Game tab Defaults reset runtime-applied knobs too —
            // a "reset to defaults" that needs an OK click to take effect
            // is confusing UX. Audio is the same precedent.
            if (_optionsMenu.ActiveTab == OptionsMenuPanel.Tab.Input
                || _optionsMenu.ActiveTab == OptionsMenuPanel.Tab.Game)
                ApplyOptionsRuntime();
            _optionsMenu.ClearEdgeFlags();
        }
    }

    /// <summary>Phase 23-SC-OPTIONS-FOLD2 — push the menu's non-audio runtime
    /// knobs to the live engine. Today: Input tab (camera invert + mouse
    /// sensitivity) and Game tab (Show Framerate). Resolution/Gamma/Shadows
    /// etc. stay persist-only until splinter SC-OPTIONS-VIDEO-RUNTIME wires
    /// them. Game Speed is deferred — it threads through actor + particle
    /// + nav tick scaling and needs more care than this fold.
    ///
    /// Reads Staged not Live because Defaults updates Staged only (Live
    /// stays put until OK commits). Audio's live-apply has the same
    /// contract — Defaults must take effect immediately for the user to
    /// understand the click did anything. After CommitStaged on the OK
    /// path, Staged == Live, so reading either is equivalent there.</summary>
    private void ApplyOptionsRuntime()
    {
        var s = _optionsMenu.Staged;
        if (_camera is not null)
        {
            _camera.InvertX = s.CameraInverseX;
            _camera.InvertY = s.CameraInverseY;
            // DS1's slider is 0..100; map so default 50 → 1.0x (no behavior
            // change vs pre-fold), 100 → 2.0x (fast), 0 → tiny floor (still
            // technically usable rather than completely frozen). Linear so
            // the value-readout above the slider tracks intuition.
            float mouse = MathF.Max(0.1f, s.MouseSensitivity / 50f);
            _camera.SensitivityScale = mouse;
        }
        _showFps = s.ShowFramerate;
    }

    /// <summary>Phase 23-SC-OPTIONS-C — push the menu's audio settings
    /// to the live engine. SoundEnabled gates everything via master;
    /// MasterVolume rides on OpenAL's listener gain (covers SFX +
    /// ambient + music in one knob); MusicVolume is a separate
    /// MusicPlayer source gain stacked on top; SFX volume caps the
    /// per-Play SFX mix below master. DS1's 0..127 range maps to
    /// our [0,1] OpenAL gains.</summary>
    private void ApplyOptionsAudio() => ApplyAudioVolumes(_optionsMenu.Live);

    /// <summary>Phase 23-SC-OPTIONS-FOLD — live-preview applier for
    /// Audio-tab slider drags. Reads the staged (in-flight) settings
    /// instead of Live so the user hears the volume change during
    /// drag instead of waiting for OK. Cancel reverts by re-syncing
    /// Staged from Live and re-firing this through the event.</summary>
    private void ApplyStagedAudio() => ApplyAudioVolumes(_optionsMenu.Staged);

    private void ApplyAudioVolumes(SiegeFX.Runtime.Render.Hud.OptionsMenuPanel.Settings s)
    {
        if (_audio is not null)
        {
            float master = s.SoundEnabled ? s.MasterVolume / 127f : 0f;
            _audio.SetMasterVolume(master);
            _audio.SetSfxVolume(s.SfxVolume / 127f);
        }
        _music?.SetVolume(s.MusicVolume / 127f);
    }

    private void FlushCreator()
    {
        if (_pendingSpawner is null) return;
        if (_creator.IsOpen) return;
        var spawner = _pendingSpawner;
        var navMesh = _pendingNavMesh;
        _pendingSpawner = null;
        _pendingNavMesh = null;
        // Phase 22-SC-MUSIC-FOLD — swap menu music straight to the
        // active region's mood-driven track on Begin/Cancel. Pre-fold
        // called PlayMusicTrack(null) and relied on OnPlayerRegionChanged
        // to bring music back, but the player spawns INSIDE the launch
        // region — region-changed never fires — so the post-creator
        // gameplay was silent until the user crossed a region boundary.
        // ApplyMoodMusic (which remembers the mood from the
        // LoadPlayActors-time apply at line ~3252) reseats the
        // standard/battle track in one go; PlayMusicTrack's basename
        // equality short-circuits if the region track is already what's
        // playing. Falls through to a hard stop if no mood resolved
        // (viewer modes, regions with no mood definitions).
        if (_activeMood is not null) ApplyMoodMusic(_activeMood);
        else PlayMusicTrack(null);
        if (_creator.Confirmed)
        {
            var name = string.IsNullOrEmpty(_creator.HeroName) ? null : _creator.HeroName;
            Console.WriteLine($"  creator: Begin — name='{name ?? "<unset>"}'");
            TrySpawnPlayerWithPicker(spawner, navMesh, _creator.Picker, name);
        }
        else if (_creator.Cancelled)
        {
            Console.WriteLine("  creator: Cancel — falling through to env-var defaults");
            TrySpawnPlayerWithPicker(spawner, navMesh, HeroVariantPicker.FromEnv(), heroName: null);
        }
    }

    private void TrySpawnPlayer(ActorSpawner spawner, SiegeFX.Core.Nav.NavMesh? navMesh)
    {
        if (_actors.Count == 0)
        {
            Console.WriteLine("  player: no NPCs spawned, skipping player spawn (nothing to anchor against)");
            return;
        }

        // 21d-2a-viii-b — gate the spawn behind the character creator UI.
        // SIEGEFX_CREATOR=0 (or any non-empty value other than "1") bypasses
        // the panel and uses env-var picks directly — keeps headless flows
        // (test-all option 61, CI smoke runs) deterministic.
        var creatorEnv = Environment.GetEnvironmentVariable("SIEGEFX_CREATOR");
        bool useCreator = string.Equals(creatorEnv, "1", StringComparison.Ordinal);
        if (useCreator)
        {
            _pendingSpawner = spawner;
            _pendingNavMesh = navMesh;
            _creator.Reset();
            // Phase 22-SC-MUSIC-B — kick the menu music as the creator
            // panel comes up. DS1's frontend track is s_m_frontend.mp3
            // (~986 KB / ~2:30). Stops automatically when FlushCreator
            // commits Begin/Cancel and a region track takes over.
            PlayMusicTrack("frontend");
            Console.WriteLine("  player: character creator open (Begin to spawn, Cancel for env-var defaults)");
            return;
        }
        TrySpawnPlayerWithPicker(spawner, navMesh, HeroVariantPicker.FromEnv(), heroName: null);
    }

    /// <summary>Phase 21d-2a-viii-e — lazily build the heromenu chrome the
    /// first frame the creator wants to draw it. Both pre-conditions
    /// (<see cref="_playResolver"/> set by <see cref="LoadPlayActors"/> and
    /// the GL context up via <see cref="_gl"/>) hold by the time the creator
    /// is open, but the order with respect to <see cref="OnLoad"/> is
    /// data-flow-driven, not constructor-driven, so the lazy init avoids
    /// a brittle eager wire-up. A failure to load the chrome (missing
    /// .raw / .asp in the open tank) surfaces once on stderr and leaves
    /// <see cref="_frontendScene"/> null — the panel falls back to its
    /// scaffolded BarRenderer fills.</summary>
    private void EnsureFrontendScene()
    {
        if (_frontendScene is not null) return;
        if (_gl is null || _playResolver is null) return;
        try
        {
            _frontendScene = new FrontendScene(_gl, _playResolver);
            _frontendScene.SetState(FrontendScene.ScreenState.CharacterSelect);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  frontend scene load failed: {ex.Message} (falling back to scaffold)");
            _frontendScene = null;
        }
    }

    // Phase 24-MAINMENU step 1+2 — splash texture cache. Loaded lazily as the
    // state machine enters each splash; both sets stay resident afterward
    // (~1.6 MB total, never enough to matter). Keyed by texture name so the
    // same lookup pattern works once batch 2 wires logo.asp's PRS clip
    // textures through the same path.
    private readonly Dictionary<string, GlTexture?> _splashTexCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Phase 24-MAINMENU step 1+2 — render the splash → main menu
    /// frame for boot mode. Drives the FrontendScene state machine via
    /// Tick(dt) and renders the active splash's three panels with the
    /// state-driven alpha. Subsequent batches replace the FadeOut beat
    /// with the gpg_intro.bik playback + logo drop, and the MainMenu
    /// state with the real 7-button frontend chrome.</summary>
    private void DrawBootScene(float dt, int viewportW, int viewportH)
    {
        if (_frontendScene is null || _barRenderer is null || _iconRenderer is null
            || _textRenderer is null || _playResolver is null) return;
        // Phase 24-MAINMENU step 1+2-FOLD — Tick moved to OnUpdate so the
        // splash advances even when Render is paused (e.g. window minimized).
        // Solid black backdrop — the splash strip is centered on a 640×480
        // authored canvas and we letterbox the rest.
        _textRenderer.BeginPass();
        _barRenderer.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH,
            new Vector4(0f, 0f, 0f, 1f));

        var prefix = _frontendScene.IntroTexturePrefix;
        var alpha  = _frontendScene.IntroAlpha;
        if (prefix is not null && alpha > 0f)
        {
            // Authored canvas is 640×480; uniform-scale by the smaller of
            // the two viewport-to-authored ratios so the splash never
            // crops, then center on whatever space remains.
            float s = MathF.Min(viewportH / 480f, viewportW / 640f);
            int dx = (viewportW - (int)MathF.Round(640 * s)) / 2;
            int dy = (viewportH - (int)MathF.Round(480 * s)) / 2;
            // intro_microsoft.gas / intro_gaspowered.gas authored rects:
            //   panel_1 = 0,0,256,256 → b_gui_nis_<set>_01
            //   panel_2 = 256,0,512,256 → b_gui_nis_<set>_02
            //   panel_3 = 512,0,640,256 → b_gui_nis_<set>_03 (memory says
            //              .raw is 256×128; rect is 128×256 — host stretches
            //              non-uniformly. Visual verification pending in
            //              batch 1 self-test; flag in
            //              project_siegefx_frontend_assets.md if wrong.)
            DrawSplashPanel(prefix + "01", 0,   0, 256, 256, s, dx, dy, alpha, viewportW, viewportH);
            DrawSplashPanel(prefix + "02", 256, 0, 256, 256, s, dx, dy, alpha, viewportW, viewportH);
            DrawSplashPanel(prefix + "03", 512, 0, 128, 256, s, dx, dy, alpha, viewportW, viewportH);
        }
        // Phase 24-MAINMENU step 1-6 — once the splash sequence drains
        // past the Bink-stub fade, hand drawing to the existing
        // FrontendScene composer for IntroLogoDrop (backdrop + sides
        // + logo.asp with logo-enter.prs) AND for MainMenu state. The
        // chrome is correct DS1 art but currently in the wrong pose
        // (cd-state placeholder until splinter SC-MAINMENU-CHROME-PROPER
        // wires the real main-menu-pose PRS clips + subset masks); it
        // still reads as "DS1 chrome" rather than a black void, which
        // the user prefers per the polish triage even before the pose
        // pass lands.
        else if (_frontendScene.State == Hud.FrontendScene.ScreenState.IntroLogoDrop
              || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroLogoHold
              || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroLogoExit
              || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroMenuFlyIn
              || _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenu
              || _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenuToSp
              || _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayer
              || _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayerToMm
              || _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayerToCd
              || _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect
              || _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelectToSp)
        {
            // Phase 26-VIEWPORT — clip rendering to the chrome's letterbox
            // area so meshes that extend BEYOND backdrop's authored bounds
            // (pillars at ±2.17 vs backdrop's ±1.64; mainmenu/menubars at Y
            // up to 5.18 in bind pose) don't bleed into the side bars or
            // above/below the visible screen frame. DS1 was authored 4:3
            // with the assumption that the viewport crops the outer
            // fringes; at modern 16:9 / ultrawide / 4K resolutions we have
            // to enforce that crop explicitly.
            //
            // Letterbox aspect = backdrop's authored aspect (~1.32, very
            // close to 4:3). On widescreen the chrome fills vertical
            // height, the sides letterbox to black. On taller-than-4:3
            // the chrome fills horizontal width, top/bottom letterbox.
            const float chromeAspect = 1.32f;
            int boxW, boxH;
            float vpAspect = viewportW / (float)viewportH;
            if (vpAspect > chromeAspect)
            {
                boxH = viewportH;
                boxW = (int)(viewportH * chromeAspect);
            }
            else
            {
                boxW = viewportW;
                boxH = (int)(viewportW / chromeAspect);
            }
            int boxX = (viewportW - boxW) / 2;
            // glScissor uses framebuffer coords (origin BOTTOM-left); our
            // y values are top-left so we have to flip when computing scissor Y.
            int boxYTop = (viewportH - boxH) / 2;
            int boxYGlBottom = viewportH - boxYTop - boxH;
            _gl?.Enable(EnableCap.ScissorTest);
            _gl?.Scissor(boxX, boxYGlBottom, (uint)boxW, (uint)boxH);
            _frontendScene.Draw(viewportW, viewportH);
            if (_frontendScene.State == Hud.FrontendScene.ScreenState.MainMenu)
            {
                // MainMenuPanel owns hit-testing + the click→action pipeline
                // for all 7 buttons; it renders nothing visual now (Phase 26-
                // ARTMAP). The 5 menubars buttons render through menubars.asp
                // chrome inside FrontendScene.Draw above; the EXIT button
                // renders through backbutton.asp + art_mapping.gas overrides
                // immediately below.
                _mainMenu.Draw(_barRenderer, _textRenderer, _iconRenderer, viewportW, viewportH);
                // Phase 26-ARTMAP — render the EXIT button via the proper
                // DS1 asset chain. MainMenuPanel exposes the screen rect +
                // hover/press state; FrontendScene runs the asp draw with
                // art_mapping.gas's [button_exit] texture-swap recipe.
                if (_mainMenu.TryGetButtonStateAndRect(
                        Hud.MainMenuPanel.Action.Exit,
                        viewportW, viewportH,
                        out int ex, out int ey, out int ew, out int eh,
                        out bool eHover, out bool ePress))
                {
                    _frontendScene.DrawExitButton(viewportW, viewportH,
                        ex, ey, ew, eh, eHover, ePress);
                }
                // Phase 27-SP-FLYOUT — hover overlay on every main menu
                // button so the user gets visual feedback under the cursor.
                // Subtle white tint (alpha .12) layered over the wood
                // chrome — reads as a soft glow without obscuring the
                // engraved label. Same overlay applies to EXIT.
                DrawMainMenuHoverOverlays(viewportW, viewportH);
                if (_aboutOpen)
                    DrawAboutOverlay(viewportW, viewportH);
            }
            else if (_frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayer
                  || _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenuToSp
                  || _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayerToMm)
            {
                // Phase 27-SP-FLYOUT-FIX3 — per-button asp render via
                // art_mapping.gas. NEW GAME / LOAD GAME each render
                // through DrawMenubarsButton, BACK through
                // DrawSpBackButton. One asp draw per widget with ONLY
                // its art_mapping-specified subsets — adjacent atlas
                // regions can't bleed because their subsets are masked.
                if (_spMenu.TryGetButtonStateAndRect(
                        Hud.SinglePlayerMenuPanel.Action.NewGame,
                        viewportW, viewportH,
                        out int ngx, out int ngy, out int ngw, out int ngh,
                        out bool ngHover, out bool ngPress))
                {
                    _frontendScene.DrawMenubarsButton(viewportW, viewportH,
                        ngx, ngy, ngw, ngh, ngHover, ngPress, "button_start_new_game");
                }
                if (_spMenu.TryGetButtonStateAndRect(
                        Hud.SinglePlayerMenuPanel.Action.LoadGame,
                        viewportW, viewportH,
                        out int lgx, out int lgy, out int lgw, out int lgh,
                        out bool lgHover, out bool lgPress))
                {
                    _frontendScene.DrawMenubarsButton(viewportW, viewportH,
                        lgx, lgy, lgw, lgh, lgHover, lgPress, "button_load_game");
                }
                if (_spMenu.TryGetButtonStateAndRect(
                        Hud.SinglePlayerMenuPanel.Action.Back,
                        viewportW, viewportH,
                        out int bx, out int by, out int bw, out int bh,
                        out bool bHover, out bool bPress))
                {
                    _frontendScene.DrawSpBackButton(viewportW, viewportH,
                        bx, by, bw, bh, bHover, bPress);
                }
                // Hover overlays for the SP submenu buttons (only meaningful
                // in the settled SinglePlayer state where _spMenu has
                // hover state).
                if (_frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayer)
                    DrawSpMenuHoverOverlays(viewportW, viewportH);
            }
            else if (_frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayerToCd
                  || _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect
                  || _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelectToSp)
            {
                // Phase 28-CD-FLYOUT — render the Previous/Next nav
                // buttons via art_mapping.gas[button_previous/next]
                // recipes. Same per-button pattern as the SP submenu's
                // BACK button. Hit-testing settles only after the
                // transition completes (panel sets IsActive=false
                // during transitions); rendering during transitions
                // keeps the visual stable.
                if (_csMenu.TryGetButtonStateAndRect(
                        Hud.CharacterSelectMenuPanel.Action.Previous,
                        viewportW, viewportH,
                        out int px, out int py, out int pw, out int ph,
                        out bool pHover, out bool pPress))
                {
                    _frontendScene.DrawPreviousButton(viewportW, viewportH,
                        px, py, pw, ph, pHover, pPress);
                }
                if (_csMenu.TryGetButtonStateAndRect(
                        Hud.CharacterSelectMenuPanel.Action.Next,
                        viewportW, viewportH,
                        out int nx, out int ny, out int nw, out int nh,
                        out bool nHover, out bool nPress))
                {
                    _frontendScene.DrawNextButton(viewportW, viewportH,
                        nx, ny, nw, nh, nHover, nPress);
                }
                if (_frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect)
                {
                    DrawCsMenuHoverOverlays(viewportW, viewportH);
                    // Phase 29-CD-CREATOR-FIX2 — Reset() moved to the
                    // state-edge handler in OnUpdate so HeroName isn't
                    // clobbered every frame while typing.
                    // Build hero preview lazily once the deps are ready
                    // (LoadPlayActors populates _skinShader / _templateStore /
                    // _playResolver — but at boot only _playResolver is set,
                    // so the preview renders empty until a region loads).
                    if (_heroPreview is null && _gl is not null && _skinShader is not null
                        && _templateStore is not null && _playResolver is not null)
                    {
                        _heroPreview = new HeroPreviewRenderer(
                            _gl, _skinShader, _templateStore, _playResolver,
                            ResolveActorTexture);
                    }
                    if (_heroPreview is not null)
                    {
                        _heroPreview.EnsurePreview(_creator.Picker);
                        _heroPreview.Tick(dt);
                        _heroPreview.Draw(viewportW, viewportH);
                    }
                    // Per-button asp render + hover overlays + name overlay.
                    DrawCharacterCreatorOverlays(viewportW, viewportH);
                }
            }
            // Phase 26-VIEWPORT — disable scissor so any HUD layers that
            // intentionally fill the framebuffer (e.g. About overlay's
            // 60% black scrim) aren't clipped to the chrome letterbox.
            _gl?.Disable(EnableCap.ScissorTest);
            // Phase 27-SP-FLYOUT — sword cursor in the frontend menu.
            // Render only in the post-logo chrome states (the same set
            // EnsureOsCursorHidden uses to hide the OS cursor) so the
            // intro splash + sword-drop frames keep the OS cursor and
            // we don't double-render. Always the Pointer state — combat
            // / talk / loot icons make no sense outside gameplay.
            var fs = _frontendScene.State;
            bool inMenu = fs == Hud.FrontendScene.ScreenState.MainMenu
                       || fs == Hud.FrontendScene.ScreenState.MainMenuToSp
                       || fs == Hud.FrontendScene.ScreenState.SinglePlayer
                       || fs == Hud.FrontendScene.ScreenState.SinglePlayerToMm
                       || fs == Hud.FrontendScene.ScreenState.SinglePlayerToCd
                       || fs == Hud.FrontendScene.ScreenState.CharacterSelect
                       || fs == Hud.FrontendScene.ScreenState.CharacterSelectToSp
                       || fs == Hud.FrontendScene.ScreenState.IntroMenuFlyIn;
            if (inMenu)
            {
                EnsureCursorTextures();
                if (_iconRenderer is not null && !_mouseLookActive && _cursorPointer is not null)
                {
                    const int big = 64, hsBigX = 21, hsBigY = 13;
                    int cx = (int)_currentMousePos.X - hsBigX;
                    int cy = (int)_currentMousePos.Y - hsBigY;
                    _iconRenderer.DrawIcon(viewportW, viewportH, _cursorPointer, cx, cy, big, big, Vector4.One);
                }
            }
        }
        _textRenderer.EndPass();
    }

    /// <summary>Phase 24-MAINMENU step 6 — placeholder About sub-screen.
    /// Modal dim + a centered card with title + copyright text + a "click
    /// anywhere to close" hint. Future splinter SC-MAINMENU-ABOUT-RAW will
    /// load the shipped <c>about_dialog.gas</c> layout + swap the card for
    /// DS1's wood-bordered chrome.</summary>
    private void DrawAboutOverlay(int viewportW, int viewportH)
    {
        if (_barRenderer is null || _textRenderer is null) return;
        // Modal scrim — same 60% black PauseMenu / OptionsMenu use, so
        // the About card reads as the topmost layer regardless of what's
        // behind it.
        _barRenderer.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH,
            new Vector4(0f, 0f, 0f, 0.60f));
        // Centered card sized at 800x600-authored 480x240 (so it scales
        // proportionally with the menu chrome).
        float scale = MathF.Min(viewportH / 600f, viewportW / 800f);
        int fontScale = Math.Max(1, (int)MathF.Round(scale));
        int cw = (int)MathF.Round(480 * scale);
        int ch = (int)MathF.Round(240 * scale);
        int cx = (viewportW - cw) / 2;
        int cy = (viewportH - ch) / 2;
        _barRenderer.DrawRect(viewportW, viewportH, cx, cy, cw, ch,
            new Vector4(0.10f, 0.06f, 0.04f, 0.95f));
        // Hand-rolled 1px border (matches MainMenuPanel.DrawBorder).
        var border = new Vector4(0.65f, 0.50f, 0.30f, 1f);
        _barRenderer.DrawRect(viewportW, viewportH, cx,          cy,            cw, 1,  border);
        _barRenderer.DrawRect(viewportW, viewportH, cx,          cy + ch - 1,   cw, 1,  border);
        _barRenderer.DrawRect(viewportW, viewportH, cx,          cy,            1,  ch, border);
        _barRenderer.DrawRect(viewportW, viewportH, cx + cw - 1, cy,            1,  ch, border);
        var ink    = new Vector4(0.95f, 0.85f, 0.65f, 1f);
        var inkDim = new Vector4(0.65f, 0.55f, 0.40f, 1f);
        // Multi-line text. Plain copperplate, scale_aware vertical stride.
        var lines = new[]
        {
            "About SiegeFX",
            "",
            "Open-source Dungeon Siege 1 reimplementation.",
            "MIT-licensed; ships no copyrighted assets.",
            "Requires the original Dungeon Siege game data.",
            "",
            "Dungeon Siege © 2002 Gas Powered Games / Microsoft.",
            "",
            "(click anywhere or press Esc to close)",
        };
        int lineH = 16 * fontScale;
        int totalH = lines.Length * lineH;
        int ty = cy + (ch - totalH) / 2;
        for (int i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            int tw = _textRenderer.MeasureWidth(s, fontScale);
            int tx = cx + (cw - tw) / 2;
            var color = (i == 0 || i == lines.Length - 1) ? ink : inkDim;
            _textRenderer.DrawString(viewportW, viewportH, s, tx, ty + i * lineH, color, fontScale);
        }
    }

    /// <summary>Phase 27-SP-FLYOUT — translucent white overlay on the
    /// hovered main menu button so the cursor leaves a visual trail
    /// over the wood-button chrome. Pressed buttons get a darker
    /// overlay so press is visible too. Alphas tuned to read clearly
    /// over the wood-orange chrome (0.12 was too subtle on the
    /// engraved label per user feedback).</summary>
    private void DrawMainMenuHoverOverlays(int viewportW, int viewportH)
    {
        if (_barRenderer is null) return;
        var actions = new[]
        {
            Hud.MainMenuPanel.Action.SinglePlayer,
            Hud.MainMenuPanel.Action.Multiplayer,
            Hud.MainMenuPanel.Action.Options,
            Hud.MainMenuPanel.Action.Continue,
            Hud.MainMenuPanel.Action.About,
            Hud.MainMenuPanel.Action.Exit,
        };
        var hoverTint = new Vector4(1f, 1f, 1f, 0.22f);
        var pressTint = new Vector4(0f, 0f, 0f, 0.30f);
        foreach (var a in actions)
        {
            if (!_mainMenu.TryGetButtonStateAndRect(a, viewportW, viewportH,
                    out int x, out int y, out int w, out int h,
                    out bool hov, out bool pr)) continue;
            if (pr) _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, pressTint);
            else if (hov) _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, hoverTint);
        }
    }

    /// <summary>Phase 27-SP-FLYOUT — same hover overlay treatment for
    /// the SP submenu's three buttons.</summary>
    private void DrawSpMenuHoverOverlays(int viewportW, int viewportH)
    {
        if (_barRenderer is null) return;
        var actions = new[]
        {
            Hud.SinglePlayerMenuPanel.Action.NewGame,
            Hud.SinglePlayerMenuPanel.Action.LoadGame,
            Hud.SinglePlayerMenuPanel.Action.Back,
        };
        var hoverTint = new Vector4(1f, 1f, 1f, 0.22f);
        var pressTint = new Vector4(0f, 0f, 0f, 0.30f);
        foreach (var a in actions)
        {
            if (!_spMenu.TryGetButtonStateAndRect(a, viewportW, viewportH,
                    out int x, out int y, out int w, out int h,
                    out bool hov, out bool pr)) continue;
            if (pr) _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, pressTint);
            else if (hov) _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, hoverTint);
        }
    }

    /// <summary>Phase 29-CD-CREATOR — render the 12 spinner-arrow
    /// buttons (per art_mapping.gas) + their hover overlays + the
    /// typed-name TextRenderer overlay. Caller must already have
    /// drawn the chrome (FrontendScene) and the live hero preview
    /// (HeroPreviewRenderer). Used both from the boot CharacterSelect
    /// chrome render block and from the in-region _creator.IsOpen
    /// block — same overlay layer in both cases.</summary>
    private void DrawCharacterCreatorOverlays(int viewportW, int viewportH)
    {
        if (_frontendScene is null) return;
        var actions = new[]
        {
            (Hud.CharacterCreatorPanel.Action.GenderLeft,  "button_gender_left"),
            (Hud.CharacterCreatorPanel.Action.GenderRight, "button_gender_right"),
            (Hud.CharacterCreatorPanel.Action.HeadLeft,    "button_head_left"),
            (Hud.CharacterCreatorPanel.Action.HeadRight,   "button_head_right"),
            (Hud.CharacterCreatorPanel.Action.FaceLeft,    "button_face_left"),
            (Hud.CharacterCreatorPanel.Action.FaceRight,   "button_face_right"),
            (Hud.CharacterCreatorPanel.Action.HairLeft,    "button_hair_left"),
            (Hud.CharacterCreatorPanel.Action.HairRight,   "button_hair_right"),
            (Hud.CharacterCreatorPanel.Action.ShirtLeft,   "button_shirt_left"),
            (Hud.CharacterCreatorPanel.Action.ShirtRight,  "button_shirt_right"),
            (Hud.CharacterCreatorPanel.Action.PantsLeft,   "button_pants_left"),
            (Hud.CharacterCreatorPanel.Action.PantsRight,  "button_pants_right"),
        };
        foreach (var (act, widget) in actions)
        {
            if (!_creator.TryGetButtonStateAndRect(act, viewportW, viewportH,
                    out int x, out int y, out int w, out int h,
                    out bool hov, out bool pr)) continue;
            _frontendScene.DrawHeromenuButton(viewportW, viewportH,
                x, y, w, h, hov, pr, widget);
        }
        // Flat _barRenderer hover/press tint removed. heromenu-up.raw and
        // heromenu-down.raw already bake the proper arrow-tip glow shape
        // (curved, only at the actual ◄ ► geometry); painting a flat
        // alpha rectangle on top stretches that glow into a boxy bar
        // across the whole 102px half-bar. Texture swap stands alone.
        // Phase 29-CD-CREATOR-FIX2 — "CHOOSE HERO" title overlay.
        // The mainmenu mesh (which carried this label via its text-01
        // atlas at sng2cd@1.0) is masked off at cd state because its
        // chrome rail rendered as an unwanted "menu in the middle"
        // and the same text-01 atlas bled MM/SP-tree labels across
        // the column. TextRenderer overlay at the authored top-of-
        // screen position keeps the title without the atlas baggage.
        if (_textRenderer is not null && _textRenderer.HasFont)
        {
            float titleScale = MathF.Min(viewportH / 600f, viewportW / 800f);
            int titleFontScale = Math.Max(1, (int)MathF.Round(titleScale)) + 1;
            const string title = "CHOOSE HERO";
            int titleW = _textRenderer.MeasureWidth(title, titleFontScale);
            int titleH = (_textRenderer.Font?.Height ?? 12) * titleFontScale;
            // Center horizontally on the spinner column (gas X 176-377,
            // center 276), at gas Y ≈ 90 above the first spinner row.
            int authoredW = (int)MathF.Round(800 * titleScale);
            int authoredH = (int)MathF.Round(600 * titleScale);
            int dx = (viewportW - authoredW) / 2;
            int dy = (viewportH - authoredH) / 2;
            int titleCenterX = dx + (int)MathF.Round(276 * titleScale);
            int titleY = dy + (int)MathF.Round(90 * titleScale);
            int titleX = titleCenterX - titleW / 2;
            var titleInk = new Vector4(0.95f, 0.85f, 0.65f, 1f);
            _textRenderer.DrawString(viewportW, viewportH, title, titleX, titleY, titleInk, titleFontScale);
        }

        // Typed-name overlay at the gas-authored name_edit_box rect.
        // Phase 29-CD-CREATOR-FIX — bump font scale by 1 (12px → ~24px
        // → 36px for clearer reading on the name plate, per user feedback)
        // and left-align inside the rect (text sits to the LEFT of the
        // image plate it's drawn over, not centered).
        if (_textRenderer is not null && _textRenderer.HasFont)
        {
            var nameRect = _creator.NameRectInViewport(viewportW, viewportH);
            string typed = string.IsNullOrEmpty(_creator.HeroName) ? "" : _creator.HeroName;
            int fontScale = _creator.FontScale + 1;
            var ink = new Vector4(0.95f, 0.85f, 0.65f, 1f);
            int padX = Math.Max(2, fontScale * 2);
            if (typed.Length == 0)
            {
                _barRenderer?.DrawRect(viewportW, viewportH,
                    nameRect.X + padX, nameRect.Y + nameRect.H - 2,
                    nameRect.W - padX * 2, 1, new Vector4(0.65f, 0.55f, 0.40f, 0.75f));
            }
            else
            {
                int th = (_textRenderer.Font?.Height ?? 12) * fontScale;
                int tx = nameRect.X + padX;
                int ty = nameRect.Y + (nameRect.H - th) / 2;
                _textRenderer.DrawString(viewportW, viewportH, typed, tx, ty, ink, fontScale);
            }
        }
    }

    /// <summary>Phase 28-CD-FLYOUT — hover overlay for the Character
    /// Creator's Previous / Next bottom-nav buttons.</summary>
    private void DrawCsMenuHoverOverlays(int viewportW, int viewportH)
    {
        if (_barRenderer is null) return;
        var actions = new[]
        {
            Hud.CharacterSelectMenuPanel.Action.Previous,
            Hud.CharacterSelectMenuPanel.Action.Next,
        };
        var hoverTint = new Vector4(1f, 1f, 1f, 0.22f);
        var pressTint = new Vector4(0f, 0f, 0f, 0.30f);
        foreach (var a in actions)
        {
            if (!_csMenu.TryGetButtonStateAndRect(a, viewportW, viewportH,
                    out int x, out int y, out int w, out int h,
                    out bool hov, out bool pr)) continue;
            if (pr) _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, pressTint);
            else if (hov) _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, hoverTint);
        }
    }

    /// <summary>Phase 27-SP-FLYOUT-FIX — render the SP submenu's two
    /// button labels via TextRenderer. Replaces the masked-off
    /// menubars.asp text subsets (6-15) which bled adjacent atlas
    /// rows onto the visible slots at SP pose. Same overlay-on-wood
    /// approach as <c>DrawBackButton</c>; centered in each slot rect
    /// at the same font-scale the panel uses for hit testing so
    /// label position tracks viewport size.</summary>
    private void DrawSinglePlayerLabels(int viewportW, int viewportH)
    {
        if (_textRenderer is null || !_textRenderer.HasFont) return;
        int fontScale = _spMenu.FontScale;
        var ink = new Vector4(0.95f, 0.85f, 0.65f, 1f);
        DrawSlotLabel(Hud.SinglePlayerMenuPanel.Action.NewGame,  "START NEW GAME", fontScale, ink, viewportW, viewportH);
        DrawSlotLabel(Hud.SinglePlayerMenuPanel.Action.LoadGame, "LOAD GAME",      fontScale, ink, viewportW, viewportH);
    }

    private void DrawSlotLabel(Hud.SinglePlayerMenuPanel.Action a, string label,
                               int fontScale, Vector4 ink, int viewportW, int viewportH)
    {
        if (_textRenderer is null) return;
        if (!_spMenu.TryGetButtonStateAndRect(a, viewportW, viewportH,
                out int x, out int y, out int w, out int h, out _, out _)) return;
        int tw = _textRenderer.MeasureWidth(label, fontScale);
        int th = (_textRenderer.Font?.Height ?? 12) * fontScale;
        int tx = x + (w - tw) / 2;
        int ty = y + (h - th) / 2;
        _textRenderer.DrawString(viewportW, viewportH, label, tx, ty, ink, fontScale);
    }

    /// <summary>Helper for <see cref="DrawBootScene"/>: resolve a splash
    /// RAW by basename through the play resolver, cache the upload, and
    /// blit at the authored rect (scale-translated to viewport pixels).
    /// Silent no-op when the texture can't be resolved so a missing asset
    /// just leaves a black hole rather than crashing the boot path.</summary>
    private void DrawSplashPanel(string texName, int ax, int ay, int aw, int ah,
                                 float scale, int dx, int dy, float alpha,
                                 int viewportW, int viewportH)
    {
        if (_iconRenderer is null || _gl is null || _playResolver is null) return;
        if (!_splashTexCache.TryGetValue(texName, out var tex))
        {
            try
            {
                if (!_playResolver.TryLoadByBasename(texName + ".raw", out var bytes))
                {
                    _splashTexCache[texName] = null!;
                    Console.Error.WriteLine($"  boot: splash tex '{texName}.raw' not found");
                    return;
                }
                var raw = SiegeFX.Core.Assets.RawImage.Load(bytes);
                tex = new GlTexture(_gl, raw);
                _splashTexCache[texName] = tex;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  boot: splash tex '{texName}' load failed — {ex.Message}");
                return;
            }
        }
        if (tex is null) return;
        int rx = dx + (int)MathF.Round(ax * scale);
        int ry = dy + (int)MathF.Round(ay * scale);
        int rw = (int)MathF.Round(aw * scale);
        int rh = (int)MathF.Round(ah * scale);
        _iconRenderer.DrawIcon(viewportW, viewportH, tex, rx, ry, rw, rh,
            new Vector4(1f, 1f, 1f, alpha));
    }

    /// <summary>21d-2a-viii-b — entry point shared by env-var spawn (no UI) and
    /// the creator's "Begin" path. <paramref name="heroName"/> is the
    /// player-typed name; null for env-var path. Persistence of the name into
    /// the save schema is viii-c's concern; viii-b just logs it.</summary>
    private void TrySpawnPlayerWithPicker(
        ActorSpawner spawner,
        SiegeFX.Core.Nav.NavMesh? navMesh,
        HeroVariantPicker pick,
        string? heroName)
    {
        if (_actors.Count == 0)
        {
            Console.WriteLine("  player: no NPCs spawned, skipping player spawn (nothing to anchor against)");
            return;
        }

        var centroid = Vector3.Zero;
        foreach (var s in _actors) centroid += s.Actor.WorldTransform.Translation;
        centroid /= _actors.Count;

        // Prefer the authored start_positions.gas slot (puts the PC at the
        // farmhouse next to Norick on fh_r1). Centroid is a fallback so
        // play-region of an info-less region still works.
        var spawnPos = _authoredSpawn ?? centroid;
        if (navMesh is not null && navMesh.TryFindTriangle(spawnPos, out var tri))
            spawnPos = spawnPos with { Y = navMesh.SampleYOnTriangle(tri, spawnPos) };

        // 21d-2a-viii-c — remember the variant + name so quicksave persists
        // them through F5; F9 restores into these fields via ApplySave.
        _heroName = heroName ?? "";
        _heroVariant = pick;

        string playerTemplate = pick.Gender == HeroGender.Girl ? "farmgirl" : "farmboy";
        // Scid 0xffffff00 is well clear of region actor.gas scids (region scids are
        // 0x01xxxxxx); any stable out-of-band value works, this one is easy to spot
        // in the combat log.
        var inst = SiegeFX.Core.Assets.ActorInstance.CreateSynthetic(
            playerTemplate, scid: 0xffffff00u, worldPosition: spawnPos,
            orientation: System.Numerics.Quaternion.Identity);

        // 21d-2a-vi / Phase 9-SC-10 — peek at es_weapon_hand (and es_shield_hand)
        // to pick the right idle stance. See ComputePreferredPlayerStance for the
        // stance map. At spawn time the live _playerEquipment dict is empty, so
        // we seed the picker from the template's authored [inventory][equipment]
        // block; pickup-time refreshes feed the live dict instead.
        IReadOnlyDictionary<string, string>? spawnEquip = null;
        if (_templateStore is not null && _templateStore.TryGet(playerTemplate, out var pcTpl))
        {
            var eqSection = _templateStore.GetSection(pcTpl, "inventory", "equipment");
            if (eqSection is not null)
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var attr in eqSection.Attributes)
                    if (!string.IsNullOrWhiteSpace(attr.Value))
                        dict[attr.Name] = attr.Value!.Trim();
                spawnEquip = dict;
            }
            // Phase 21-SC-INV-B (round 2) — portrait icon for the Character
            // pane / mini-HUD lives on [actor]portrait_icon. Farmboy ships
            // b_gui_ig_i_ic_c_fb_01; farmgirl variants point at b_gui_ig_i_ic_c_fg_*.
            _playerPortraitIconName =
                (_templateStore.GetAttribute(pcTpl, "actor", "portrait_icon") ?? "")
                .Trim().Trim('"');
        }
        int? preferredStance = ComputePreferredPlayerStance(spawnEquip);

        // Build the variant override (model only — texture overrides flow
        // through ResolveActorTexture's player-only block via the renderer
        // fields below). The picker resolves the body's armor_version from
        // the template so 'a3' picks m_c_gah_fb_pos_a3 for boy, m_c_gah_fg_pos_a3
        // for girl, etc. — we don't hardcode 'gah_fb'.
        SiegeFX.Core.Actors.TemplateOverride? heroOverride = null;
        if (_templateStore is not null
            && _templateStore.TryGet(playerTemplate, out var pickTpl))
        {
            heroOverride = pick.BuildOverride(_templateStore, pickTpl);
        }
        _skinTexOverrideName  = heroOverride?.SkinTextureName;
        _pantsTexOverrideName = heroOverride?.ClothingTextureName;
        var overrides = heroOverride is null
            ? null
            : new Dictionary<string, SiegeFX.Core.Actors.TemplateOverride>(StringComparer.OrdinalIgnoreCase)
              { [playerTemplate] = heroOverride };
        Console.WriteLine(
            $"  player: variant pick gender={pick.Gender} body={pick.BodyTypeIdx + 1} " +
            $"skin={pick.SkinSuffix ?? "<default>"} pants={pick.PantsSuffix ?? "<default>"} " +
            $"=> model={heroOverride?.ModelName ?? "<template>"}");

        var diagsBefore = spawner.Diagnostics.Count;
        var spawned = spawner.Spawn(new[] { inst }, preferredStance, overrides);
        if (spawned.Count == 0)
        {
            Console.WriteLine($"  player: '{playerTemplate}' did not spawn (spawner diagnostics follow)");
            foreach (var d in spawner.Diagnostics.TakeLast(4))
                Console.WriteLine($"    !! {d}");
            return;
        }
        Console.WriteLine($"  player: stance preference = {(preferredStance.HasValue ? preferredStance.ToString() : "none")}");
        for (int i = diagsBefore; i < spawner.Diagnostics.Count; i++)
            Console.WriteLine($"    .. {spawner.Diagnostics[i]}");

        var player = spawned[0];
        // Phase 21-SC-BARREL-FOLD — load every chore_attack sub-anim into
        // AttackVariants so PerformPlayerSwing / PerformPropBreak can rotate
        // 0mid ↔ high (R→L slash + L→R backhand) per swing. Spawn loads only
        // the first sub-anim; RefreshMotionClips is the single place that
        // also walks variants. Running it once at spawn time means combat
        // gets the alternating swing from frame zero, not just after the
        // first equipment change.
        spawner.RefreshMotionClips(player, preferredStance);
        if (!_actorMeshCache.TryGetValue(player.Mesh, out var gl))
        {
            gl = new SkinnedMesh(_gl!, player.Mesh);
            _actorMeshCache[player.Mesh] = gl;
        }

        var state = new ActorRenderState
        {
            Actor             = player,
            GlMesh            = gl,
            AnimTime          = 0,
            LastClipIndex     = player.CurrentClipIndex,
            Brain             = null,
            CurrentTransform  = player.WorldTransform,
            IsPlayer          = true,
        };
        _actors.Add(state);
        _player = state;
        // Phase 16d — XP/level state. Requires the formulas store, which loads
        // alongside the play-region path; viewer modes that skip TrySpawnPlayer
        // also skip this. The PC starts at level 1 with zero XP regardless of
        // template authoring (DS1 PCs always start fresh).
        if (_formulas is not null)
            _progression = new SiegeFX.Core.Actors.PlayerProgression(player, _formulas);
        // Phase 17a — slot the canonical spell_zap into the player's primary
        // book slot. Future inventory/learn UI will populate this from the
        // spellbook items in the PC's [inventory][equipment]; for now zap is
        // the always-on starter so 'Q' has something to fire.
        //
        // Phase 21-SC-SPELL-VFX-AUDIT follow-up — `SIEGEFX_DEBUG_SPELLS=primary
        // [,secondary]` overrides the hardcoded defaults so the SC-SPELL-VFX-3
        // work plan (build a primitive, test the spells it unblocks) doesn't
        // need pickup→spellbook wiring (which is the separate scroll↔spellbook
        // SC). Bare names accepted (`spell_` prefix added if missing) so the
        // env var reads naturally as `SIEGEFX_DEBUG_SPELLS=fireball,iceshard`.
        if (_spellCatalog is not null)
        {
            string primaryName   = DefaultPrimarySpellName;
            string secondaryName = DefaultSecondarySpellName;
            var debugSpells = Environment.GetEnvironmentVariable("SIEGEFX_DEBUG_SPELLS");
            if (!string.IsNullOrWhiteSpace(debugSpells))
            {
                var parts = debugSpells.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 1 && parts[0].Length > 0)
                    primaryName = parts[0].StartsWith("spell_", StringComparison.OrdinalIgnoreCase) ? parts[0] : "spell_" + parts[0];
                if (parts.Length >= 2 && parts[1].Length > 0)
                    secondaryName = parts[1].StartsWith("spell_", StringComparison.OrdinalIgnoreCase) ? parts[1] : "spell_" + parts[1];
                Console.WriteLine($"  SIEGEFX_DEBUG_SPELLS override: primary={primaryName} secondary={secondaryName}");
                // Phase 21-SC-SCROLL-C-1 — extra names beyond the first two
                // seed PlayerSpellbook.Placed[] so the user has a roster to
                // swap among during test sessions. e.g.
                //   SIEGEFX_DEBUG_SPELLS=fireball,iceshard,lightning,shock_wave,nurture
                // primaries Q+W from index 0/1, plus 3 placed rows from
                // index 2..4.
                //
                // Phase 21-SC-SCROLL-D follow-up — slots 12+ overflow into
                // the player's inventory as scroll items so a test roster
                // can exercise the drag-from-inventory path (E) without
                // needing world drops + pickup. Example seeding 12 active+
                // placed + 4 inventory scrolls:
                //   SIEGEFX_DEBUG_SPELLS=fireball,iceshard,lightning,shock_wave,
                //                        nurture,bombard,acid_cloud,death_blast,
                //                        spark,fire_pillar,implosion,starburst,
                //                        zap,frigid_armor,heal_bind,leech_life
                if (parts.Length > 2)
                {
                    int placedCount = Math.Min(parts.Length - 2, 10);
                    var placedNames = new string[placedCount];
                    for (int pi = 0; pi < placedCount; pi++)
                    {
                        var name = parts[pi + 2];
                        placedNames[pi] = name.StartsWith("spell_", StringComparison.OrdinalIgnoreCase)
                            ? name : "spell_" + name;
                    }
                    _pendingPlacedSeeds = placedNames;
                    Console.WriteLine($"  SIEGEFX_DEBUG_SPELLS placed seed: {string.Join(", ", placedNames)}");
                }
                if (parts.Length > 12)
                {
                    int invCount = parts.Length - 12;
                    var invNames = new string[invCount];
                    for (int ii = 0; ii < invCount; ii++)
                    {
                        var name = parts[ii + 12];
                        invNames[ii] = name.StartsWith("spell_", StringComparison.OrdinalIgnoreCase)
                            ? name : "spell_" + name;
                    }
                    _pendingInventoryScrollSeeds = invNames;
                    Console.WriteLine($"  SIEGEFX_DEBUG_SPELLS inventory seed: {string.Join(", ", invNames)}");
                }
            }

            // Phase 21-SC-SPELL-VFX-3p — when SIEGEFX_DEBUG_SPELLS names a
            // template the catalog skipped (summons have no damage/heal so
            // SpellTemplate.FromTemplate returns null), fall back to the
            // debug factory which builds a synthetic SpellTemplate stub
            // from the raw template. Lets the user slot summon_helper /
            // summon_drake_green / etc. and see/hear the authored cast
            // effect on Q-press without needing the spawn-verb runtime.
            var primary = ResolveSlottableSpell(primaryName, debugSpells);
            if (primary is not null)
            {
                _playerSpellbook = new SiegeFX.Core.Actors.PlayerSpellbook(
                    player, new Random(unchecked((int)0x5C617AC1u)));
                _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Primary, primary);
                Console.WriteLine($"  spellbook: primary <- {primary.Name} (\"{primary.ScreenName}\") " +
                                  $"range={primary.CastRange:F1} cd={primary.CastReloadDelay:F2}s");
                // Phase 17c — slot a self-heal into Secondary so 'W' has something
                // to fire. spell_healing_wind has a simple `(#magic+1)*5.15` mana
                // formula and a tractable alter_life enchantment value our
                // SpellExpr resolver handles without hitting the ternary syntax
                // the more complex heal templates use.
                var secondary = ResolveSlottableSpell(secondaryName, debugSpells);
                if (secondary is not null)
                {
                    _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Secondary, secondary);
                    Console.WriteLine($"  spellbook: secondary <- {secondary.Name} (\"{secondary.ScreenName}\") " +
                                      $"kind={secondary.Kind} cd={secondary.CastReloadDelay:F2}s");
                }
                else if (!string.IsNullOrWhiteSpace(debugSpells))
                {
                    Console.WriteLine($"  spellbook: secondary '{secondaryName}' not resolvable (slot empty)");
                }
                // Phase 21-SC-SCROLL-C-1 — seed Placed[] from extra
                // SIEGEFX_DEBUG_SPELLS names parsed at the catalog override
                // step. ResolveSlottableSpell handles non-catalog templates
                // (summons etc.) via the synthetic FromTemplateForDebug path,
                // so a placed row can hold any spell_* template.
                if (_pendingPlacedSeeds is not null)
                {
                    int seeded = 0;
                    for (int pi = 0; pi < _pendingPlacedSeeds.Length && pi < _playerSpellbook.PlacedCount; pi++)
                    {
                        var seedSpell = ResolveSlottableSpell(_pendingPlacedSeeds[pi], debugSpells);
                        if (seedSpell is not null)
                        {
                            _playerSpellbook.SetPlaced(pi, seedSpell);
                            seeded++;
                        }
                    }
                    Console.WriteLine($"  spellbook: seeded {seeded}/{_pendingPlacedSeeds.Length} placed rows from SIEGEFX_DEBUG_SPELLS");
                    // Drop the reference now that consumption is done; the
                    // field's <summary> says "Null = no env-var seed in
                    // effect" so leaving it populated past this point is
                    // misleading for any future caller that re-reads.
                    _pendingPlacedSeeds = null;
                }
                // Phase 21-SC-SCROLL-D follow-up — overflow names go into a
                // ground LOOT PILE next to the player. Walking over picks
                // them up via F-2 (auto-route to spellbook Placed[]; falls
                // back to inventory once Placed is full). Lets a test
                // session exercise the world-drop -> pickup path AND
                // populate the inventory grid for further drag-from-
                // inventory testing once Placed fills up. Original
                // direct-inventory seeding switched at user request:
                // "if it's easier you can just put the spells on the
                // ground next to the player."
                if (_pendingInventoryScrollSeeds is not null)
                {
                    var pileItems = new List<SiegeFX.Core.Actors.LootEntry>();
                    foreach (var name in _pendingInventoryScrollSeeds)
                    {
                        var seedSpell = ResolveSlottableSpell(name, debugSpells);
                        if (seedSpell is null) continue;
                        pileItems.Add(new SiegeFX.Core.Actors.LootEntry(
                            Slot: "", Reference: seedSpell.Name));
                    }
                    if (pileItems.Count > 0)
                    {
                        // Drop ~2u in front of the player along their facing
                        // (or +X if facing isn't yet set). No throw arc — these
                        // are pre-placed for the user to walk over.
                        var origin = player.WorldTransform.Translation;
                        var face = _playerFacing.LengthSquared() > 0.01f
                            ? _playerFacing : new Vector3(1f, 0f, 0f);
                        // Phase 21-SC-SCROLL-CLICKLOOT — short throw, item
                        // lands near the player so click-to-loot is fast.
                        // Was 2.0u (felt like a mini-trebuchet shot).
                        var dropPos = origin + face * 0.7f;
                        _lootPiles.Add(new LootPile(dropPos, pileItems));
                        Console.WriteLine($"  ground scrolls: pile of {pileItems.Count} scrolls at " +
                                          $"({dropPos.X:F1},{dropPos.Z:F1}) — walk over to pick up");
                    }
                    _pendingInventoryScrollSeeds = null;
                }
            }
            else if (!string.IsNullOrWhiteSpace(debugSpells))
            {
                Console.WriteLine($"  spellbook: primary '{primaryName}' not resolvable (spellbook empty)");
            }
        }
        // Phase 13b — once a PC exists, default to chase cam. Toggle with C if the
        // user wants to fly around for debugging.
        _cameraMode = CameraMode.Chase;

        // Phase 13c — build the click-to-move NavFollower now so LMB handler can
        // simply SetTarget without null-checking a fresh PC. Speed falls back to
        // 4.5u/s (typical DS1 walk) when the template's stats chain didn't resolve
        // a max_life/walk_speed — see project_siegefx_max_life_formula.md.
        if (navMesh is not null)
        {
            var speed = player.Stats.WalkSpeed > 0f ? player.Stats.WalkSpeed : 4.5f;
            _playerFollower = new SiegeFX.Core.Nav.NavFollower(navMesh, spawnPos, speed);
        }
        _playerFacing = Vector3.UnitZ;
        // 9-SC-10b — re-seed the render-interp buffers for the fresh spawn so
        // the body doesn't lerp from the previous PC's last position.
        _playerRenderInit = false;
        _playerRenderPosPrev = _playerRenderPosNext = spawnPos;
        _playerRenderFacingPrev = _playerRenderFacingNext = _playerFacing;

        // Phase 14b — seed PC equipment from the template's [inventory][equipment]
        // block. base_farmboy authors es_weapon_hand=dg_g_d_1h_fun (fun dagger),
        // es_feet=bo_bo_le_light, es_spellbook=book_glb_magic_01. Any attribute
        // whose name starts with es_ is treated as an equip slot -> item ref.
        _playerEquipment.Clear();
        if (_templateStore is not null)
        {
            var eq = _templateStore.GetSection(player.Template, "inventory", "equipment");
            if (eq is not null)
            {
                foreach (var a in eq.Attributes)
                {
                    if (!a.Name.StartsWith("es_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(a.Value)) continue;
                    _playerEquipment[a.Name] = a.Value.Trim();
                }
            }
        }

        Console.WriteLine(
            $"  player: '{playerTemplate}' spawned at " +
            $"({spawnPos.X:F1}, {spawnPos.Y:F1}, {spawnPos.Z:F1})  " +
            $"life={player.Stats.MaxLife:F0} " +
            $"walk={player.Stats.WalkSpeed:F1}u/s");
        if (_playerEquipment.Count > 0)
        {
            var slots = new List<string>(_playerEquipment.Count);
            foreach (var kv in _playerEquipment) slots.Add($"[{kv.Key}] {kv.Value}");
            Console.WriteLine($"  equipment: {string.Join(", ", slots)}");
        }
        // Phase 9-SC-12 — surface the spellbook reference if the PC carries one.
        // The book's pcontent_level is logged so the future pcontent roller (SC-16)
        // has visibility on what to roll. Until SC-16 lands, the runtime still uses
        // the DefaultPrimary/Secondary stand-ins for Q/W; this just confirms the
        // data path is alive and correctly seeing es_spellbook.
        if (_templateStore is not null
            && _playerEquipment.TryGetValue("es_spellbook", out var bookRef)
            && _templateStore.TryGet(bookRef, out var bookTpl))
        {
            var pcontentLevel = _templateStore.GetAttribute(bookTpl!, "magic", "pcontent_level")
                              ?? _templateStore.GetAttribute(bookTpl!, "pcontent", "base", "pcontent_level")
                              ?? "(none)";
            Console.WriteLine($"  spellbook: book='{bookRef}' pcontent_level={pcontentLevel} (rolling deferred to SC-16)");
        }

        // Phase 14d — cache the weapon_grip bone index from the PC's skeleton and
        // preload the initial weapon mesh/texture. base_farmboy's body.bone_translator
        // authors weapon_bone=weapon_grip, which is a bone name on the biped skeleton.
        _weaponGripBoneIdx = -1;
        for (int bi = 0; bi < player.Mesh.BoneNames.Count; bi++)
        {
            if (string.Equals(player.Mesh.BoneNames[bi], "weapon_grip", StringComparison.OrdinalIgnoreCase))
            { _weaponGripBoneIdx = bi; break; }
        }
        if (_weaponGripBoneIdx < 0)
            Console.WriteLine("  weapon: no weapon_grip bone on PC skeleton — weapon render disabled");
        else
            TryLoadPlayerWeapon();

        // Phase 21d-2a-vii — compose layered equipment (boots, helm, gauntlets,
        // chest texture override) from the same [inventory][equipment] block.
        TryLoadPlayerEquipment(player.Template);

        // Phase 9-SC-10 verification hook — when SIEGEFX_DEBUG_DROP is set,
        // place a single-item loot pile 1.5u in front of the spawn so the user
        // can walk over it and exercise the real pickup -> auto-equip ->
        // LoadAttachedItem path without hunting for a mob that drops the right
        // slot. Format: "<slot>:<itemRef>" (e.g. "shield_hand:sh_m_g_c_r_s_avg")
        // or just "<itemRef>" for a non-equipped drop.
        var debugDrop = Environment.GetEnvironmentVariable("SIEGEFX_DEBUG_DROP");
        if (!string.IsNullOrWhiteSpace(debugDrop))
        {
            var parts = debugDrop.Split(':', 2);
            var dropSlot = parts.Length == 2 ? parts[0].Trim() : "";
            var dropRef  = parts.Length == 2 ? parts[1].Trim() : debugDrop.Trim();
            var dropPos  = spawnPos + new Vector3(1.5f, 0f, 0f);
            _lootPiles.Add(new LootPile(dropPos,
                new List<SiegeFX.Core.Actors.LootEntry> {
                    new(dropSlot, dropRef)
                })
                {
                    RestPitch = ComputeLootRestPitch(dropRef),
                });
            Console.WriteLine($"  debug-drop: [{(string.IsNullOrEmpty(dropSlot) ? "drop" : dropSlot)}] {dropRef} at ({dropPos.X:F1},{dropPos.Z:F1})");
        }
    }

    /// <summary>Phase 9-SC-10 — pick the chore_default stance number that matches
    /// the PC's equipped weapon class. fs0=unarmed, fs1=1H melee, fs5=ranged.
    /// The fs2 stance is NOT a "1H melee + shield" idle — visual testing showed
    /// it warps the dagger wrist pose and doesn't help shield placement, so the
    /// shield stays in stance 1 with the existing bone-attach on shield_grip.</summary>
    private int? ComputePreferredPlayerStance(IReadOnlyDictionary<string, string>? slots)
    {
        if (_templateStore is null || slots is null) return null;
        if (!slots.TryGetValue("es_weapon_hand", out var weaponRef)
            || string.IsNullOrWhiteSpace(weaponRef)) return null;
        if (!_templateStore.TryGet(weaponRef, out var weaponTpl)) return null;
        for (var t = weaponTpl; t is not null; t = t.Specializes)
        {
            if (string.Equals(t.Name, "weapon_melee", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(t.Name, "weapon_ranged", StringComparison.OrdinalIgnoreCase))
                return 5;
        }
        return null;
    }

    /// <summary>Phase 9-SC-10 — pick the natural ground orientation for a freshly
    /// dropped item. Shields are modeled vertically (oriented along the
    /// forearm-facing axis on shield_grip) so they need a 90° tip to lie flat;
    /// weapons and generic loot keep RestPitch=0 because their meshes are already
    /// authored "lying along the ground" at their bind pose.</summary>
    private float ComputeLootRestPitch(string itemRef)
    {
        if (_templateStore is null || string.IsNullOrEmpty(itemRef)) return 0f;
        var resolved = ResolveItemRef(itemRef);
        if (!_templateStore.TryGet(resolved, out var tpl)) return 0f;
        for (var t = tpl; t is not null; t = t.Specializes)
        {
            if (string.Equals(t.Name, "base_shield", StringComparison.OrdinalIgnoreCase))
                return MathF.PI * 0.5f; // 90° — lay flat
        }
        return 0f;
    }

    /// <summary>Phase 9-SC-10 — re-pick the PC's idle/walk clip after equipment
    /// changes. Called from the loot-pickup path so that picking up a shield
    /// switches the idle from fs1 to fs2 without a respawn. No-op when the
    /// spawner or player hasn't materialized yet.</summary>
    private void RefreshPlayerStance()
    {
        if (_actorSpawner is null || _player is null) return;
        var stance = ComputePreferredPlayerStance(_playerEquipment);
        _actorSpawner.RefreshMotionClips(_player.Actor, stance);
        Console.WriteLine($"  stance: refreshed -> {(stance.HasValue ? stance.ToString() : "default")}");
    }

    /// <summary>Phase 21d-2a-vii — resolve every <c>[inventory][equipment]</c>
    /// slot via <see cref="EquipmentResolver"/> and load the layered meshes for
    /// SkinnedLayer entries (boots/helm/gauntlets). The primary weapon
    /// (<c>es_weapon_hand</c>) is loaded by <see cref="TryLoadPlayerWeapon"/>
    /// because hit-cue/material selection is entangled with that slot;
    /// <em>other</em> AttachBone slots — shields today, quivers/torches/etc.
    /// tomorrow — go through <see cref="LoadAttachedItem"/> into
    /// <c>_attachedItems</c> (Phase 9-SC-10). ChestTexture entries set
    /// <c>_chestTexOverrideName</c> so the body's slot-1 binding swaps to the
    /// armor's <c>b_c_pos_*</c> texture.</summary>
    private void TryLoadPlayerEquipment(SiegeFX.Core.Assets.Template playerTemplate)
    {
        DisposeEquippedLayers();
        DisposeAttachedItems();
        _chestTexOverrideName = null;
        if (_gl is null || _playResolver is null || _templateStore is null) return;

        // Phase 9-SC-10 — feed the live _playerEquipment dict so picked-up items
        // (e.g. a shield acquired after spawn) layer in alongside the template's
        // authored loadout. EquipmentResolver falls back to the template's
        // [inventory][equipment] block when liveSlots is null, which is what we
        // want during the very first call from TrySpawnPlayer (the dict is
        // populated from the template right before this call).
        var layers = SiegeFX.Core.Assets.EquipmentResolver.Resolve(
            _templateStore, _playResolver, playerTemplate, _playerEquipment);
        foreach (var layer in layers)
        {
            switch (layer.Strategy)
            {
                case SiegeFX.Core.Assets.EquipmentResolver.Strategy.None:
                    break;

                case SiegeFX.Core.Assets.EquipmentResolver.Strategy.AttachBone:
                    // es_weapon_hand is owned by TryLoadPlayerWeapon (hit-cue
                    // material lookup, swap-on-pickup integration). Everything
                    // else (shield_hand today; quiver/torch/etc. tomorrow) goes
                    // through the generic single-bone attach path.
                    if (string.Equals(layer.SlotName, "es_weapon_hand",
                            StringComparison.OrdinalIgnoreCase))
                        break;
                    LoadAttachedItem(layer);
                    break;

                case SiegeFX.Core.Assets.EquipmentResolver.Strategy.SkinnedLayer:
                    if (string.IsNullOrEmpty(layer.MeshBaseName)) break;
                    if (!_playResolver.TryLoadModel(layer.MeshBaseName, out var aspBytes))
                    {
                        Console.WriteLine(
                            $"  equip: '{layer.SlotName}' = '{layer.ItemRef}' mesh '{layer.MeshBaseName}.asp' not in any tank");
                        break;
                    }
                    SiegeFX.Core.Assets.AspMesh asp;
                    try { asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  equip: '{layer.MeshBaseName}.asp' load failed: {ex.Message}");
                        break;
                    }
                    if (!asp.HasSkin)
                    {
                        Console.WriteLine($"  equip: '{layer.MeshBaseName}.asp' has no skin (rigid?); SkinnedLayer skipped");
                        break;
                    }
                    SkinnedMesh sm;
                    try { sm = new SkinnedMesh(_gl, asp); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  equip: SkinnedMesh build failed for '{layer.MeshBaseName}.asp': {ex.Message}");
                        break;
                    }
                    var texName = layer.TextureBaseName;
                    if (string.IsNullOrEmpty(texName) && asp.TextureNames.Count > 0)
                        texName = asp.TextureNames[0];
                    var tex = LoadEquipmentTexture(layer.MeshBaseName, texName);
                    _equippedLayers.Add(new EquippedLayer
                    {
                        Asp = asp, Mesh = sm, Texture = tex,
                        SlotName = layer.SlotName, ItemRef = layer.ItemRef,
                        MeshBaseName = layer.MeshBaseName,
                    });
                    Console.WriteLine(
                        $"  equip: layered '{layer.SlotName}' = '{layer.ItemRef}' " +
                        $"mesh='{layer.MeshBaseName}' bones={asp.BoneCount} tex='{texName}' " +
                        $"({(tex is null ? "MISS" : "OK")})");
                    break;

                case SiegeFX.Core.Assets.EquipmentResolver.Strategy.ChestTexture:
                    _chestTexOverrideName = layer.OverrideBaseName;
                    Console.WriteLine(
                        $"  equip: chest texture override '{layer.SlotName}' = '{layer.ItemRef}' " +
                        $"slot={layer.OverrideTextureSlot} tex='{layer.OverrideBaseName}'");
                    break;
            }
        }
    }

    private void DisposeEquippedLayers()
    {
        foreach (var l in _equippedLayers)
        {
            l.Mesh.Dispose();
            // Textures are cached in _equipTexCache and may be shared across
            // layers; defer their disposal to OnClosing.
        }
        _equippedLayers.Clear();
    }

    /// <summary>Phase 21d-2a-vii — load (and cache) the albedo for a layered
    /// equipment ASP. Cache key is (mesh base name, texture base name) so
    /// swapping armor that re-uses a previously-loaded texture doesn't pay a
    /// second round trip + GL upload. Returns null on miss.</summary>
    private GlTexture? LoadEquipmentTexture(string meshBaseName, string? textureBaseName)
    {
        if (string.IsNullOrEmpty(textureBaseName)) return null;
        var key = (meshBaseName, textureBaseName);
        if (_equipTexCache.TryGetValue(key, out var hit)) return hit;
        var tex = LoadTexsetTexture(textureBaseName);
        _equipTexCache[key] = tex;
        return tex;
    }

    /// <summary>Phase 9-SC-10 — load a single bone-attached prop (shield, quiver,
    /// torch) into <c>_attachedItems</c>. Mirrors <see cref="TryLoadPlayerWeapon"/>
    /// but driven from <see cref="EquipmentResolver"/> so the bone name is whatever
    /// <c>body.bone_translator</c> declared (shield → <c>shield_grip</c> on
    /// base_farmboy). Silent on resolution misses — the slot just doesn't render.</summary>
    private void LoadAttachedItem(SiegeFX.Core.Assets.EquipmentResolver.EquipmentLayer layer)
    {
        if (_gl is null || _playResolver is null) return;
        if (_player is null) return;
        if (string.IsNullOrEmpty(layer.MeshBaseName) || string.IsNullOrEmpty(layer.AttachBoneName))
            return;

        // Resolve bone index against the PC's skeleton. Without a matching bone
        // there's nowhere to attach the mesh — log and bail.
        var pcMesh = _player.Actor.Mesh;
        int boneIdx = -1;
        for (int bi = 0; bi < pcMesh.BoneNames.Count; bi++)
        {
            if (string.Equals(pcMesh.BoneNames[bi], layer.AttachBoneName,
                    StringComparison.OrdinalIgnoreCase))
            { boneIdx = bi; break; }
        }
        if (boneIdx < 0)
        {
            Console.WriteLine(
                $"  attach: '{layer.SlotName}' = '{layer.ItemRef}' bone " +
                $"'{layer.AttachBoneName}' not on PC skeleton — skipped");
            return;
        }

        if (!_playResolver.TryLoadModel(layer.MeshBaseName, out var aspBytes))
        {
            Console.WriteLine($"  attach: model '{layer.MeshBaseName}.asp' not in any tank");
            return;
        }
        SiegeFX.Core.Assets.AspMesh asp;
        try { asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes); }
        catch (Exception ex)
        {
            Console.WriteLine($"  attach: '{layer.MeshBaseName}.asp' load failed: {ex.Message}");
            return;
        }

        var mesh = new StaticMesh(_gl, asp);
        var bindInv = asp.InverseBindMatrices.Length > 0
            ? asp.InverseBindMatrices[0]
            : Matrix4x4.Identity;

        GlTexture? tex = null;
        if (asp.TextureNames.Count > 0 &&
            _playResolver.TryLoadByBasename(asp.TextureNames[0] + ".raw", out var texBytes))
        {
            try { tex = new GlTexture(_gl, RawImage.Load(texBytes)); }
            catch (Exception ex) { Console.WriteLine($"  attach: texture load failed: {ex.Message}"); }
        }

        _attachedItems.Add(new AttachedItem
        {
            SlotName = layer.SlotName,
            ItemRef = layer.ItemRef,
            Mesh = mesh,
            Texture = tex,
            BindInv = bindInv,
            BoneIdx = boneIdx,
        });
        Console.WriteLine(
            $"  attach: '{layer.SlotName}' = '{layer.ItemRef}' " +
            $"mesh='{layer.MeshBaseName}' bone='{layer.AttachBoneName}' (idx={boneIdx}) " +
            $"({(tex is null ? "untextured" : asp.TextureNames[0])})");
    }

    private void DisposeAttachedItems()
    {
        foreach (var a in _attachedItems)
        {
            a.Mesh.Dispose();
            a.Texture?.Dispose();
        }
        _attachedItems.Clear();
    }


    // Phase 14d — load (or replace) the mesh + texture for whatever is currently in
    // es_weapon_hand. Silent no-op when no resolver / no slot / template missing;
    // we only render if the full chain resolves. Called on initial spawn and every
    // time an auto-equip swaps the weapon slot.
    private void TryLoadPlayerWeapon()
    {
        if (_gl is null) return;
        if (_playResolver is null || _templateStore is null) return;
        if (!_playerEquipment.TryGetValue("es_weapon_hand", out var weaponRef)) return;
        if (!_templateStore.TryGet(weaponRef, out var tpl)) return;
        var modelName = _templateStore.GetAttribute(tpl!, "aspect", "model");
        if (string.IsNullOrEmpty(modelName)) return;
        // Phase 9-SC-8 — cache weapon material (steelsword, steeledge, wood, etc.)
        // so the click-attack hit cue can pick the matching impact WAV family.
        var material = _templateStore.GetAttribute(tpl!, "aspect", "material");
        if (!string.IsNullOrEmpty(material)) _playerWeaponMaterial = material!;
        if (!_playResolver.TryLoadModel(modelName, out var aspBytes))
        {
            Console.WriteLine($"  weapon: model '{modelName}.asp' not in any tank");
            return;
        }
        SiegeFX.Core.Assets.AspMesh asp;
        try { asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes); }
        catch (Exception ex)
        {
            Console.WriteLine($"  weapon: '{modelName}.asp' load failed: {ex.Message}");
            return;
        }

        _weaponMesh?.Dispose();
        _weaponMesh = new StaticMesh(_gl, asp);
        _weaponBindInv = asp.InverseBindMatrices.Length > 0
            ? asp.InverseBindMatrices[0]
            : Matrix4x4.Identity;

        _weaponTexture?.Dispose();
        _weaponTexture = null;
        if (asp.TextureNames.Count > 0 &&
            _playResolver.TryLoadByBasename(asp.TextureNames[0] + ".raw", out var texBytes))
        {
            try { _weaponTexture = new GlTexture(_gl, RawImage.Load(texBytes)); }
            catch (Exception ex) { Console.WriteLine($"  weapon: texture load failed: {ex.Message}"); }
        }
        Console.WriteLine(
            $"  weapon: equipped mesh '{modelName}' ({asp.Corners.Length} corners, " +
            $"{(_weaponTexture is null ? "untextured" : asp.TextureNames[0])})");
    }

    // Phase 13c — LMB click-to-move. Unprojects the screen-space cursor to a
    // world ray, intersects it with the horizontal plane at the player's current
    // Y (cheap and accurate enough at DS1's shallow terrain slopes), confirms the
    // hit lands on the nav mesh, and retargets the player's follower. No-op when
    // there is no player, no nav mesh, the ray misses the Y plane, or the hit
    // point lies outside any walkable triangle.
    private void TryClickToMove(Vector2 cursorPx)
    {
        if (_player is null || _playerFollower is null || _navMesh is null) return;
        if (_player.IsDead) return;
        if (_window is null) return;
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return;

        // Screen → NDC. GL's NDC Y is up, window Y is down, hence the 1 - ...
        float ndcX = (cursorPx.X / size.X) * 2f - 1f;
        float ndcY = 1f - (cursorPx.Y / size.Y) * 2f;

        // Unproject near + far NDC points through the inverse view-proj. Row-vector
        // convention: the forward matrix is row-major stored, View * Proj; inverse
        // multiplies a row vector on the left. Vector4.Transform with Matrix4x4 uses
        // the same convention, so we can feed Vector4(ndcX, ndcY, z_ndc, 1) in and
        // divide by W to get the world-space point.
        float aspect = (float)size.X / size.Y;
        if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
        if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return;
        var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir  = far_ - near;
        if (dir.LengthSquared() < 1e-8f) return;

        // Intersect with the horizontal plane Y = player.Y. Ray: p = near + t*dir.
        // Solve: near.Y + t*dir.Y = planeY. Reject near-parallel rays so a top-down
        // view (dir.Y ~= 0) doesn't land us at effectively infinity.
        float planeY = _playerFollower.Position.Y;
        if (MathF.Abs(dir.Y) < 1e-4f) return;
        float t = (planeY - near.Y) / dir.Y;
        if (t < 0f) return;
        var hit = near + dir * t;

        // Phase 21-SC-SCROLL-CLICKLOOT — DS1 has no walk-over auto-pickup;
        // items must be clicked to be looted. If the click landed near a
        // settled loot pile (within 1.0u tolerance), pick it up and
        // suppress click-to-move so the player doesn't ALSO walk past
        // the now-empty pile spot.
        if (TryClickPickupAt(hit)) return;

        if (!_navMesh.TryFindTriangle(hit, out var tri)) return;
        hit = hit with { Y = _navMesh.SampleYOnTriangle(tri, hit) };
        _playerFollower.SetTarget(hit);
        // Phase 12-SC-1 — an LMB move overrides any pending walk-up-and-swing.
        _pendingAttackTarget = null;
        Console.WriteLine($"click-move: target=({hit.X:F1}, {hit.Y:F1}, {hit.Z:F1})  tri={tri}");
    }

    // Phase 20a — RMB-on-talkable. Same unproject + closest-actor pick as
    // TryClickToAttack, but the candidate set is "actors with at least one
    // conversation key whose first key resolves in the region's pool" — which
    // is what makes Edward et al. talkable while goblins ignore the click.
    // Returns true if the click consumed by opening dialogue; caller then
    // skips the attack path. Hostile-aligned actors (alignment_evil) never
    // open dialogue even if a template author left a stub conversation block.
    private const float ClickTalkRadius = 3f;
    private bool TryClickToTalk(Vector2 cursorPx)
    {
        if (_player is null || _window is null || _actors.Count == 0) return false;
        if (_player.IsDead) return false;
        if (_conversations is null || _conversations.Count == 0) return false;
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return false;

        float ndcX = (cursorPx.X / size.X) * 2f - 1f;
        float ndcY = 1f - (cursorPx.Y / size.Y) * 2f;
        float aspect = (float)size.X / size.Y;
        if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return false;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
        if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return false;
        var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir  = far_ - near;
        if (dir.LengthSquared() < 1e-8f || MathF.Abs(dir.Y) < 1e-4f) return false;

        float planeY = _player.CurrentTransform.Translation.Y;
        float t = (planeY - near.Y) / dir.Y;
        if (t < 0f) return false;
        var groundHit = near + dir * t;

        ActorRenderState? best = null;
        SiegeFX.Core.Assets.ConversationDef? bestConv = null;
        SiegeFX.Core.Actors.VendorDefinition? bestVendor = null;
        float bestDist = ClickTalkRadius;
        foreach (var s in _actors)
        {
            if (s.IsDead) continue;
            if (s.IsPlayer) continue;
            // The "has a [conversation] block in the placement" check is a
            // stronger talkable signal than Stats.IsCombatant: DS1's narrator
            // template inherits combat stats but is meant to be a static
            // talker outside NIS scenes, and would otherwise be filtered out.
            var keys = SiegeFX.Core.Assets.ConversationStore.KeysFromInstance(s.Actor.Instance.Node);

            SiegeFX.Core.Assets.ConversationDef? conv = null;
            foreach (var k in keys)
            {
                if (_conversations.TryGetValue(k, out var hit) && hit.Nodes.Count > 0)
                { conv = hit; break; }
            }

            // Phase 20d follow-up — vendors in DS1 ship with an EMPTY
            // [conversation] block (Norick is the canonical example). They
            // don't chat; clicking opens trade. Treat a vendor-catalog match
            // as talkable too, so RMB on an empty-conversation vendor still
            // resolves; the open-time branch picks dialogue vs trade.
            var vdef = SiegeFX.Core.Actors.VendorCatalog.Find(s.Actor.Template.Name);
            if (conv is null && vdef is null) continue;

            var pos = s.CurrentTransform.Translation;
            float dx = pos.X - groundHit.X;
            float dz = pos.Z - groundHit.Z;
            float d  = MathF.Sqrt(dx * dx + dz * dz);
            if (d < bestDist) { bestDist = d; best = s; bestConv = conv; bestVendor = vdef; }
        }
        if (best is null) return false;
        // No dialogue tree but the actor is a vendor — open trade directly.
        if (bestConv is null && bestVendor is not null)
        {
            _lastTalkedTemplate = best.Actor.Template.Name;
            _vendor.Open(bestVendor);
            Console.WriteLine($"trade: opened vendor panel for {bestVendor.ScreenName} (no dialogue)");
            return true;
        }
        if (bestConv is null) return false;

        var screenName = _templateStore?.GetAttribute(best.Actor.Template, "common", "screen_name");
        if (!string.IsNullOrWhiteSpace(screenName))
        {
            // common.screen_name comes through quoted in the gas; strip the
            // wrapping pair if present so the title bar doesn't read "Edward".
            screenName = screenName.Trim();
            if (screenName.Length >= 2 && screenName[0] == '"' && screenName[^1] == '"')
                screenName = screenName[1..^1];
        }
        if (string.IsNullOrWhiteSpace(screenName)) screenName = best.Actor.Template.Name;
        _lastTalkedTemplate = best.Actor.Template.Name;
        _dialogue.Open(screenName, bestConv);
        Console.WriteLine(
            $"talk: opened '{bestConv.Key}' with {screenName} ({bestConv.Nodes.Count} node(s))");
        return true;
    }

    /// <summary>Phase 20d — call after every interaction that might have
    /// closed the dialogue panel. If the last talked actor matches a vendor
    /// catalog entry, open the trade overlay; otherwise no-op. Idempotent —
    /// runs unconditionally each tick the panel is open is fine because the
    /// guard checks are O(catalog).</summary>
    private void TryOpenVendorAfterTalk()
    {
        if (_dialogue.IsOpen) return;
        if (_vendor.IsOpen) return;
        if (string.IsNullOrEmpty(_lastTalkedTemplate)) return;
        var def = SiegeFX.Core.Actors.VendorCatalog.Find(_lastTalkedTemplate);
        if (def is null) return;
        _vendor.Open(def);
        Console.WriteLine($"trade: opened vendor panel for {def.ScreenName}");
        _lastTalkedTemplate = null; // one-shot per talk
    }

    /// <summary>Phase 20d — apply a Buy/Sell intent emitted by the vendor
    /// panel. Buy debits gold (rejects if short) and appends a LootEntry to
    /// the player inventory; Sell removes the inventory row and credits the
    /// resolved sell price. The panel itself is purely intent — all stat
    /// mutation happens here so future vendor sources (other catalogs, hot
    /// reload) can fan in without duplicating economy code.</summary>
    private void ApplyVendorAction(SiegeFX.Runtime.Render.Hud.VendorAction act)
    {
        if (_progression is null) return;
        if (act.Kind == SiegeFX.Runtime.Render.Hud.VendorActionKind.Buy)
        {
            var def = _vendor.OpenVendor;
            if (def is null || act.Index < 0 || act.Index >= def.Stock.Count) return;
            var row = def.Stock[act.Index];
            if (!_progression.TryDebitGold(row.Price))
            {
                Console.WriteLine($"trade: cannot afford {row.ScreenName} ({row.Price}g, have {_progression.Gold}g)");
                return;
            }
            _playerInventory.Add(new SiegeFX.Core.Actors.LootEntry(row.Slot, row.ItemReference));
            _inventoryPanel.NotifyItemAdded();
            Console.WriteLine($"trade: bought {row.ScreenName} for {row.Price}g (gold now {_progression.Gold})");
        }
        else if (act.Kind == SiegeFX.Runtime.Render.Hud.VendorActionKind.Sell)
        {
            if (act.Index < 0 || act.Index >= _playerInventory.Count) return;
            var entry = _playerInventory[act.Index];
            long price = SiegeFX.Runtime.Render.Hud.VendorPanel.ResolveSellPrice(entry.Reference);
            _inventoryPanel.NotifyItemRemoved(act.Index);
            _playerInventory.RemoveAt(act.Index);
            _progression.CreditGold(price);
            Console.WriteLine($"trade: sold {entry.Reference} for {price}g (gold now {_progression.Gold})");
        }
    }

    // Phase 13d — RMB click-to-target attack. Same unproject math as
    // TryClickToMove, but instead of retargeting the follower we look for the
    // closest living combatant near the click point (XZ radius <see cref="ClickAttackRadius"/>).
    // Damage uses the player's stats when the template chain resolves non-zero
    // values; farmboy currently has 0 damage in stats (max_life formula bug,
    // project_siegefx_max_life_formula.md), so we fall back to a synthetic
    // 1-3 profile to keep the kill loop playable until the stat fix lands in
    // Phase 13e.
    private const float ClickAttackRadius = 3f;
    // Phase 12-SC-1 — drop-back reach when the PC template doesn't author one
    // (hero baseline ships AttackRange=0.5u which is wrist-length, too tight
    // to land a swing on a 1.8u-radius krug bubble). 2u mirrors ActorBrain's
    // mob fallback.
    // Phase 21-SC-BARREL-FOLD — 2 → 2.5 was a touch too far per the
    // user's eyes-on; settled at 2.3 which puts the hero ~2.18u from
    // target center (0.95 standoff multiplier in ComputeApproachPoint)
    // — ~1.18u clearance from a 1u-radius krug body, ~1.68u from a
    // 0.5u-radius barrel. Adjacent enough to swing but visibly not
    // overlapping.
    private const float MeleeReachFallback = 2.3f;
    // Phase 21-SC-BARREL-FOLD — was 0.1f. HeroBaselineStats authors a
    // 0.5u AttackRange (wrist length, intentional for bare-fist attacks)
    // which the old 0.1 gate accepted as the player's real reach — net
    // effect was the hero stopping 0.475u from any target's center, deep
    // inside the body. 1.5u threshold means anything below "weapon-tip
    // reach" falls back to MeleeReachFallback; only authored long-reach
    // weapons (polearms, two-handers) override it.
    private const float MeleeReachAttackRangeThreshold = 1.5f;
    // Phase 12-SC-1 — once latched, abandon the walk-up if the target wandered
    // farther than this. Bigger than reach so a moving krug doesn't break the
    // chase mid-stride; smaller than aggro so we don't track across the map.
    private const float MeleeChaseGiveUp = 14f;
    // Phase 12-SC-1 — pending click-attack target. Set when the player RMBs an
    // enemy beyond reach: the follower walks up, and once inside MeleeReach the
    // per-tick check fires the actual swing through PerformPlayerSwing.
    private ActorRenderState? _pendingAttackTarget;
    // Phase 13e — fallback attacker profile used when the PC template didn't
    // resolve real damage stats (hero templates author damage=0 and derive it
    // from the equipped weapon — Phase 14). 1-3 matches a bare-fisted level-1
    // hero swinging at farmhouse krug_scouts: ~4-5 hits per kill instead of
    // the instant kills the old 200-300 placeholder gave.
    private static readonly SiegeFX.Core.Actors.ActorStats HeroBaselineStats = new(
        MaxLife: 49f, MaxMana: 30f,
        DamageMin: 1f, DamageMax: 3f,
        Defense: 0f, AttackRange: 0.5f, WalkSpeed: 4.5f, ExperienceValue: 0,
        Strength: 10f, Dexterity: 10f, Intelligence: 10f);

    static float PlayerMeleeReach(SiegeFX.Core.Actors.ActorStats attacker) =>
        attacker.AttackRange > MeleeReachAttackRangeThreshold ? attacker.AttackRange : MeleeReachFallback;

    private void TryClickToAttack(Vector2 cursorPx)
    {
        if (_player is null || _window is null || _actors.Count == 0) return;
        if (_player.IsDead) return;
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return;

        float ndcX = (cursorPx.X / size.X) * 2f - 1f;
        float ndcY = 1f - (cursorPx.Y / size.Y) * 2f;
        float aspect = (float)size.X / size.Y;
        if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
        if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return;
        var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir  = far_ - near;
        if (dir.LengthSquared() < 1e-8f || MathF.Abs(dir.Y) < 1e-4f) return;

        float planeY = _player.CurrentTransform.Translation.Y;
        float t = (planeY - near.Y) / dir.Y;
        if (t < 0f) return;
        var groundHit = near + dir * t;

        // Closest-combatant-to-click in XZ. DS1's selection is more of a screen-
        // picking test, but on DS1's shallow terrain a planar radius at the click
        // point is indistinguishable from "the actor you clicked on" for 99% of
        // cases. Revisit if we start getting mis-picks behind tall props.
        ActorRenderState? best = null;
        float bestDist = ClickAttackRadius;
        foreach (var s in _actors)
        {
            if (s.IsDead) continue;
            if (s.IsPlayer) continue;
            if (!s.Actor.Stats.IsCombatant) continue;
            var pos = s.CurrentTransform.Translation;
            float dx = pos.X - groundHit.X;
            float dz = pos.Z - groundHit.Z;
            float d  = MathF.Sqrt(dx * dx + dz * dz);
            if (d < bestDist) { bestDist = d; best = s; }
        }
        if (best is null)
        {
            // Phase 17-SC-K — no live combatant under the cursor; try a
            // breakable static prop (barrels, crates, jugs). DS1 lets the
            // player smash these for a frag burst — same pick radius, same
            // reach gate as actor combat. PerformPropBreak handles the hit.
            if (TryClickToBreakProp(groundHit)) return;
            Console.WriteLine(
                $"click-attack: no combatant within {ClickAttackRadius:F1}u of " +
                $"({groundHit.X:F1}, {groundHit.Z:F1})");
            return;
        }

        // Phase 12-SC-1 — gate by player→target reach, not just click-pick
        // tolerance. ClickAttackRadius=3u is just "did you click on this guy
        // or near him"; the *swing* requires the player be within attacker
        // reach of the target. If not, latch _pendingAttackTarget so the
        // per-tick player block walks the follower up and fires the swing
        // when in range.
        var attackerStats = GetPlayerAttackStats();
        float reach = PlayerMeleeReach(attackerStats);
        var pPos = _playerFollower?.Position ?? _player.CurrentTransform.Translation;
        var tPos = best.CurrentTransform.Translation;
        float playerDist = MathF.Sqrt(
            (pPos.X - tPos.X) * (pPos.X - tPos.X) +
            (pPos.Z - tPos.Z) * (pPos.Z - tPos.Z));
        if (playerDist > reach)
        {
            _pendingAttackTarget = best;
            // Walk to a point just inside reach on the player→target line so
            // the follower stops at swinging distance instead of plowing in.
            var stop = ComputeApproachPoint(pPos, tPos, reach);
            _playerFollower?.SetTarget(stop);
            Console.WriteLine(
                $"click-attack: approaching {best.Actor.Template.Name} " +
                $"(dist={playerDist:F1}u, reach={reach:F1}u)");
            return;
        }

        PerformPlayerSwing(best, attackerStats);
    }

    static Vector3 ComputeApproachPoint(Vector3 from, Vector3 toCenter, float stopShortBy)
    {
        var d = new Vector3(toCenter.X - from.X, 0f, toCenter.Z - from.Z);
        float len = d.Length();
        if (len < 1e-3f) return from;
        // Phase 21-SC-BARREL-FOLD — DS1 stops the hero at the edge of
        // swing reach, not deep inside it. Pre-fold's 0.8x stand-off had
        // the hero plant 0.4u past the reach boundary toward the target,
        // overlapping any ~1u-radius body and making the target hard to
        // re-click. 0.95x leaves a 5% in-range safety margin (the "in
        // range" check at the per-tick site uses `pdist <= reach` so we
        // need to stop just inside the boundary, not on it) and visually
        // clears the target's body bubble for the typical hero/krug
        // pairing. Eyes-on the user expects "stands close to but not on
        // top of" — that's this distance.
        float walk = MathF.Max(0f, len - stopShortBy * 0.95f);
        var dir = d / len;
        return new Vector3(from.X + dir.X * walk, toCenter.Y, from.Z + dir.Z * walk);
    }

    /// <summary>Phase 17-SC-K — pick the closest live breakable static prop
    /// to the cursor's ground hit. Walks to it if out of reach; otherwise
    /// shatters it on the spot. DS1 barrels/crates/jugs are 1HP so a single
    /// swing is the norm, but we still route through max_life so heavier
    /// crates would take multiple hits if any ship them.</summary>
    private bool TryClickToBreakProp(Vector3 groundHit)
    {
        if (_player is null) return false;
        StaticPropInstance? best = null;
        float bestDist = ClickAttackRadius;
        foreach (var prop in _staticProps)
        {
            if (!prop.IsBreakable || prop.IsDestroyed) continue;
            var pos = prop.World.Translation;
            float dx = pos.X - groundHit.X;
            float dz = pos.Z - groundHit.Z;
            float d  = MathF.Sqrt(dx * dx + dz * dz);
            if (d < bestDist) { bestDist = d; best = prop; }
        }
        if (best is null) return false;

        var attackerStats = GetPlayerAttackStats();
        float reach = PlayerMeleeReach(attackerStats);
        var pPos = _playerFollower?.Position ?? _player.CurrentTransform.Translation;
        var tPos = best.World.Translation;
        float playerDist = MathF.Sqrt(
            (pPos.X - tPos.X) * (pPos.X - tPos.X) +
            (pPos.Z - tPos.Z) * (pPos.Z - tPos.Z));
        if (playerDist > reach)
        {
            // Static props don't sit in _pendingAttackTarget (that field
            // expects an ActorRenderState); for now just walk closer and
            // ask the user to click again. A latched-prop pending-target is
            // a Phase 21 polish item.
            var stop = ComputeApproachPoint(pPos, tPos, reach);
            _playerFollower?.SetTarget(stop);
            Console.WriteLine(
                $"click-break: approaching {best.Template} " +
                $"(dist={playerDist:F1}u, reach={reach:F1}u)");
            return true;
        }

        PerformPropBreak(best, attackerStats);
        return true;
    }

    /// <summary>Phase 17-SC-K — apply one swing's worth of damage to a
    /// breakable static prop. On Life&lt;=0 we kick a small smoke + spark
    /// burst at the prop origin (the wood-frag mesh chain referenced by
    /// `[break_particulate]` is a Phase 21 polish job — particles read as
    /// "shatter" until those frags wire through the loot pipeline) and
    /// flip IsDestroyed so the next render skips the placement.</summary>
    private void PerformPropBreak(StaticPropInstance prop, SiegeFX.Core.Actors.ActorStats attacker)
    {
        if (_player is null || prop.IsDestroyed) return;
        // Phase 21-SC-BARREL-FOLD — same one-swing-per-animation gate
        // PerformPlayerSwing uses. Click-spam on a barrel pre-fold
        // shattered the prop on the first click but kept firing damage
        // / debris / loot rolls on every queued click; the gate makes
        // that a no-op while the swing chore is in flight.
        if (_player.Actor.Host.IsOverrideActive) return;
        // Phase 21-SC-BARREL-FOLD — face the prop before swinging so
        // pivoting from a finished enemy to a barrel (or vice-versa)
        // doesn't swing in the stale direction.
        SnapPlayerFacingTo(prop.World.Translation);
        // Phase 21-SC-BARREL-FOLD — rotate through chore_attack sub-anims
        // (0mid + high alternates as a R→L / L→R cadence per the user's
        // observed DS1 behavior) and play the picked clip's full authored
        // duration instead of the old hardcoded 0.6s — DS1's fs1 (1H melee)
        // is 0.83s, so 0.6 was cutting the swing off at ~72%.
        float swingDur = _player.Actor.PrepNextSwingClip();
        _player.Actor.PlayChoreOnce("chore_attack", swingDur);
        _audio?.Play(SfxMeleeSwingGroup);
        // Single-hit shatter is the DS1 default for life=1 props; we still
        // roll real damage so heavier crates (if any) would survive a weak
        // swing rather than always one-shotting.
        var rng = new Random();
        float raw = MathF.Max(1f,
            SiegeFX.Core.Actors.CombatResolver.RollMeleeDamage(
                attacker,
                new SiegeFX.Core.Actors.ActorStats(
                    MaxLife: prop.MaxLife, MaxMana: 0,
                    DamageMin: 0, DamageMax: 0, Defense: 0,
                    AttackRange: 0, WalkSpeed: 0, ExperienceValue: 0,
                    Strength: 0, Dexterity: 0, Intelligence: 0),
                rng));
        prop.Life = MathF.Max(0f, prop.Life - raw);
        Console.WriteLine(
            $"click-break: hit {prop.Template} for {raw:F0} " +
            $"({prop.Life:F0}/{prop.MaxLife:F0})" +
            (prop.Life <= 0f ? "  *** SHATTERED ***" : ""));
        if (prop.Life <= 0f)
        {
            prop.IsDestroyed = true;
            // Phase 21-SC-BARREL-C — frag-mesh debris from the template's
            // [physics][break_particulate] block. The smoke puff + sparkle
            // ring stays as the on-impact "shatter" cue; the frag instances
            // are the lasting visual that DS1 ships.
            SpawnPropDebris(prop);
            // Phase 21-SC-BARREL-D — roll inventory.pcontent for gold +
            // item drops. Most barrels are empty; the regional templates
            // (barrel_glb_fh_r1 etc.) carry the actual loot tables.
            LogPropLootDrop(prop);
            // Phase 21-SC-BARREL-FOLD — material-specific break sound.
            // Wood barrels/crates author it as [aspect][voice][die][*];
            // stone/clay/metal containers as [physics][break_sound]. The
            // helper walks both and plays whichever the template defines.
            PlayPropBreakSfx(prop);
            if (_particles is not null)
            {
                var origin = prop.World.Translation + new Vector3(0f, 0.4f, 0f);
                _particles.SpawnSmoke(origin, new Vector4(1f, 1f, 1f, 1f), 0.8f, 1.2f, 12);
                _particles.SpawnSpark(origin, new Vector4(0.85f, 0.7f, 0.45f, 1f), 0.6f, 0.6f, 14);
            }
        }
    }

    /// <summary>Phase 21-SC-BARREL-B — cast the spell in <paramref name="slot"/>
    /// at a breakable static prop instead of an actor. Mirrors the actor cast
    /// path (range / mana / cooldown gate, chore_magic on success, native
    /// sfx_script + fallback SpawnSpellVisual, cast SFX, floating damage),
    /// but routes the rolled damage into the prop's life pool. On Life&lt;=0
    /// the prop disappears + smoke/spark debris kicks (frag-mesh debris and
    /// pcontent drops are sub-slices C and D respectively).</summary>
    private void PerformSpellOnProp(SiegeFX.Core.Actors.SpellSlot slot,
                                    SiegeFX.Core.Assets.SpellTemplate spell,
                                    StaticPropInstance prop, float magicLevel)
    {
        if (_player is null || _playerSpellbook is null || prop.IsDestroyed) return;
        var playerPos = _player.CurrentTransform.Translation;
        var propPos = prop.World.Translation;
        float dx = propPos.X - playerPos.X;
        float dz = propPos.Z - playerPos.Z;
        float dist = MathF.Sqrt(dx * dx + dz * dz);

        var result = _playerSpellbook.TryCastAtPoint(slot, dist, magicLevel);
        var anchor = propPos + new Vector3(0f, 0.6f, 0f);
        switch (result.Outcome)
        {
            case SiegeFX.Core.Actors.CastOutcome.OutOfRange:
                AddFloatingText("out of range", anchor + new Vector3(0f, 1.0f, 0f),
                                new Vector4(0.95f, 0.65f, 0.20f, 1f));
                return;
            case SiegeFX.Core.Actors.CastOutcome.NoMana:
                AddFloatingText("no mana", playerPos + new Vector3(0f, 2.1f, 0f),
                                new Vector4(0.45f, 0.65f, 1.00f, 1f));
                _audio?.Play(SfxGuiOutOfMana);
                return;
            case SiegeFX.Core.Actors.CastOutcome.OnCooldown:
                return;
            case SiegeFX.Core.Actors.CastOutcome.Cast:
                break;
            default:
                return;
        }

        _player.Actor.PlayChoreOnce("chore_magic", 0.7f);
        // Snap player facing so the bolt visibly originates *toward* the prop.
        if (dx * dx + dz * dz > 1e-6f)
        {
            float fl = MathF.Sqrt(dx * dx + dz * dz);
            _playerFacing = new Vector3(dx / fl, 0f, dz / fl);
            float pyaw = MathF.Atan2(_playerFacing.X, _playerFacing.Z);
            _player.CurrentTransform =
                Matrix4x4.CreateRotationY(pyaw) *
                Matrix4x4.CreateTranslation(playerPos);
            _playerRenderFacingPrev = _playerFacing;
            _playerRenderFacingNext = _playerFacing;
        }
        var src = playerPos + _playerFacing * 0.45f + new Vector3(0f, 1.25f, 0f);
        var dst = anchor;
        var elemColor = SpellElementColor(spell.Element);

        bool ranNativeScript = false;
        bool nativeProducedVisual = false;
        if (_sfxRuntime is not null && _sfxStore is not null
            && !string.IsNullOrEmpty(spell.CastSfxScript)
            && _sfxStore.TryGet(spell.CastSfxScript, out var castScript)
            && IsCastScriptFullyCovered(spell, castScript))
        {
            var ctx = new SiegeFX.Core.Sfx.SfxContext(
                SourcePos:     playerPos + new Vector3(0f, 1.0f, 0f),
                TargetPos:     dst,
                WeaponBonePos: src,
                Resolver:      ResolvePlayerBone);
            int boltsBefore       = _particles?.LiveBoltCount ?? 0;
            int particlesBefore   = _particles?.LiveParticleCount ?? 0;
            int persistentBefore  = _sfxRuntime.LivePersistentCount;
            ranNativeScript = _sfxRuntime.Spawn(spell.CastSfxScript, ctx);
            nativeProducedVisual = ((_particles?.LiveBoltCount ?? 0) > boltsBefore)
                                || ((_particles?.LiveParticleCount ?? 0) > particlesBefore)
                                || (_sfxRuntime.LivePersistentCount > persistentBefore);
        }
        if (!ranNativeScript || !nativeProducedVisual)
            SpawnSpellVisual(src, dst, spell.Element, elemColor);
        _audio?.Play(ResolveSpellCastSound(spell));

        float dealt = MathF.Max(1f, result.Damage);
        prop.Life = MathF.Max(0f, prop.Life - dealt);
        AddFloatingText($"{spell.ScreenName.ToUpperInvariant()} -{(int)MathF.Round(dealt)}",
                        anchor + new Vector3(0f, 1.4f, 0f), elemColor);
        AwardCombatXp((long)dealt, 0, SiegeFX.Core.Assets.SkillKind.CombatMagic);
        Console.WriteLine(
            $"cast {spell.ScreenName}: hit {prop.Template} for {dealt:F0} " +
            $"({prop.Life:F0}/{prop.MaxLife:F0})" +
            (prop.Life <= 0f ? "  *** SHATTERED ***" : ""));
        if (prop.Life <= 0f)
        {
            prop.IsDestroyed = true;
            if (_particles is not null)
            {
                var origin = prop.World.Translation + new Vector3(0f, 0.4f, 0f);
                _particles.SpawnSmoke(origin, new Vector4(1f, 1f, 1f, 1f), 0.8f, 1.2f, 12);
                _particles.SpawnSpark(origin, new Vector4(0.85f, 0.7f, 0.45f, 1f), 0.6f, 0.6f, 14);
            }
        }
    }

    /// <summary>Phase 21-SC-BARREL-A1 — first-touch lazy load of every cursor
    /// sprite. Static cursors are direct .raw lookups; animated ones decode
    /// .flm into per-frame 32x32 BGRA buffers. Run once per session — if any
    /// individual lookup fails (older content tank, corrupted DSRES) the
    /// matching state falls back to the pointer in
    /// <see cref="ResolveCursorVisual"/> so the cursor never disappears.</summary>
    private void EnsureCursorTextures()
    {
        if (_cursorTexturesAttempted) return;
        if (_gl is null || _playResolver is null) return;
        _cursorTexturesAttempted = true;
        _cursorPointer    = TryGetGuiTexture("b_gui_c_pointer");
        _cursorAttack     = TryGetGuiTexture("b_gui_c_attack1");
        // Phase 21-SC-BARREL-FOLD — DS1's b_gui_c_magic3 is authored as
        // a red sword overlaid with a blue magic glow ("magic strike on
        // enemy"); reads visually as two-icons-stacked because it
        // composes both the melee (red sword) AND magic (blue glow)
        // signals into one sprite. Per the user's eyes-on, the cleaner
        // read is "red = melee, blue = magic" with no overlap; using
        // b_gui_c_magic2 (the cast-on-terrain glow without the sword)
        // for spell-mode enemy hover gives that clean split. Falls back
        // to magic3 then attack/pointer if magic2.raw is missing.
        _cursorCastAttack = TryGetGuiTexture("b_gui_c_magic2")
                         ?? TryGetGuiTexture("b_gui_c_magic3");
        _cursorTalk       = TryGetGuiTexture("b_gui_c_talk");
        _cursorSmash      = LoadFlmFrames("b_gui_c_smash1.flm");
        _cursorGrab       = LoadFlmFrames("b_gui_c_grab1.flm");
        Console.WriteLine(
            $"[cursor] pointer={(_cursorPointer is not null ? "ok" : "MISS")}" +
            $" attack={(_cursorAttack is not null ? "ok" : "MISS")}" +
            $" castattack={(_cursorCastAttack is not null ? "ok" : "MISS")}" +
            $" talk={(_cursorTalk is not null ? "ok" : "MISS")}" +
            $" smash={(_cursorSmash is null ? "MISS" : _cursorSmash.Length + " frames")}" +
            $" grab={(_cursorGrab is null ? "MISS" : _cursorGrab.Length + " frames")}");
    }

    private GlTexture[]? LoadFlmFrames(string fileName)
    {
        if (_gl is null || _playResolver is null) return null;
        if (!_playResolver.TryLoadByBasename(fileName, out var bytes)) return null;
        var raw = SiegeFX.Core.Assets.FlmAnimation.LoadFrames(bytes);
        if (raw.Length == 0) return null;
        var texs = new GlTexture[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            texs[i] = new GlTexture(_gl, raw[i],
                SiegeFX.Core.Assets.FlmAnimation.FrameSize,
                SiegeFX.Core.Assets.FlmAnimation.FrameSize);
        return texs;
    }

    /// <summary>Phase 21-SC-BARREL-A1 — pick a cursor state for this render
    /// frame. Priority order matches the click handlers' search order so
    /// the cursor visual is a faithful preview of what a click does:
    /// enemy &gt; breakable &gt; loot pile &gt; talkable NPC &gt; default.
    /// Skipped while RMB camera-look is active (the OS owns the cursor in
    /// Raw mode), or before <see cref="_player"/> is spawned.</summary>
    private void UpdateCursorState()
    {
        _cursorState = CursorState.Pointer;
        if (_player is null || _window is null || _player.IsDead) return;
        if (_mouseLookActive) return;
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return;

        var cursorPx = _currentMousePos;
        float ndcX = (cursorPx.X / size.X) * 2f - 1f;
        float ndcY = 1f - (cursorPx.Y / size.Y) * 2f;
        float aspect = (float)size.X / size.Y;
        if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
        if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return;
        var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir  = far_ - near;
        if (dir.LengthSquared() < 1e-8f || MathF.Abs(dir.Y) < 1e-4f) return;
        float planeY = _player.CurrentTransform.Translation.Y;
        float t = (planeY - near.Y) / dir.Y;
        if (t < 0f) return;
        var groundHit = near + dir * t;

        float r2 = ClickAttackRadius * ClickAttackRadius;
        // 1) enemy under cursor → red sword (melee/ranged) or blue-glow
        //    sword (spell-mode). Active ability slot drives the swap:
        //    0=melee, 1=ranged → cursor_attack; 2=spell-Q, 3=spell-W →
        //    cursor_cast_attack. Mirrors the DS1 cursors.gas split
        //    between b_gui_c_attack1 and b_gui_c_magic3.
        bool spellMode = _activeAbilityIdx >= 2;
        foreach (var s in _actors)
        {
            if (s.IsDead || s.IsPlayer) continue;
            if (!s.Actor.Stats.IsCombatant) continue;
            var pos = s.CurrentTransform.Translation;
            float dx = pos.X - groundHit.X, dz = pos.Z - groundHit.Z;
            if (dx * dx + dz * dz < r2)
            {
                _cursorState = spellMode ? CursorState.CastAttack : CursorState.Attack;
                return;
            }
        }
        // 2) live breakable static prop under cursor → hammer.
        foreach (var prop in _staticProps)
        {
            if (!prop.IsBreakable || prop.IsDestroyed) continue;
            var pos = prop.World.Translation;
            float dx = pos.X - groundHit.X, dz = pos.Z - groundHit.Z;
            if (dx * dx + dz * dz < r2) { _cursorState = CursorState.Smash; return; }
        }
        // 3) loot pile under cursor → grab hand. Skip piles that are still
        //    in their throw-tumble (auto-pickup is gated on the same flag).
        foreach (var pile in _lootPiles)
        {
            if (pile.Throw is not null && pile.Throw.Elapsed < pile.Throw.Duration) continue;
            var pos = pile.Position;
            float dx = pos.X - groundHit.X, dz = pos.Z - groundHit.Z;
            if (dx * dx + dz * dz < r2) { _cursorState = CursorState.Grab; return; }
        }
        // 4) talkable NPC inside the wider talk radius → talk marker.
        if (_conversations is not null && _conversations.Count > 0)
        {
            float t2 = ClickTalkRadius * ClickTalkRadius;
            foreach (var s in _actors)
            {
                if (s.IsDead || s.IsPlayer) continue;
                var pos = s.CurrentTransform.Translation;
                float dx = pos.X - groundHit.X, dz = pos.Z - groundHit.Z;
                if (dx * dx + dz * dz > t2) continue;
                var keys = SiegeFX.Core.Assets.ConversationStore.KeysFromInstance(s.Actor.Instance.Node);
                bool talkable = false;
                foreach (var k in keys)
                    if (_conversations.TryGetValue(k, out var c) && c.Nodes.Count > 0) { talkable = true; break; }
                if (!talkable && SiegeFX.Core.Actors.VendorCatalog.Find(s.Actor.Template.Name) is not null) talkable = true;
                if (talkable) { _cursorState = CursorState.Talk; return; }
            }
        }
    }

    /// <summary>Phase 21-SC-BARREL-A1 — map the current cursor state to a
    /// (texture, hotspot, size) tuple. Animated states cycle frames at
    /// 12 fps off <see cref="_terrainTime"/> (so animation rate stays
    /// consistent regardless of render fps). Hotspots come straight from
    /// cursors.gas: 64x64 sprites use sethotspot(21,13); 32x32 use (9,5).
    /// All states fall back to the pointer if their dedicated texture
    /// failed to load — never returns null while the pointer resolved.</summary>
    private (GlTexture? tex, int hsx, int hsy, int sz) ResolveCursorVisual()
    {
        EnsureCursorTextures();
        const int big = 64, small = 32;
        const int hsBigX = 21, hsBigY = 13, hsSmallX = 9, hsSmallY = 5;
        switch (_cursorState)
        {
            case CursorState.Attack:
                return (_cursorAttack ?? _cursorPointer, hsBigX, hsBigY, big);
            case CursorState.CastAttack:
                return (_cursorCastAttack ?? _cursorAttack ?? _cursorPointer, hsBigX, hsBigY, big);
            case CursorState.Smash when _cursorSmash is { Length: > 0 }:
            {
                int frame = (int)(_terrainTime * 12.0) % _cursorSmash.Length;
                return (_cursorSmash[frame], hsSmallX, hsSmallY, small);
            }
            case CursorState.Grab when _cursorGrab is { Length: > 0 }:
            {
                int frame = (int)(_terrainTime * 12.0) % _cursorGrab.Length;
                return (_cursorGrab[frame], hsSmallX, hsSmallY, small);
            }
            case CursorState.Talk when _cursorTalk is not null:
                return (_cursorTalk, hsSmallX, hsSmallY, small);
            case CursorState.Pointer:
            default:
                return (_cursorPointer, hsBigX, hsBigY, big);
        }
    }

    /// <summary>Phase 21-SC-BARREL-A1 — hide the OS cursor once we've
    /// committed to drawing our own sprite. Idempotent; cheap to call
    /// every frame. Skipped while RMB camera-look is active (CursorMode
    /// is in Raw there for the look-grab).
    /// Phase 27-SP-FLYOUT — also hides during the frontend menu flow
    /// so the sword cursor (b_gui_c_pointer) sits in the menus too.</summary>
    private void EnsureOsCursorHidden()
    {
        if (_osCursorHidden) return;
        if (_input is null || _input.Mice.Count == 0) return;
        if (_mouseLookActive) return;
        // In-game: hide once player is spawned. Boot/frontend: hide once
        // the FrontendScene has reached the main-menu chrome stage so
        // the sword sprite owns the cursor in the menus too. The earlier
        // splash + Bink-stub frames keep the OS cursor (no menu ever
        // requires aiming during them and the brief flicker is fine).
        bool inGame = _player is not null;
        bool inMenuChrome = _bootMode && _frontendScene is not null
            && (_frontendScene.State == Hud.FrontendScene.ScreenState.MainMenu
             || _frontendScene.State == Hud.FrontendScene.ScreenState.MainMenuToSp
             || _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayer
             || _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayerToMm
             || _frontendScene.State == Hud.FrontendScene.ScreenState.SinglePlayerToCd
             || _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect
             || _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelectToSp
             || _frontendScene.State == Hud.FrontendScene.ScreenState.IntroMenuFlyIn);
        if (!inGame && !inMenuChrome) return;
        _input.Mice[0].Cursor.CursorMode = Silk.NET.Input.CursorMode.Hidden;
        _osCursorHidden = true;
    }

    /// <summary>Phase 21-SC-BARREL-FOLD — pivot the hero to face
    /// <paramref name="targetPos"/>. Pre-fold the player's facing only
    /// updated from movement deltas, so finishing one enemy and pivoting
    /// to a second adjacent target swung in the OLD direction (the hero
    /// hadn't moved between kills, so _playerFacing was stale). Same
    /// facing-snap pattern the spell-cast paths use; safe to call when
    /// already facing the target (the e2-radius gate skips the no-op
    /// case so we don't recreate the matrix on every swing).</summary>
    private void SnapPlayerFacingTo(Vector3 targetPos)
    {
        if (_player is null) return;
        var playerPos = _player.CurrentTransform.Translation;
        float dx = targetPos.X - playerPos.X;
        float dz = targetPos.Z - playerPos.Z;
        float fl2 = dx * dx + dz * dz;
        if (fl2 < 1e-6f) return;
        float fl = MathF.Sqrt(fl2);
        _playerFacing = new Vector3(dx / fl, 0f, dz / fl);
        float pyaw = MathF.Atan2(_playerFacing.X, _playerFacing.Z);
        _player.CurrentTransform =
            Matrix4x4.CreateRotationY(pyaw) *
            Matrix4x4.CreateTranslation(playerPos);
        _playerRenderFacingPrev = _playerFacing;
        _playerRenderFacingNext = _playerFacing;
    }

    private void PerformPlayerSwing(ActorRenderState best, SiegeFX.Core.Actors.ActorStats attacker)
    {
        if (_player is null || best.IsDead) return;
        // Phase 21-SC-BARREL-FOLD — gate rapid clicks on the swing
        // animation. Pre-fold a click-spammed RMB fired full damage +
        // swing audio per click while the chore override silently got
        // replaced each time, producing 5-6 audible "hits" per visible
        // swing arc. DS1 locks the player to one swing per animation;
        // mirroring that by ignoring the click while a chore_attack
        // override is still draining is the simplest faithful version.
        if (_player.Actor.Host.IsOverrideActive) return;
        // Phase 12-SC-2 / 21-SC-BARREL-FOLD — face the target before the
        // swing fires so finishing enemy 1 then attacking enemy 2 in the
        // same melee position pivots the hero to face the new victim
        // instead of swinging in the previous direction.
        SnapPlayerFacingTo(best.CurrentTransform.Translation);
        // Variant alternation (0mid ↔ high so the swing alternates
        // R→L horizontal and L→R backhand) plus the clip's authored
        // duration replace the pre-fold hardcoded 0.6s that truncated DS1's
        // 0.83s fs1 swing.
        float swingDur = _player.Actor.PrepNextSwingClip();
        _player.Actor.PlayChoreOnce("chore_attack", swingDur);
        // Phase 14c: damage derives from the equipped weapon when es_weapon_hand is
        // populated; otherwise the 1-3 HeroBaselineStats fallback stands in for
        // bare-fisted swings. DS1 hero templates author damage=0 because the real
        // number is the weapon's own attack.damage_min/max.
        var rng = new Random();
        float raw = SiegeFX.Core.Actors.CombatResolver.RollMeleeDamage(
            attacker, best.Actor.Stats, rng);
        float dealt = best.Actor.Combat.ApplyDamage(raw);
        float life = best.Actor.Combat.CurrentLife;
        float maxLife = best.Actor.Stats.MaxLife;
        Console.WriteLine(
            $"click-attack: hit {best.Actor.Template.Name} for {dealt:F0} " +
            $"({life:F0}/{maxLife:F0}){(best.Actor.Combat.IsDead ? "  *** DEAD ***" : "")}");

        // Phase 18b — every swing makes the swing sound; only swings that
        // land also play the flesh-hit variant. Whiff condition (dealt <=
        // 0) hits the miss SFX instead, which mirrors DS1's "hit/whiff
        // pair" behavior on melee.
        // Phase 18c — swing stays player-relative (your weapon), but the
        // hit/miss happens at the target's chest and pans accordingly.
        _audio?.Play(SfxMeleeSwingGroup);
        var hitPos = best.CurrentTransform.Translation + new Vector3(0f, 1.0f, 0f);
        if (dealt > 0f) PlayMeleeHit(hitPos);
        else            _audio?.PlayAt(SfxMeleeMiss, hitPos);

        // Phase 16d — XP per damage point + kill bonus from aspect.experience_value.
        // Melee skill is hardcoded for now (the only swing flavor we have); when
        // ranged/spells land they pick their own SkillKind at the call site.
        AwardCombatXp((long)dealt, best.Actor.Combat.IsDead ? best.Actor.Stats.ExperienceValue : 0,
                       SiegeFX.Core.Assets.SkillKind.Melee);

        if (best.Actor.Combat.ConsumeJustDied())
        {
            best.IsDead = true;
            best.Brain = null;
            // Phase 12-SC-4 — fire chore_die so the corpse falls instead of staying
            // upright on the idle clip. Held forever (PositiveInfinity); the
            // AnimTime tick + draw loop clamp time at the last frame so we don't
            // loop the death animation.
            BeginDeathChore(best);
            // Phase 9-SC-2 — death SFX from template's [aspect][voice][die].
            PlayDeathSfx(best.Actor.Template, best.CurrentTransform.Translation);
            LogLootDrop(best.Actor, best.CurrentTransform.Translation);
            OnActorKilled(best.Actor.Template.Name, best.CurrentTransform.Translation);
            CreditGoldFromKill(best.Actor.Stats.ExperienceValue, best.CurrentTransform.Translation);
        }
    }

    /// <summary>Phase 12-SC-4 — kick off the death chore on an actor that just died.
    /// Pins <c>chore_die</c> on the override layer with infinite duration; the
    /// AnimTime tick block clamps the playhead at the last frame so the corpse
    /// holds its final pose rather than looping. Falls back silently if the
    /// template doesn't ship a chore_die (Phase 10-SC-2 catalogue showed 179/179
    /// fh_r1 combatants do).</summary>
    static void BeginDeathChore(ActorRenderState s)
    {
        int dieIdx = s.Actor.GetClipIndex("chore_die");
        if (dieIdx < 0) return;
        s.Actor.PlayChoreOnce("chore_die", float.PositiveInfinity);
        s.AnimTime = 0;
        s.LastClipIndex = dieIdx;
    }

    /// <summary>Phase 20c — single funnel for "an actor just died". Credits
    /// the kill against any active quest objectives and queues a HUD toast for
    /// each quest that just completed. Called from all three player-kill paths
    /// (melee click, spell zap, debug F-key) so the loop is consistent across
    /// input surfaces. Future trap / ally-kill credit can hit the same funnel.</summary>
    private void OnActorKilled(string templateName, Vector3 worldPos)
    {
        if (_progression is null || string.IsNullOrEmpty(templateName)) return;
        var completed = _progression.Journal.RegisterKill(templateName);
        foreach (var key in completed)
        {
            string label = _progression.Journal.TryGet(key, out var entry) && entry?.Definition is { } d
                ? d.ScreenName : key;
            Console.WriteLine($"[quest] completed: {label} ({key})");
            AddFloatingText($"Quest complete: {label}",
                            (_player?.CurrentTransform.Translation ?? worldPos) + new Vector3(0f, 2.4f, 0f),
                            new Vector4(1.00f, 0.85f, 0.40f, 1f));
        }
    }

    /// <summary>Phase 20d — credit a gold drop. Pulled out of the death funnel
    /// so the spell zap, melee click, and debug F-key can all hand the same
    /// (template, worldPos) shape down. DS1 rolls a per-template gold range
    /// off treasure_set.gas; until that's wired we proxy off the dead actor's
    /// experience value (a stable proxy for "how dangerous was this kill"),
    /// scaled and floored so a low-XP chicken never drops 0 and a juggernaut
    /// doesn't dump the entire economy in one shot.</summary>
    private void CreditGoldFromKill(int experienceValue, Vector3 worldPos)
    {
        if (_progression is null || experienceValue <= 0) return;
        // Proxy formula: half the XP value, +25% jitter, floored at 1. The
        // caller already gated on "actor died this frame" so per-kill firing
        // is implicit — no need for a kill-edge guard here.
        long drop = Math.Max(1, experienceValue / 2);
        _progression.CreditGold(drop);
        AddFloatingText($"+{drop} gold", worldPos + new Vector3(0f, 1.6f, 0f),
                        new Vector4(1.00f, 0.92f, 0.40f, 1f));
    }

    /// <summary>Phase 20c — render a chevron pointing at the active quest's
    /// nearest live target. Two cases: on-screen target gets a chevron drawn at
    /// the projected screen position (above the actor's head); off-screen or
    /// behind-camera target gets the chevron clamped to a 28-px margin around
    /// the viewport, rotated to point outward. Draws nothing when no active
    /// quest has a kill objective or no live target matches.</summary>
    private void DrawQuestGoalMarker(int viewportW, int viewportH, Matrix4x4 vp)
    {
        if (_progression is null || _barRenderer is null) return;

        // Pick the first active quest with an unmet kill objective. Stable
        // pick (enumeration order in the Dictionary is insertion order in
        // .NET's implementation) so the marker doesn't strobe between quests.
        SiegeFX.Core.Actors.QuestEntry? questEntry = null;
        foreach (var e in _progression.Journal.Active)
        {
            if (e.Definition is null) continue;
            if (e.Definition.KillCountGoal <= 0) continue;
            if (e.KillProgress >= e.Definition.KillCountGoal) continue;
            questEntry = e; break;
        }
        if (questEntry?.Definition is not { } qdef) return;

        // Find nearest live actor whose template matches the goal substring.
        var anchor = _player?.CurrentTransform.Translation ?? _camera.Position;
        ActorRenderState? best = null;
        float bestDist = float.MaxValue;
        foreach (var s in _actors)
        {
            if (s.IsDead || s.Actor.Combat.IsDead) continue;
            if (s.Actor.Template.Name.IndexOf(qdef.KillTargetTemplate,
                StringComparison.OrdinalIgnoreCase) < 0) continue;
            float d = Vector3.DistanceSquared(s.CurrentTransform.Translation, anchor);
            if (d < bestDist) { bestDist = d; best = s; }
        }
        if (best is null) return;

        // Project the target's head to clip space. Behind the camera handling:
        // we still want to show an off-screen arrow pointing toward it, so when
        // W<=0 we flip the projected NDC by negating both axes — that yields an
        // off-screen point in roughly the right direction once clamped.
        var headPos = best.CurrentTransform.Translation + new Vector3(0f, 2.4f, 0f);
        var clip    = Vector4.Transform(new Vector4(headPos, 1f), vp);
        bool behind = clip.W <= 0.001f;
        float ndcX  = clip.X / (behind ? -clip.W : clip.W);
        float ndcY  = clip.Y / (behind ? -clip.W : clip.W);
        if (behind) { ndcX = -ndcX; ndcY = -ndcY; }

        float sx = (ndcX * 0.5f + 0.5f) * viewportW;
        float sy = (1f - (ndcY * 0.5f + 0.5f)) * viewportH;

        const int margin = 28;
        bool offscreen = behind || sx < margin || sx > viewportW - margin
                                || sy < margin || sy > viewportH - margin;

        var marker = new Vector4(1.00f, 0.85f, 0.40f, 0.95f);
        if (!offscreen)
        {
            // On-screen: chevron above the actor's head — small downward
            // triangle (3 stacked rects) so we don't need a triangle batcher.
            int cx = (int)sx, cy = (int)sy;
            _barRenderer.DrawRect(viewportW, viewportH, cx - 7, cy - 14, 14, 3, marker);
            _barRenderer.DrawRect(viewportW, viewportH, cx - 4, cy - 10, 8,  3, marker);
            _barRenderer.DrawRect(viewportW, viewportH, cx - 1, cy -  6, 2,  3, marker);
        }
        else
        {
            // Off-screen: clamp to a margin rectangle around the viewport so
            // the player can still see "the quest target is over there".
            float cxF = MathF.Min(MathF.Max(sx, margin), viewportW - margin);
            float cyF = MathF.Min(MathF.Max(sy, margin), viewportH - margin);
            int cx = (int)cxF, cy = (int)cyF;
            // 12px square pip with a 4px tail toward the off-screen direction.
            _barRenderer.DrawRect(viewportW, viewportH, cx - 6, cy - 6, 12, 12, marker);
            float dx = sx - viewportW * 0.5f;
            float dy = sy - viewportH * 0.5f;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.001f)
            {
                int tx = cx + (int)(dx / len * 10f);
                int ty = cy + (int)(dy / len * 10f);
                _barRenderer.DrawRect(viewportW, viewportH, tx - 2, ty - 2, 4, 4, marker);
            }
        }
    }

    /// <summary>Phase 18b — resolve a template name to the matching death
    /// .wav. Phase 9-SC-2 made this fully data-driven: walk the actor's
    /// template's specializes chain for <c>[aspect][voice][die] *</c>; that
    /// attribute's value is the WAV stem (e.g. <c>s_e_die_krug_scout</c>).
    /// On first encounter, register the clip from Sound.dsres; subsequent
    /// kills hit the cached buffer. Falls back silently if no ancestor
    /// authored the cue (chickens go quietly).
    /// Phase 18c added <paramref name="worldPos"/> so the death scream
    /// pans + falls off from the corpse's location.</summary>
    /// <summary>Phase 22-SC-MUSIC-B — extract the named track from
    /// Sound.dsres and hand it to <see cref="_music"/>. <paramref name="trackBasename"/>
    /// omits the leading <c>s_m_</c> and trailing <c>.mp3</c>, so callers
    /// pass <c>frontend</c>, <c>maintheme</c>, <c>battle</c> verbatim.
    /// Idempotent on the same track — re-calling with the active track
    /// is a no-op so region re-entry / quickload don't restart the clip.
    /// Pass <c>null</c> or empty to stop music entirely.</summary>
    private void PlayMusicTrack(string? trackBasename)
    {
        if (_music is null) return;
        if (string.IsNullOrEmpty(trackBasename))
        {
            if (_currentMusicTrack.Length == 0) return;
            _music.Stop();
            _currentMusicTrack = "";
            return;
        }
        if (string.Equals(_currentMusicTrack, trackBasename, StringComparison.OrdinalIgnoreCase))
            return; // already playing this track
        if (_playSoundTank is null) return;
        var path = $"/sound/music/s_m_{trackBasename}.mp3";
        var reader = new SiegeFX.Core.Tank.TankReader(_playSoundTank);
        if (!reader.TryGetFile(path, out _))
        {
            Console.Error.WriteLine($"  music: track '{path}' not in Sound.dsres");
            return;
        }
        try
        {
            var bytes = reader.ExtractToMemory(path);
            if (_music.Play(bytes))
            {
                _currentMusicTrack = trackBasename;
                Console.WriteLine($"  music: playing {path} ({bytes.Length:N0} bytes)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  music: track '{trackBasename}' failed — {ex.Message}");
        }
    }

    private void PlayDeathSfx(SiegeFX.Core.Assets.Template template, Vector3 worldPos)
    {
        if (_audio is null || template is null || _templateStore is null) return;
        var cue = _templateStore.GetAttribute(template, "aspect", "voice", "die", "*");
        if (string.IsNullOrEmpty(cue)) return;
        if (_registeredDeathCues.Add(cue) && _playSoundTank is not null)
        {
            var reader = new SiegeFX.Core.Tank.TankReader(_playSoundTank);
            TryRegisterSfx(reader, cue, $"/sound/effects/{cue}.wav");
        }
        _audio.PlayAt(cue, worldPos + new Vector3(0f, 1.0f, 0f));
    }

    /// <summary>Phase 21-SC-BARREL-FOLD — play the shatter cue for a
    /// breakable static prop. DS1 stores the cue under one of two
    /// attribute paths depending on which authoring tool produced the
    /// template:
    /// <list type="bullet">
    ///   <item><c>[aspect][voice][die][*]</c> — wood barrels / crates /
    ///         doors / breakable rocks. Same path PlayDeathSfx uses for
    ///         actors, so we reuse the registry cache.</item>
    ///   <item><c>[physics][break_sound]</c> — stone / clay / metal
    ///         containers. Authored by a different content team and
    ///         landed on a different attribute. <c>break_sound</c> with
    ///         a leading-empty value means "no sound" (powder kegs etc.
    ///         author this to suppress the default break cue), so guard
    ///         against an empty attribute resolving to a stale cache hit.</item>
    /// </list>
    /// Falls back silently when neither attribute is authored — keeps
    /// the audio path graceful for templates that ship break_particulate
    /// without a sound (the explosion variants explicitly use camera FX
    /// instead).</summary>
    private void PlayPropBreakSfx(StaticPropInstance prop)
    {
        if (_audio is null || _templateStore is null) return;
        if (!_templateStore.TryGet(prop.Template, out var template) || template is null) return;
        var cue = _templateStore.GetAttribute(template, "aspect", "voice", "die", "*")
               ?? _templateStore.GetAttribute(template, "physics", "break_sound");
        if (string.IsNullOrWhiteSpace(cue)) return;
        if (_registeredDeathCues.Add(cue) && _playSoundTank is not null)
        {
            var reader = new SiegeFX.Core.Tank.TankReader(_playSoundTank);
            TryRegisterSfx(reader, cue, $"/sound/effects/{cue}.wav");
        }
        _audio.PlayAt(cue, prop.World.Translation + new Vector3(0f, 0.6f, 0f));
    }

    /// <summary>Phase 9-SC-9 — read the dropped item's
    /// <c>[aspect][voice][put_down] *</c> off the resolved template, lazy-
    /// register the WAV from Sound.dsres, and play it positioned at the drop.
    /// Falls back to the generic gui_inventory_sheet cue if no put_down was
    /// authored, or if the cue resolves to a SED-only stem (suffix
    /// <c>_sed</c> — that's a Sound Effect Descriptor with multiple variants,
    /// no single WAV by that name; full SED→WAV resolution is parked for a
    /// later phase). Mirrors PlayDeathSfx caching otherwise.</summary>
    private void PlayPutDownSfx(string itemRef, Vector3 worldPos)
    {
        if (_audio is null || _templateStore is null) return;
        if (!_templateStore.TryGet(itemRef, out var tpl) || tpl is null)
        {
            _audio.PlayAt(SfxGuiInventory, worldPos);
            return;
        }
        var cue = _templateStore.GetAttribute(tpl, "aspect", "voice", "put_down", "*");
        if (string.IsNullOrEmpty(cue) || cue.EndsWith("_sed", StringComparison.OrdinalIgnoreCase))
        {
            _audio.PlayAt(SfxGuiInventory, worldPos);
            return;
        }
        if (_registeredPutDownCues.Contains(cue))
        {
            _audio.PlayAt(cue, worldPos);
            return;
        }
        if (_playSoundTank is null)
        {
            _audio.PlayAt(SfxGuiInventory, worldPos);
            return;
        }
        var reader = new SiegeFX.Core.Tank.TankReader(_playSoundTank);
        if (TryRegisterSfx(reader, cue, $"/sound/effects/{cue}.wav"))
        {
            _registeredPutDownCues.Add(cue);
            _audio.PlayAt(cue, worldPos);
        }
        else
        {
            _audio.PlayAt(SfxGuiInventory, worldPos);
        }
    }

    /// <summary>Phase 9-SC-9 — pop an inventory entry out of the player's bag
    /// and spawn it back into the world as a one-item loot pile tossed
    /// forward of the PC by ~3.5u (past <see cref="PickupRadius"/>) so the
    /// auto-pickup tick doesn't immediately scoop it back up. Mirrors DS1's
    /// "drop animation tosses the item a step away" behavior — the pile sits
    /// in front of you until you walk forward into the pickup radius.
    /// Notifies the inventory panel first so its placement array stays
    /// indexed against the live list. Triggers the per-template put_down cue.</summary>
    private void DropInventoryItem(int index)
    {
        if (index < 0 || index >= _playerInventory.Count) return;
        var entry = _playerInventory[index];
        var feet = _player?.CurrentTransform.Translation ?? _camera.Position;

        // Toss outward in the PC's current facing. Falls back to a fixed +Z
        // offset when no facing has been recorded yet (immediately after spawn
        // before any movement input). 3.5u clears the 1.8u pickup radius
        // with margin, matching DS1's visible "throw" distance.
        var facing = _playerFacing;
        if (facing.LengthSquared() < 1e-4f) facing = Vector3.UnitZ;
        else facing = Vector3.Normalize(facing);
        var dropPos = feet + facing * 3.5f;

        _inventoryPanel.NotifyItemRemoved(index);
        _playerInventory.RemoveAt(index);

        // Phase 9-SC-10 — if the dropped item was currently equipped (e.g. the
        // PC throws their own shield), unbind the equipment slot and refresh
        // the attach + layered-equipment passes so the rendered shield/boot/helm
        // stops floating on the bone after the inventory entry is gone.
        bool weaponDropped = false;
        bool nonWeaponDropped = false;
        string? droppedSlotKey = null;
        foreach (var kv in _playerEquipment)
        {
            if (string.Equals(kv.Value, entry.Reference, StringComparison.OrdinalIgnoreCase))
            { droppedSlotKey = kv.Key; break; }
        }
        if (droppedSlotKey is not null)
        {
            _playerEquipment.Remove(droppedSlotKey);
            if (string.Equals(droppedSlotKey, "es_weapon_hand", StringComparison.OrdinalIgnoreCase))
                weaponDropped = true;
            else
                nonWeaponDropped = true;
            Console.WriteLine($"  unequipped: [{droppedSlotKey}] (dropped to world)");
        }
        if (weaponDropped) TryLoadPlayerWeapon();
        if (nonWeaponDropped && _player is not null)
            TryLoadPlayerEquipment(_player.Actor.Template);
        if (weaponDropped || nonWeaponDropped) RefreshPlayerStance();

        var pile = new LootPile(feet, new List<SiegeFX.Core.Actors.LootEntry>
        {
            new SiegeFX.Core.Actors.LootEntry("", entry.Reference)
        })
        {
            RestPitch = ComputeLootRestPitch(entry.Reference),
            Throw = new LootThrow
            {
                Source        = feet,
                Target        = dropPos,
                Duration      = 0.45f,
                Elapsed       = 0f,
                ArcHeight     = 0.6f,
                Spins         = 1f, // one full turn while in the air
                StartRotation = MathF.Atan2(facing.X, facing.Z),
            }
        };
        _lootPiles.Add(pile);
        Console.WriteLine($"  drop: {entry.Reference} at {dropPos.X:F1},{dropPos.Z:F1}");
        PlayPutDownSfx(entry.Reference, dropPos);
    }

    /// <summary>Phase 9-SC-13 — pcontent-spec → concrete-template resolver
    /// shared by every system that needs to look something up off the rolled
    /// template (mesh, icon, screen_name, equipment swap, save). Returns
    /// <paramref name="itemRef"/> unchanged when it's already a concrete name
    /// or when the resolver can't roll it. Logs the resolution once per spec
    /// so the diagnostic trail still says <c>pcontent: #club/2-3 -> X</c>.</summary>
    private string ResolveItemRef(string itemRef)
    {
        if (string.IsNullOrEmpty(itemRef)) return itemRef;
        if (_resolvedSpecCache.TryGetValue(itemRef, out var hit)) return hit;
        if (_pcontentResolver is null
            || !SiegeFX.Core.Actors.PcontentResolver.IsSpec(itemRef)
            || !_pcontentResolver.TryResolve(itemRef, _pcontentRng, out var rolled, out var power))
        {
            _resolvedSpecCache[itemRef] = itemRef;
            return itemRef;
        }
        _resolvedSpecCache[itemRef] = rolled;
        Console.WriteLine($"  pcontent: {itemRef} -> {rolled} (power={power})");
        return rolled;
    }

    /// <summary>Phase 9-SC-7 — resolve and cache the ASP+RAW for an item
    /// template name. Returns null when the template has no <c>[aspect][model]</c>
    /// (e.g. gold-only piles, scroll-less items) or the asset failed to load;
    /// the negative result is cached too so the lookup is one-shot per name.</summary>
    private ItemMesh? TryGetItemMesh(string itemRef)
    {
        if (_gl is null || _playResolver is null || _templateStore is null) return null;
        if (_itemMeshCache.TryGetValue(itemRef, out var cached)) return cached;

        var resolvedRef = ResolveItemRef(itemRef);

        ItemMesh? result = null;
        string? missReason = null;
        try
        {
            if (!_templateStore.TryGet(resolvedRef, out var tpl))
            {
                missReason = resolvedRef.StartsWith('#')
                    ? "pcontent-spec unresolved (class not in resolver index)"
                    : "no template";
            }
            else
            {
                var modelName = _templateStore.GetAttribute(tpl!, "aspect", "model");
                if (string.IsNullOrEmpty(modelName))
                {
                    missReason = "no aspect.model";
                }
                else if (!_playResolver.TryLoadModel(modelName!, out var aspBytes))
                {
                    missReason = $"model load failed: {modelName} (resolved from {itemRef})";
                }
                else
                {
                    var asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes);
                    var mesh = new StaticMesh(_gl, asp);
                    GlTexture? tex = null;
                    if (asp.TextureNames.Count > 0
                        && _playResolver.TryLoadByBasename(asp.TextureNames[0] + ".raw", out var texBytes))
                    {
                        try { tex = new GlTexture(_gl, RawImage.Load(texBytes)); }
                        catch { tex = null; }
                    }
                    // Phase 9-SC-LL — pull the player-facing label off the
                    // resolved template's [common][screen_name]. Quote-stripped
                    // because gas serializes string attributes wrapped in "".
                    // Falls back to null when the template omits screen_name;
                    // the world-label code then skips drawing rather than
                    // exposing the engine name (e.g. "cb_un_2h_troll_rock").
                    var label = _templateStore.GetAttribute(tpl!, "common", "screen_name");
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        label = label.Trim();
                        if (label.Length >= 2 && label[0] == '"' && label[^1] == '"')
                            label = label[1..^1];
                    }
                    if (string.IsNullOrWhiteSpace(label)) label = null;
                    result = new ItemMesh(mesh, tex, label);
                }
            }
        }
        catch (Exception ex) { result = null; missReason = $"exception: {ex.GetType().Name}"; }

        if (result is null && missReason is not null && _loggedItemRefMisses.Add(itemRef))
            Console.WriteLine($"  loot-mesh miss: {itemRef} ({missReason})");

        _itemMeshCache[itemRef] = result;
        return result;
    }

    /// <summary>Phase 9-SC-13 — resolve and cache the inventory icon for an
    /// item template name. Mirrors <see cref="TryGetItemMesh"/>'s
    /// pcontent-spec-aware path: rolls #specs through the resolver, reads
    /// <c>[gui][inventory_icon]</c>, loads the matching .raw from the play
    /// resolver. Returns null when the template omits the icon attribute
    /// or the .raw is missing — the panel falls back to a text label.</summary>
    private GlTexture? TryGetItemIcon(string itemRef)
    {
        if (_gl is null || _playResolver is null || _templateStore is null) return null;
        if (_itemIconCache.TryGetValue(itemRef, out var cached)) return cached;

        var resolvedRef = ResolveItemRef(itemRef);

        GlTexture? result = null;
        string? missReason = null;
        try
        {
            if (!_templateStore.TryGet(resolvedRef, out var tpl))
            {
                missReason = "no template";
            }
            else
            {
                var iconName = _templateStore.GetAttribute(tpl!, "gui", "inventory_icon");
                if (string.IsNullOrEmpty(iconName))
                {
                    missReason = "no gui.inventory_icon";
                }
                else if (!_playResolver.TryLoadByBasename(iconName + ".raw", out var iconBytes))
                {
                    missReason = $"icon load failed: {iconName}";
                }
                else
                {
                    try { result = new GlTexture(_gl, RawImage.Load(iconBytes)); }
                    catch (Exception ex) { missReason = $"icon decode failed: {ex.GetType().Name}"; }
                }
            }
        }
        catch (Exception ex) { missReason = $"exception: {ex.GetType().Name}"; }

        if (result is null && missReason is not null && _loggedItemIconMisses.Add(itemRef))
            Console.WriteLine($"  inv-icon miss: {itemRef} ({missReason})");

        _itemIconCache[itemRef] = result;
        return result;
    }

    /// <summary>Phase 9-SC-14 — resolve and cache the inventory footprint
    /// authored on the template's <c>[gui][inventory_width]</c> /
    /// <c>[gui][inventory_height]</c>. Defaults to (1,1) when either is
    /// missing or non-positive; clamps to the panel grid so a wildly oversized
    /// authored value can't break placement. Pcontent specs flow through the
    /// shared <see cref="ResolveItemRef"/> cache.</summary>
    private (int W, int H) TryGetItemGridSize(string itemRef)
    {
        if (_templateStore is null) return (1, 1);
        if (_itemGridCache.TryGetValue(itemRef, out var cached)) return cached;

        var resolvedRef = ResolveItemRef(itemRef);
        int w = 1, h = 1;
        if (_templateStore.TryGet(resolvedRef, out var tpl))
        {
            var ws = _templateStore.GetAttribute(tpl!, "gui", "inventory_width");
            var hs = _templateStore.GetAttribute(tpl!, "gui", "inventory_height");
            if (int.TryParse(ws, out var pw) && pw > 0) w = pw;
            if (int.TryParse(hs, out var ph) && ph > 0) h = ph;
        }
        if (w > InventoryPanel.GridCols) w = InventoryPanel.GridCols;
        if (h > InventoryPanel.GridRows) h = InventoryPanel.GridRows;
        var result = (w, h);
        _itemGridCache[itemRef] = result;
        return result;
    }

    /// <summary>Phase 9-SC-8 — pick the hit cue based on the equipped
    /// weapon's material. DS1 ships flesh-impact variants for steelsword
    /// (5) and steeledge (3); other materials (wood, etc.) currently
    /// fall back to steelsword. Lazy-registers the WAVs and the group on
    /// first encounter; subsequent kills hit the cached buffers.</summary>
    private void PlayMeleeHit(Vector3 worldPos)
    {
        if (_audio is null) return;
        var material = _playerWeaponMaterial;
        var groupId = SfxMeleeHitGroupPrefix + material;
        if (!_registeredHitGroups.Contains(groupId))
        {
            if (_playSoundTank is null) { _audio.PlayAt(SfxMeleeMiss, worldPos); return; }
            var reader = new SiegeFX.Core.Tank.TankReader(_playSoundTank);
            var registered = new List<string>();
            for (int i = 1; i <= 5; i++)
            {
                var clipId = $"hit_{material}_flesh{i}";
                var path = $"/sound/effects/s_e_hit_{material}_flesh{i}.wav";
                if (TryRegisterSfx(reader, clipId, path)) registered.Add(clipId);
            }
            if (registered.Count == 0 && !string.Equals(material, "steelsword", StringComparison.OrdinalIgnoreCase))
            {
                // Material has no shipped flesh cues — fall back to steelsword's family.
                _playerWeaponMaterial = "steelsword";
                PlayMeleeHit(worldPos);
                return;
            }
            if (registered.Count > 0)
                _audio.RegisterGroup(groupId, registered.ToArray());
            _registeredHitGroups.Add(groupId);
        }
        _audio.PlayAt(groupId, worldPos);
    }

    /// <summary>Pull a .wav blob out of <paramref name="reader"/> and hand
    /// it to the audio engine. One-time call at LoadPlayActors. Logs and
    /// keeps going on failure so a missing/corrupt asset doesn't tank the
    /// scene load.</summary>
    private bool TryRegisterSfx(SiegeFX.Core.Tank.TankReader reader,
                                string clipId, string tankPath)
    {
        if (_audio is null) return false;
        try
        {
            if (!reader.TryGetFile(tankPath, out _))
            {
                Console.Error.WriteLine($"  audio: '{tankPath}' missing in Sound.dsres");
                return false;
            }
            var bytes = reader.ExtractToMemory(tankPath);
            if (_audio.RegisterClip(clipId, bytes))
            {
                // Phase 21d-2a-xii — apply DS1 SED playback range if one
                // exists for this clip's source file. SED keys match the
                // wav basename (no extension, no path) — e.g. /sound/
                // effects/s_e_zap_cast.wav → SED "s_e_zap_cast". Skip the
                // lookup if no SED store loaded or no descriptor found
                // (clip plays at unity pitch — same as pre-xii).
                if (_sedStore is not null)
                {
                    var basename = Path.GetFileNameWithoutExtension(tankPath);
                    if (_sedStore.TryGetValue(basename, out var sed)
                        && (sed.MinPlaybackRate != 1f || sed.MaxPlaybackRate != 1f))
                    {
                        _audio.RegisterPitch(clipId, sed.MinPlaybackRate, sed.MaxPlaybackRate);
                    }
                }
                Console.WriteLine($"  audio: '{clipId}' ← {tankPath} ({bytes.Length} B)");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  audio: '{clipId}' load threw — {ex.Message}");
            return false;
        }
    }

    // Phase 21-SC-SPELL-VFX-3a — pick the audio clip-id this spell's cast
    // wants and ensure it's registered with the audio engine. Returns the
    // id Play() should be called with. The caller does the Play.
    //
    // Strategy on the first cast of each spell: parse its compiled cast
    // script for the first `sound play <clip>` statement, strip any `_SED`
    // suffix (SED resolution kicks in via SedStore basename match inside
    // TryRegisterSfx), check the wav exists in Sound.dsres, register it.
    // On any miss (no SoundPlay in script, wav not in tank, parse threw)
    // fall back to SfxZapCast which is pre-registered at startup.
    //
    // Cached per spell so subsequent casts don't recompile the script or
    // hit the tank reader. Cache value IS the final Play()-able clip id —
    // SfxZapCast for fallbacks, the resolved name for hits.
    private string ResolveSpellCastSound(SiegeFX.Core.Assets.SpellTemplate spell)
    {
        if (_spellCastSoundCache.TryGetValue(spell.Name, out var cached))
            return cached;
        string clip = ResolveCastSoundClipName(spell);
        string final;
        if (string.IsNullOrEmpty(clip) || _playSoundTank is null)
        {
            final = SfxZapCast;
        }
        else
        {
            var reader = new SiegeFX.Core.Tank.TankReader(_playSoundTank);
            var wavPath = $"/sound/effects/{clip}.wav";
            if (reader.TryGetFile(wavPath, out _))
            {
                // wav exists. Register the clip. Note: AudioEngine.RegisterClip
                // is NOT cheap on duplicate ids — it re-decodes + re-uploads
                // the WAV every call (see AudioEngine.cs ~line 139). The cache
                // we set just below ensures each spell hits TryRegisterSfx at
                // most once per launch session, so the redundancy never runs.
                TryRegisterSfx(reader, clip, wavPath);
                final = clip;
            }
            else
            {
                Console.Error.WriteLine(
                    $"  spellbook: cast wav missing for '{spell.Name}' " +
                    $"({wavPath} not in Sound.dsres) — falling back to zap cue");
                final = SfxZapCast;
            }
        }
        _spellCastSoundCache[spell.Name] = final;
        return final;
    }

    // Phase 21-SC-SPELL-VFX-3p — resolve a spell name to a slottable
    // SpellTemplate. First the catalog (offensive + heal, the normal
    // gameplay spells); on miss, falls back to the debug factory that
    // synthesizes a stub from the raw template (summons, charm/buff,
    // anything else with a we_req_cast trigger row). Returns null only
    // when the name doesn't even exist as a template — that's a typo.
    private SiegeFX.Core.Assets.SpellTemplate? ResolveSlottableSpell(
        string spellName, string? debugSpellsEnv)
    {
        if (_spellCatalog is not null && _spellCatalog.TryGet(spellName, out var fromCatalog))
            return fromCatalog;
        if (_templateStore is null) return null;
        if (!_templateStore.TryGet(spellName, out var template)) return null;
        // Only accept templates that actually look like spells (under
        // /world/contentdb/templates/spells/* or named spell_*) so a typo
        // like SIEGEFX_DEBUG_SPELLS=krug_grunt doesn't silently slot a
        // monster template as the primary. Path check is the strict gate.
        if (!spellName.StartsWith("spell_", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(debugSpellsEnv))
                Console.WriteLine($"  spellbook: '{spellName}' not a spell_* template — refusing to slot");
            return null;
        }
        var synthetic = SiegeFX.Core.Assets.SpellTemplate.FromTemplateForDebug(template, _templateStore);
        if (!string.IsNullOrWhiteSpace(debugSpellsEnv))
            Console.WriteLine(
                $"  spellbook: '{spellName}' synthesized from template " +
                $"(non-offensive/heal — cast script + sound only, no damage). " +
                $"sfx='{synthetic.CastSfxScript}'");
        return synthetic;
    }

    // Phase 21-SC-SPELL-VFX-3c — true iff every `sfx create` in this spell's
    // cast script names a kind SfxRuntime.MapMode actually handles. Empty
    // create-kinds (vacuous: sound-only stubs) count as covered — 3b's
    // post-spawn visual check catches those separately. Cached per spell so
    // the static walk runs once.
    private bool IsCastScriptFullyCovered(
        SiegeFX.Core.Assets.SpellTemplate spell,
        SiegeFX.Core.Assets.SfxScript script)
    {
        if (_spellCoverageCache.TryGetValue(spell.Name, out var cached))
            return cached;
        // Phase 21-SC-SPELL-VFX-3d — recurse through `call <subscript>` so
        // fireball-class spells (top-level body is just `call fireball_base`)
        // pick up the trackball / fire creates that live in the called
        // primitive. The 3c first pass walked only the top-level program
        // and reported fireball as covered, letting the VM run and producing
        // the static-fire-at-caster bug. SfxRuntime owns the recursion now
        // so the audit CLI and the cast-site share the same logic.
        bool covered = _sfxStore is null
            ? false
            : SiegeFX.Core.Sfx.SfxRuntime.IsScriptFullyCovered(script, _sfxStore);
        _spellCoverageCache[spell.Name] = covered;
        return covered;
    }

    // First `sound play <clip>` token in the spell's cast script (or "").
    // Static helper so it can be unit-tested without standing up RenderHost.
    private string ResolveCastSoundClipName(SiegeFX.Core.Assets.SpellTemplate spell)
    {
        if (_sfxStore is null
            || string.IsNullOrEmpty(spell.CastSfxScript)
            || !_sfxStore.TryGet(spell.CastSfxScript, out var script))
            return "";
        try
        {
            var prog = SiegeFX.Core.Sfx.SfxScriptCompiler.Compile(script.Name, script.Body);
            foreach (var stmt in prog.Statements)
            {
                if (stmt.Kind != SiegeFX.Core.Sfx.StatementKind.SoundPlay) continue;
                if (stmt.Tokens.Count == 0) continue;
                var clip = stmt.Tokens[0].Trim().Trim('"');
                if (clip.EndsWith("_SED", StringComparison.OrdinalIgnoreCase))
                    clip = clip.Substring(0, clip.Length - 4);
                if (clip.Length > 0) return clip;
            }
        }
        catch (Exception ex)
        {
            // Match the rest of the file's diagnostic style — a parse throw
            // for shipped data is unexpected, surface it once. The caller's
            // cache locks the fallback in place so this fires at most once
            // per spell per launch session.
            Console.Error.WriteLine(
                $"  spellbook: cast script parse threw for '{spell.Name}' " +
                $"(script '{spell.CastSfxScript}') — {ex.Message}");
        }
        return "";
    }

    static IEnumerable<string> CollectDistinctAmbientTracks(
        IReadOnlyDictionary<string, SiegeFX.Core.Assets.MoodSetting> moods)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in moods.Values)
        {
            var t = m.AmbientTrack?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            if (seen.Add(t)) yield return t;
        }
    }

    /// <summary>Pull the bare region name (e.g. "fh_r1") and map name (e.g.
    /// "world") off a region path of the shape
    /// <c>/world/maps/map_&lt;map&gt;/regions/&lt;region&gt;</c>. Returns null
    /// when the path doesn't match — the launch path always does, but the
    /// helper guards a custom-mod scenario where regionPath is freelance.</summary>
    static string? DeriveMapName(string? regionPath)
    {
        if (string.IsNullOrEmpty(regionPath)) return null;
        var norm = regionPath.Replace('\\', '/').TrimEnd('/');
        const string token = "/maps/map_";
        int idx = norm.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int start = idx + token.Length;
        int end = norm.IndexOf('/', start);
        if (end < 0) return null;
        return norm[start..end];
    }

    static string? DeriveRegionName(string? regionPath)
    {
        if (string.IsNullOrEmpty(regionPath)) return null;
        var norm = regionPath.Replace('\\', '/').TrimEnd('/');
        const string token = "/regions/";
        int idx = norm.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return norm[(idx + token.Length)..];
    }

    /// <summary>Phase 21d-2a-xi — pick the default mood for the player's
    /// current region and swap the looping ambient bed to its track. No-op
    /// when the mood store didn't load, the audio engine is silent, or we've
    /// already applied this region. The bed selection is mood-name-driven
    /// (lowest-numbered <c>map_&lt;map&gt;_&lt;region&gt;_N</c> with a non-empty
    /// ambient_track), which matches DS1's authoring convention even though
    /// we don't yet honor mood_change() trigger actions.</summary>
    private void ApplyAmbientForRegion(string? regionPath)
    {
        if (_audio is null || _moodStore is null || _moodMapName is null) return;
        var regionName = DeriveRegionName(regionPath);
        if (regionName is null) return;
        if (string.Equals(regionName, _activeBedRegion, StringComparison.OrdinalIgnoreCase))
            return;

        var mood = SiegeFX.Core.Assets.MoodStore.FindRegionDefault(_moodStore, _moodMapName, regionName);
        _activeBedRegion = regionName;
        if (mood is null)
        {
            Console.WriteLine($"  ambient: region '{regionName}' has no matching mood — bed unchanged");
            return;
        }
        if (string.IsNullOrEmpty(mood.AmbientTrack))
        {
            // Mood found but bed is intentionally silent — clear the loop.
            // Most moods (487/520) ship with a standard_track even when the
            // ambient bed is empty (fh_r1 is the canary), so we still fall
            // through to ApplyMoodMusic below.
            _audio.SetAmbientBed(null);
            Console.WriteLine($"  ambient: region '{regionName}' → mood '{mood.Name}' (silent bed)");
        }
        else
        {
            _audio.SetAmbientBed(mood.AmbientTrack);
            Console.WriteLine($"  ambient: region '{regionName}' → mood '{mood.Name}' → '{mood.AmbientTrack}'");
        }
        // Phase 22-SC-MUSIC-C — also kick the mood's music track. DS1
        // moods author standard_track + battle_track alongside the
        // ambient bed; standard is the looping region music, battle
        // takes over during combat (SC-MUSIC-D scope). Runs even when
        // the bed is silent — fh_r1 has no bed but ships standard_track
        // = s_m_Farmhouse_01 which is the music the player should hear.
        ApplyMoodMusic(mood);
    }

    /// <summary>Phase 22-SC-MUSIC-C/D — translate the active mood into a
    /// <see cref="PlayMusicTrack"/> call, picking battle_track when
    /// combat is active and standard_track otherwise. Stores the mood
    /// on <see cref="_activeMood"/> so <see cref="TickCombatMusic"/> can
    /// switch tracks on the in-combat ↔ out-of-combat edge without
    /// re-resolving the region→mood lookup. Empty track inherits — DS1
    /// has bed-only moods that carry forward the previous mood's music.</summary>
    private void ApplyMoodMusic(SiegeFX.Core.Assets.MoodSetting mood)
    {
        // Phase 22-SC-MUSIC-FOLD — reset combat state on a real mood
        // change so a region transition while combat is active doesn't
        // leave us pinned in a stale BattleTrack from the previous mood.
        // Same-mood re-applies (no-op region-bed re-checks) keep state.
        if (!ReferenceEquals(_activeMood, mood))
        {
            _inCombat = false;
            _combatExitTimer = 0f;
        }
        _activeMood = mood;
        var track = _inCombat && !string.IsNullOrEmpty(mood.BattleTrack)
            ? mood.BattleTrack
            : mood.StandardTrack;
        if (!string.IsNullOrEmpty(track)) PlayMusicTrackBasename(track);
    }

    /// <summary>Phase 22-SC-MUSIC-D — strip the optional <c>s_m_</c>
    /// prefix from a track name and hand the basename to
    /// <see cref="PlayMusicTrack"/>. Moods author tracks like
    /// <c>s_m_Farmhouse_02</c>; PlayMusicTrack rebuilds the full path
    /// from the basename. Centralized here so combat-music swaps,
    /// region-mood applies, and any future trigger-driven changes go
    /// through one stripping path.</summary>
    private void PlayMusicTrackBasename(string trackWithMaybePrefix)
    {
        var basename = trackWithMaybePrefix;
        if (basename.StartsWith("s_m_", StringComparison.OrdinalIgnoreCase))
            basename = basename.Substring(4);
        // Defensive .mp3 suffix strip — most moods author bare names but
        // a small number ship `standard_track = "s_m_Foo.mp3"` with the
        // extension baked in. PlayMusicTrack rebuilds the path including
        // .mp3, so leaving the suffix would resolve to `…/s_m_Foo.mp3.mp3`
        // and miss.
        if (basename.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            basename = basename.Substring(0, basename.Length - 4);
        PlayMusicTrack(basename);
    }

    /// <summary>Phase 22-SC-MUSIC-D — drive battle music in response to
    /// hostile NPCs aggro-ing the player. Walk the actor list once per
    /// frame; if any non-player non-dead actor's brain is in
    /// <see cref="SiegeFX.Core.Actors.ActorBrain.BrainState.Chase"/> or
    /// <see cref="SiegeFX.Core.Actors.ActorBrain.BrainState.Attack"/>,
    /// flip into combat (immediately swap to the active mood's
    /// battle_track if it has one). Out of combat is gated by
    /// <see cref="CombatExitDelay"/> so single-skirmish encounters
    /// don't flicker the music. Boss music (DS1 ships dedicated boss
    /// tracks like s_m_boss_01) is a future polish item — needs a
    /// per-template "is_boss" flag we don't extract yet.</summary>
    private void TickCombatMusic(float dt)
    {
        if (_player is null || _activeMood is null) return;
        bool nowInCombat = false;
        for (int i = 0; i < _actors.Count; i++)
        {
            var s = _actors[i];
            if (s.IsPlayer || s.IsDead) continue;
            // Phase 22-SC-MUSIC-FOLD — defensive combatant gate. The brain
            // tick at ActorBrain.Tick only feeds the player-target into
            // combatants today, so non-combatants stay in Wander and the
            // gate is a no-op in practice. But that coupling lives across
            // two files; if a future phase wires NPC-vs-NPC aggro, every
            // chicken/peasant brain that enters Chase against another NPC
            // would silently flip the player's music mix without this
            // filter. Match the IsCombatant check the existing combat
            // pipeline uses (e.g. PerformPlayerSwing's actor scan).
            if (!s.Actor.Stats.IsCombatant) continue;
            var brain = s.Brain;
            if (brain is null) continue;
            if (brain.State == SiegeFX.Core.Actors.ActorBrain.BrainState.Chase
             || brain.State == SiegeFX.Core.Actors.ActorBrain.BrainState.Attack)
            { nowInCombat = true; break; }
        }
        if (nowInCombat)
        {
            _combatExitTimer = 0f;
            if (!_inCombat)
            {
                _inCombat = true;
                if (!string.IsNullOrEmpty(_activeMood.BattleTrack))
                    PlayMusicTrackBasename(_activeMood.BattleTrack);
            }
            return;
        }
        if (!_inCombat) return;
        _combatExitTimer += dt;
        if (_combatExitTimer >= CombatExitDelay)
        {
            _inCombat = false;
            _combatExitTimer = 0f;
            if (!string.IsNullOrEmpty(_activeMood.StandardTrack))
                PlayMusicTrackBasename(_activeMood.StandardTrack);
        }
    }

    /// <summary>Phase 17-SC-H-DBG — step the Primary slot through every
    /// non-self-heal entry in <see cref="SpellCatalog"/>. Self-heals are
    /// filtered out because Primary's flow expects a click target; heals
    /// stay in Secondary on 'W'. Used to verify SC-H's per-element sfx
    /// scripts (zap, fireball, freeze, charm, …) without an inventory UI.
    /// Logs the new slot so the user can correlate cast → script.</summary>
    private void CyclePrimarySpell(bool forward)
    {
        if (_playerSpellbook is null || _spellCatalog is null) return;
        var pool = new List<SiegeFX.Core.Assets.SpellTemplate>();
        foreach (var s in _spellCatalog.All)
            if (s.Kind != SiegeFX.Core.Assets.SpellKind.SelfHeal) pool.Add(s);
        if (pool.Count == 0) return;

        // First cycle press: snap the index to whatever's currently slotted
        // so the user steps relative to the live Primary, not from zero.
        if (_spellCycleIdx < 0 && _playerSpellbook.Primary is { } cur)
        {
            for (int i = 0; i < pool.Count; i++)
                if (string.Equals(pool[i].Name, cur.Name, StringComparison.OrdinalIgnoreCase))
                { _spellCycleIdx = i; break; }
        }
        _spellCycleIdx = ((_spellCycleIdx < 0 ? 0 : _spellCycleIdx) + (forward ? 1 : -1) + pool.Count) % pool.Count;
        var next = pool[_spellCycleIdx];
        _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Primary, next);
        Console.WriteLine($"  spell-cycle: primary <- {next.Name} (\"{next.ScreenName}\") " +
                          $"kind={next.Kind} range={next.CastRange:F1} cd={next.CastReloadDelay:F2}s " +
                          $"[{_spellCycleIdx + 1}/{pool.Count}]");
    }

    /// <summary>Phase 17a — cast the slotted spell at whatever the cursor's
    /// currently picking. Mirrors <see cref="TryClickToAttack"/>'s unproject-
    /// then-pick-nearest-combatant logic so casting and clicking share one
    /// targeting model. The spellbook owns range/mana/cooldown gating; this
    /// method just routes the result into the floating-text + XP pipelines.</summary>
    private void TryClickToCast(SiegeFX.Core.Actors.SpellSlot slot)
    {
        if (_player is null || _window is null || _input is null) return;
        if (_playerSpellbook is null) return;
        var spell = slot == SiegeFX.Core.Actors.SpellSlot.Primary
            ? _playerSpellbook.Primary : _playerSpellbook.Secondary;
        if (spell is null) return;
        if (_input.Mice.Count == 0) return;

        var playerPos = _player.CurrentTransform.Translation;
        // Phase 16d ships one combined skill pool, so the player's progression
        // level is the only "magic level" we have to feed into the spell formula.
        // 17b+ replaces this with the dedicated CombatMagic / NatureMagic level.
        float magicLevel = _progression?.Level ?? 1;

        ActorRenderState? best = null;
        // Phase 17c — self-heal spells skip target picking entirely. The
        // spellbook ignores the target arg for SpellKind.SelfHeal, but we
        // also skip the unproject math so the cast still fires when the
        // cursor is hovering empty terrain.
        if (spell.Kind == SiegeFX.Core.Assets.SpellKind.OffensiveInstantHit)
        {
            if (_actors.Count == 0) return;
            var cursor = _input.Mice[0].Position;
            var size = _window.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0) return;

            float ndcX = (cursor.X / size.X) * 2f - 1f;
            float ndcY = 1f - (cursor.Y / size.Y) * 2f;
            float aspect = (float)size.X / size.Y;
            if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return;

            var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
            var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
            if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return;
            var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
            var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
            var dir  = far_ - near;
            if (dir.LengthSquared() < 1e-8f || MathF.Abs(dir.Y) < 1e-4f) return;

            float planeY = _player.CurrentTransform.Translation.Y;
            float t = (planeY - near.Y) / dir.Y;
            if (t < 0f) return;
            var groundHit = near + dir * t;

            float bestDist = ClickAttackRadius;
            foreach (var s in _actors)
            {
                if (s.IsDead) continue;
                if (s.IsPlayer) continue;
                if (!s.Actor.Stats.IsCombatant) continue;
                var pos = s.CurrentTransform.Translation;
                float dx = pos.X - groundHit.X;
                float dz = pos.Z - groundHit.Z;
                float d  = MathF.Sqrt(dx * dx + dz * dz);
                if (d < bestDist) { bestDist = d; best = s; }
            }

            // Phase 21-SC-BARREL-B — no actor under cursor? Try a breakable
            // static prop. The cursor sprite already previews this state
            // (CursorState.Smash), so casting in that frame should land on
            // the barrel/crate the user was aiming at.
            if (best is null)
            {
                StaticPropInstance? bestProp = null;
                float bestPropDist = ClickAttackRadius;
                foreach (var prop in _staticProps)
                {
                    if (!prop.IsBreakable || prop.IsDestroyed) continue;
                    var pos = prop.World.Translation;
                    float dx = pos.X - groundHit.X;
                    float dz = pos.Z - groundHit.Z;
                    float d  = MathF.Sqrt(dx * dx + dz * dz);
                    if (d < bestPropDist) { bestPropDist = d; bestProp = prop; }
                }
                if (bestProp is not null)
                {
                    PerformSpellOnProp(slot, spell, bestProp, magicLevel);
                    return;
                }
            }
        }

        SiegeFX.Core.Actors.CastResult result;
        if (spell.Kind == SiegeFX.Core.Assets.SpellKind.SelfHeal)
        {
            result = _playerSpellbook.TryCast(slot, _player.Actor, 0f, magicLevel);
        }
        else if (best is null)
        {
            result = new SiegeFX.Core.Actors.CastResult(
                SiegeFX.Core.Actors.CastOutcome.NoTarget, spell, 0, 0, 0, false);
        }
        else
        {
            float dx = best.CurrentTransform.Translation.X - playerPos.X;
            float dz = best.CurrentTransform.Translation.Z - playerPos.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            result = _playerSpellbook.TryCast(slot, best.Actor, dist, magicLevel);
        }

        // Cast feedback. Console line is the canonical record (matches the
        // melee log shape); the floating text gives the player the at-a-glance
        // "did it land" cue — anchored in world space at the target so big
        // sprawl battles don't blur the hit count.
        var anchor = best?.CurrentTransform.Translation ?? (playerPos + new Vector3(0f, 1.5f, 0f));
        switch (result.Outcome)
        {
            case SiegeFX.Core.Actors.CastOutcome.Cast:
                // Phase 12-SC-2 — play chore_magic on a successful cast so the
                // PC actually performs the spell motion. 144/179 combatant
                // templates ship one (Phase 10-SC-2 receipt); falls back
                // silently if not. Duration matches the melee cadence so the
                // clip plays through once and reverts.
                _player.Actor.PlayChoreOnce("chore_magic", 0.7f);
                if (spell!.Kind == SiegeFX.Core.Assets.SpellKind.SelfHeal)
                {
                    // Phase 17c — self-heal cast: no target, no bolt, green
                    // restore popup over the player. Heal is applied inside
                    // the spellbook so the HP bar is already updated by the
                    // time this branch runs.
                    Console.WriteLine(
                        $"cast {spell.ScreenName}: heal +{result.HealAmount:F0} " +
                        $"(mana -{result.ManaSpent:F1})");
                    AddFloatingText($"+{(int)MathF.Round(result.HealAmount)} HP",
                                    playerPos + new Vector3(0f, 2.2f, 0f),
                                    new Vector4(0.40f, 0.95f, 0.40f, 1f));
                    // Phase 21-SC-SPELL-VFX-3a — per-spell cast SFX. Same
                    // resolver as the offensive branch above, so swapping
                    // the secondary slot to e.g. spell_nurture (a different
                    // sound-only heal) plays its authored cast cue, not
                    // healing_wind's. Fallback chain inside ResolveSpellCastSound
                    // lands on SfxZapCast on a miss; if the secondary is
                    // healing_wind specifically we re-fall to SfxHealingWindCast
                    // since it's pre-registered and matches the pre-3a
                    // experience for that one spell.
                    var healClip = ResolveSpellCastSound(spell);
                    if (healClip == SfxZapCast && spell.Name.Equals("spell_healing_wind",
                            StringComparison.OrdinalIgnoreCase))
                        healClip = SfxHealingWindCast;
                    _audio?.Play(healClip);
                    // Heals award NatureMagic XP proportional to HP restored,
                    // matching DS1's "cast_experience grows with effect" rule.
                    // Without this the heal slot is progression-dead.
                    AwardCombatXp((long)result.HealAmount, 0,
                                  SiegeFX.Core.Assets.SkillKind.NatureMagic);
                }
                else
                {
                    Console.WriteLine(
                        $"cast {spell.ScreenName}: hit {best!.Actor.Template.Name} for {result.Damage:F0} " +
                        $"(mana -{result.ManaSpent:F1}){(result.TargetKilled ? "  *** DEAD ***" : "")}");
                    // Phase 17b — snap PC facing toward the target so the cast
                    // at least visually originates *toward* the victim.
                    // _playerFacing normally only updates from movement deltas;
                    // without this, a player who casts while standing still
                    // would shoot bolts out of his back.
                    var tp = best.CurrentTransform.Translation;
                    float fx = tp.X - playerPos.X;
                    float fz = tp.Z - playerPos.Z;
                    float fl2 = fx * fx + fz * fz;
                    if (fl2 > 1e-6f)
                    {
                        float fl = MathF.Sqrt(fl2);
                        _playerFacing = new Vector3(fx / fl, 0f, fz / fl);
                        float pyaw = MathF.Atan2(_playerFacing.X, _playerFacing.Z);
                        _player.CurrentTransform =
                            Matrix4x4.CreateRotationY(pyaw) *
                            Matrix4x4.CreateTranslation(playerPos);
                        // 9-SC-10b — snap the render-interp buffers too, otherwise
                        // the next frame's pass would lerp back toward the old
                        // walking-facing and the bolt would launch from the side.
                        _playerRenderFacingPrev = _playerFacing;
                        _playerRenderFacingNext = _playerFacing;
                    }
                    // Phase 21-SC-SPELL-VFX — caster's hand-area position. We
                    // don't have @weapon_bone resolution yet (DS1's sfx scripts
                    // anchor lightning to bip01_l_hand / @weapon_bone source),
                    // so approximate: shoulder-height + a half-step forward
                    // along facing. Reads as "from the hand" to a casual eye.
                    var src = playerPos + _playerFacing * 0.45f + new Vector3(0f, 1.25f, 0f);
                    var dst = tp        + new Vector3(0f, 1.0f, 0f);
                    var elemColor = SpellElementColor(spell.Element);
                    // Phase 17-SC-H — invoke the spell's sfx_script through the
                    // VM (still partial: lightning/fire emitter verbs only).
                    // Even when the script runs, we ALSO spawn an element-aware
                    // 3D primitive below so every spell has a clear visible
                    // beam-or-projectile. Phase 21-SC-SPELL-VFX dropped the
                    // gating on spellSfxFired — the prior gate left ~all 69
                    // OffensiveInstantHit spells with no visible cast effect
                    // because the VM's lightning shorthand spawned a 1-unit
                    // pale-blue stub at the target that washed out at gameplay
                    // distance.
                    // Phase 21-SC-SPELL-VFX-2 — feed the VM a real SfxContext
                    // (caster + target + weapon-bone-area approximation) so
                    // shipped DS1 scripts (zap, fireball) render natively
                    // through `sfx target source` / `sfx attach_point
                    // @weapon_bone source`. SpawnSpellVisual stays as the
                    // fallback for spells whose CastSfxScript isn't in the
                    // store.
                    bool ranNativeScript = false;
                    bool nativeProducedVisual = false;
                    bool scriptFullyCovered = false;
                    string sfxTrace;
                    if (_sfxRuntime is not null && _sfxStore is not null
                        && !string.IsNullOrEmpty(spell.CastSfxScript)
                        && _sfxStore.TryGet(spell.CastSfxScript, out var castScript))
                    {
                        // Phase 21-SC-SPELL-VFX-3c — pre-flight static check.
                        // If the script asks for any unmodeled `sfx create`
                        // kind (orbiter, trackball, cylinder, lightsource,
                        // flurry, fireb, sray, curve, …) then running the VM
                        // anyway leaves stranded emitters bound to handles
                        // that were never created. fireball is the canonical
                        // case: it `sfx create trackball ...; set $trackball
                        // #POP; sfx target $fire $trackball;` — the trackball
                        // never resolves so the fire emitter falls back to
                        // its #SOURCE anchor and burns at the caster forever.
                        // Cached per spell so the static walk runs once.
                        scriptFullyCovered = IsCastScriptFullyCovered(spell, castScript);
                        if (scriptFullyCovered)
                        {
                            var ctx = new SiegeFX.Core.Sfx.SfxContext(
                                SourcePos:     playerPos + new Vector3(0f, 1.0f, 0f),
                                TargetPos:     dst,
                                WeaponBonePos: src,
                                Resolver:      ResolvePlayerBone);
                            int boltsBefore       = _particles?.LiveBoltCount ?? 0;
                            int particlesBefore   = _particles?.LiveParticleCount ?? 0;
                            int persistentBefore  = _sfxRuntime.LivePersistentCount;
                            ranNativeScript = _sfxRuntime.Spawn(spell.CastSfxScript, ctx);
                            int boltsAfter        = _particles?.LiveBoltCount ?? 0;
                            int particlesAfter    = _particles?.LiveParticleCount ?? 0;
                            int persistentAfter   = _sfxRuntime.LivePersistentCount;
                            // Phase 21-SC-SPELL-VFX-3b — Spawn returning true
                            // only means the script ran without a parse error;
                            // the 5 UNCOVERED sound-only stubs DS1 ships
                            // (iceblast / iceshard / icefury / lightning_storm /
                            // explosive_powder — see iceblast_launch.gas's
                            // `// only a sound now, should hook up an effect.
                            // -ET` TODO) get past 3c's fully-covered gate
                            // because they have no `sfx create` at all (vacuously
                            // covered), then run "successfully" and produce no
                            // visual. Gate the skip-fallback on whether the run
                            // actually grew bolts, particles, or persistent
                            // emitters — otherwise fall through to placeholder.
                            nativeProducedVisual = (boltsAfter > boltsBefore)
                                                 || (particlesAfter > particlesBefore)
                                                 || (persistentAfter > persistentBefore);
                            sfxTrace = ranNativeScript
                                ? $"native '{spell.CastSfxScript}' src=({src.X:F1},{src.Y:F1},{src.Z:F1}) dst=({dst.X:F1},{dst.Y:F1},{dst.Z:F1}) bolts={boltsBefore}->{boltsAfter} parts={particlesBefore}->{particlesAfter} persist={persistentBefore}->{persistentAfter} visual={nativeProducedVisual}"
                                : $"native '{spell.CastSfxScript}' Spawn returned false";
                        }
                        else
                        {
                            // Script uses an unmodeled `sfx create` kind. Skip
                            // the VM entirely; the placeholder visual below
                            // gives a clean element-tinted projectile + impact
                            // instead of stranded fire-at-caster. As primitives
                            // ship in future SC slices (orbiter -> 20 spells,
                            // trackball -> 18, cylinder -> 12, …) those spells
                            // automatically promote to native here.
                            sfxTrace = $"placeholder '{spell.CastSfxScript}' uses unmodeled creates — VM skipped";
                        }
                    }
                    else
                    {
                        sfxTrace = $"fallback (rt={_sfxRuntime is not null} st={_sfxStore is not null} script='{spell.CastSfxScript}')";
                    }
                    if (!ranNativeScript || !nativeProducedVisual)
                        SpawnSpellVisual(src, dst, spell.Element, elemColor);
                    Console.WriteLine($"  cast vfx: {sfxTrace}");
                    // Phase 21-SC-SPELL-VFX-3a — per-spell cast SFX from the
                    // sfx_script's first `sound play` statement. Replaces the
                    // 17-era hardcoded SfxZapCast that played zap on every
                    // cast. Resolution + registration happens on the first
                    // cast of each spell; subsequent casts hit the cache.
                    // Falls back to SfxZapCast when the wav is missing or
                    // the script has no sound (so we don't go silent).
                    _audio?.Play(ResolveSpellCastSound(spell));
                    AddFloatingText($"{spell.ScreenName.ToUpperInvariant()} -{(int)MathF.Round(result.Damage)}",
                                    anchor + new Vector3(0f, 1.8f, 0f),
                                    elemColor);
                    AwardCombatXp((long)result.Damage,
                                  result.TargetKilled ? best.Actor.Stats.ExperienceValue : 0,
                                  SiegeFX.Core.Assets.SkillKind.CombatMagic);
                    if (result.TargetKilled && best.Actor.Combat.ConsumeJustDied())
                    {
                        best.IsDead = true;
                        best.Brain = null;
                        // Phase 12-SC-4 — chore_die on spell-kill (mirrors the
                        // melee-kill site).
                        BeginDeathChore(best);
                        // Phase 9-SC-2 — death scream from template's voice block.
                        PlayDeathSfx(best.Actor.Template, best.CurrentTransform.Translation);
                        LogLootDrop(best.Actor, best.CurrentTransform.Translation);
                        OnActorKilled(best.Actor.Template.Name, best.CurrentTransform.Translation);
                        CreditGoldFromKill(best.Actor.Stats.ExperienceValue, best.CurrentTransform.Translation);
                    }
                }
                break;
            case SiegeFX.Core.Actors.CastOutcome.NoTarget:
                AddFloatingText("no target", playerPos + new Vector3(0f, 2.1f, 0f), new Vector4(0.85f, 0.85f, 0.85f, 1f));
                break;
            case SiegeFX.Core.Actors.CastOutcome.OutOfRange:
                AddFloatingText("out of range", anchor + new Vector3(0f, 1.8f, 0f), new Vector4(0.95f, 0.65f, 0.20f, 1f));
                break;
            case SiegeFX.Core.Actors.CastOutcome.NoMana:
                AddFloatingText("no mana", playerPos + new Vector3(0f, 2.1f, 0f), new Vector4(0.45f, 0.65f, 1.00f, 1f));
                _audio?.Play(SfxGuiOutOfMana);
                break;
            case SiegeFX.Core.Actors.CastOutcome.OnCooldown:
                // Cooldown is short (zap = 0.15s) — silent miss feels right rather
                // than spamming feedback for the player just leaning on the key.
                break;
            case SiegeFX.Core.Actors.CastOutcome.TargetDead:
                AddFloatingText("already dead", anchor + new Vector3(0f, 1.8f, 0f), new Vector4(0.7f, 0.7f, 0.7f, 1f));
                break;
            case SiegeFX.Core.Actors.CastOutcome.AlreadyFull:
                AddFloatingText("at full health", playerPos + new Vector3(0f, 2.1f, 0f),
                                new Vector4(0.40f, 0.95f, 0.40f, 1f));
                break;
        }
    }

    /// <summary>Push a world-anchored short-lived label onto the floating-text
    /// list. Drawn in the HUD pass each frame, projected from world to screen
    /// so it tracks the actor as the camera moves and fades over its lifetime.</summary>
    private void AddFloatingText(string text, Vector3 worldPos, Vector4 color)
    {
        _floatingTexts.Add(new FloatingText
        {
            Text = text,
            WorldPos = worldPos,
            Color = color,
            Remaining = FloatingTextDuration,
            Total = FloatingTextDuration,
        });
    }

    /// <summary>Phase 16d — fold a damage roll + kill bonus into the player's
    /// progression. Pulled out so the click-attack and F-key debug paths share
    /// identical XP awarding (and the eventual ranged/spell attacks just call
    /// in with their own <see cref="SiegeFX.Core.Assets.SkillKind"/>).</summary>
    private void AwardCombatXp(long damageXp, int killBonus, SiegeFX.Core.Assets.SkillKind skill)
    {
        if (_progression is null) return;
        long total = damageXp + killBonus;
        if (total <= 0) return;
        int oldLevel = _progression.Level;
        _progression.AwardXp(total, skill);
        if (_progression.Level > oldLevel)
        {
            // Level-up announce: console line for diagnostics + on-screen banner
            // so the user actually notices. The toast fades after 3 seconds.
            var s = _player!.Actor.Stats;
            Console.WriteLine(
                $"  *** LEVEL UP! L{oldLevel}->L{_progression.Level} " +
                $"str={s.Strength:F2} dex={s.Dexterity:F2} int={s.Intelligence:F2} " +
                $"life={s.MaxLife:F0} mana={s.MaxMana:F0} ***");
            _levelUpToastRemaining = LevelUpToastDuration;
            _levelUpToastLevel = _progression.Level;
            // Phase 18b — punctuate the toast with the matching DS1 jingle.
            // Single shared melee-flavored cue for now; per-skill cues
            // (magic_nature/magic_dark/ranged) can land in 18c next to the
            // skill-flavored XP bar.
            _audio?.Play(SfxLevelUp);
        }
    }

    // Phase 12d/12e — roll the dying actor's loot table, log the outcome, and place
    // a visible pile at the actor's last known position. RNG is seeded from scid so
    // a given instance's drop is stable across re-kills; the visible pile is added
    // whenever the roll produced at least one entry (which for combatants with an
    // equipped weapon is every kill — the weapon bucket has no chance gate).
    private void LogLootDrop(SiegeFX.Core.Actors.Actor actor, Vector3 deathPos)
    {
        if (_templateStore is null) return;
        var table = SiegeFX.Core.Actors.LootTable.FromTemplate(_templateStore, actor.Template);
        if (table.IsEmpty)
        {
            Console.WriteLine($"  loot: {actor.Template.Name} has no inventory.pcontent");
            return;
        }
        var rng = new Random((int)actor.Instance.Scid);
        var drops = SiegeFX.Core.Actors.LootRoller.Roll(table, rng);
        if (drops.Count == 0)
        {
            Console.WriteLine($"  loot: {actor.Template.Name} dropped nothing this kill");
            return;
        }
        // Phase 21-SC-BARREL-FOLD — split gold from items so [gold*] entries
        // (krug.gas / heroes.gas top-level + the regional barrel templates)
        // credit gold + show "+N gold" instead of landing in inventory as a
        // ghost "12-15" template. Pre-fold this logic only existed on the
        // prop-shatter path; without it actor-death gold buckets would
        // resolve into inventory once FromTemplate started accepting them.
        var (items, goldTotal) = SplitGoldFromDrops(drops, rng);
        var parts = new List<string>(drops.Count);
        if (goldTotal > 0) parts.Add($"{goldTotal} gold");
        foreach (var d in items)
            parts.Add(d.IsEquipped ? $"[{d.Slot}] {d.Reference}" : d.Reference);
        Console.WriteLine($"  loot: {actor.Template.Name} dropped {string.Join(", ", parts)}");
        if (goldTotal > 0)
        {
            _progression?.CreditGold(goldTotal);
            AddFloatingText($"+{goldTotal} gold", deathPos + new Vector3(0f, 1.6f, 0f),
                            new Vector4(1.00f, 0.92f, 0.40f, 1f));
        }
        if (items.Count == 0) return;
        // Phase 9-SC-9 — enemy drops get the same toss arc as PC drops so
        // the kill→loot moment reads as "items flew off the body" instead of
        // "cube appeared." Random horizontal angle keeps repeated kills from
        // stacking piles in the exact same spot, and the spin is half what
        // a player toss does (a body drop, not a deliberate pitch).
        var dropDir = new Vector2(
            (float)rng.NextDouble() * 2f - 1f,
            (float)rng.NextDouble() * 2f - 1f);
        if (dropDir.LengthSquared() < 1e-4f) dropDir = new Vector2(0f, 1f);
        else dropDir = Vector2.Normalize(dropDir);
        // Phase 21-SC-SCROLL-CLICKLOOT — short throw, item lands near
        // the body. DS1 enemies drop loot in a tight scatter, not flung
        // 1.4u out. Was 1.4f.
        var dropTarget = deathPos + new Vector3(dropDir.X * 0.6f, 0f, dropDir.Y * 0.6f);
        var deathPile = new LootPile(deathPos, items)
        {
            Throw = new LootThrow
            {
                Source        = deathPos,
                Target        = dropTarget,
                Duration      = 0.55f,
                Elapsed       = 0f,
                ArcHeight     = 0.45f,
                Spins         = 0.5f,
                StartRotation = (float)rng.NextDouble() * MathF.PI * 2f,
            }
        };
        // Phase 9-SC-10 — first resolvable item drives the pile's rest pitch
        // (shields lie flat on the ground, weapons stay upright).
        foreach (var d in items)
        {
            var pitch = ComputeLootRestPitch(d.Reference);
            if (pitch != 0f) { deathPile.RestPitch = pitch; break; }
        }
        _lootPiles.Add(deathPile);
    }

    /// <summary>Phase 21-SC-BARREL-FOLD — extracted helper. Walks the drop
    /// list, sums gold (resolving each entry's min-max range against the
    /// supplied RNG), and returns the non-gold items as a fresh list. Both
    /// LogLootDrop (actor death) and LogPropLootDrop (prop shatter) need
    /// this split or the synthetic gold entries land in inventory.</summary>
    private static (List<SiegeFX.Core.Actors.LootEntry> Items, long GoldTotal)
        SplitGoldFromDrops(IReadOnlyList<SiegeFX.Core.Actors.LootEntry> drops, Random rng)
    {
        var items = new List<SiegeFX.Core.Actors.LootEntry>(drops.Count);
        long goldTotal = 0;
        foreach (var d in drops)
        {
            if (d.IsGold)
            {
                var (lo, hi) = d.GoldRange();
                goldTotal += hi > lo ? rng.Next(lo, hi + 1) : Math.Max(0, lo);
            }
            else
            {
                items.Add(d);
            }
        }
        return (items, goldTotal);
    }

    /// <summary>Phase 21-SC-BARREL-D — roll a shattered breakable's
    /// inventory.pcontent. Mirrors <see cref="LogLootDrop"/> but for
    /// static props: gold entries credit the player directly with a
    /// floating "+N gold" cue (no pickup needed), item entries form a
    /// short-throw LootPile next to the prop. RNG is seeded off the
    /// world position so a given barrel rolls the same drop across
    /// reloads of the session — matches the actor-death stable-RNG rule
    /// (<see cref="LogLootDrop"/> uses scid; props don't carry one, so
    /// position is the next-cleanest fingerprint). Empty pcontent is
    /// the dominant case for most barrel templates — DS1's distribution
    /// is ~60-70% empty, ~20-25% gold, ~5-10% potion, ~2-5% gear.</summary>
    private void LogPropLootDrop(StaticPropInstance prop)
    {
        if (_templateStore is null) return;
        if (!_templateStore.TryGet(prop.Template, out var template)) return;
        var table = SiegeFX.Core.Actors.LootTable.FromTemplate(_templateStore, template);
        if (table.IsEmpty) return;

        var origin = prop.World.Translation;
        // Phase 21-SC-BARREL-FOLD — same seed widening as SpawnPropDebris:
        // include Y + template name so stacked barrels roll different loot.
        int seed = unchecked(
            BitConverter.SingleToInt32Bits(origin.X) * 73856093 ^
            BitConverter.SingleToInt32Bits(origin.Y) * 83492791 ^
            BitConverter.SingleToInt32Bits(origin.Z) * 19349663 ^
            (prop.Template?.GetHashCode() ?? 0));
        var rng = new Random(seed);
        var drops = SiegeFX.Core.Actors.LootRoller.Roll(table, rng);
        if (drops.Count == 0)
        {
            Console.WriteLine($"  loot: {prop.Template} dropped nothing this shatter");
            return;
        }

        var (items, goldTotal) = SplitGoldFromDrops(drops, rng);
        var parts = new List<string>(drops.Count);
        if (goldTotal > 0) parts.Add($"{goldTotal} gold");
        foreach (var d in items)
            parts.Add(d.IsEquipped ? $"[{d.Slot}] {d.Reference}" : d.Reference);
        Console.WriteLine($"  loot: {prop.Template} dropped {string.Join(", ", parts)}");

        if (goldTotal > 0)
        {
            _progression?.CreditGold(goldTotal);
            AddFloatingText($"+{goldTotal} gold", origin + new Vector3(0f, 1.4f, 0f),
                            new Vector4(1.00f, 0.92f, 0.40f, 1f));
        }
        if (items.Count == 0) return;

        // Short tumble out from the prop center — same throw shape as
        // actor-death drops (Phase 9-SC-9), reusing the existing pile
        // animation so frags + items both visibly fly out of the wreckage.
        var dropDir = new Vector2(
            (float)rng.NextDouble() * 2f - 1f,
            (float)rng.NextDouble() * 2f - 1f);
        if (dropDir.LengthSquared() < 1e-4f) dropDir = new Vector2(0f, 1f);
        else dropDir = Vector2.Normalize(dropDir);
        var dropTarget = origin + new Vector3(dropDir.X * 0.7f, 0f, dropDir.Y * 0.7f);
        var pile = new LootPile(origin, items)
        {
            Throw = new LootThrow
            {
                Source        = origin,
                Target        = dropTarget,
                Duration      = 0.6f,
                Elapsed       = 0f,
                ArcHeight     = 0.55f,
                Spins         = 0.5f,
                StartRotation = (float)rng.NextDouble() * MathF.PI * 2f,
            }
        };
        foreach (var d in items)
        {
            var pitch = ComputeLootRestPitch(d.Reference);
            if (pitch != 0f) { pile.RestPitch = pitch; break; }
        }
        _lootPiles.Add(pile);
    }

    // Phase 14c — a dropped "weapon_hand" entry is an upgrade iff its template has
    // a non-zero damage_max AND that max beats what the PC currently wields. DS1
    // drops non-weapon equipment (boots, capes) into other es_ slots; we auto-equip
    // those unconditionally on pickup and only gate the weapon swap by damage.
    private bool IsWeaponUpgrade(SiegeFX.Core.Actors.LootEntry entry)
    {
        if (!string.Equals(entry.Slot, "weapon_hand", StringComparison.OrdinalIgnoreCase))
            return true; // non-weapon slot -> always accept

        if (_templateStore is null) return false;
        if (!_templateStore.TryGet(entry.Reference, out var tpl)) return false;
        var dmaxStr = _templateStore.GetAttribute(tpl!, "attack", "damage_max");
        if (!float.TryParse(dmaxStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var newMax)) return false;
        if (newMax <= 0f) return false;

        var currentMax = GetPlayerAttackStats().DamageMax;
        return newMax > currentMax;
    }

    // Phase 14c — resolve the PC's effective attacker stats. If es_weapon_hand is
    // equipped and the referenced template has an [attack] block with damage_min/max,
    // use those; otherwise fall back to HeroBaselineStats. The rest of the attacker
    // stats (defense, attack range, walk speed, etc.) stay on the hero baseline —
    // we only override the two damage numbers the weapon dictates.
    // Phase 21-SC-INV-A — sum [armor]{defense} across every equipped armor slot
    // for the character pane's ARMOR RATING readout. Slots that don't carry an
    // armor block (weapons, spell books) are skipped so the running total
    // stays an honest sum even when the equipment dict mixes types.
    private int ComputePlayerArmorRating()
    {
        if (_templateStore is null || _playerEquipment.Count == 0) return 0;
        float total = 0f;
        foreach (var kv in _playerEquipment)
        {
            if (!_templateStore.TryGet(kv.Value, out var tpl) || tpl is null) continue;
            var defStr = _templateStore.GetAttribute(tpl, "armor", "defense");
            if (string.IsNullOrEmpty(defStr)) continue;
            if (float.TryParse(defStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0f)
                total += d;
        }
        return (int)MathF.Round(total);
    }

    private SiegeFX.Core.Actors.ActorStats GetPlayerAttackStats()
    {
        if (_templateStore is null) return HeroBaselineStats;
        if (!_playerEquipment.TryGetValue("es_weapon_hand", out var weaponRef))
            return HeroBaselineStats;
        if (!_templateStore.TryGet(weaponRef, out var weaponTpl)) return HeroBaselineStats;

        var dminStr = _templateStore.GetAttribute(weaponTpl!, "attack", "damage_min");
        var dmaxStr = _templateStore.GetAttribute(weaponTpl!, "attack", "damage_max");
        if (!float.TryParse(dminStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dmin)) return HeroBaselineStats;
        if (!float.TryParse(dmaxStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dmax)) return HeroBaselineStats;
        if (dmax <= 0f) return HeroBaselineStats;

        return HeroBaselineStats with { DamageMin = dmin, DamageMax = dmax };
    }

    // Phase 14a — PC auto-pickup. Called each logic tick after the player follower
    // advances. Any pile within PickupRadius (XZ) of the PC is emptied into the
    // inventory list; the pile is removed so its cube despawns. Iterating backwards
    // so a mid-loop RemoveAt doesn't skip the next pile.
    /// <summary>Phase 21-SC-SCROLL-CLICKLOOT — empty a single LootPile into
    /// the player's inventory + spellbook + equipment slots. Mid-throw
    /// piles are rejected (the toss arc is animation-only; click-to-loot
    /// only succeeds once the pile has settled). Returns true on success.
    ///
    /// <para>Callers used to walk every pile within PickupRadius and run
    /// this body inline (see the deleted TryAutoPickup loop). DS1 doesn't
    /// auto-pickup — items on the ground require an explicit click. Click
    /// path now invokes this helper for the chosen pile only.</para></summary>
    private bool LootPileNow(LootPile pile, int pileIndex)
    {
        if (pile.Throw is not null) return false;

        // Phase 21-SC-SCROLL-F-2 — handle scroll items first so they
        // route to spellbook Placed[] instead of the flat inventory.
        // Scroll items are removed from the pile before the inventory
        // pass below so they don't double-stuff. Spellbook-full
        // scrolls fall back to inventory inside the helper.
        TryAutoPickupScrollsFromPile(pile);

        // Phase 9-SC-13 — resolve pcontent specs (#club/2-3) to a concrete
        // template name at pickup, so the inventory grid sees the same name
        // the pile rendered on the ground. The shared spec cache means the
        // mesh, icon, and stored ref all agree.
        var parts = new List<string>(pile.Items.Count);
        foreach (var it in pile.Items)
        {
            var resolved = ResolveItemRef(it.Reference);
            var entry = resolved == it.Reference ? it : it with { Reference = resolved };
            _playerInventory.Add(entry);
            _inventoryPanel.NotifyItemAdded();
            parts.Add(entry.IsEquipped ? $"[{entry.Slot}] {entry.Reference}" : entry.Reference);
        }
        if (parts.Count > 0)
            Console.WriteLine(
                $"  pickup: acquired {string.Join(", ", parts)}  (inventory: {_playerInventory.Count})");
        _audio?.PlayAt(SfxGuiPickup, pile.Position);

        // Phase 14c — auto-equip dropped weapons. If the loot entry came from
        // an equipped slot on the dead actor (Slot=weapon_hand/shield_hand/etc)
        // and the new item has a non-zero damage_max, swap it into the PC's
        // matching es_ slot. Keeps the kill -> loot -> stronger-hit loop
        // visible without a real inventory UI.
        bool weaponSwapped = false;
        bool nonWeaponSwapped = false;
        foreach (var it in pile.Items)
        {
            if (!it.IsEquipped) continue;
            if (!IsWeaponUpgrade(it)) continue;
            var slotKey = "es_" + it.Slot;
            var resolvedRef = ResolveItemRef(it.Reference);
            _playerEquipment[slotKey] = resolvedRef;
            Console.WriteLine($"  equipped: [{slotKey}] <- {resolvedRef}");
            if (string.Equals(slotKey, "es_weapon_hand", StringComparison.OrdinalIgnoreCase))
                weaponSwapped = true;
            else
                nonWeaponSwapped = true;
        }
        if (weaponSwapped) TryLoadPlayerWeapon();
        if (nonWeaponSwapped && _player is not null)
            TryLoadPlayerEquipment(_player.Actor.Template);
        if (weaponSwapped || nonWeaponSwapped) RefreshPlayerStance();

        _lootPiles.RemoveAt(pileIndex);
        return true;
    }

    /// <summary>Phase 21-SC-SCROLL-CLICKLOOT — find the closest settled
    /// loot pile to a world-space click point and loot it. Tolerance is
    /// 1.0u so the user can click roughly on or near the pile rather than
    /// pixel-precise on the mesh. Returns true when a pile was looted
    /// (caller swallows the click so it doesn't also fire click-to-move).</summary>
    private bool TryClickPickupAt(Vector3 clickPos)
    {
        if (_lootPiles.Count == 0) return false;
        const float clickRadius = 1.0f;
        const float radiusSq = clickRadius * clickRadius;
        int bestIdx = -1;
        float bestDistSq = radiusSq;
        for (int i = 0; i < _lootPiles.Count; i++)
        {
            var pile = _lootPiles[i];
            if (pile.Throw is not null) continue;
            float dx = pile.Position.X - clickPos.X;
            float dz = pile.Position.Z - clickPos.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 < bestDistSq) { bestDistSq = d2; bestIdx = i; }
        }
        if (bestIdx < 0) return false;
        return LootPileNow(_lootPiles[bestIdx], bestIdx);
    }

    private void OnRender(double dt)
    {
        if (_gl is null) return;
        if (_diagMode) DiagRecordFrame(dt);
        if (_levelUpToastRemaining > 0f) _levelUpToastRemaining -= (float)dt;
        // Phase 18c — listener follows the PC every frame. We use camera
        // forward (not _playerFacing) so the audio image rotates with
        // the camera instead of the body — matches what the user is
        // looking at and avoids weird pans when the PC stands still
        // facing one way while the user orbits the cam. Falls back to
        // camera position when no PC has spawned (debug fly-cam mode).
        if (_audio is not null)
        {
            var listenerPos = _player?.CurrentTransform.Translation ?? _camera.Position;
            _audio.UpdateListener(listenerPos, _camera.Forward, Vector3.UnitY);
        }
        // Phase 17a — tick + prune floating cast feedback. Reverse-iterate so
        // RemoveAt is O(1) per kill and the list compacts in-place.
        for (int i = _floatingTexts.Count - 1; i >= 0; i--)
        {
            _floatingTexts[i].Remaining -= (float)dt;
            if (_floatingTexts[i].Remaining <= 0f) _floatingTexts.RemoveAt(i);
        }
        // Phase 17-SC-E — integrate billboard particles + lightning bolts +
        // (Phase 21-SC-SPELL-VFX) flying projectile heads. The screen-space
        // _spellBolts trail was retired here; primary cast visuals are now
        // the 3D world-space primitives spawned through SpawnSpellVisual.
        _particles?.Tick((float)dt);
        // Phase 22-SC-MUSIC-A/B — refill the music streaming queue every
        // frame. Cheap when nothing's playing (one OpenAL state read);
        // when a track is active it decodes ~0-3 chunks per tick to
        // refill drained buffers.
        // Phase 22-SC-MUSIC-FOLD — Tick returns false on natural EOS
        // (decoder ran out, source drained). Clear _currentMusicTrack
        // so a subsequent PlayMusicTrack with the same basename
        // actually re-fires — without this, a frontend track that
        // played to completion would refuse to replay because the
        // idempotency guard still thought it was active.
        if (_music is not null && !_music.Tick() && _currentMusicTrack.Length > 0)
            _currentMusicTrack = "";
        // Phase 22-SC-MUSIC-D — flip music to mood.battle_track when any
        // hostile NPC is engaging the player; revert to standard_track
        // after CombatExitDelay seconds without aggro. Cheap loop over
        // _actors; bails on the first hostile so worst-case is
        // population-bounded but typically O(few).
        TickCombatMusic((float)dt);
        // Phase 21-SC-BARREL-C — integrate frag debris (gravity + ground
        // settle). Same per-tick cadence as particles; a frag's settled
        // pose then lasts until lifetime expires.
        TickFragDebris((float)dt);
        // Phase 17-SC-F-2 — advance any active sfx_script coroutines and
        // run continuous-emitter spawn budgets. Must run after the particle
        // Tick so this frame's spawns get drawn rather than waiting one tick.
        _sfxRuntime?.Tick((float)dt);
        // Phase 9-SC-9 — advance toss arcs on freshly-dropped piles. Linear
        // XZ lerp from feet to target with a parabolic Y arc (0 → ArcHeight
        // at midpoint → 0 at landing). Once Elapsed reaches Duration the
        // throw is cleared so auto-pickup becomes eligible.
        for (int i = 0; i < _lootPiles.Count; i++)
        {
            var pile = _lootPiles[i];
            var th = pile.Throw;
            if (th is null) continue;
            th.Elapsed += (float)dt;
            float t = th.Elapsed / th.Duration;
            if (t >= 1f)
            {
                pile.Position = th.Target;
                pile.RotationY = 0f;
                pile.RotationX = 0f;
                pile.Throw = null;
                continue;
            }
            var basePos = Vector3.Lerp(th.Source, th.Target, t);
            float arc = th.ArcHeight * 4f * t * (1f - t); // peak at t=0.5
            pile.Position = new Vector3(basePos.X, basePos.Y + arc, basePos.Z);
            pile.RotationY = th.StartRotation + th.Spins * MathF.PI * 2f * t;
            // Phase 21-SC-SCROLL-F — X-axis tumble. Same easing as Y: linear
            // through the flight, comes to rest exactly on landing because
            // the t>=1 branch above clears it. With XSpins=1.0 the item
            // does one full end-over-end during the throw arc; legacy
            // enemy drops keep XSpins=0 for the original pure-yaw look.
            pile.RotationX = th.StartRotationX + th.XSpins * MathF.PI * 2f * t;
        }
        // Phase 21-SC-SCROLL-GLITTER — DS1's continuous "pixie dust" on
        // resting spell scrolls. Distinct visual recipe per user feedback:
        // pure white (not element-tinted), pronounced, and SCATTERED
        // across the scroll's top surface with a rolling drift — like
        // twinkling stars rolling across a night sky.
        //
        // Per-pile timer accumulates spawn budget at glitterRate; when
        // it tips over an integer, fires SpawnTwinkle which scatters the
        // batch across the pile's footprint (uniform-area XZ sample) and
        // gives each particle a tangential drift velocity so the field
        // visibly moves rather than puffing in place. Y-offset puts the
        // emit plane just above the scroll mesh's top (pile renders at
        // pos.Y + pileSize=0.5, so 0.55 floats sparkles right above the
        // visible scroll surface — the prior 0.10 emitted UNDER the mesh
        // which read as "wrong side of the scroll").
        //
        // Pickup flow already removes scroll items from the pile, so the
        // glitter naturally turns off when the player picks up — no
        // explicit gate needed.
        if (_particles is not null)
        {
            // Tuning per "storm cloud" user feedback (b4accc5 v2 was too
            // tall + too loose). Hugging the scroll mesh now: emit plane
            // just above the visible top, half the prior XZ scatter, and
            // shorter lifetime so particles can't accumulate altitude
            // before they fade.
            const float glitterRate = 18f;          // sparkles per second per pile
            const float scrollTopY  = 0.30f;        // was 0.55 — emit just above the resting scroll
            const float footprintR  = 0.16f;        // was 0.30 — tighter cluster on the scroll
            var sparkleColor = new Vector4(1f, 1f, 1f, 1f); // pure white
            for (int i = 0; i < _lootPiles.Count; i++)
            {
                var pile = _lootPiles[i];
                if (pile.Throw is not null) continue;
                bool hasScroll = false;
                foreach (var entry in pile.Items)
                {
                    if (string.IsNullOrEmpty(entry.Slot)
                        && entry.Reference.StartsWith("spell_", StringComparison.OrdinalIgnoreCase))
                    {
                        hasScroll = true;
                        break;
                    }
                }
                if (!hasScroll) { pile.GlitterCarry = 0f; continue; }
                pile.GlitterCarry += glitterRate * (float)dt;
                if (pile.GlitterCarry < 1f) continue;
                int batch = (int)pile.GlitterCarry;
                pile.GlitterCarry -= batch;
                _particles.SpawnTwinkle(
                    pile.Position + new Vector3(0f, scrollTopY, 0f),
                    sparkleColor,
                    footprintRadius: footprintR,
                    scale: 0.14f,         // was 0.18 — tighter visual size
                    duration: 0.45f,      // was 0.70 — fade before drifting too high
                    count: batch);
            }
        }
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var size = _window.FramebufferSize;

        // Phase 24-MAINMENU step 1+2 — boot mode renders the splash → main
        // menu sequence in the absence of a loaded region. Runs early so
        // the gameplay-render block below (which checks for null tanks /
        // null player and silently skips) sees a black-cleared frame; the
        // splash overlay is drawn by DrawBootScene at the HUD-pass site.
        // Phase 24-POLISH-A — early-return so the dev fly-cam grid + any
        // other always-on debug helpers don't peek through behind the
        // splash / main menu. Only the boot-scene draw runs in boot mode.
        if (_bootMode)
        {
            DrawBootScene((float)dt, size.X, size.Y);
            return; // Silk.NET swaps buffers automatically when OnRender returns
        }
        var aspect = size.Y == 0 ? 1f : (float)size.X / size.Y;
        var vp = _camera.GetViewProjection(aspect);

        if (_gridShader is not null && _grid is not null)
        {
            _gridShader.Use();
            _gridShader.SetMatrix4("uViewProj", vp);
            _grid.Draw();
        }

        if (_meshShader is not null && _mesh is not null)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetMatrix4("uModel", Matrix4x4.Identity);
            _meshShader.SetInt("uAlbedo", 0);
            _meshShader.SetInt("uHasTexture", _texture is null ? 0 : 1);
            _meshShader.SetInt("uFlipV", 1);
            ApplyLightingUniforms(_meshShader);
            _texture?.Bind(TextureUnit.Texture0);
            _mesh.Draw();
        }

        if (_meshShader is not null && _sno is not null)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetMatrix4("uModel", Matrix4x4.Identity);
            _meshShader.SetInt("uAlbedo", 0);
            _meshShader.SetInt("uFlipV", 1);
            ApplyLightingUniforms(_meshShader);
            for (var i = 0; i < _sno.Subsets.Count; i++)
            {
                var subset = _sno.Subsets[i];
                if (_snoTextures.TryGetValue(subset.TextureName, out var tex))
                {
                    tex.Bind(TextureUnit.Texture0);
                    _meshShader.SetInt("uHasTexture", 1);
                }
                else
                {
                    _meshShader.SetInt("uHasTexture", 0);
                }
                _meshShader.SetVec2("uUvOffset", ComputeTexUvOffset(subset.TextureName, _terrainTime));
                _sno.DrawSubset(i);
            }
            _meshShader.SetVec2("uUvOffset", Vector2.Zero);
        }

        if (_skinShader is not null && _skinnedMesh is not null && _skinnedAsp is not null && _anim is not null)
        {
            // Loop the clip by wrapping the running time. AnimationRuntime clamps internally,
            // so this is just for visual continuity — without the wrap a long-running session
            // would freeze on the last keyframe forever.
            var t = (float)(_anim.AnimLength > 0f ? _animTime % _anim.AnimLength : 0.0);
            var skin = AnimationRuntime.ComputeSkinMatrices(_skinnedAsp, _anim, t);

            _skinShader.Use();
            _skinShader.SetMatrix4("uViewProj", vp);
            _skinShader.SetMatrix4("uModel", Matrix4x4.Identity);
            // "uBones[0]" not "uBones": GL 3.3 spec says either is valid for array-of-basic-type
            // uniforms, but several older Intel / mesa drivers only return a real location for
            // the element-0 form. With the bare name, SetMatrix4Array silently no-ops (loc<0)
            // and you get a T-posed mesh with no GL error.
            _skinShader.SetMatrix4Array("uBones[0]", skin);
            _skinShader.SetInt("uAlbedo", 0);
            _skinShader.SetInt("uHasTexture", _animTexture is null ? 0 : 1);
            _skinShader.SetInt("uFlipV", 1);
            ApplyLightingUniforms(_skinShader);
            _animTexture?.Bind(TextureUnit.Texture0);
            _skinnedMesh.Draw();
        }

        if (_skinShader is not null && _actors.Count > 0)
        {
            // One draw call per actor. The mesh cache keeps unique ASPs down (DS1 ships ~12
            // distinct archetypes per region) but each instance has its own bones, so we
            // re-upload uBones per actor. This is cheap at 181 actors — can batch per-mesh
            // later if a 4-region stream pushes the actor count past ~2k.
            _skinShader.Use();
            _skinShader.SetMatrix4("uViewProj", vp);
            _skinShader.SetInt("uAlbedo", 0);
            ApplyLightingUniforms(_skinShader);
            // Phase 21c-4 — uFlipV is now per-actor (set inside the loop) because
            // older ASP versions (v2.3, krug et al) author UVs bottom-up to match
            // .raw byte order while v2.5+ (player farmboy, late-content NPCs) use
            // D3D top-down. One global flip got either the krug or the player
            // wrong. See SetFlipForMesh below.
            // Phase 21c — track the currently-bound albedo so we don't rebind for runs
            // of actors sharing the same mesh (DS1 regions ship ~12 unique archetypes
            // for ~180 actors, so most adjacent draws hit the cache). Phase 21c-4 keys
            // on (template, mesh) because krug_grunt and krug_scout share the same
            // pose mesh but each template overrides the texset name.
            int lastFlipV = -1;
            // Phase 21d-2a-v — uFlipV is the same for ALL skinned actors. Earlier
            // analysis (Phase 21c-4) thought v2.5+ ASPs needed the flip. The
            // subset-tint diagnostic in 21d-2a-v showed otherwise: farmboy's head
            // subset (asp V[0.01,0.54]) needs to sample the face/hair region of
            // its skin .raw, which sits at the start of file bytes (= GL V≈0)
            // because OpenGL puts byte 0 at the bottom-left and ImageSharp dumps
            // confirm face data is at PNG row 0. With uFlipV=1 those UVs invert
            // to GL V[0.46,0.99] = top of GL = the brown gradient strip, which
            // is exactly the "smeared face / no detail" the user reported.
            // SIEGEFX_FORCE_FLIPV=0|1 overrides for one-off testing.
            int defaultFlipV = 0;
            string? forceFlipEnv = Environment.GetEnvironmentVariable("SIEGEFX_FORCE_FLIPV");
            if (forceFlipEnv == "0") defaultFlipV = 0;
            else if (forceFlipEnv == "1") defaultFlipV = 1;
            foreach (var s in _actors)
            {
                int flipV = defaultFlipV;
                if (flipV != lastFlipV)
                {
                    _skinShader.SetInt("uFlipV", flipV);
                    lastFlipV = flipV;
                }
                var clips = s.Actor.Clips;
                int boneCount = s.Actor.Mesh.BoneCount;
                ReadOnlySpan<Matrix4x4> skin;
                if (clips.Length == 0)
                {
                    // No parsable PRS for this actor (shipped 0x0202 clips we don't support
                    // yet). Identity per bone = bind-pose pass-through, which renders the
                    // mesh as authored. Good enough to confirm placement. The cached
                    // identity array is reused across frames per mesh, so no per-frame
                    // alloc here either.
                    if (!_actorIdentityBones.TryGetValue(s.Actor.Mesh, out var identity))
                    {
                        identity = new Matrix4x4[boneCount];
                        for (int i = 0; i < identity.Length; i++) identity[i] = Matrix4x4.Identity;
                        _actorIdentityBones[s.Actor.Mesh] = identity;
                    }
                    skin = identity;
                }
                else
                {
                    // Phase 21c-4 — pick walk clip (Actor.WalkClipIndex) when the brain
                    // is translating this actor; otherwise honor the skrit-driven default.
                    // Without this every NPC played its idle while striding across the map.
                    int idx;
                    if (!s.IsDead && s.IsMoving && s.Actor.WalkClipIndex >= 0 && s.Actor.WalkClipIndex < clips.Length)
                        idx = s.Actor.WalkClipIndex;
                    else
                        idx = Math.Min(s.Actor.CurrentClipIndex, clips.Length - 1);
                    var clip = clips[idx];
                    // Phase 12-SC-4 — dead actors hold the last frame of chore_die
                    // (clamp to AnimLength) instead of looping. Without the clamp the
                    // corpse would replay the death over and over.
                    float t;
                    if (clip.AnimLength <= 0f) t = 0f;
                    else if (s.IsDead) t = MathF.Min((float)s.AnimTime, clip.AnimLength - 0.01f);
                    else t = (float)(s.AnimTime % clip.AnimLength);
                    // Phase 21b-2 — write into the shared scratch buffer instead of
                    // allocating a fresh Matrix4x4[] per actor per frame. The upload
                    // below copies the bytes synchronously so the next iteration may
                    // overwrite the buffer immediately.
                    if (_skinScratch.Length < boneCount)
                        _skinScratch = new Matrix4x4[Math.Max(boneCount, 64)];
                    AnimationRuntime.ComputeSkinMatrices(s.Actor.Mesh, clip, t, _skinScratch);
                    skin = _skinScratch.AsSpan(0, boneCount);
                }
                _skinShader.SetMatrix4("uModel", s.CurrentTransform);
                _skinShader.SetMatrix4Array("uBones[0]", skin);

                // Phase 21d-2a-ii — walk per-mesh subsets emitted by the ASP parser.
                // Each subset is one BSMM (textureIndex, faceSpan) record carved out
                // of the flattened triangle list; binding per subset prevents farmboy's
                // clothing strip (slot 1 = b_c_pos_a1_015) from inheriting the skin
                // sampler bound for slot 0. Single-subset meshes (krug, goblins, most
                // monsters) issue one bind + one DrawSubset, identical in cost to the
                // old single Draw() path.
                var subsets = s.Actor.Mesh.Subsets;
                if (subsets.Length == 0)
                {
                    var tex = ResolveActorTexture(s.Actor, 0);
                    if (tex is not null) { tex.Bind(TextureUnit.Texture0); _skinShader.SetInt("uHasTexture", 1); }
                    else _skinShader.SetInt("uHasTexture", 0);
                    s.GlMesh.Draw();
                }
                else
                {
                    int lastSlot = -1;
                    int subsetIdx = 0;
                    foreach (var sub in subsets)
                    {
                        if (sub.TextureIndex != lastSlot)
                        {
                            var tex = ResolveActorTexture(s.Actor, sub.TextureIndex);
                            if (tex is not null) { tex.Bind(TextureUnit.Texture0); _skinShader.SetInt("uHasTexture", 1); }
                            else _skinShader.SetInt("uHasTexture", 0);
                            lastSlot = sub.TextureIndex;
                        }
                        if (_subsetTintActive)
                        {
                            // Phase 21d-2a-v — solid-color tint per subset (red, green,
                            // blue, yellow, magenta, cyan, orange, purple). Wraps if a
                            // mesh has more subsets than palette entries; the largest
                            // shipped DS1 multi-subset mesh tops out at 5 (farmboy body),
                            // well under the palette length.
                            var (tr, tg, tb) = SubsetTintFor(subsetIdx);
                            _skinShader.SetInt("uSubsetTintActive", 1);
                            _skinShader.SetVec4("uSubsetTint", tr, tg, tb, 1f);
                        }
                        s.GlMesh.DrawSubset(sub.FirstTriangle, sub.TriangleCount);
                        subsetIdx++;
                    }
                    if (_subsetTintActive)
                        _skinShader.SetInt("uSubsetTintActive", 0);
                }
            }

            // Phase 21d-2a-vii — layered equipment pass. Each entry is a skinned
            // ASP that shares the player body's biped skeleton; we re-skin it
            // against the body's current animation clip + time, then bind the
            // equipment texture and draw. AnimationRuntime caches its bone-name
            // → PRS-bone-index map per (asp, anim) pair, so the only per-frame
            // cost per layer is the skin matrix walk + one DrawSubset per
            // subset. We reuse _skinScratch — bones get overwritten with the
            // layer's skin matrices, then the layer issues its draws, then the
            // weapon-attach pass below recomputes bone worlds from a fresh walk.
            if (_player is not null && !_player.IsDead && _equippedLayers.Count > 0)
            {
                var pcMesh = _player.Actor.Mesh;
                var pcClips = _player.Actor.Clips;
                int pcCidx;
                if (pcClips.Length > 0)
                {
                    if (_player.IsMoving && _player.Actor.WalkClipIndex >= 0
                        && _player.Actor.WalkClipIndex < pcClips.Length)
                        pcCidx = _player.Actor.WalkClipIndex;
                    else
                        pcCidx = Math.Min(_player.Actor.CurrentClipIndex, pcClips.Length - 1);
                }
                else pcCidx = -1;

                var pcClip = pcCidx >= 0 ? pcClips[pcCidx] : null;
                var pcTime = pcClip is not null && pcClip.AnimLength > 0f
                    ? (float)(_player.AnimTime % pcClip.AnimLength)
                    : 0f;

                // _skinShader is bound + uViewProj/lighting/uAlbedo are set by
                // the body actor loop above today; re-set them here defensively
                // so this block stays correct if a future pass slips between
                // the two and clobbers the skin-shader state.
                _skinShader.Use();
                _skinShader.SetMatrix4("uViewProj", vp);
                _skinShader.SetInt("uAlbedo", 0);
                ApplyLightingUniforms(_skinShader);
                _skinShader.SetMatrix4("uModel", _player.CurrentTransform);
                int layerFlipV = 0;
                _skinShader.SetInt("uFlipV", layerFlipV);

                foreach (var layer in _equippedLayers)
                {
                    int boneCount = layer.Asp.BoneCount;
                    if (boneCount == 0) continue;
                    // Defensive cap: SkinnedMesh ctor already rejects ASPs over
                    // MaxBones, so reaching here with boneCount>64 means the
                    // SkinnedMesh and its source ASP have drifted out of sync.
                    // Skip rather than upload past the uBones[64] array end.
                    if (boneCount > SkinnedMesh.MaxBones)
                    {
                        Console.WriteLine(
                            $"  equip: '{layer.MeshBaseName}.asp' bones={boneCount} > " +
                            $"SkinnedMesh.MaxBones={SkinnedMesh.MaxBones}; skipping layer draw");
                        continue;
                    }
                    if (_skinScratch.Length < boneCount)
                        _skinScratch = new Matrix4x4[Math.Max(boneCount, 64)];
                    if (pcClip is not null)
                    {
                        AnimationRuntime.ComputeSkinMatrices(layer.Asp, pcClip, pcTime, _skinScratch);
                    }
                    else
                    {
                        for (int i = 0; i < boneCount; i++) _skinScratch[i] = Matrix4x4.Identity;
                    }
                    _skinShader.SetMatrix4Array("uBones[0]", _skinScratch.AsSpan(0, boneCount));

                    var subsets = layer.Asp.Subsets;
                    if (subsets.Length == 0)
                    {
                        if (layer.Texture is not null)
                        {
                            layer.Texture.Bind(TextureUnit.Texture0);
                            _skinShader.SetInt("uHasTexture", 1);
                        }
                        else _skinShader.SetInt("uHasTexture", 0);
                        layer.Mesh.Draw();
                    }
                    else
                    {
                        // Track first-iteration with a bool, not a sentinel int:
                        // sub.TextureIndex == -1 is a valid "use BMSH default"
                        // signal and would silently skip the bind branch if we
                        // initialised lastSlot to -1.
                        bool firstSubset = true;
                        int lastSlot = 0;
                        foreach (var sub in subsets)
                        {
                            if (firstSubset || sub.TextureIndex != lastSlot)
                            {
                                // Equipment ASPs ship a single template-driven
                                // texture (b_a_boot_<style>); when a multi-subset
                                // boot ASP shows up we bind the same texture for
                                // every subset since defend.armor_style is one
                                // value. Fall back to the ASP's BMSH default for
                                // out-of-range slots.
                                GlTexture? tex = layer.Texture;
                                if (tex is null && sub.TextureIndex >= 0
                                    && sub.TextureIndex < layer.Asp.TextureNames.Count)
                                {
                                    tex = LoadTexsetTexture(layer.Asp.TextureNames[sub.TextureIndex]);
                                }
                                if (tex is not null) { tex.Bind(TextureUnit.Texture0); _skinShader.SetInt("uHasTexture", 1); }
                                else _skinShader.SetInt("uHasTexture", 0);
                                lastSlot = sub.TextureIndex;
                                firstSubset = false;
                            }
                            layer.Mesh.DrawSubset(sub.FirstTriangle, sub.TriangleCount);
                        }
                    }
                }
            }
        }

        // Phase 21c — static prop layer (trees, barrels, fences, crops, candles,
        // chairs, etc. from non_interactive/container/inventory/interactive/emitter
        // .gas). Drawn through the same static-mesh pipeline weapons use; one draw
        // call per placement, with the texture cached per AspMesh so adjacent props
        // sharing a model only rebind once. Cull-face is disabled for the pass so
        // foliage leaf cards render from both sides (DS1 trees/shrubs/crops are
        // alpha-cutout single-sided quads; backface culling makes half the leaves
        // disappear). The fragment shader handles the alpha discard; no blend
        // state needed for hard cutout.
        if (_meshShader is not null && _staticProps.Count > 0)
        {
            _gl!.Disable(GLEnum.CullFace);
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetInt("uAlbedo", 0);
            // Static-prop UVs are authored against the same orientation the .raw is
            // stored in, so the D3D→GL V flip we do for skinned meshes overshoots
            // here and lands the lid art on the body. Opt out for this pass only.
            _meshShader.SetInt("uFlipV", 0);
            ApplyLightingUniforms(_meshShader);
            GlTexture? lastTex = null;
            int lastHas = -1;
            foreach (var prop in _staticProps)
            {
                // Phase 17-SC-K — once a breakable prop's life hits zero we
                // simulate the shatter by dropping it from the render set;
                // the on-hit code already kicked debris particles at the
                // origin so the disappearance reads as a break, not a pop.
                if (prop.IsDestroyed) continue;
                if (!ReferenceEquals(prop.Texture, lastTex))
                {
                    if (prop.Texture is not null)
                    {
                        prop.Texture.Bind(TextureUnit.Texture0);
                        if (lastHas != 1) { _meshShader.SetInt("uHasTexture", 1); lastHas = 1; }
                    }
                    else
                    {
                        if (lastHas != 0) { _meshShader.SetInt("uHasTexture", 0); lastHas = 0; }
                    }
                    lastTex = prop.Texture;
                }
                Matrix4x4 model = prop.World;
                if (prop.SpinRadPerSec != 0f)
                {
                    // Rotate around the model's local axis (wheel spindle is at
                    // its asp origin), then apply the placement transform — this
                    // way the wheel spins in place and the placement orientation
                    // continues to point the spindle in the right direction.
                    var theta = (float)(_terrainTime * prop.SpinRadPerSec);
                    var spin = prop.SpinAxis switch
                    {
                        1 => Matrix4x4.CreateRotationY(theta),
                        2 => Matrix4x4.CreateRotationZ(theta),
                        _ => Matrix4x4.CreateRotationX(theta),
                    };
                    model = spin * prop.World;
                }
                _meshShader.SetMatrix4("uModel", model);
                prop.Mesh.Draw();
            }
            _gl.Enable(GLEnum.CullFace);
        }

        // Phase 21-SC-BARREL-C — frag-debris pass. Same shader and uFlipV
        // convention as the static-prop layer (frag .asps are authored
        // bottom-up like every other DS1 prop), but the model matrix is
        // rebuilt per frame from the integrated position + spin axis.
        // Cull-face stays enabled — frag fragments are solid little
        // bone/wood/metal chunks, not foliage cards.
        if (_meshShader is not null && _fragDebris.Count > 0)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetInt("uAlbedo", 0);
            _meshShader.SetInt("uFlipV", 0);
            ApplyLightingUniforms(_meshShader);
            GlTexture? lastTex = null;
            int lastHas = -1;
            foreach (var f in _fragDebris)
            {
                if (!ReferenceEquals(f.Asset.Texture, lastTex))
                {
                    if (f.Asset.Texture is not null)
                    {
                        f.Asset.Texture.Bind(TextureUnit.Texture0);
                        if (lastHas != 1) { _meshShader.SetInt("uHasTexture", 1); lastHas = 1; }
                    }
                    else if (lastHas != 0) { _meshShader.SetInt("uHasTexture", 0); lastHas = 0; }
                    lastTex = f.Asset.Texture;
                }
                var spin = Matrix4x4.CreateFromAxisAngle(f.SpinAxis, f.SpinAngle);
                var model = spin * Matrix4x4.CreateTranslation(f.Pos);
                _meshShader.SetMatrix4("uModel", model);
                f.Asset.Mesh.Draw();
            }
        }

        // Phase 14d — render the PC's equipped weapon attached to the weapon_grip
        // bone. We recompute the animated bone worlds for the PC (cheap — the bone
        // walk already happened inside ComputeSkinMatrices, we just didn't surface
        // the intermediate array). weaponWorld = weaponBindInv * worldAnim[weapon_grip]
        // * player.CurrentTransform. Drawn with the static-mesh pipeline because
        // DS1 weapon ASPs weight every corner to bone 0 (effectively rigid); the
        // bind-inverse pre-multiply is what snaps the ASP's grip bone onto the hand.
        if (_meshShader is not null && _weaponMesh is not null && _player is not null
            && !_player.IsDead && _weaponGripBoneIdx >= 0)
        {
            var pcMesh = _player.Actor.Mesh;
            var clips = _player.Actor.Clips;
            int pcBones = pcMesh.BoneCount;
            // Phase 21b-2 — reuse a shared bone-world scratch instead of allocating
            // a fresh Matrix4x4[] every frame just to read one element.
            if (_boneWorldsScratch.Length < pcBones)
                _boneWorldsScratch = new Matrix4x4[Math.Max(pcBones, 64)];
            if (clips.Length == 0)
            {
                AnimationRuntime.ComputeAnimatedBoneWorlds(pcMesh, null, 0f, _boneWorldsScratch);
            }
            else
            {
                // Mirror the body's walk-swap so the weapon's grip bone tracks the same
                // clip the body is being skinned with. Without this the body uses the
                // walk clip while the weapon's bone-worlds are still sampled from the
                // idle clip — symptom: dagger "floats" at the idle wrist position
                // while the walking arm swings through it.
                int cidx = _player.Actor.CurrentClipIndex;
                if (_player.IsMoving && _player.Actor.WalkClipIndex >= 0 && _player.Actor.WalkClipIndex < clips.Length)
                    cidx = _player.Actor.WalkClipIndex;
                cidx = Math.Min(cidx, clips.Length - 1);
                var clip = clips[cidx];
                var t = (float)(clip.AnimLength > 0f ? _player.AnimTime % clip.AnimLength : 0.0);
                AnimationRuntime.ComputeAnimatedBoneWorlds(pcMesh, clip, t, _boneWorldsScratch);
            }

            if (_weaponGripBoneIdx < pcBones)
            {
                var gripLocal = _boneWorldsScratch[_weaponGripBoneIdx];
                // weaponBindInv cancels the weapon ASP's own grip-bone bind offset
                // so the grip sits at the hand bone's world origin, then gripLocal
                // places it in the player mesh frame, then CurrentTransform moves
                // the whole rig to world space.
                //
                // SiegeMax ASPImport.ms ("grips must be prerotated (hack fix)",
                // line 765) applies an `angleAxis 90 [1,0,0]` to weapon_grip /
                // shield_grip on import; PRS keys are authored against that frame.
                // Without the same rotation at draw time the dagger sits 90° off —
                // blade points down (icepick / "stabbing" grip) instead of forward
                // (thrust / "piercing" grip). s_gripPreRot / s_gripPreTrans baked
                // from 21d-2a-vi A/B work — see field-level comment for context.
                var gripPreRot = Matrix4x4.CreateFromQuaternion(s_gripPreRot);
                var gripPreTrans = Matrix4x4.CreateTranslation(s_gripPreTrans);
                var weaponModel = _weaponBindInv * gripPreRot * gripPreTrans * gripLocal * _player.CurrentTransform;

                _meshShader.Use();
                _meshShader.SetMatrix4("uViewProj", vp);
                _meshShader.SetMatrix4("uModel", weaponModel);
                _meshShader.SetInt("uAlbedo", 0);
                // 21d-2a-vi: weapon textures (e.g. b_w_weapons.raw, a 9-mip atlas
                // covering daggers/swords/axes/staves) are stored bottom-up like
                // every other DS1 .raw. uFlipV=1 was inverting the dagger's UV
                // V[0.667,0.998] into V[0.002,0.333], which sampled a magic-staff
                // glow strip at the bottom of the atlas — visible as a "rainbow"
                // dagger. Match the skinned-actor / static-prop convention.
                _meshShader.SetInt("uFlipV", 0);
                ApplyLightingUniforms(_meshShader);
                if (_weaponTexture is not null)
                {
                    _weaponTexture.Bind(TextureUnit.Texture0);
                    _meshShader.SetInt("uHasTexture", 1);
                }
                else
                {
                    _meshShader.SetInt("uHasTexture", 0);
                }
                _weaponMesh.Draw();
            }

            // Phase 9-SC-10 — render every other bone-attached prop on the PC
            // (shields today, quiver/torch tomorrow). Reuses the per-frame
            // _boneWorldsScratch the weapon draw just populated, so we don't
            // recompute the bone walk; each attach takes one bone from the
            // already-animated array, applies its bind-inverse + the shared
            // grip prerotation/translation, and draws as a static mesh. The
            // same prerotation rule applies to shield_grip per SiegeMax
            // ASPImport.ms (see s_gripPreRot field comment).
            if (_attachedItems.Count > 0 && _meshShader is not null)
            {
                int pcBoneCount = _player.Actor.Mesh.BoneCount;
                var gripPreRot = Matrix4x4.CreateFromQuaternion(s_gripPreRot);
                var gripPreTrans = Matrix4x4.CreateTranslation(s_gripPreTrans);
                _meshShader.Use();
                _meshShader.SetMatrix4("uViewProj", vp);
                _meshShader.SetInt("uAlbedo", 0);
                _meshShader.SetInt("uFlipV", 0);
                ApplyLightingUniforms(_meshShader);
                // Phase 9-SC-10 — extra rotation for the shield slot, applied on
                // top of the shared grip prerotation. See s_shieldExtraRot for the
                // rationale behind the +85° X bake.
                var shieldExtraRot = Matrix4x4.CreateFromQuaternion(s_shieldExtraRot);
                var shieldExtraTrans = Matrix4x4.CreateTranslation(s_shieldExtraTrans);
                foreach (var att in _attachedItems)
                {
                    if (att.BoneIdx < 0 || att.BoneIdx >= pcBoneCount) continue;
                    var boneLocal = _boneWorldsScratch[att.BoneIdx];
                    bool isShield = string.Equals(att.SlotName, "es_shield_hand",
                        StringComparison.OrdinalIgnoreCase);
                    var model = isShield
                        ? att.BindInv * shieldExtraRot * shieldExtraTrans * gripPreRot * gripPreTrans * boneLocal * _player.CurrentTransform
                        : att.BindInv * gripPreRot * gripPreTrans * boneLocal * _player.CurrentTransform;
                    _meshShader.SetMatrix4("uModel", model);
                    if (att.Texture is not null)
                    {
                        att.Texture.Bind(TextureUnit.Texture0);
                        _meshShader.SetInt("uHasTexture", 1);
                    }
                    else
                    {
                        _meshShader.SetInt("uHasTexture", 0);
                    }
                    att.Mesh.Draw();
                }
            }
        }

        // Phase 9-SC-7 — render the first item in each loot pile as its real
        // ASP mesh (and texture, if any). Items without [aspect][model] (gold,
        // potions whose template is scroll-only, etc.) fall back to the legacy
        // untextured cube so every pile is still visually distinct from terrain.
        // Phase 9-SC-LL — same loop captures the per-pile world-space label so
        // the text-overlay pass can draw "Spiked Club" / "Healing Potion" /
        // etc. above each pile (gold, red on hover) without re-resolving.
        _frameLootLabels.Clear();
        if (_meshShader is not null && _lootPiles.Count > 0)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetInt("uAlbedo", 0);
            ApplyLightingUniforms(_meshShader);
            const float pileSize = 0.5f;
            const float itemScale = 0.6f; // matches the cube footprint visually
            foreach (var pile in _lootPiles)
            {
                var pos = pile.Position;
                // Walk the pile rather than only trying Items[0] — DS1 mixes
                // resolvable templates ("wpn_axe_001") with pcontent specs
                // ("#weapon/9") in the same drop list, and Items[0] is often
                // the unrollable spec. First resolvable wins; fall through to
                // the cube only if every entry misses.
                ItemMesh? itemMesh = null;
                for (var i = 0; i < pile.Items.Count; i++)
                {
                    itemMesh = TryGetItemMesh(pile.Items[i].Reference);
                    if (itemMesh is not null) break;
                }

                if (itemMesh is not null)
                {
                    // Phase 9-SC-10 — RestPitch tips items that don't sit
                    // naturally upright (shields lay flat). While a throw is
                    // in flight, fade the pitch in over the back half so it
                    // doesn't pop on landing.
                    float pitch = pile.RestPitch;
                    if (pile.Throw is { } th2 && th2.Duration > 0f)
                    {
                        float pt = MathF.Min(1f, th2.Elapsed / th2.Duration);
                        pitch *= MathF.Max(0f, (pt - 0.5f) * 2f);
                    }
                    // Phase 21-SC-SCROLL-F — sum the static rest-pitch with the
                    // throw-driven X tumble so a flying item reads as "twist
                    // + flop" while a landed item just sits at its rest angle.
                    // RotationX is already 0 once the throw lands (cleared at
                    // t>=1 in the tick), so this collapses to RestPitch only.
                    var model = Matrix4x4.CreateScale(itemScale)
                              * Matrix4x4.CreateRotationX(pitch + pile.RotationX)
                              * Matrix4x4.CreateRotationY(pile.RotationY)
                              * Matrix4x4.CreateTranslation(pos.X, pos.Y + pileSize * 0.5f, pos.Z);
                    _meshShader.SetMatrix4("uModel", model);
                    _meshShader.SetInt("uFlipV", 0);
                    if (itemMesh.Texture is not null)
                    {
                        itemMesh.Texture.Bind(TextureUnit.Texture0);
                        _meshShader.SetInt("uHasTexture", 1);
                    }
                    else
                    {
                        _meshShader.SetInt("uHasTexture", 0);
                    }
                    itemMesh.Mesh.Draw();
                    if (itemMesh.DisplayName is not null)
                    {
                        // Sit the label nearly on top of the mesh — pile mesh
                        // top is at pos.Y + pileSize (0.5), and a 0.05 gap
                        // floats the text just above the surface without
                        // clipping the textured top face. Was +pileSize+0.6
                        // which floated the labels well above the items;
                        // user feedback on SC-SCROLL ground pile asked for
                        // "much closer to the objects, nearly right on top."
                        var labelPos = new Vector3(pos.X, pos.Y + pileSize + 0.05f, pos.Z);
                        _frameLootLabels.Add((labelPos, itemMesh.DisplayName));
                    }
                }
                else if (_lootCube is not null)
                {
                    var model = Matrix4x4.CreateScale(pileSize)
                              * Matrix4x4.CreateTranslation(pos.X, pos.Y + pileSize * 0.5f, pos.Z);
                    _meshShader.SetMatrix4("uModel", model);
                    _meshShader.SetInt("uFlipV", 1);
                    _meshShader.SetInt("uHasTexture", 0);
                    _lootCube.Draw();
                }
            }
        }

        if (_meshShader is not null && _regionInstances.Count > 0)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetInt("uAlbedo", 0);
            _meshShader.SetInt("uFlipV", 1);
            ApplyLightingUniforms(_meshShader);
            foreach (var inst in _regionInstances)
            {
                _meshShader.SetMatrix4("uModel", inst.World);
                // Phase 21b-2 — pulled from the (mesh, texsetAbbr) cache so the
                // inner subset loop allocates zero strings per frame.
                var resolvedNames = GetResolvedSubsetTexNames(inst.Mesh, inst.TexsetAbbr);
                for (var i = 0; i < inst.Mesh.Subsets.Count; i++)
                {
                    if (_snoTextures.TryGetValue(resolvedNames[i], out var tex))
                    {
                        tex.Bind(TextureUnit.Texture0);
                        _meshShader.SetInt("uHasTexture", 1);
                    }
                    else
                    {
                        _meshShader.SetInt("uHasTexture", 0);
                    }
                    _meshShader.SetVec2("uUvOffset", ComputeTexUvOffset(resolvedNames[i], _terrainTime));
                    inst.Mesh.DrawSubset(i);
                }
            }
            _meshShader.SetVec2("uUvOffset", Vector2.Zero);
        }

        // Phase 17-SC-E — billboard particles. Sit above the world scene
        // (depth-tested against actors + props) but below the HUD ortho
        // pass so smoke columns get occluded by the farmhouse correctly.
        // Camera basis comes from the view matrix's transpose inside the
        // shader — passing view + proj separately rather than a combined
        // VP keeps that math local.
        if (_particles is not null)
        {
            var pview = _camera.GetView();
            var pproj = _camera.GetProjection(aspect);
            _particles.Draw(pview, pproj, _camera.Position);
        }

        // Phase 15a / Phase 21-SC-INV-A — 2D HUD overlay. Drawn last so it
        // sits over the 3D scene; BeginPass turns off depth + enables alpha
        // blend, EndPass restores both. The previous always-on HUD (200-wide
        // HP/MP bars + XP + Gold lines) is replaced by a small mini-HUD in
        // the top-left (two tiny vials around a portrait stub plus the two
        // active-spell icons) so the three DS1 panels (Character / Inventory
        // / SpellBook) can dock across the top dock row.
        // Phase 21-SC-BARREL-A1 — DS1 sprite cursor: pick state for this
        // frame (priority: enemy > breakable > loot pile > NPC > default)
        // and hide the OS cursor on the first frame our sprite is ready.
        // Cheap per-frame work (a few k-prop scans) and run before the HUD
        // pass so animated states get the same _terrainTime stride the
        // particle systems use.
        UpdateCursorState();
        EnsureOsCursorHidden();

        if (_textRenderer is not null && _textRenderer.HasFont)
        {
            _textRenderer.BeginPass();
            var col   = new Vector4(1f, 1f, 1f, 1f);
            var dim   = new Vector4(0.7f, 0.7f, 0.7f, 1f);
            // Phase 21d-2a-viii-c — hero name banner. Sits top-center so the
            // creator-typed name (or restored save value) is visible without
            // crowding the panel dock row. Skipped when empty (env-var path).
            if (_player is not null && !string.IsNullOrEmpty(_heroName))
            {
                int textW = _textRenderer.MeasureWidth(_heroName);
                int hx = (size.X - textW) / 2;
                _textRenderer.DrawString(size.X, size.Y, _heroName, hx, 12, col);
            }

            // Phase 23-SC-OPTIONS-FOLD2 — Show Framerate top-right. EMA
            // smoothing on dt so the counter doesn't flicker every frame;
            // 1/16 weight is a reasonable balance for a 60fps source.
            // Clamp dt to 100ms before mixing so a multi-second region
            // load (where dt spikes to 1000+ ms) doesn't poison the EMA
            // and leave the counter showing single-digit fps for ~1s
            // after the load completes.
            if (_showFps)
            {
                double sample = Math.Min(dt * 1000.0, 100.0);
                _fpsEmaMs += (sample - _fpsEmaMs) / 16.0;
                int fps = _fpsEmaMs > 0 ? (int)(1000.0 / _fpsEmaMs) : 0;
                var fpsStr = $"{fps} fps";
                int fw = _textRenderer.MeasureWidth(fpsStr);
                _textRenderer.DrawString(size.X, size.Y, fpsStr, size.X - fw - 8, 8, col);
            }

            if (_player is not null && _barRenderer is not null)
            {
                DrawMiniStatusHud(size.X, size.Y);
            }

            // Phase 21-SC-INV-A — three docked panels (Character / Inventory
            // / SpellBook) tile across the TOP of the screen, immediately to
            // the right of the always-on ability bar (round 7). Each panel
            // toggles independently (C / I / B) and sets its own OriginX/Y
            // so the dock layout is local to this method. Top margin matches
            // the mini-HUD's so the panels and the bar share the same
            // baseline at y=4; horizontal gap between panels keeps borders
            // visually separate.
            const int panelTopY    = 4;
            const int panelGutter  = 4;
            int dockX = (_miniHudRightX > 0 ? _miniHudRightX : panelGutter);

            if (_charPanelOpen && _player is not null)
            {
                _characterPanel.OriginX = dockX;
                _characterPanel.OriginY = panelTopY;
                _characterPanel.IsOpen  = true;
                int armor = ComputePlayerArmorRating();
                // Phase 21-SC-INV-B — skill XP fraction. Until per-skill XP
                // pools land all four skill rows + STR/DEX/INT bars share
                // the combined progression's into-level fraction.
                float xpFrac = 0f;
                if (_progression is not null)
                {
                    long span = _progression.XpForNextLevel - _progression.XpForCurrentLevel;
                    if (span > 0) xpFrac = (float)_progression.XpIntoCurrentLevel / span;
                }
                var portrait = string.IsNullOrEmpty(_playerPortraitIconName)
                    ? null
                    : TryGetGuiTexture(_playerPortraitIconName);
                _characterPanel.Draw(_barRenderer!, _textRenderer,
                    size.X, size.Y, _heroName, _player.Actor, _progression,
                    GetPlayerAttackStats(), armor, xpFrac,
                    _iconRenderer, portrait);
                dockX += CharacterPanel.PanelWidth + panelGutter;
            }
            if (_inventoryOpen && _barRenderer is not null)
            {
                _inventoryPanel.OriginX     = dockX;
                _inventoryPanel.OriginY     = panelTopY;
                _inventoryPanel.DimBackdrop = false;
                _inventoryPanel.Gold        = _progression?.Gold ?? 0;
                var invClose = TryGetGuiTexture("b_gui_ig_mnu_minimize-up");
                var goldCoin = TryGetGuiTexture("b_gui_ig_mnu_ip_gold");
                _inventoryPanel.Draw(_barRenderer, _textRenderer, _iconRenderer,
                    size.X, size.Y, _playerInventory, TryGetItemIcon, TryGetItemGridSize,
                    invClose, goldCoin);
                dockX += InventoryPanel.PanelWidth + panelGutter;
            }
            if (_spellBookOpen && _barRenderer is not null)
            {
                _spellBookPanel.OriginX = dockX;
                _spellBookPanel.OriginY = panelTopY;
                _spellBookPanel.IsOpen  = true;
                // Phase 21-SC-SCROLL-C-1 — the 10 below-active rows are now
                // backed by PlayerSpellbook.Placed. Null entries render as
                // empty cells; the scroll-drag flow (B/C-2) populates them.
                IReadOnlyList<SiegeFX.Core.Assets.SpellTemplate?> placed =
                    _playerSpellbook?.Placed ?? System.Array.Empty<SiegeFX.Core.Assets.SpellTemplate?>();
                var spellClose = TryGetGuiTexture("b_gui_ig_mnu_minimize-book-up");
                _spellBookPanel.Draw(_barRenderer, _textRenderer,
                    size.X, size.Y, _playerSpellbook?.Primary, _playerSpellbook?.Secondary, placed,
                    _iconRenderer, spellClose, ResolveSpellInventoryIcon);
                dockX += SpellBookPanel.PanelWidth + panelGutter;
            }

            if (_player is not null)
            {
                // Phase 16d — level-up toast. Big banner near top-center while
                // _levelUpToastRemaining > 0; a yellow-on-black backdrop grabs
                // the eye even with a busy 3D scene behind. Console line stays
                // for diagnostics; this is the player-facing signal.
                if (_levelUpToastRemaining > 0f && _barRenderer is not null)
                {
                    string banner = $"** LEVEL UP!  Lv {_levelUpToastLevel} **";
                    int textW = banner.Length * 7;  // 7px per glyph approx
                    int padX = 16, padY = 6;
                    int boxW = textW + padX * 2;
                    int boxH = 24;
                    int boxX = (size.X - boxW) / 2;
                    int boxY = 60;
                    _barRenderer.DrawRect (size.X, size.Y, boxX, boxY, boxW, boxH, new Vector4(0.10f, 0.08f, 0.02f, 0.85f));
                    _barRenderer.DrawBorder(size.X, size.Y, boxX, boxY, boxW, boxH, new Vector4(1.00f, 0.85f, 0.20f, 1f));
                    _textRenderer.DrawString(size.X, size.Y, banner, boxX + padX, boxY + padY, new Vector4(1f, 0.92f, 0.40f, 1f));
                }
            }

            // Phase 21-SC-INV-A — the grid inventory was relocated into the
            // top-dock row above (alongside the character + spell book panes)
            // so the previous centered/modal draw call for it is gone here.
            // Phase 20b — quest log overlay (toggled by 'L'). Sits at the same
            // z-tier as the inventory; both can be open without conflict but
            // visually overlap if so. The pause menu still draws on top.
            if (_questLogOpen && _barRenderer is not null && _progression is not null)
            {
                QuestLogPanel.Draw(_barRenderer, _textRenderer, size.X, size.Y, _progression.Journal);
            }
            // Phase 20a: dialogue panel. Sits above the inventory but under the
            // pause menu, so pressing Esc while talking still surfaces the pause
            // overlay if the dialogue close-on-Esc somehow misses.
            if (_dialogue.IsOpen && _barRenderer is not null)
            {
                _dialogue.Draw(_barRenderer, _textRenderer, size.X, size.Y);
            }
            // Phase 20d: vendor trade overlay. Same z-tier as the dialogue —
            // they're mutually exclusive in practice (vendor only opens once
            // dialogue closes) but the explicit draw keeps both branches local.
            if (_vendor.IsOpen && _barRenderer is not null)
            {
                _vendor.Draw(_barRenderer, _textRenderer, size.X, size.Y,
                             _playerInventory, _progression?.Gold ?? 0);
            }
            // Phase 15d: pause menu (Esc). Drawn after the inventory so its
            // backdrop dims the inventory grid too — pause is the topmost UI.
            if (_pauseMenu.IsOpen && _barRenderer is not null)
            {
                _pauseMenu.Draw(_barRenderer, _textRenderer, size.X, size.Y);
            }
            // Phase 23-SC-OPTIONS-A: options dialog. Drawn after pause
            // menu so a future "open Options from pause" hookup stacks
            // visually correctly (Options on top of Pause's dim layer).
            if (_optionsMenu.IsOpen && _barRenderer is not null)
            {
                _optionsMenu.Draw(_barRenderer, _textRenderer, size.X, size.Y);
            }
            // Phase 21d-2a-viii-b: character creator. Topmost UI when open
            // (gates player spawn). Sits above pause because Esc-while-creator
            // is meaningless; the panel owns the screen until Begin/Cancel.
            // Phase 29-CD-CREATOR-FIX — skip this in-region block when the
            // FrontendScene is already in CharacterSelect (boot path); the
            // boot chrome render block already drew the full chrome + preview
            // + per-button overlays, and re-running here would paint an
            // opaque black backdrop over the pillars and chrome.
            bool inBootCreatorPath = _bootMode && _frontendScene is not null
                && _frontendScene.State == Hud.FrontendScene.ScreenState.CharacterSelect;
            if (_creator.IsOpen && _barRenderer is not null && !inBootCreatorPath)
            {
                // Phase 21d-2a-viii-FE — render the full DS1 frontend scene
                // (8 layered ASPs, cd-state PRS pose). Lazy-loaded on first
                // open (Objects.dsres handle isn't ready until LoadPlayActors
                // wires _playResolver). Falls back to the BarRenderer
                // scaffolded card if any of the meshes / clips fail to load
                // so the user is never staring at a blank screen during dev.
                EnsureFrontendScene();
                if (_frontendScene is not null)
                {
                    // Modal backdrop — DS1's frontend is fullscreen with no
                    // gameplay behind it, but our creator opens against an
                    // already-rendered region (so the play camera can come
                    // back instantly on Cancel). Fill the viewport opaque
                    // before the chrome so the world doesn't bleed through
                    // gaps in the layered ASPs (backdrop is 9-cell stained
                    // glass with transparency between cells; leftside /
                    // rightside don't reach the screen edges; etc).
                    _barRenderer.DrawRect(size.X, size.Y, 0, 0, size.X, size.Y,
                        new Vector4(0f, 0f, 0f, 1f));
                    _frontendScene.Tick((float)dt);
                    _frontendScene.Draw(size.X, size.Y);

                    // Phase 21d-2a-viii-FE-2 — live hero preview into the
                    // listener rect. Lazy-built on first cd-state frame
                    // (needs _templateStore + _playResolver + _skinShader,
                    // all populated by LoadPlayActors). Renders BEFORE the
                    // text overlay so typed name + axis labels read on top
                    // of the rotating model.
                    if (_heroPreview is null && _gl is not null && _skinShader is not null
                        && _templateStore is not null && _playResolver is not null)
                    {
                        _heroPreview = new HeroPreviewRenderer(
                            _gl, _skinShader, _templateStore, _playResolver,
                            ResolveActorTexture);
                    }
                    if (_heroPreview is not null)
                    {
                        _heroPreview.EnsurePreview(_creator.Picker);
                        _heroPreview.Tick((float)dt);
                        _heroPreview.Draw(size.X, size.Y);
                    }

                    // Phase 29-CD-CREATOR — per-button asp render for the
                    // 12 spinner widgets (gender / head / face / hair /
                    // shirt / pants × L/R) via art_mapping.gas, plus
                    // typed-name overlay via TextRenderer.
                    DrawCharacterCreatorOverlays(size.X, size.Y);
                }
            }
            // Phase 20c — quest goal marker. Picks the first active quest with
            // an unmet kill objective, finds the nearest live target actor whose
            // template name contains the goal template substring, and either
            // drops a chevron over its head (on-screen) or clamps a chevron to
            // the viewport edge pointing toward it (off-screen). Drawn before
            // floating text so combat numerics layer on top — the marker is
            // ambient, the float is feedback.
            DrawQuestGoalMarker(size.X, size.Y, vp);

            // Phase 9-SC-LL — world-space item-name labels above loot piles.
            // Drawn before the floating cast labels so a damage popup can
            // briefly stack on top of a label when both occupy the same area.
            // Gold by default, red when the mouse pointer is over the label
            // rect — matches DS1's hover behavior. Backdrop is a thin dark
            // panel for legibility against busy terrain.
            if (_frameLootLabels.Count > 0 && _barRenderer is not null)
            {
                int mx = -1, my = -1;
                if (_input is not null && _input.Mice.Count > 0)
                {
                    var mp = _input.Mice[0].Position;
                    mx = (int)mp.X;
                    my = (int)mp.Y;
                }
                // DS1 ground-loot labels use the same #AAA78E body font as the
                // top-dock panels; hover bumps to red so the cursor target still pops.
                // Phase 21-SC-INV-A2 (round 9) — bg pushed to near-opaque +
                // light-grey border so the tag reads as a proper chrome
                // panel against grass; before this round the 0.78-alpha
                // dark fill paired with the muted ink disappeared into busy
                // terrain (user reported "lost the label boxes").
                var gold      = new Vector4(0.667f, 0.655f, 0.557f, 1f);
                var red       = new Vector4(1.00f, 0.28f, 0.20f, 1f);
                var bg        = new Vector4(0.06f, 0.05f, 0.04f, 0.92f);
                var labelEdge = new Vector4(0.55f, 0.57f, 0.60f, 1f);
                foreach (var (world, label) in _frameLootLabels)
                {
                    var wp4 = new Vector4(world, 1f);
                    var clip = Vector4.Transform(wp4, vp);
                    if (clip.W <= 0.001f) continue;
                    float ndcX = clip.X / clip.W;
                    float ndcY = clip.Y / clip.W;
                    if (ndcX < -1.2f || ndcX > 1.2f || ndcY < -1.2f || ndcY > 1.2f) continue;
                    int sx = (int)((ndcX * 0.5f + 0.5f) * size.X);
                    int sy = (int)((1f - (ndcY * 0.5f + 0.5f)) * size.Y);
                    int textW = _textRenderer.MeasureWidth(label);
                    int textH = _textRenderer.Font?.Height ?? 14;
                    int padX = 4, padY = 2;
                    int boxW = textW + padX * 2;
                    int boxH = textH + padY * 2;
                    int boxX = sx - boxW / 2;
                    int boxY = sy - boxH;
                    bool hover = mx >= boxX && mx < boxX + boxW
                              && my >= boxY && my < boxY + boxH;
                    _barRenderer.DrawRect  (size.X, size.Y, boxX, boxY, boxW, boxH, bg);
                    _barRenderer.DrawBorder(size.X, size.Y, boxX, boxY, boxW, boxH, labelEdge);
                    _textRenderer.DrawString(size.X, size.Y, label,
                        boxX + padX, boxY + padY, hover ? red : gold);
                }
            }

            // Phase 17a — floating cast-feedback labels. World-anchored, so we
            // project each WorldPos through the same VP used for the 3D pass.
            // Behind the camera (clip-space w<=0) entries skip silently.
            if (_floatingTexts.Count > 0)
            {
                foreach (var ft in _floatingTexts)
                {
                    var wp = new Vector4(ft.WorldPos, 1f);
                    var clip = Vector4.Transform(wp, vp);
                    if (clip.W <= 0.001f) continue;
                    float ndcXX = clip.X / clip.W;
                    float ndcYY = clip.Y / clip.W;
                    int sx = (int)((ndcXX * 0.5f + 0.5f) * size.X);
                    int sy = (int)((1f - (ndcYY * 0.5f + 0.5f)) * size.Y);
                    // Lifecycle: rise ~12px over its life and fade alpha in
                    // the last third — float-up popup behavior.
                    float t01 = 1f - (ft.Remaining / MathF.Max(0.0001f, ft.Total));
                    sy -= (int)(t01 * 12f);
                    float a = ft.Remaining > ft.Total * 0.33f
                        ? 1f
                        : ft.Remaining / (ft.Total * 0.33f);
                    var c = ft.Color; c.W *= a;
                    int textW = ft.Text.Length * 7;
                    _textRenderer.DrawString(size.X, size.Y, ft.Text, sx - textW / 2, sy, c);
                }
            }
            // Phase 21-SC-SCROLL-PRE-1 — cursor-anchored spell-scroll overlay.
            // Drawn LAST in the HUD pass so it stacks above every panel,
            // matching the DS1 behavior where a held scroll occludes any
            // window the cursor is over. No-op when no drag is in flight
            // (the `_cursorScroll != null` gate). Uses the spell's
            // authored `inventory_icon` from the larger `_inv` icon set
            // (b_gui_ig_i_ic_sp_*_inv) for DS1-faithful art.
            //
            // Invariant: this block must run while `_textRenderer.BeginPass`
            // is still active (the inner `if (_textRenderer is not null)`
            // higher up wraps the whole HUD pass through to the EndPass on
            // the line below). IconRenderer.DrawIcon requires an active
            // blend pass; if a future refactor moves EndPass earlier this
            // block needs its own pass scope.
            if (_cursorScroll is not null && _iconRenderer is not null)
            {
                var iconTex = ResolveSpellInventoryIcon(_cursorScroll);
                if (iconTex is not null)
                {
                    const int iconSize = 32;
                    int cx = (int)_currentMousePos.X - iconSize / 2;
                    int cy = (int)_currentMousePos.Y - iconSize / 2;
                    _iconRenderer.DrawIcon(size.X, size.Y, iconTex,
                        cx, cy, iconSize, iconSize, Vector4.One);
                }
            }
            // Phase 21-SC-BARREL-A1 — DS1 sprite cursor (sword / red sword /
            // hammer / hand / talk). Drawn after every other HUD element so
            // it stacks above panels (matches DS1 layering). Suppressed when
            // a scroll is mid-drag — the scroll overlay above replaces the
            // cursor — and when RMB camera-look has the OS cursor in Raw
            // mode. UpdateCursorState ran once per frame just below the
            // scene draw; ResolveCursorVisual maps the state to a texture
            // and authored hotspot.
            else if (_iconRenderer is not null && !_mouseLookActive && _player is not null)
            {
                var (cTex, hsx, hsy, sz) = ResolveCursorVisual();
                if (cTex is not null)
                {
                    int cx = (int)_currentMousePos.X - hsx;
                    int cy = (int)_currentMousePos.Y - hsy;
                    _iconRenderer.DrawIcon(size.X, size.Y, cTex, cx, cy, sz, sz, Vector4.One);
                }
            }
            _textRenderer.EndPass();
        }
    }

    /// <summary>Phase 21-SC-SCROLL-PRE-2 — start a scroll drag from the
    /// given source. Sets <see cref="_cursorScroll"/> so the next render
    /// frame draws the scroll on the cursor; the actual source-slot
    /// clearing happens in the source-side handler (so a cancel can
    /// restore it).</summary>
    private void BeginScrollDrag(SiegeFX.Core.Assets.SpellTemplate spell,
                                 CursorScrollSource source, int sourceIndex)
    {
        _cursorScroll = spell;
        _cursorScrollSource = source;
        _cursorScrollSourceIndex = sourceIndex;
    }

    /// <summary>Phase 21-SC-SCROLL-PRE-2 — release the drag without dropping.
    /// Called by drop-handlers after they've successfully placed the
    /// scroll at its destination. <see cref="CancelScrollDrag"/> is the
    /// inverse for ESC/RMB cases that need to restore the source.</summary>
    private void ClearScrollDrag()
    {
        _cursorScroll = null;
        _cursorScrollSource = CursorScrollSource.None;
        _cursorScrollSourceIndex = 0;
    }

    /// <summary>Phase 21-SC-SCROLL-B-2 — cancel an in-flight scroll drag
    /// and put the spell back where it came from. ESC handler, RMB
    /// handler, and any state change (pause, save, load, region change)
    /// that interrupts the drag should call this.</summary>
    private void CancelScrollDrag()
    {
        if (_cursorScroll is null) { ClearScrollDrag(); return; }
        var spell = _cursorScroll;
        RestoreToSource(spell);
        Console.WriteLine($"  scroll drag: cancel — restored {spell.Name} to {_cursorScrollSource}[{_cursorScrollSourceIndex}]");
        ClearScrollDrag();
    }

    /// <summary>Phase 21-SC-SCROLL-C-2 — put a spell back into the slot
    /// recorded as the current drag's source. Used by both cancel (the
    /// drag itself ends) and swap (the displaced spell lands at the
    /// source). Caller is responsible for calling ClearScrollDrag if
    /// the drag itself ends.</summary>
    private void RestoreToSource(SiegeFX.Core.Assets.SpellTemplate spell)
    {
        switch (_cursorScrollSource)
        {
            case CursorScrollSource.SpellbookActive1:
                _playerSpellbook?.Slot(SiegeFX.Core.Actors.SpellSlot.Primary, spell);
                break;
            case CursorScrollSource.SpellbookActive2:
                _playerSpellbook?.Slot(SiegeFX.Core.Actors.SpellSlot.Secondary, spell);
                break;
            case CursorScrollSource.SpellbookPlaced:
                _playerSpellbook?.SetPlaced(_cursorScrollSourceIndex, spell);
                break;
            case CursorScrollSource.Inventory:
                // Phase 21-SC-SCROLL-E-2 — cancel of an inventory pickup
                // re-inserts the scroll back into the inventory list.
                // Index may not match exactly (other items may have shifted)
                // but a clamped-end Insert keeps the spell in the player's
                // bag, which is what the user expects from a cancel.
                int restoreIdx = Math.Clamp(_cursorScrollSourceIndex, 0, _playerInventory.Count);
                _playerInventory.Insert(restoreIdx,
                    new SiegeFX.Core.Actors.LootEntry(Slot: "", Reference: spell.Name));
                break;
        }
    }

    /// <summary>Phase 21-SC-SCROLL-C-2 — read the spell currently in a
    /// spellbook slot (Active1, Active2, or Placed[index]).</summary>
    private SiegeFX.Core.Assets.SpellTemplate? ReadSpellbookSlot(
        Hud.SpellBookPanel.SlotKind kind, int index)
    {
        if (_playerSpellbook is null) return null;
        return kind switch
        {
            Hud.SpellBookPanel.SlotKind.Active1 => _playerSpellbook.Primary,
            Hud.SpellBookPanel.SlotKind.Active2 => _playerSpellbook.Secondary,
            Hud.SpellBookPanel.SlotKind.Placed  => _playerSpellbook.Placed[index],
            _ => null,
        };
    }

    /// <summary>Phase 21-SC-SCROLL-C-2 — write a spell into a spellbook
    /// slot, replacing whatever was there. Pass null to clear. Active
    /// slots preserve their cooldown (resetCooldown:false) so a
    /// drag-and-redrop can't reset a mid-cooldown spell to 0s.</summary>
    private void WriteSpellbookSlot(
        Hud.SpellBookPanel.SlotKind kind, int index,
        SiegeFX.Core.Assets.SpellTemplate? spell)
    {
        if (_playerSpellbook is null) return;
        switch (kind)
        {
            case Hud.SpellBookPanel.SlotKind.Active1:
                _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Primary,   spell, resetCooldown: false);
                break;
            case Hud.SpellBookPanel.SlotKind.Active2:
                _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Secondary, spell, resetCooldown: false);
                break;
            case Hud.SpellBookPanel.SlotKind.Placed:
                _playerSpellbook.SetPlaced(index, spell);
                break;
        }
    }

    /// <summary>Phase 21-SC-SCROLL-C-2 — clear by SlotKind (used at
    /// pickup time to leave the source empty).</summary>
    private void ClearSpellbookSlot(Hud.SpellBookPanel.SlotKind kind, int index)
        => WriteSpellbookSlot(kind, index, null);

    /// <summary>Phase 21-SC-SCROLL-F-1 — toss the cursor-held scroll onto
    /// the ground in front of the player. Reuses the Phase 9-SC-9 throw
    /// arc + Y-spin and adds the SC-SCROLL-F X-axis tumble so the scroll
    /// reads as twisting end-over-end mid-flight. Drag ends; auto-pickup
    /// of the resulting pile is gated until landing (existing 9-SC-9
    /// behavior). The pile contains a single LootEntry whose Reference
    /// is the spell's template name; F-2 routes the pickup through
    /// AutoPickupScrollToSpellbook.</summary>
    private void DropScrollToWorld(SiegeFX.Core.Assets.SpellTemplate spell)
    {
        if (_player is null) { ClearScrollDrag(); return; }
        var origin = _player.CurrentTransform.Translation;
        // Throw lands ~1.5u in front of the player, matching DS1's visible
        // drop distance. Falls back to +X if facing isn't set yet.
        var facing = _playerFacing.LengthSquared() > 0.01f
            ? _playerFacing : new Vector3(1f, 0f, 0f);
        // Phase 21-SC-SCROLL-CLICKLOOT — short throw arc; scroll lands
        // near the player so click-to-loot is fast. Was 1.5f.
        var target = origin + facing * 0.7f;
        var entry  = new SiegeFX.Core.Actors.LootEntry(Slot: "", Reference: spell.Name);
        var pile   = new LootPile(origin, new List<SiegeFX.Core.Actors.LootEntry> { entry })
        {
            Throw = new LootThrow
            {
                Source         = origin,
                Target         = target,
                Duration       = 0.55f,
                Elapsed        = 0f,
                ArcHeight      = 0.45f,
                Spins          = 0.5f,                                   // half a yaw spin
                XSpins         = 1.0f,                                   // SC-SCROLL-F: one end-over-end
                StartRotation  = MathF.Atan2(facing.X, facing.Z),
                StartRotationX = 0f,                                     // start flat, tumble forward
            },
        };
        _lootPiles.Add(pile);
        _audio?.Play(SfxGuiPutDownScroll);
        Console.WriteLine($"  scroll drag: world drop {spell.Name} -> pile at " +
                          $"({target.X:F1},{target.Z:F1}) with throw-tumble");
        ClearScrollDrag();
    }

    /// <summary>Phase 21-SC-SCROLL-F-2 — when the player walks over a
    /// loot pile that contains a spell-scroll item, route it to the
    /// spellbook (first empty Placed[] slot) instead of the flat
    /// inventory. Falls back to inventory when Placed is full so the
    /// scroll isn't lost. Returns true if any scroll items in the
    /// pile were handled (caller should still process non-scroll items
    /// the normal way). Active1/Active2 are NEVER auto-filled — those
    /// stay player-controlled via drag-drop.</summary>
    private bool TryAutoPickupScrollsFromPile(LootPile pile)
    {
        if (_playerSpellbook is null || _spellCatalog is null) return false;
        bool anyHandled = false;
        for (int i = pile.Items.Count - 1; i >= 0; i--)
        {
            var entry = pile.Items[i];
            if (!string.IsNullOrEmpty(entry.Slot)) continue;     // equipped item, not a scroll
            var spell = ResolveSlottableSpell(entry.Reference, debugSpellsEnv: null);
            if (spell is null) continue;                          // not a spell template
            // First empty Placed slot wins.
            int dest = -1;
            for (int p = 0; p < _playerSpellbook.PlacedCount; p++)
                if (_playerSpellbook.Placed[p] is null) { dest = p; break; }
            if (dest >= 0)
            {
                _playerSpellbook.SetPlaced(dest, spell);
                Console.WriteLine($"  scroll pickup: {spell.Name} -> spellbook Placed[{dest}]");
            }
            else
            {
                // Spellbook full — keep as inventory item so it isn't lost.
                _playerInventory.Add(entry);
                _inventoryPanel.NotifyItemAdded();
                Console.WriteLine($"  scroll pickup: {spell.Name} -> inventory (spellbook Placed full)");
            }
            pile.Items.RemoveAt(i);
            anyHandled = true;
        }
        return anyHandled;
    }

    /// <summary>Overload that takes the cursor-source enum so a
    /// successful drop-into-empty-slot can clear the original source.</summary>
    private void ClearSpellbookSlot(CursorScrollSource source, int index)
    {
        if (_playerSpellbook is null) return;
        switch (source)
        {
            case CursorScrollSource.SpellbookActive1:
                _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Primary,   null, resetCooldown: false);
                break;
            case CursorScrollSource.SpellbookActive2:
                _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Secondary, null, resetCooldown: false);
                break;
            case CursorScrollSource.SpellbookPlaced:
                _playerSpellbook.SetPlaced(index, null);
                break;
        }
    }

    /// <summary>Draws a single HP/MP-style bar at (<paramref name="x"/>,<paramref name="y"/>):
    /// dark background, fill scaled to current/max, 1-pixel border, and a label like
    /// "HP 38/50" laid alongside via <see cref="TextRenderer"/>.</summary>
    private void DrawHudBar(int viewportW, int viewportH, int x, int y, int w, int h,
                            float current, float max, Vector4 fillColor, string label)
    {
        if (_barRenderer is null || _textRenderer is null) return;
        if (max <= 0f) return;
        float pct = Math.Clamp(current / max, 0f, 1f);
        int fillW = (int)MathF.Round(pct * (w - 2));
        var bg     = new Vector4(0.10f, 0.10f, 0.10f, 0.82f);
        var border = new Vector4(0.85f, 0.85f, 0.85f, 0.85f);
        var text   = new Vector4(1f, 1f, 1f, 1f);

        _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, bg);
        if (fillW > 0)
            _barRenderer.DrawRect(viewportW, viewportH, x + 1, y + 1, fillW, h - 2, fillColor);
        _barRenderer.DrawBorder(viewportW, viewportH, x, y, w, h, border);

        var caption = $"{label} {(int)MathF.Round(current)}/{(int)MathF.Round(max)}";
        _textRenderer.DrawString(viewportW, viewportH, caption, x + w + 8, y - 1, text);
    }

    /// <summary>Phase 21-SC-INV-A — small always-on HUD shown in the top-left
    /// of the screen regardless of panel state. Two thin vertical vials (HP /
    /// MP) flank a portrait stub; the two active-spell slot icons sit to the
    /// right with a tiny LV/XP/Gold strip below. Stays visible even when the
    /// character pane is closed so the player never loses sight of their
    /// pools (the user calls these "the small ones that appear all the
    /// time"). Heavier numeric readouts live inside CharacterPanel.</summary>
    private void DrawMiniStatusHud(int vw, int vh)
    {
        if (_barRenderer is null || _textRenderer is null || _player is null) return;

        var combat = _player.Actor.Combat;
        var stats  = _player.Actor.Stats;
        var hpFill = new Vector4(0.78f, 0.10f, 0.10f, 1f);
        var mpFill = new Vector4(0.18f, 0.40f, 0.90f, 1f);
        var slotBg = new Vector4(0.04f, 0.04f, 0.05f, 0.92f);
        var slotEm = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk = new Vector4(0.65f, 0.62f, 0.55f, 1f);
        var gold   = new Vector4(1.00f, 0.85f, 0.40f, 1f);

        const int x0 = 4, y0 = 4;
        // Phase 21-SC-INV-A2 (round 7) — small vials doubled vertically so
        // the always-on HUD reads at glance instead of pinching down to a
        // bookmark strip; portrait stays its 32×32 between them and pins
        // to the top so the vial extension grows downward only.
        const int vialW = 10, vialH = 64;
        const int portraitW = 32, portraitH = 32;
        const int gap = 2;
        // Phase 21-SC-INV-A2 (round 6) — cells widened (+4) and lengthened
        // (+10) so a vertical XP bar can rise behind the icon without
        // crowding the glyph. The icon stays square at the top; the lower
        // strip carries the per-skill progress fill.
        // Phase 21-SC-INV-A2 (round 9) — cells doubled vertically (28→56)
        // so the AWP icon centers comfortably inside a tall slot and the
        // XP fill behind it has visible vertical real-estate. Width stays
        // the same so the bar still tucks into the upper-left corner.
        const int abilitySlotW = 22;
        const int abilitySlotH = 56;

        int xHp = x0;
        int xPortrait = xHp + vialW + gap;
        int xMp = xPortrait + portraitW + gap;
        int xA0 = xMp + vialW + gap + 4;
        int xA1 = xA0 + abilitySlotW + gap;
        int xA2 = xA1 + abilitySlotW + gap;
        int xA3 = xA2 + abilitySlotW + gap;

        // HP vial (vertical fill from bottom up, horizontal gradient).
        _barRenderer.DrawRect(vw, vh, xHp, y0, vialW, vialH, slotBg);
        if (stats.MaxLife > 0f)
        {
            int fh = (int)(vialH * MathF.Max(0f, MathF.Min(1f, combat.CurrentLife / stats.MaxLife)));
            if (fh > 0) _barRenderer.DrawHGradientFill(vw, vh, xHp, y0 + (vialH - fh), vialW, fh, hpFill);
        }
        _barRenderer.DrawBorder(vw, vh, xHp, y0, vialW, vialH, slotEm);

        // Portrait — DS1 ships the hero head between the two small vials,
        // visible whenever the mini-HUD is up. Sourced from the player
        // template's [actor]portrait_icon (e.g. b_gui_ig_i_ic_c_fb_01).
        // Falls back to the hero-name initial glyph when the texture hasn't
        // been resolved (creator dev path / pre-load).
        _barRenderer.DrawRect  (vw, vh, xPortrait, y0, portraitW, portraitH, slotBg);
        var portraitTex = string.IsNullOrEmpty(_playerPortraitIconName)
            ? null
            : TryGetGuiTexture(_playerPortraitIconName);
        if (portraitTex is not null && _iconRenderer is not null)
        {
            // Centered inside the box: portrait_icon raws are square so a
            // straight stretch keeps the head in frame.
            _iconRenderer.DrawIcon(vw, vh, portraitTex,
                xPortrait, y0, portraitW, portraitH, new Vector4(1f, 1f, 1f, 1f));
        }
        else if (!string.IsNullOrEmpty(_heroName))
        {
            string ch = _heroName.Substring(0, 1).ToUpperInvariant();
            int chW = _textRenderer.MeasureWidth(ch);
            _textRenderer.DrawString(vw, vh, ch,
                xPortrait + (portraitW - chW) / 2, y0 + (portraitH - 8) / 2, ink);
        }
        _barRenderer.DrawBorder(vw, vh, xPortrait, y0, portraitW, portraitH, slotEm);

        // MP vial.
        _barRenderer.DrawRect(vw, vh, xMp, y0, vialW, vialH, slotBg);
        if (stats.MaxMana > 0f)
        {
            int fh = (int)(vialH * MathF.Max(0f, MathF.Min(1f, combat.CurrentMana / stats.MaxMana)));
            if (fh > 0) _barRenderer.DrawHGradientFill(vw, vh, xMp, y0 + (vialH - fh), vialW, fh, mpFill);
        }
        _barRenderer.DrawBorder(vw, vh, xMp, y0, vialW, vialH, slotEm);

        // Phase 21-SC-INV-A2 — DS1 ability bar. Four selectable cells —
        // melee, ranged, spell-1 (Q), spell-2 (W) — sit immediately right of
        // the MP vial. Click any cell to mark it the active combat ability.
        // The selected cell gets a thicker golden ring; the rest are dim.
        // (Live cast/swing keys still fire from LMB/Q/W; this is the
        // "what's primed" affordance the player sees on the HUD.)
        // Phase 21-SC-INV-A2 (round 5) — pull dedicated AWP HUD icons. Melee
        // tracks the equipped weapon's [gui]inventory_icon (so picking up a
        // sword swaps the cell art), falling back to the unarmed-fist glyph;
        // ranged is always the bow icon; Q/W use each spell's own inventory
        // icon — same texture the spell book panel uses.
        GlTexture? meleeIcon = null;
        if (_playerEquipment.TryGetValue("es_weapon_hand", out var weaponRef)
            && !string.IsNullOrEmpty(weaponRef))
        {
            meleeIcon = TryGetItemIcon(weaponRef);
        }
        meleeIcon ??= TryGetGuiTexture("b_gui_ig_mnu_combat");
        var rangedIcon = TryGetGuiTexture("b_gui_ig_mnu_ranged");
        var primaryIcon   = _playerSpellbook?.Primary   is { } sp ? ResolveSpellInventoryIcon(sp) : null;
        var secondaryIcon = _playerSpellbook?.Secondary is { } ss ? ResolveSpellInventoryIcon(ss) : null;

        // Phase 21-SC-INV-A2 (round 6) — per-skill progress fractions for the
        // bar that rises behind each cell's icon. Q/W route through the spell's
        // element to pick CombatMagic vs NatureMagic so a mage's two slots can
        // show independent progress; null spell → 0 fill.
        float meleeFrac   = _progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Melee) ?? 0f;
        float rangedFrac  = _progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Ranged) ?? 0f;
        float primaryFrac   = _playerSpellbook?.Primary   is { } sp2
            ? _progression?.SkillProgressFraction(SkillForSpell(sp2)) ?? 0f : 0f;
        float secondaryFrac = _playerSpellbook?.Secondary is { } ss2
            ? _progression?.SkillProgressFraction(SkillForSpell(ss2)) ?? 0f : 0f;

        DrawMiniAbilitySlot(vw, vh, xA0, y0, abilitySlotW, abilitySlotH, "M", null,                          0, ink, dimInk, slotBg, slotEm, meleeIcon,     meleeFrac);
        DrawMiniAbilitySlot(vw, vh, xA1, y0, abilitySlotW, abilitySlotH, "R", null,                          1, ink, dimInk, slotBg, slotEm, rangedIcon,    rangedFrac);
        DrawMiniAbilitySlot(vw, vh, xA2, y0, abilitySlotW, abilitySlotH, null, _playerSpellbook?.Primary,    2, ink, dimInk, slotBg, slotEm, primaryIcon,   primaryFrac);
        DrawMiniAbilitySlot(vw, vh, xA3, y0, abilitySlotW, abilitySlotH, null, _playerSpellbook?.Secondary,  3, ink, dimInk, slotBg, slotEm, secondaryIcon, secondaryFrac);
        _abilitySlotRects[0] = (xA0, y0, abilitySlotW, abilitySlotH);
        _abilitySlotRects[1] = (xA1, y0, abilitySlotW, abilitySlotH);
        _abilitySlotRects[2] = (xA2, y0, abilitySlotW, abilitySlotH);
        _abilitySlotRects[3] = (xA3, y0, abilitySlotW, abilitySlotH);
        // Right edge of the always-on HUD — published for the panel-dock
        // layout so it can plant the three large panels immediately to the
        // right of the ability bar instead of stacking under everything.
        _miniHudRightX = xA3 + abilitySlotW + 4;

        // Phase 21-SC-INV-A2 — open-panels strip below the ability bar. A
        // thin tan-on-dark rectangle with three rightward golden chevrons;
        // clicking it opens (or closes) all three large panels at once so
        // the player has a visible mouse-only path to the panes — DS1's UI
        // exposes the same one-click "open all" affordance.
        int arrowsX = xA0;
        int arrowsY = y0 + abilitySlotH + 2;
        int arrowsW = (xA3 + abilitySlotW) - xA0;
        int arrowsH = 9;
        _openArrowsRect = (arrowsX, arrowsY, arrowsW, arrowsH);
        _barRenderer.DrawRect  (vw, vh, arrowsX, arrowsY, arrowsW, arrowsH, slotBg);
        _barRenderer.DrawBorder(vw, vh, arrowsX, arrowsY, arrowsW, arrowsH, slotEm);
        // Chevron direction tracks panel state: panels closed → arrows point
        // LEFT (the click target sits on the left edge of the screen, so the
        // arrow reads as "click here to summon"); panels open → arrows flip
        // right and the click closes them again.
        bool panelsOpen = _charPanelOpen || _inventoryOpen || _spellBookOpen;
        DrawTinyChevrons(vw, vh, arrowsX, arrowsY, arrowsW, arrowsH, gold, panelsOpen);

        // Phase 21-SC-INV-A2 (round 9) — DS1 doesn't show a Lv/XP/Gold
        // strip on the always-on HUD. Level rolls up via the per-skill
        // progress fills inside the ability cells; gold lives in the
        // inventory pane's title bar. Mini-HUD ends here.
    }

    /// <summary>Phase 21-SC-INV-A2 — single ability cell of the mini-HUD's
    /// 4-cell bar. <paramref name="glyph"/> is the static glyph for melee/
    /// ranged ('M' / 'R'); <paramref name="spell"/> is the live spell when
    /// the cell is a slot for Primary/Secondary (renders the spell's first
    /// letter). The selected cell (matches <see cref="_activeAbilityIdx"/>)
    /// gets the bright border + ink; the rest dim out.</summary>
    private void DrawMiniAbilitySlot(int vw, int vh, int x, int y, int w, int h,
                                     string? glyph,
                                     SiegeFX.Core.Assets.SpellTemplate? spell,
                                     int index,
                                     Vector4 ink, Vector4 dimInk,
                                     Vector4 slotBg, Vector4 slotEm,
                                     GlTexture? icon = null,
                                     float progressFrac = 0f)
    {
        if (_barRenderer is null || _textRenderer is null) return;
        bool selected = _activeAbilityIdx == index;
        var border = selected ? new Vector4(1.00f, 0.85f, 0.40f, 1f) : slotEm;
        var fg     = selected ? ink : dimInk;
        _barRenderer.DrawRect(vw, vh, x, y, w, h, slotBg);
        // Phase 21-SC-INV-A2 (round 6) — XP-into-current-rank fill rises from
        // the bottom of the cell behind the icon. Drawn before the icon so the
        // glyph stays legible; quiet bronze tone so the bar reads as
        // "background" rather than competing with the action art.
        if (progressFrac > 0f)
        {
            float clamped = MathF.Min(1f, progressFrac);
            int fh = (int)((h - 2) * clamped);
            if (fh > 0)
            {
                var fill = new Vector4(0.55f, 0.42f, 0.18f, 0.55f);
                _barRenderer.DrawRect(vw, vh, x + 1, y + h - 1 - fh, w - 2, fh, fill);
            }
        }
        _barRenderer.DrawBorder(vw, vh, x, y, w, h, border);
        if (selected)
        {
            // Inner ring so selection reads even at 100% scale on a dark scene.
            _barRenderer.DrawBorder(vw, vh, x + 1, y + 1, w - 2, h - 2, border);
        }
        // Phase 21-SC-INV-A2 (round 9) — prefer the AWP/spell/inventory icon
        // when the resolver gave us one. With the cell now doubled vertically
        // the icon centers on both axes; the XP fill rises behind it from the
        // bottom so both reads stay clear without one crowding the other.
        int iconSz = Math.Min(w - 4, 22);
        int iconX = x + (w - iconSz) / 2;
        int iconY = y + (h - iconSz) / 2;
        if (icon is not null && _iconRenderer is not null)
        {
            var tint = selected ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.85f, 0.85f, 0.85f, 1f);
            _iconRenderer.DrawIcon(vw, vh, icon, iconX, iconY, iconSz, iconSz, tint);
            return;
        }
        string? ch = glyph;
        if (ch is null && spell is not null)
        {
            var label = string.IsNullOrEmpty(spell.ScreenName) ? spell.Name : spell.ScreenName;
            if (!string.IsNullOrEmpty(label)) ch = label.Substring(0, 1).ToUpperInvariant();
        }
        if (string.IsNullOrEmpty(ch)) return;
        int chW = _textRenderer.MeasureWidth(ch);
        _textRenderer.DrawString(vw, vh, ch, x + (w - chW) / 2, iconY + (iconSz - 8) / 2, fg);
    }

    /// <summary>Phase 21-SC-INV-A2 (round 6) — map a spell to the per-skill
    /// XP pool that ranks it. DS1 splits offensive (combat) from supportive
    /// (nature) by keyword; we mirror that split through the
    /// <see cref="SiegeFX.Core.Assets.SpellElement"/> bucket the template
    /// already carries: Fire/Lightning/Death belong to Combat Magic, Ice/
    /// Acid/Holy belong to Nature Magic. Generic falls to Combat as a
    /// reasonable default for unrecognized templates.</summary>
    private static SiegeFX.Core.Assets.SkillKind SkillForSpell(SiegeFX.Core.Assets.SpellTemplate spell)
    {
        return spell.Element switch
        {
            SiegeFX.Core.Assets.SpellElement.Ice       => SiegeFX.Core.Assets.SkillKind.NatureMagic,
            SiegeFX.Core.Assets.SpellElement.Acid      => SiegeFX.Core.Assets.SkillKind.NatureMagic,
            SiegeFX.Core.Assets.SpellElement.Holy      => SiegeFX.Core.Assets.SkillKind.NatureMagic,
            _                                          => SiegeFX.Core.Assets.SkillKind.CombatMagic,
        };
    }

    /// <summary>Phase 21-SC-INV-A2 — three chevrons inside the open-panels
    /// strip. Drawn as five 1px stair-step rects per chevron (the BarRenderer
    /// is rect-only). <paramref name="pointRight"/> flips the stair-step so
    /// the same call site can show "click to open" (left) when panels are
    /// closed and "click to close" (right) when they're open. Phase 21-SC-INV-B
    /// (round 4): per-pixel brightness graduates from the chevron tip (full
    /// color) to the trailing edges (dimmed) so the arrows read as 3D.</summary>
    private void DrawTinyChevrons(int vw, int vh, int x, int y, int w, int h, Vector4 col,
                                  bool pointRight)
    {
        if (_barRenderer is null) return;
        int cw = 5, gap = 6;
        int totalW = cw * 3 + gap * 2;
        int x0 = x + (w - totalW) / 2;
        int cy = y + h / 2;
        // Tip column = full color; middle column = 78%; tail column = 55%.
        Vector4 Bright(float k) => new(col.X * k, col.Y * k, col.Z * k, col.W);
        var tip   = Bright(1.00f);
        var mid   = Bright(0.78f);
        var tail  = Bright(0.55f);
        for (int i = 0; i < 3; i++)
        {
            int cx = x0 + i * (cw + gap);
            if (pointRight)
            {
                _barRenderer.DrawRect(vw, vh, cx + 0, cy - 2, 1, 1, tail);
                _barRenderer.DrawRect(vw, vh, cx + 1, cy - 1, 1, 1, mid);
                _barRenderer.DrawRect(vw, vh, cx + 2, cy + 0, 1, 1, tip);
                _barRenderer.DrawRect(vw, vh, cx + 1, cy + 1, 1, 1, mid);
                _barRenderer.DrawRect(vw, vh, cx + 0, cy + 2, 1, 1, tail);
            }
            else
            {
                _barRenderer.DrawRect(vw, vh, cx + 2, cy - 2, 1, 1, tail);
                _barRenderer.DrawRect(vw, vh, cx + 1, cy - 1, 1, 1, mid);
                _barRenderer.DrawRect(vw, vh, cx + 0, cy + 0, 1, 1, tip);
                _barRenderer.DrawRect(vw, vh, cx + 1, cy + 1, 1, 1, mid);
                _barRenderer.DrawRect(vw, vh, cx + 2, cy + 2, 1, 1, tail);
            }
        }
    }

    private void OnResize(Vector2D<int> size) => _gl?.Viewport(size);

    private bool _glDisposed;

    private void OnClosing()
    {
        // GL context is still current here. Release every GL handle we own
        // before Silk.NET tears the window down — the outer Dispose() runs
        // post-context and would crash on DeleteTexture/DeleteBuffer.
        if (_glDisposed) return;
        _glDisposed = true;

        foreach (var tex in _snoTextures.Values) tex.Dispose();
        _snoTextures.Clear();
        foreach (var mesh in _regionMeshes.Values) mesh.Dispose();
        _regionMeshes.Clear();
        _regionInstances.Clear();
        _resolvedTexNameCache.Clear();
        foreach (var mesh in _actorMeshCache.Values) mesh.Dispose();
        _actorMeshCache.Clear();
        _actorIdentityBones.Clear();
        _actors.Clear();
        // Phase 21c — release prop GL resources. Texture cache is shared with the
        // actor draw path so it covers both populations in one sweep.
        foreach (var mesh in _propGlMeshCache.Values) mesh.Dispose();
        _propGlMeshCache.Clear();
        _propAspCache.Clear();
        _staticProps.Clear();
        foreach (var tex in _aspTextureCache.Values) tex?.Dispose();
        _aspTextureCache.Clear();
        // Phase 24-MAINMENU step 1+2-FOLD — splash texture cache. Six
        // GlTextures across both splash sets; trivial leak in raw bytes
        // but the OnClosing invariant is "every cache disposed before
        // the GL context dies."
        foreach (var tex in _splashTexCache.Values) tex?.Dispose();
        _splashTexCache.Clear();
        // Phase 21d-2a-ii — multiple slot keys may resolve to the same GlTexture
        // when a template overrides slot 0 to the same name shipped in slot 1
        // (or via texset suffix probing). Dispose unique instances only.
        var disposedActorTex = new HashSet<GlTexture>(ReferenceEqualityComparer.Instance);
        foreach (var tex in _actorTextureCache.Values)
            if (tex is not null && disposedActorTex.Add(tex)) tex.Dispose();
        _actorTextureCache.Clear();
        // Phase 21d-2a-vii — layered equipment GL resources.
        foreach (var l in _equippedLayers) l.Mesh.Dispose();
        _equippedLayers.Clear();
        var disposedEquipTex = new HashSet<GlTexture>(ReferenceEqualityComparer.Instance);
        foreach (var tex in _equipTexCache.Values)
            if (tex is not null && disposedEquipTex.Add(tex)) tex.Dispose();
        _equipTexCache.Clear();
        // Phase 9-SC-10 — bone-attached props (shields, future quivers/torches).
        // Each item owns its mesh + texture (no shared cache yet) so a flat
        // sweep is safe.
        foreach (var a in _attachedItems)
        {
            a.Mesh.Dispose();
            a.Texture?.Dispose();
        }
        _attachedItems.Clear();
        _animTexture?.Dispose();
        _skinnedMesh?.Dispose();
        _texture?.Dispose();
        _sno?.Dispose();
        _mesh?.Dispose();
        _lootCube?.Dispose();
        _grid?.Dispose();
        // Phase 21-SC-BARREL-FOLD — release sprite cursor textures and
        // restore the OS pointer. Without restoring CursorMode here, an
        // abnormal shutdown leaves the OS cursor permanently hidden in
        // any reentrant tooling that re-uses the IInputContext.
        _cursorPointer?.Dispose();
        _cursorAttack?.Dispose();
        _cursorTalk?.Dispose();
        if (_cursorSmash is not null) foreach (var t in _cursorSmash) t.Dispose();
        if (_cursorGrab  is not null) foreach (var t in _cursorGrab)  t.Dispose();
        if (_osCursorHidden && _input is not null && _input.Mice.Count > 0)
        {
            try { _input.Mice[0].Cursor.CursorMode = CursorMode.Normal; }
            catch { /* input layer may already be down */ }
        }
        // Phase 21-SC-BARREL-FOLD — frag debris held references into
        // _propGlMeshCache, which the loop above just disposed. Clear the
        // dangling refs here so any stray draw call after teardown is a
        // no-op rather than a use-after-dispose on a freed GL handle.
        _fragDebris.Clear();
        _fragAssets.Clear();
        _textRenderer?.Dispose();
        _barRenderer?.Dispose();
        _iconRenderer?.Dispose();
        foreach (var t in _itemIconCache.Values) t?.Dispose();
        _itemIconCache.Clear();
        _heroPreview?.Dispose();
        _heroPreview = null;
        _frontendScene?.Dispose();
        _skinShader?.Dispose();
        _meshShader?.Dispose();
        _gridShader?.Dispose();
    }

    // Phase 19b — capture every piece of mid-session state worth restoring
    // and bake it into a SaveFile. Everything Apply re-derives from the GAS
    // (mesh, animation, skrit, region layout) is left out; only the things
    // that drift during play (life/mana/dead/positions/inventory/spellbook
    // cooldowns/camera/XP) live in the save. RegionPath stamps the snapshot
    // so the load path can refuse a save written for a different region.
    internal SiegeFX.Core.Save.SaveFile CaptureSave()
    {
        var save = new SiegeFX.Core.Save.SaveFile
        {
            SchemaVersion = SiegeFX.Core.Save.SaveFile.CurrentSchemaVersion,
            SavedAt       = DateTime.UtcNow,
            RegionPath    = _regionPath ?? "",
        };

        foreach (var s in _actors)
        {
            // CurrentTransform tracks the live position (brain / follower
            // updates it each tick); WorldTransform is the spawn pose and
            // would mis-restore an actor that wandered.
            var pos = s.CurrentTransform.Translation;
            save.Actors.Add(new SiegeFX.Core.Save.ActorSnapshot
            {
                Scid         = s.Actor.Instance.Scid,
                TemplateName = s.Actor.Template.Name,
                Position     = SiegeFX.Core.Save.Vec3.From(pos),
                CurrentLife  = s.Actor.Combat.CurrentLife,
                CurrentMana  = s.Actor.Combat.CurrentMana,
                IsDead       = s.IsDead || s.Actor.Combat.IsDead,
            });
        }

        foreach (var pile in _lootPiles)
        {
            var pileSnap = new SiegeFX.Core.Save.LootPileSnapshot
            {
                Position = SiegeFX.Core.Save.Vec3.From(pile.Position),
            };
            foreach (var it in pile.Items)
                pileSnap.Entries.Add(new SiegeFX.Core.Save.LootEntrySnapshot
                {
                    Slot      = it.Slot,
                    Reference = it.Reference,
                });
            save.LootPiles.Add(pileSnap);
        }

        if (_player is not null)
        {
            var p = new SiegeFX.Core.Save.PlayerSnapshot
            {
                Scid          = _player.Actor.Instance.Scid,
                TotalXp       = _progression?.TotalXp ?? 0,
                Level         = _progression?.Level ?? 1,
                Strength      = _player.Actor.Stats.Strength,
                Dexterity     = _player.Actor.Stats.Dexterity,
                Intelligence  = _player.Actor.Stats.Intelligence,
                Facing        = SiegeFX.Core.Save.Vec3.From(_playerFacing),
                CameraMode    = (int)_cameraMode,
                ChaseYaw      = _chaseYaw,
                ChaseDistance = _chaseDistance,
                ChaseHeight   = _chaseHeight,
                CameraPos     = SiegeFX.Core.Save.Vec3.From(_camera.Position),
                CameraYaw     = _camera.Yaw,
                CameraPitch   = _camera.Pitch,
            };
            // Inventory + equipment fold into the same flat list the loot
            // path uses. On load, equipped items get re-wired by walking
            // entries with es_-style slots; non-equipped items stay as
            // inventory rows. Equipment lives in _playerEquipment as
            // (es_slot -> ref); convert each to an entry whose Slot drops
            // the es_ prefix so the round-trip matches LootEntry shape.
            foreach (var item in _playerInventory)
                p.Inventory.Add(new SiegeFX.Core.Save.LootEntrySnapshot
                {
                    Slot      = item.Slot,
                    Reference = item.Reference,
                });
            foreach (var kv in _playerEquipment)
            {
                var slot = kv.Key.StartsWith("es_", StringComparison.OrdinalIgnoreCase)
                    ? kv.Key.Substring(3) : kv.Key;
                p.Inventory.Add(new SiegeFX.Core.Save.LootEntrySnapshot
                {
                    Slot      = "equipped:" + slot,
                    Reference = kv.Value,
                });
            }

            if (_playerSpellbook is not null)
            {
                var snapshot = new SiegeFX.Core.Save.SpellbookSnapshot
                {
                    PrimarySpell      = _playerSpellbook.Primary?.Name,
                    SecondarySpell    = _playerSpellbook.Secondary?.Name,
                    PrimaryCooldown   = _playerSpellbook.PrimaryCooldownRemaining,
                    SecondaryCooldown = _playerSpellbook.SecondaryCooldownRemaining,
                };
                // Phase 21-SC-SCROLL-G — persist Placed[] alongside the actives
                // so a quicksave round-trips the user's spellbook layout. Null
                // entries are written as JSON null and stay null on load.
                for (int p_i = 0; p_i < _playerSpellbook.PlacedCount; p_i++)
                    snapshot.Placed.Add(_playerSpellbook.Placed[p_i]?.Name);
                p.Spellbook = snapshot;
            }

            if (_progression is not null)
            {
                foreach (var entry in _progression.Journal.Entries)
                    p.Quests.Add(new SiegeFX.Core.Save.QuestSnapshot
                    {
                        Key          = entry.Key,
                        State        = entry.State,
                        KillProgress = entry.KillProgress,
                    });
                p.Gold = _progression.Gold;
            }

            // 21d-2a-viii-c — hero name + variant pick from the creator (or
            // env-var spawn). Empty/null when the creator was bypassed; the
            // load path treats that as "no override" too.
            p.HeroName = _heroName;
            if (_heroVariant is not null)
            {
                p.Variant = new SiegeFX.Core.Save.HeroVariantSnapshot
                {
                    Gender      = _heroVariant.Gender == HeroGender.Girl ? "girl" : "boy",
                    BodyTypeIdx = _heroVariant.BodyTypeIdx,
                    SkinSuffix  = _heroVariant.SkinSuffix,
                    PantsSuffix = _heroVariant.PantsSuffix,
                };
            }
            save.Player = p;
        }
        return save;
    }

    // Phase 19b — patch a captured SaveFile back onto the live scene. Assumes
    // the same region is loaded (LoadPlayActors already ran), and the live
    // _actors list overlaps with save.Actors by Scid. Actors present in the
    // save but missing from the scene are skipped with a log; ones present
    // in the scene but missing from the save are left untouched at their
    // current state (treats them as "spawned after this save" — same as a
    // patch that adds NPCs).
    internal void ApplySave(SiegeFX.Core.Save.SaveFile save)
    {
        if (!string.Equals(save.RegionPath, _regionPath ?? "", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"  load: region mismatch — save was '{save.RegionPath}', live region is '{_regionPath}'");
            return;
        }

        // Phase 22-SC-MUSIC-FOLD — clear runtime music state so the
        // post-load region apply re-fires the mood + standard track.
        // Without this reset, a save mid-combat reloaded into a fresh
        // session keeps `_inCombat=true` (potentially with an
        // _activeMood from a different region) and the per-frame
        // TickCombatMusic would either no-op or play a stale battle
        // track. Save schema deliberately doesn't carry music state —
        // it's pure runtime, derived from the player's region + nearby
        // hostiles — so resetting here is correct.
        _inCombat = false;
        _combatExitTimer = 0f;
        _activeMood = null;
        _activeBedRegion = null;
        _currentMusicTrack = "";
        ApplyAmbientForRegion(_regionPath);

        // Index live actors by scid so the patch loop is O(N+M) not O(N*M).
        var byScid = new Dictionary<uint, ActorRenderState>(_actors.Count);
        foreach (var s in _actors) byScid[s.Actor.Instance.Scid] = s;

        int patched = 0, missing = 0;
        foreach (var snap in save.Actors)
        {
            if (!byScid.TryGetValue(snap.Scid, out var s)) { missing++; continue; }
            s.Actor.Combat.RestoreFromSave(snap.CurrentLife, snap.CurrentMana, snap.IsDead);
            s.IsDead = snap.IsDead;
            // Phase 12-SC-4 — restored corpses re-enter the death pose so they
            // don't pop back to idle after a quickload. BeginDeathChore is a
            // no-op when the template doesn't ship chore_die.
            if (snap.IsDead) { s.Brain = null; BeginDeathChore(s); }
            // Position restore: actors with a brain (on-mesh) teleport via
            // the follower; off-mesh pinned actors get their CurrentTransform
            // updated directly since they have no follower to drive movement.
            var pos = snap.Position.ToVector3();
            if (s.Brain is not null) s.Brain.Teleport(pos);
            s.CurrentTransform = Matrix4x4.CreateTranslation(pos);
            patched++;
        }

        _lootPiles.Clear();
        foreach (var pileSnap in save.LootPiles)
        {
            var items = new List<SiegeFX.Core.Actors.LootEntry>(pileSnap.Entries.Count);
            foreach (var e in pileSnap.Entries)
                items.Add(new SiegeFX.Core.Actors.LootEntry(e.Slot, e.Reference));
            _lootPiles.Add(new LootPile(pileSnap.Position.ToVector3(), items));
        }

        if (save.Player is not null && _player is not null)
        {
            var ps = save.Player;
            // Republish auto-grown attributes through ResyncStats so MaxLife/
            // MaxMana track what the player actually had at save time, not the
            // template's L1 values. Then RestoreFromSave on the combat state
            // reapplies current life/mana under the new caps.
            if (_formulas is not null)
            {
                var current = _player.Actor.Stats;
                var newMaxLife = _formulas.MaxLife(ps.Strength, ps.Dexterity, ps.Intelligence);
                var newMaxMana = _formulas.MaxMana(ps.Strength, ps.Dexterity, ps.Intelligence);
                _player.Actor.ResyncStats(current with
                {
                    Strength = ps.Strength,
                    Dexterity = ps.Dexterity,
                    Intelligence = ps.Intelligence,
                    MaxLife = newMaxLife,
                    MaxMana = newMaxMana,
                });
                // The actor snapshot already reapplied CurrentLife/Mana above;
                // running RestoreFromSave again would only hit the new caps.
            }
            if (_progression is not null)
            {
                _progression.RestoreFromSave(ps.TotalXp, ps.Level);
                _progression.Journal.RestoreFromSave(
                    ps.Quests.Select(q => (q.Key, q.State, q.KillProgress)));
                _progression.RestoreGoldFromSave(ps.Gold);
            }
            _playerFacing = ps.Facing.ToVector3();
            // 9-SC-10b — same reseed as on spawn so the body doesn't smear from
            // its pre-load position to the loaded one over a single tick.
            _playerRenderInit = false;
            if (_playerFollower is not null)
                _playerRenderPosPrev = _playerRenderPosNext = _playerFollower.Position;
            _playerRenderFacingPrev = _playerRenderFacingNext = _playerFacing;
            _cameraMode   = (CameraMode)ps.CameraMode;
            _chaseYaw     = ps.ChaseYaw;
            _chaseDistance = ps.ChaseDistance;
            _chaseHeight  = ps.ChaseHeight;
            _camera.Position = ps.CameraPos.ToVector3();
            _camera.Yaw   = ps.CameraYaw;
            _camera.Pitch = ps.CameraPitch;

            _playerInventory.Clear();
            _playerEquipment.Clear();
            _inventoryPanel.Reset();
            foreach (var e in ps.Inventory)
            {
                if (e.Slot.StartsWith("equipped:", StringComparison.OrdinalIgnoreCase))
                {
                    var slotKey = "es_" + e.Slot.Substring("equipped:".Length);
                    _playerEquipment[slotKey] = e.Reference;
                }
                else
                {
                    _playerInventory.Add(new SiegeFX.Core.Actors.LootEntry(e.Slot, e.Reference));
                    _inventoryPanel.NotifyItemAdded();
                }
            }
            // Re-render the equipped weapon mesh so the visible model matches
            // the restored es_weapon_hand entry. Safe even when no weapon was
            // saved — TryLoadPlayerWeapon early-outs on a missing slot.
            TryLoadPlayerWeapon();

            if (ps.Spellbook is not null && _playerSpellbook is not null && _spellCatalog is not null)
            {
                if (ps.Spellbook.PrimarySpell is { } pn && _spellCatalog.TryGet(pn, out var ps1))
                    _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Primary, ps1);
                if (ps.Spellbook.SecondarySpell is { } sn && _spellCatalog.TryGet(sn, out var ps2))
                    _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Secondary, ps2);
                _playerSpellbook.RestoreCooldowns(
                    ps.Spellbook.PrimaryCooldown, ps.Spellbook.SecondaryCooldown);
                // Phase 21-SC-SCROLL-G — restore Placed[]. v5 saves have no
                // Placed list (defaults to empty list via the deserializer),
                // so the loop simply doesn't fire and the player's placed
                // rows stay at their startup state (all null). v6+ saves
                // round-trip the layout. Resolves through ResolveSlottableSpell
                // so synthesized summon templates restore too.
                int placedSlots = Math.Min(ps.Spellbook.Placed.Count, _playerSpellbook.PlacedCount);
                for (int p_i = 0; p_i < placedSlots; p_i++)
                {
                    var name = ps.Spellbook.Placed[p_i];
                    if (name is null) { _playerSpellbook.SetPlaced(p_i, null); continue; }
                    var spell = ResolveSlottableSpell(name, debugSpellsEnv: null);
                    _playerSpellbook.SetPlaced(p_i, spell);
                }
            }

            _heroName = ps.HeroName ?? "";
            if (ps.Variant is not null)
            {
                var restored = new HeroVariantPicker
                {
                    Gender = string.Equals(ps.Variant.Gender, "girl", StringComparison.OrdinalIgnoreCase)
                             ? HeroGender.Girl : HeroGender.Boy,
                    BodyTypeIdx = ps.Variant.BodyTypeIdx,
                    SkinSuffix  = ps.Variant.SkinSuffix,
                    PantsSuffix = ps.Variant.PantsSuffix,
                };
                // Re-derive the texture overrides so the next ResolveActorTexture
                // call paints skin/pants from the saved variant, not whichever
                // override was active at boot. The body mesh itself is whatever
                // was spawned at startup — re-spawning mid-load is out of scope;
                // a mismatch only matters when the env-var spawn picker disagrees
                // with the saved one (cross-session load), in which case the warn
                // tells the user the model on screen doesn't match the bytes.
                string playerTpl = restored.Gender == HeroGender.Girl ? "farmgirl" : "farmboy";
                if (_templateStore is not null
                    && _templateStore.TryGet(playerTpl, out var pickTpl))
                {
                    var ov = restored.BuildOverride(_templateStore, pickTpl);
                    _skinTexOverrideName  = ov?.SkinTextureName;
                    _pantsTexOverrideName = ov?.ClothingTextureName;
                    if (_heroVariant is not null
                        && (_heroVariant.BodyTypeIdx != restored.BodyTypeIdx
                         || _heroVariant.Gender      != restored.Gender))
                    {
                        Console.Error.WriteLine(
                            $"  load: warning — saved hero variant (gender={restored.Gender}, " +
                            $"body={restored.BodyTypeIdx + 1}) differs from spawned mesh; " +
                            $"textures updated, body mesh stays as spawned");
                    }
                }
                _heroVariant = restored;
            }
        }

        Console.WriteLine($"  load: patched {patched}/{save.Actors.Count} actor(s), " +
                          $"{(missing > 0 ? $"{missing} missing from scene, " : "")}" +
                          $"{save.LootPiles.Count} loot pile(s) restored");
    }

    public void Dispose()
    {
        // Belt-and-braces — if Closing didn't fire (process kill, exception
        // during Run), GL resources leak with the process. Don't try to
        // re-release them post-context here; the OS will reclaim everything.
        _input?.Dispose();
        // Audio first — DeleteSources/Buffers before tearing down the
        // Sound.dsres handle isn't required (we already extracted bytes),
        // but the OpenAL context wants to outlive any pending playback.
        // Music before the engine because MusicPlayer's source + buffers
        // are owned by it but live in the engine's OpenAL context.
        _music?.Dispose();
        _audio?.Dispose();
        _playSoundTank?.Dispose();
        _playMapTank?.Dispose();
        _playLogicTank?.Dispose();
        _playObjectsTank?.Dispose();
        _playTerrainTank?.Dispose();
        _window.Dispose();
    }
}

using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Sfx;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime.Render;

/// <summary>One billboard particle. Fields are public + mutable: the system
/// stores them in a dense list and integrates them in-place every frame.
/// Color RGBA is the modulation tint; the bound texture supplies the alpha
/// and shape. Velocity / accel are in world units per second.</summary>
public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Accel;
    public Vector4 Color0;          // start tint (alpha = 1 at birth)
    public Vector4 Color1;          // end tint   (alpha = 0 at death by default)
    public float   Scale0;          // world-space half-size at birth
    public float   Scale1;          // world-space half-size at death
    public float   Life;            // remaining seconds
    public float   TotalLife;       // total seconds (for lerp)
    public byte    TexSlot;         // 0=fire, 1=smoke, 2=sparkle, 3=spark
    public byte    Additive;        // 1 = additive blend, 0 = alpha
}

/// <summary>One world-space lightning bolt segment. Drawn as N small alpha
/// quads displaced perpendicular to the source-target line, decaying over a
/// short lifetime. Cheap stand-in for DS1's <c>sfx create lightning</c>
/// primitive — visible at gameplay distance, no per-frame jitter cost.</summary>
public struct LightningBolt
{
    public Vector3 Source;
    public Vector3 Target;
    public Vector4 Color;
    public float   Life;
    public float   TotalLife;
    public uint    Seed;            // displacement noise seed
    /// <summary>Phase 21-SC-SPELL-VFX-2 — DS1 lightning param
    /// <c>maxdisplace(N)</c> (units of jitter perpendicular to the
    /// line). 0 means "use renderer default" (length-relative).</summary>
    public float   Displace;
}

/// <summary>Phase 21-SC-SPELL-VISUAL-A — DS1 cylinder primitive: a textured
/// expanding ground ring centered on a single anchor with axis rotation
/// (spin) and fade-in/out lifetime (tin/tout/dur). Used by 19 shipped
/// spells (kill, shock_wave, earthquake, fireball impact, summon ritual
/// pillars, etc.). Pre-A this rendered as a straight lightning bolt with
/// displace=0 — wrong shape entirely.
///
/// <para>Inventory finding: DS1's cylinder is NOT a beam between two
/// points; almost every shipped script anchors at one position (#TARGET
/// or #SOURCE) and authors radial extents via `rp0(start,mid,end)`. The
/// `hp0/hp1` values are typically near-zero per-end offsets, not world
/// endpoints. The dominant visual is a **ground-snapped impact ring**.
/// Outliers (laser_major's 20m beam, armor bone-attached cylinders,
/// energy_ball's X-pattern) are deferred to follow-up tweaks.</para>
///
/// <para>Renders as a flat ring on the Y-up plane at <see cref="Anchor"/>
/// with N segments forming the circle. Outer radius from `rp0` mid-value;
/// inner radius from <see cref="ThicknessRatio"/> (default 0.7 = ring,
/// 0.0 = solid disc). Texture wraps around the circumference; <see
/// cref="Spin"/> rolls the U coordinate over time so the ring appears
/// to rotate.</para></summary>
public struct SpellCylinder
{
    public Vector3 Anchor;
    public Vector4 Color;
    public float   RadiusOuter;     // rp0/rp1 mid value
    public float   ThicknessRatio;  // 0..1; 0=solid disc, 0.7=donut ring
    /// <summary>Axis spin rate. Interpretation: radians/sec — divided by
    /// MathF.Tau in the emit pass to give per-second U-tile revolutions
    /// (so DS1's `spin(15)` reads as ~2.39 revs/sec). Renaming would
    /// touch IParticleSink + 2 callsites; the unit comment is the cheaper
    /// disambiguation per the review.</summary>
    public float   Spin;
    public float   FadeIn;          // tin — seconds to ramp alpha 0→1
    public float   FadeOut;         // tout — seconds to ramp alpha 1→0 at end
    public float   TotalLife;       // dur
    public float   Elapsed;
    public byte    TexSlot;         // ParticleSystem texture slot index
    public byte    Segments;        // segments(N), default 24
}

/// <summary>Phase 21-SC-SPELL-VISUAL-B — DS1 sray primitive: a tapered
/// streak/lightray emitted radially out from an anchor. Used by 7 spells
/// (firebomb_base impact tail, death_blast, explode_body, implosion,
/// killing_fist, dust_explosion_cast). Inventory finding: sray is
/// untextured (no texture param ever shipped), uses lmin/lmax for length,
/// wsmin/wsmax + wemin/wemax for taper width, and count for fan size.
/// theta/phi are always (0,0,0) when present — rays distribute evenly in
/// azimuth around the Y axis. Single-ray scripts (implosion's pillar)
/// shoot straight up; multi-ray scripts (explode_body's count(50)) form
/// a radial fan.</summary>
public struct SpellSray
{
    public Vector3 Anchor;
    public Vector4 ColorStart;      // color0 (typically dark/black)
    public Vector4 ColorEnd;        // color1 (typically gold/orange)
    public float   LengthMin;       // lmin
    public float   LengthMax;       // lmax
    public float   WidthStart;      // wsmin..wsmax average
    public float   WidthEnd;        // wemin..wemax average
    public float   TotalLife;       // dur
    public float   Elapsed;
    public ushort  RayCount;        // count(N) — fan spokes around Y axis
    public uint    Seed;            // per-ray jitter (length/width within their ranges)
}

/// <summary>Phase 21-SC-SPELL-VFX — flying spell projectile (DS1 trackball
/// stand-in). Travels Source→Target at <see cref="Speed"/> world units/sec,
/// stamping a fire/ember trail every tick and detonating a fire+spark
/// explosion on arrival. <see cref="ImpactKind"/> picks the impact flavor
/// (0=fire, 1=ice, 2=lightning).</summary>
public struct SpellProjectile
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector4 Color;
    public float   Scale;
    public float   Speed;
    public float   TrailCarry;
    public byte    ImpactKind;
    public bool    Done;
}

/// <summary>
/// Phase 17-SC-E — billboard particle backend. World-space camera-facing quads
/// with per-particle lifetime / color / scale, integrated each frame and
/// rendered in one batched draw. Two textures by default (fire + smoke,
/// pulled from <c>/art/bitmaps/sfx/b_sfx_fireball-01.raw</c> and
/// <c>b_sfx_smoke.raw</c>); more atlas slots are easy to add.
///
/// Independent of the sfx_script interpreter (Phase 17-SC-F): this class
/// just exposes typed primitives — <c>SpawnFire</c>, <c>SpawnSmoke</c>,
/// <c>SpawnSteam</c>, <c>SpawnSpark</c>, <c>SpawnLightning</c>. The
/// interpreter calls these; emitters/spells in turn invoke the
/// interpreter. RenderHost drives <see cref="Tick"/> + <see cref="Draw"/>
/// inside the world pass each frame.
/// </summary>
public sealed class ParticleSystem : IParticleSink, IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao;
    private readonly uint _vboQuad;
    private readonly uint _vboInstance;
    // Phase 21-SC-SPELL-VISUAL-A — slots 9/10/11 reserved for cylinder
    // textures (b_sfx_cyl_01/02/03). Most cylinder spells use cyl_03;
    // earthquake / death_blast etc. occasionally use the other two.
    private readonly GlTexture?[] _textures = new GlTexture?[12];
    public const byte CylinderTexSlot = 11; // b_sfx_cyl_03 — most-used

    // Phase 21-SC-SPELL-VFX-debug — runtime-cyclable bolt texture so we
    // can A/B every plausible DS1 lightning streak from the live game
    // without rebuilding. Slot index into _textures and a pretty name
    // for the on-screen log.
    private readonly (int slot, string name)[] _boltCandidates =
    {
        (4, "lightray_01"),
        (5, "lightray_02"),
        (6, "lightray_04"),
        (7, "streaks"),
        (8, "lightray01-legacy"),
        (2, "sparkle01"),
    };
    private int _boltCandidateIndex = 0;
    public byte BoltTexSlot { get; private set; } = 4;
    public string BoltTexName => _boltCandidates[_boltCandidateIndex].name;
    public string CycleBoltTexture()
    {
        _boltCandidateIndex = (_boltCandidateIndex + 1) % _boltCandidates.Length;
        BoltTexSlot = (byte)_boltCandidates[_boltCandidateIndex].slot;
        return _boltCandidates[_boltCandidateIndex].name;
    }

    private readonly List<Particle>        _particles   = new(2048);
    private readonly List<LightningBolt>   _bolts       = new(64);
    // Phase 21-SC-SPELL-VISUAL-A — DS1 cylinder primitive, drawn via the
    // ribbon path with a different texture slot.
    private readonly List<SpellCylinder>   _cylinders   = new(16);
    // Phase 21-SC-SPELL-VISUAL-B — sray streaks; emit into the same ribbon
    // pipeline as bolts/cylinders, third pass with a separate slot.
    private readonly List<SpellSray>       _srays       = new(16);
    private int _srayVertStart = 0;
    private readonly List<SpellProjectile> _projectiles = new(32);

    private const int InstanceFloats = 12; // pos(3) + scale(1) + color(4) + texSlotF(1) + lifeFrac(1) + reserved(2)
    private float[] _instanceBuffer = new float[2048 * InstanceFloats];

    // --- ribbon (continuous strip) renderer -------------------------------
    // CPU builds a triangle list with shared world-space corners at every
    // junction so adjacent segments meet at one perpendicular per point —
    // no tinsel-style cross-hatching. Per-vertex pos/uv/color, slot bound
    // as a uniform per draw call.
    private const int RibbonVertFloats = 9; // pos(3) + uv(2) + color(4)
    private float[] _ribbonVerts = new float[2048 * RibbonVertFloats];
    private int _ribbonVertCount = 0;
    // Phase 21-SC-SPELL-VISUAL-A — split point in the ribbon vertex
    // buffer between bolts and cylinders. Bolts emit first (slot
    // BoltTexSlot), cylinders second (slot CylinderTexSlot). DrawRibbons
    // does two glDrawArrays calls with different uSlot values.
    private int _cylinderVertStart = 0;
    private readonly Shader _ribbonShader;
    private readonly uint _ribbonVao;
    private readonly uint _ribbonVbo;

    public int LiveParticleCount   => _particles.Count;
    public int LiveBoltCount       => _bolts.Count;
    public int LiveCylinderCount   => _cylinders.Count;
    public int LiveSrayCount       => _srays.Count;
    public int LiveProjectileCount => _projectiles.Count;

    public ParticleSystem(GL gl)
    {
        _gl = gl;
        _shader = new Shader(gl, VertexSrc, FragmentSrc);

        // Single quad in object-space (-0.5..+0.5). Camera basis applied in shader.
        Span<float> quad = stackalloc float[]
        {
            -0.5f, -0.5f, 0f, 0f,
             0.5f, -0.5f, 1f, 0f,
             0.5f,  0.5f, 1f, 1f,
            -0.5f, -0.5f, 0f, 0f,
             0.5f,  0.5f, 1f, 1f,
            -0.5f,  0.5f, 0f, 1f,
        };

        _vao = _gl.GenVertexArray();
        _vboQuad = _gl.GenBuffer();
        _vboInstance = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboQuad);
        unsafe
        {
            fixed (float* p = quad)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(quad.Length * sizeof(float)),
                    p, GLEnum.StaticDraw);
        }
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboInstance);
        unsafe
        {
            int stride = InstanceFloats * sizeof(float);
            // pos.xyz
            _gl.VertexAttribPointer(2, 3, GLEnum.Float, false, (uint)stride, (void*)0);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribDivisor(2, 1);
            // scale
            _gl.VertexAttribPointer(3, 1, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribDivisor(3, 1);
            // color rgba
            _gl.VertexAttribPointer(4, 4, GLEnum.Float, false, (uint)stride, (void*)(4 * sizeof(float)));
            _gl.EnableVertexAttribArray(4);
            _gl.VertexAttribDivisor(4, 1);
            // texSlot, lifeFrac
            _gl.VertexAttribPointer(5, 2, GLEnum.Float, false, (uint)stride, (void*)(8 * sizeof(float)));
            _gl.EnableVertexAttribArray(5);
            _gl.VertexAttribDivisor(5, 1);
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);

        // --- ribbon (continuous strip) VAO/VBO/shader ---
        _ribbonShader = new Shader(gl, RibbonVertexSrc, RibbonFragmentSrc);
        _ribbonVao = _gl.GenVertexArray();
        _ribbonVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_ribbonVao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _ribbonVbo);
        unsafe
        {
            int rstride = RibbonVertFloats * sizeof(float);
            // pos.xyz
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)rstride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            // uv.xy
            _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)rstride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            // color.rgba
            _gl.VertexAttribPointer(2, 4, GLEnum.Float, false, (uint)rstride, (void*)(5 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
        }
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    /// <summary>Lazy-load the sprite atlas off Objects.dsres. Failures are
    /// non-fatal — particles fall back to a solid white quad if a texture
    /// can't load (the per-particle color tint still reads).</summary>
    public void LoadTextures(TankReader objectsTank)
    {
        TryLoadSlot(objectsTank, 0, "/art/bitmaps/sfx/b_sfx_fireball-01.raw");
        TryLoadSlot(objectsTank, 1, "/art/bitmaps/sfx/b_sfx_smoke.raw");
        TryLoadSlot(objectsTank, 2, "/art/bitmaps/sfx/b_sfx_sparkle01.raw");
        TryLoadSlot(objectsTank, 3, "/art/bitmaps/sfx/b_sfx_002.raw");
        // Slots 4-8 — DS1 lightning/streak candidates we can A/B at runtime
        // via CycleBoltTexture(). All five are real shipped DS1 textures.
        TryLoadSlot(objectsTank, 4, "/art/bitmaps/sfx/b_sfx_lightray_01.raw");
        TryLoadSlot(objectsTank, 5, "/art/bitmaps/sfx/b_sfx_lightray_02.raw");
        TryLoadSlot(objectsTank, 6, "/art/bitmaps/sfx/b_sfx_lightray_04.raw");
        TryLoadSlot(objectsTank, 7, "/art/bitmaps/sfx/b_sfx_streaks.raw");
        TryLoadSlot(objectsTank, 8, "/art/bitmaps/sfx/b_sfx_lightray01.raw");
        // Phase 21-SC-SPELL-VISUAL-A — cylinder textures.
        TryLoadSlot(objectsTank, 9,  "/art/bitmaps/sfx/b_sfx_cyl_01.raw");
        TryLoadSlot(objectsTank, 10, "/art/bitmaps/sfx/b_sfx_cyl_02.raw");
        TryLoadSlot(objectsTank, 11, "/art/bitmaps/sfx/b_sfx_cyl_03.raw");
    }

    void TryLoadSlot(TankReader tank, int slot, string path)
    {
        try
        {
            var bytes = tank.ExtractToMemory(path);
            var img = RawImage.Load(bytes);
            _textures[slot] = new GlTexture(_gl, img);
        }
        catch { /* leave null; shader fallbacks to white */ }
    }

    public void SpawnFire(Vector3 position, Vector4 color, float scale, float duration, int count = 12)
    {
        for (int i = 0; i < count; i++)
        {
            var jitter = new Vector3(Rand(-0.15f, 0.15f), 0f, Rand(-0.15f, 0.15f)) * scale;
            _particles.Add(new Particle
            {
                Position  = position + jitter,
                Velocity  = new Vector3(Rand(-0.05f, 0.05f), Rand(0.6f, 1.1f), Rand(-0.05f, 0.05f)) * scale,
                Accel     = new Vector3(0f, 0.4f * scale, 0f),
                Color0    = color,
                Color1    = new Vector4(color.X * 0.4f, color.Y * 0.2f, color.Z * 0.05f, 0f),
                Scale0    = scale * 0.45f,
                Scale1    = scale * 0.95f,
                Life      = duration,
                TotalLife = duration,
                TexSlot   = 0,
                Additive  = 1,
            });
        }
    }

    public void SpawnSmoke(Vector3 position, Vector4 color, float scale, float duration, int count = 8)
    {
        for (int i = 0; i < count; i++)
        {
            var jitter = new Vector3(Rand(-0.2f, 0.2f), 0f, Rand(-0.2f, 0.2f)) * scale;
            _particles.Add(new Particle
            {
                Position  = position + jitter,
                Velocity  = new Vector3(Rand(-0.1f, 0.1f), Rand(0.4f, 0.8f), Rand(-0.1f, 0.1f)) * scale,
                Accel     = new Vector3(0f, 0.05f * scale, 0f),
                Color0    = color,
                Color1    = new Vector4(color.X, color.Y, color.Z, 0f),
                Scale0    = scale * 0.6f,
                Scale1    = scale * 1.6f,
                Life      = duration,
                TotalLife = duration,
                TexSlot   = 1,
                Additive  = 0,
            });
        }
    }

    public void SpawnSteam(Vector3 position, Vector4 color, float scale, float duration, int count = 8)
    {
        for (int i = 0; i < count; i++)
        {
            _particles.Add(new Particle
            {
                Position  = position + new Vector3(Rand(-0.3f, 0.3f), 0f, Rand(-0.3f, 0.3f)) * scale,
                Velocity  = new Vector3(Rand(-0.2f, 0.2f), Rand(0.2f, 0.5f), Rand(-0.2f, 0.2f)) * scale,
                Accel     = new Vector3(0f, -0.05f * scale, 0f),
                Color0    = color,
                Color1    = new Vector4(color.X, color.Y, color.Z, 0f),
                Scale0    = scale * 0.5f,
                Scale1    = scale * 1.4f,
                Life      = duration,
                TotalLife = duration,
                TexSlot   = 1,
                Additive  = 0,
            });
        }
    }

    /// <summary>Phase 21-SC-SCROLL-GLITTER — DS1's "pixie dust" twinkle on
    /// resting magic items. Distinct from SpawnSpark (which radiates
    /// outward from a single point with strong gravity): twinkle particles
    /// are scattered across an XZ footprint, drift slowly with a
    /// rolling lateral velocity, and use a brief grow-then-fade scale curve
    /// so they read like stars twinkling on a pond. Per-particle spawn
    /// position is randomized inside <paramref name="footprintRadius"/>;
    /// each particle gets a small tangential velocity so the field looks
    /// like it's moving across the scroll's surface rather than puffing
    /// out from a single point.</summary>
    public void SpawnTwinkle(Vector3 center, Vector4 color, float footprintRadius,
                             float scale, float duration, int count)
    {
        if (count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            float ang = Rand(0f, MathF.Tau);
            float r   = MathF.Sqrt(Rand(0f, 1f)) * footprintRadius; // uniform-area sample
            var pos = center + new Vector3(MathF.Cos(ang) * r, 0f, MathF.Sin(ang) * r);
            // Rolling drift: tangential velocity so the field reads as a
            // moving star-field rather than a static cloud. Earlier draft
            // had 0.20 tangential + 0.05–0.18 upward bias which billowed
            // the field upward into a storm-cloud shape; the user wanted
            // it sitting close to the scroll. Halved tangential and
            // killed the upward bias so the cloud hugs the emit plane.
            float driftAng = ang + MathF.PI * 0.5f; // tangent to radial
            var v = new Vector3(MathF.Cos(driftAng) * 0.10f,
                                Rand(-0.02f, 0.04f),       // mostly flat, occasional slow rise
                                MathF.Sin(driftAng) * 0.10f);
            _particles.Add(new Particle
            {
                Position  = pos,
                Velocity  = v,
                Accel     = Vector3.Zero,                    // no gravity — twinkles drift
                Color0    = color,
                Color1    = new Vector4(color.X, color.Y, color.Z, 0f), // fade alpha
                Scale0    = scale * 0.30f,                   // grow-in
                Scale1    = scale * 1.10f,                   // peak before fade
                Life      = duration,
                TotalLife = duration,
                TexSlot   = 2,                               // spark/star texture
                Additive  = 1,                               // bright on dark backdrops
            });
        }
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-A — register a DS1 cylinder
    /// primitive: a flat textured ring/disc on the Y-up plane at
    /// <paramref name="anchor"/>. The 19 cylinder-using spells in DS1
    /// (kill, shock_wave, earthquake, fireball impact, summon ritual
    /// pillars, etc.) overwhelmingly author cylinders as ground-snapped
    /// impact rings rather than beams between two points; this primitive
    /// honors that dominant shape.
    /// <see cref="radiusOuter"/> comes from the script's `rp0(start,mid,end)`
    /// 3-float profile (we take the mid value); <see cref="thicknessRatio"/>
    /// 0=solid disc, 0.7=donut ring (default — matches most shipped looks).
    /// <see cref="spinPerSec"/> rolls the U coord over time so the ring
    /// appears to rotate around the axis. Outliers (laser_major's 20m
    /// beam, energy_ball's X-pattern, the armor bone-attached cylinders)
    /// are deferred to follow-up tweaks.</summary>
    public void SpawnCylinder(Vector3 anchor, Vector4 color,
                              float radiusOuter,    float thicknessRatio,
                              float spinPerSec,     float fadeIn, float fadeOut,
                              float duration,       byte texSlot, byte segments)
    {
        _cylinders.Add(new SpellCylinder
        {
            Anchor          = anchor,
            Color           = color,
            RadiusOuter     = MathF.Max(0.05f, radiusOuter),
            ThicknessRatio  = Math.Clamp(thicknessRatio, 0f, 0.95f),
            Spin            = spinPerSec,
            FadeIn          = MathF.Max(0f, fadeIn),
            FadeOut         = MathF.Max(0f, fadeOut),
            TotalLife       = MathF.Max(0.10f, duration),
            Elapsed         = 0f,
            TexSlot         = texSlot,
            Segments        = segments < 4 ? (byte)4 : segments,
        });
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-C — DS1 fireb directional fire-cone
    /// emitter. Spawns a one-shot batch of fire-textured particles flying
    /// outward from <paramref name="anchor"/> in <paramref name="velocity"/>
    /// direction, with a cone-shaped lateral spread defined by
    /// <paramref name="lowerRadius"/> (start) → <paramref name="upperRadius"/>
    /// (end). Used by 5 spells (dragon_fire, flame, inferno, pestilence,
    /// pestilence_cloud) typically layered with different velocities to
    /// build flamethrower / breath-weapon visuals.</summary>
    public void SpawnFireb(Vector3 anchor, Vector4 color, Vector3 velocity,
                           Vector3 accel, float lifetime, float maxDisplace,
                           float lowerRadius, float upperRadius,
                           int count, float flameSize)
    {
        if (count <= 0) return;
        // Direction-aligned basis: forward = velocity normalized; perp1/perp2
        // span the lateral plane so we can scatter the cone radius.
        Vector3 fwd = velocity.LengthSquared() > 0.0001f
            ? Vector3.Normalize(velocity) : Vector3.UnitZ;
        Vector3 up = MathF.Abs(fwd.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 perp1 = Vector3.Normalize(Vector3.Cross(fwd, up));
        Vector3 perp2 = Vector3.Cross(fwd, perp1);

        for (int i = 0; i < count; i++)
        {
            float ang = Rand(0f, MathF.Tau);
            // Lerp lower→upper radius along the particle's life (proxy:
            // randomized, since each particle ages independently). Picks
            // a radius in the cone profile.
            float r = MathF.Abs(lowerRadius) + Rand(0f, MathF.Abs(upperRadius - lowerRadius));
            var lateral = (perp1 * MathF.Cos(ang) + perp2 * MathF.Sin(ang)) * r;
            // Per-particle turbulence — DS1's min_displace/max_displace.
            var turb = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)) * maxDisplace;

            // Each particle gets the script's velocity (forward direction)
            // plus a small lateral spread proportional to cone radius.
            var pVel = velocity + lateral * 0.25f;

            float scale = MathF.Max(0.20f, flameSize * 0.40f);
            _particles.Add(new Particle
            {
                Position  = anchor + lateral + turb,
                Velocity  = pVel,
                Accel     = accel,
                Color0    = color,
                // Color1: warm-biased fade by default (matches fire texture).
                // Slice H will eventually honor color1(...) from the script.
                Color1    = new Vector4(color.X * 0.6f, color.Y * 0.3f, color.Z * 0.10f, 0f),
                Scale0    = scale * 0.6f,
                Scale1    = scale * 1.2f,
                Life      = lifetime,
                TotalLife = lifetime,
                TexSlot   = 0,         // b_sfx_fireball-01
                Additive  = 1,
            });
        }
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-B — register a DS1 sray streak.
    /// Lengths/widths randomized per-ray within the supplied ranges using
    /// the auto-incrementing Seed; <paramref name="rayCount"/> rays
    /// distribute evenly in azimuth around the anchor's Y axis.</summary>
    public void SpawnSray(Vector3 anchor, Vector4 colorStart, Vector4 colorEnd,
                          float lengthMin, float lengthMax,
                          float widthStart, float widthEnd,
                          float duration, int rayCount)
    {
        _srays.Add(new SpellSray
        {
            Anchor      = anchor,
            ColorStart  = colorStart,
            ColorEnd    = colorEnd,
            LengthMin   = MathF.Max(0.05f, lengthMin),
            LengthMax   = MathF.Max(lengthMin, lengthMax),
            WidthStart  = MathF.Max(0.01f, widthStart),
            WidthEnd    = MathF.Max(0.01f, widthEnd),
            TotalLife   = MathF.Max(0.05f, duration),
            Elapsed     = 0f,
            RayCount    = (ushort)Math.Clamp(rayCount, 1, 96),
            Seed        = (uint)((int)(anchor.X * 73856093f) ^ (int)(anchor.Z * 19349663f) ^ rayCount),
        });
    }

    public void SpawnSpark(Vector3 position, Vector4 color, float scale, float duration, int count = 16)
    {
        for (int i = 0; i < count; i++)
        {
            float ang = Rand(0f, MathF.Tau);
            float vy  = Rand(0.2f, 1.0f);
            var v = new Vector3(MathF.Cos(ang), vy, MathF.Sin(ang)) * scale * Rand(0.5f, 1.2f);
            _particles.Add(new Particle
            {
                Position  = position,
                Velocity  = v,
                Accel     = new Vector3(0f, -2.5f * scale, 0f),
                Color0    = color,
                Color1    = new Vector4(color.X, color.Y * 0.5f, color.Z * 0.2f, 0f),
                Scale0    = scale * 0.15f,
                Scale1    = scale * 0.05f,
                Life      = duration,
                TotalLife = duration,
                TexSlot   = 2,
                Additive  = 1,
            });
        }
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-H+sphere fold — DS1 sphere is a
    /// fully omni-directional expanding shell of particles, NOT a Y-
    /// biased fountain. Each particle spawns at the anchor with a
    /// velocity vector uniformly distributed on a unit sphere then
    /// scaled so the shell reaches roughly <paramref name="radius"/> by
    /// half its lifetime. Color-preserving fade (Color1 keeps RGB,
    /// alpha → 0) so non-warm spheres (vandegraph purple, ice cyan)
    /// don't drift to brown like the warm-biased SpawnSpark would.</summary>
    public void SpawnSphere(Vector3 anchor, Vector4 color, float radius, float duration, int count)
    {
        if (count <= 0) return;
        if (duration <= 0.05f) duration = 0.5f;
        // Speed sized so a particle covers `radius` over half the life
        // (the rest of the lifetime continues outward into a thinning
        // shell, which reads as a brief bloom).
        float speed = MathF.Max(0.1f, radius / MathF.Max(0.05f, duration * 0.5f));
        for (int i = 0; i < count; i++)
        {
            // Uniformly-random unit vector via the (z, theta) trick:
            // z ∈ [-1,1] uniform, theta ∈ [0, 2π) uniform → uniform
            // distribution on the unit sphere.
            float z     = Rand(-1f, 1f);
            float theta = Rand(0f, MathF.Tau);
            float r     = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            var dir = new Vector3(r * MathF.Cos(theta), z, r * MathF.Sin(theta));
            float jitter = Rand(0.85f, 1.15f);
            var c1 = new Vector4(color.X, color.Y, color.Z, 0f);
            _particles.Add(new Particle
            {
                Position  = anchor,
                Velocity  = dir * speed * jitter,
                Accel     = Vector3.Zero,
                Color0    = color,
                Color1    = c1,
                Scale0    = radius * 0.18f,
                Scale1    = radius * 0.06f,
                Life      = duration,
                TotalLife = duration,
                TexSlot   = 2,
                Additive  = 1,
            });
        }
    }

    public void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration)
        => SpawnLightning(source, target, color, duration, displace: 0f);

    /// <summary>Phase 21-SC-SPELL-VFX-2 — DS1's <c>maxdisplace(N)</c> param.
    /// 0 falls back to length-relative jitter.</summary>
    public void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration, float displace)
    {
        _bolts.Add(new LightningBolt
        {
            Source    = source,
            Target    = target,
            Color     = color,
            Life      = duration,
            TotalLife = duration,
            Seed      = (uint)Random.Shared.Next(),
            Displace  = displace,
        });
    }

    public void SpawnProjectile(Vector3 source, Vector3 target, Vector4 color, float scale, float speed, int impactKind)
    {
        _projectiles.Add(new SpellProjectile
        {
            Position   = source,
            Target     = target,
            Color      = color,
            Scale      = MathF.Max(0.1f, scale),
            Speed      = MathF.Max(2f, speed),
            // Clamp upper bound covers every kind the trail+impact switches
            // recognize (0 fire, 1 ice, 2 lightning, 3 acid). Adding a new
            // kind here without bumping the bound silently truncates to
            // an unrelated visual — that's how the acid fix in 8ad7483
            // initially shipped non-functional. Keep this in sync with the
            // switch arms in the trail-spawn and impact-burst blocks.
            ImpactKind = (byte)Math.Clamp(impactKind, 0, 3),
        });
    }

    /// <summary>Continuous fire emitter — call every tick to maintain a
    /// flame plume. <paramref name="rate"/> is particles per second.
    /// Returns the leftover spawn budget (caller stores between calls).</summary>
    public float MaintainFire(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry)
    {
        float budget = carry + rate * dt;
        int n = (int)budget;
        if (n > 0) SpawnFire(position, color, scale, 1.4f, n);
        return budget - n;
    }

    public float MaintainSmoke(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry)
    {
        float budget = carry + rate * dt;
        int n = (int)budget;
        if (n > 0) SpawnSmoke(position, color, scale, 3.0f, n);
        return budget - n;
    }

    public float MaintainSteam(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry)
    {
        float budget = carry + rate * dt;
        int n = (int)budget;
        if (n > 0) SpawnSteam(position, color, scale, 1.8f, n);
        return budget - n;
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-D — bright additive glow halo around
    /// <paramref name="position"/>. Spawns short-lived sparkles in a tight
    /// <paramref name="radius"/> ball with near-zero drift, so the cluster
    /// reads as a glowing core rather than a smoke wisp. Color-preserving
    /// (Color0 == Color1 RGB; alpha fades to 0). Used by lightsource motion
    /// handles whose Position the parent sfx VM refreshes each tick.</summary>
    public float MaintainGlow(Vector3 position, Vector4 color, float radius, float dt, float rate, float carry)
    {
        float budget = carry + rate * dt;
        int n = (int)budget;
        float r = MathF.Max(0.05f, radius);
        for (int i = 0; i < n; i++)
        {
            float ang = Rand(0f, MathF.Tau);
            float dy  = Rand(-0.4f, 0.4f) * r;
            float dr  = Rand(0f, r);
            var off = new Vector3(MathF.Cos(ang) * dr, dy, MathF.Sin(ang) * dr);
            float life = Rand(0.20f, 0.45f);
            float s = r * Rand(0.35f, 0.70f);
            // Color-preserving: Color1 keeps RGB and fades alpha so the
            // halo doesn't drift toward warm-orange like SpawnFire/SpawnSpark.
            var c1 = new Vector4(color.X, color.Y, color.Z, 0f);
            _particles.Add(new Particle
            {
                Position  = position + off,
                Velocity  = Vector3.Zero,
                Accel     = Vector3.Zero,
                Color0    = color,
                Color1    = c1,
                Scale0    = s,
                Scale1    = s * 0.5f,
                Life      = life,
                TotalLife = life,
                TexSlot   = 2,
                Additive  = 1,
            });
        }
        return budget - n;
    }

    public void Tick(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0f) { _particles.RemoveAt(i); continue; }
            p.Velocity += p.Accel * dt;
            p.Position += p.Velocity * dt;
            _particles[i] = p;
        }
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            b.Life -= dt;
            if (b.Life <= 0f) { _bolts.RemoveAt(i); continue; }
            _bolts[i] = b;
        }
        // Phase 21-SC-SPELL-VISUAL-A — advance cylinder lifetimes; the
        // emit pass reads Elapsed for fade-in/out + spin animation.
        for (int i = _cylinders.Count - 1; i >= 0; i--)
        {
            var c = _cylinders[i];
            c.Elapsed += dt;
            if (c.Elapsed >= c.TotalLife) { _cylinders.RemoveAt(i); continue; }
            _cylinders[i] = c;
        }
        // Phase 21-SC-SPELL-VISUAL-B — advance sray lifetimes.
        for (int i = _srays.Count - 1; i >= 0; i--)
        {
            var s = _srays[i];
            s.Elapsed += dt;
            if (s.Elapsed >= s.TotalLife) { _srays.RemoveAt(i); continue; }
            _srays[i] = s;
        }
        // Phase 21-SC-SPELL-VFX — advance projectiles toward their targets,
        // stamp a fire/ember trail along the way, detonate on arrival.
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            var pr = _projectiles[i];
            var toTarget = pr.Target - pr.Position;
            float dist = toTarget.Length();
            float step = pr.Speed * dt;
            if (step >= dist || dist < 0.05f)
            {
                // Impact burst — flavor by ImpactKind. Always loud enough to read.
                switch (pr.ImpactKind)
                {
                    case 1: // ice / frost
                        SpawnSteam(pr.Target, new Vector4(0.75f, 0.92f, 1f, 0.9f), pr.Scale * 1.6f, 0.55f, 18);
                        SpawnSpark(pr.Target, pr.Color, pr.Scale * 1.4f, 0.45f, 24);
                        break;
                    case 2: // lightning crack
                        SpawnSpark(pr.Target, pr.Color, pr.Scale * 1.4f, 0.35f, 32);
                        SpawnFire (pr.Target, pr.Color, pr.Scale * 0.9f, 0.30f, 8);
                        break;
                    case 3: // acid / poison
                        SpawnSmoke(pr.Target, pr.Color, pr.Scale * 1.6f, 0.65f, 16);
                        SpawnSpark(pr.Target, new Vector4(0.55f, 0.95f, 0.40f, 1f), pr.Scale * 1.2f, 0.40f, 20);
                        break;
                    default: // fire
                        SpawnFire (pr.Target, pr.Color, pr.Scale * 1.6f, 0.55f, 22);
                        SpawnSpark(pr.Target, new Vector4(1f, 0.85f, 0.4f, 1f), pr.Scale * 1.2f, 0.45f, 18);
                        SpawnSmoke(pr.Target, new Vector4(0.25f, 0.20f, 0.18f, 0.55f), pr.Scale * 1.4f, 0.90f, 8);
                        break;
                }
                _projectiles.RemoveAt(i);
                continue;
            }
            var dir = toTarget / dist;
            pr.Position += dir * step;
            // Trail — branch by ImpactKind so the in-flight visual reads
            // as the element it is. SpawnFire / SpawnSpark have warm-biased
            // Color1 fades baked in (color.Y * 0.2 / color.Z * 0.05), which
            // is correct for fire but turns a cyan input into brown puffs
            // mid-flight — that's why pre-fix iceshard "looked like
            // fireball." SpawnSteam / SpawnSmoke preserve the input color
            // through the alpha fade, so they're the right primitives for
            // cool elements.
            float trailRate = 90f;
            float budget = pr.TrailCarry + trailRate * dt;
            int n = (int)budget;
            if (n > 0)
            {
                switch (pr.ImpactKind)
                {
                    case 1: // ice / frost — cool steam trail + cyan sparks
                        SpawnSteam(pr.Position, pr.Color, pr.Scale * 0.55f, 0.30f, n);
                        if ((n & 1) == 0)
                            SpawnSpark(pr.Position,
                                       new Vector4(0.85f, 0.95f, 1.0f, 1f),
                                       pr.Scale * 0.6f, 0.18f, 2);
                        break;
                    case 2: // lightning crack — element-tinted sparks only
                        SpawnSpark(pr.Position, pr.Color, pr.Scale * 0.55f, 0.20f, n);
                        if ((n & 1) == 0)
                            SpawnSpark(pr.Position,
                                       new Vector4(1f, 1f, 1f, 1f),
                                       pr.Scale * 0.6f, 0.15f, 2);
                        break;
                    case 3: // acid / poison — green-preserving smoke + green sparks
                        SpawnSmoke(pr.Position, pr.Color, pr.Scale * 0.55f, 0.30f, n);
                        if ((n & 1) == 0)
                            SpawnSpark(pr.Position,
                                       new Vector4(0.55f, 0.95f, 0.40f, 1f),
                                       pr.Scale * 0.6f, 0.18f, 2);
                        break;
                    default: // fire — original warm trail
                        SpawnFire(pr.Position, pr.Color, pr.Scale * 0.55f, 0.30f, n);
                        if ((n & 1) == 0)
                            SpawnSpark(pr.Position,
                                       new Vector4(1f, 0.9f, 0.55f, 1f),
                                       pr.Scale * 0.6f, 0.20f, 2);
                        break;
                }
            }
            pr.TrailCarry = budget - n;
            _projectiles[i] = pr;
        }
    }

    public void Clear()
    {
        _particles.Clear();
        _bolts.Clear();
        _projectiles.Clear();
        _cylinders.Clear();
        _srays.Clear();
    }

    public void Draw(Matrix4x4 view, Matrix4x4 proj, Vector3 cameraPos)
    {
        if (_particles.Count == 0 && _bolts.Count == 0
            && _projectiles.Count == 0 && _cylinders.Count == 0
            && _srays.Count == 0) return;

        // Bolts + cylinders + srays all compile into the ribbon vertex
        // buffer. Each takes its own slice; DrawRibbons issues 3 draw
        // calls with different uSlot values for the texture choice.
        // Order: bolts at [0, _cylinderVertStart), cylinders at
        // [_cylinderVertStart, _srayVertStart), srays at [_srayVertStart,
        // _ribbonVertCount).
        _ribbonVertCount = 0;
        EmitBoltQuads(cameraPos);
        _cylinderVertStart = _ribbonVertCount;
        EmitCylinderQuads(cameraPos);
        _srayVertStart = _ribbonVertCount;
        EmitSrayQuads(cameraPos);
        EmitProjectileHeads();

        // Ribbon (lightning bolts + cylinders) draw before particles so
        // additive particles still composite on top cleanly.
        if (_ribbonVertCount > 0) DrawRibbons(view, proj);

        if (_particles.Count == 0) return;

        // Rebuild instance buffer.
        EnsureInstanceCapacity(_particles.Count);
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            float frac = 1f - (p.Life / MathF.Max(0.0001f, p.TotalLife));
            float scale = MathHelper.Lerp(p.Scale0, p.Scale1, frac);
            var col = Vector4.Lerp(p.Color0, p.Color1, frac);
            int o = i * InstanceFloats;
            _instanceBuffer[o + 0] = p.Position.X;
            _instanceBuffer[o + 1] = p.Position.Y;
            _instanceBuffer[o + 2] = p.Position.Z;
            _instanceBuffer[o + 3] = scale;
            _instanceBuffer[o + 4] = col.X;
            _instanceBuffer[o + 5] = col.Y;
            _instanceBuffer[o + 6] = col.Z;
            _instanceBuffer[o + 7] = col.W;
            _instanceBuffer[o + 8] = p.TexSlot;
            _instanceBuffer[o + 9] = (float)p.Additive;
            _instanceBuffer[o + 10] = 0f;
            _instanceBuffer[o + 11] = 0f;
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboInstance);
        unsafe
        {
            fixed (float* p = _instanceBuffer)
                _gl.BufferData(GLEnum.ArrayBuffer,
                    (nuint)(_particles.Count * InstanceFloats * sizeof(float)),
                    p, GLEnum.DynamicDraw);
        }

        // Two-pass draw: alpha-blended first, additive second. We sort by
        // additive flag in a single scan so the GPU draws each group in
        // one DrawArraysInstanced call.
        int alphaCount = 0, addCount = 0;
        for (int i = 0; i < _particles.Count; i++)
            if (_particles[i].Additive == 1) addCount++; else alphaCount++;

        bool depthWasOn = _gl.IsEnabled(GLEnum.DepthTest);
        _gl.Enable(GLEnum.DepthTest);
        _gl.DepthMask(false);
        _gl.Enable(GLEnum.Blend);

        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetInt("uTex0", 0);
        _shader.SetInt("uTex1", 1);
        _shader.SetInt("uTex2", 2);
        _shader.SetInt("uTex3", 3);
        _shader.SetInt("uTex4", 4);
        _shader.SetInt("uTex5", 5);
        _shader.SetInt("uTex6", 6);
        _shader.SetInt("uTex7", 7);
        _shader.SetInt("uTex8", 8);
        for (int slot = 0; slot < _textures.Length; slot++)
        {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + slot));
            _gl.BindTexture(GLEnum.Texture2D, _textures[slot]?.Handle ?? 0);
        }

        _gl.BindVertexArray(_vao);

        // Single batched draw — the per-instance Additive flag tells the
        // shader to clamp alpha down for additive (we still set the GL
        // blend mode below). Mixing alpha+additive in one draw call would
        // need pre-sorting; for SC-E we use one combined SrcAlpha/One blend
        // that approximates both modes acceptably for a first pass.
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.DrawArraysInstanced(GLEnum.Triangles, 0, 6, (uint)_particles.Count);

        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        if (!depthWasOn) _gl.Disable(GLEnum.DepthTest);
    }

    private readonly Vector3[] _boltPathScratch = new Vector3[64 + 1];
    private readonly Vector3[] _boltPerpScratch = new Vector3[64 + 1];
    void EmitBoltQuads(Vector3 cameraPos)
    {
        // Build a continuous triangle strip per bolt. Each junction point gets
        // ONE shared perpendicular (tangent × toCam, neighbor-averaged), so
        // adjacent segments meet at the same corner pair instead of each
        // computing its own perpendicular and producing the cross-hatched
        // tinsel/X effect. Result is a true wire instead of a string of pills.
        _ribbonVertCount = 0;
        if (_bolts.Count == 0) return;
        const int Segments = 24;
        for (int bi = 0; bi < _bolts.Count; bi++)
        {
            var b = _bolts[bi];
            float frac = 1f - (b.Life / MathF.Max(0.0001f, b.TotalLife));
            var dir = b.Target - b.Source;
            float len = dir.Length();
            if (len < 0.001f) continue;
            var fwd = dir / len;
            var up = Vector3.UnitY;
            var side = Vector3.Cross(fwd, up); if (side.LengthSquared() < 0.001f) side = Vector3.UnitX;
            side = Vector3.Normalize(side);
            uint rng = b.Seed + (uint)(frac * 16f);
            float xAmp = b.Displace > 0.001f
                ? MathF.Max(b.Displace, MathF.Min(len * 0.05f, 0.25f))
                : MathF.Min(len * 0.08f, 0.35f);
            float yAmp = b.Displace > 0.001f
                ? MathF.Max(b.Displace * 0.6f, MathF.Min(len * 0.04f, 0.18f))
                : MathF.Min(len * 0.06f, 0.25f);
            // Wire-thin world-space half-width. ~2 pixels at chase-cam range.
            float thickness = 0.022f;
            float lifeAlpha = MathF.Max(0.25f, 1f - frac);
            var core = b.Color;
            core.X = MathF.Min(1f, core.X * 0.4f + 0.6f);
            core.Y = MathF.Min(1f, core.Y * 0.4f + 0.6f);
            core.Z = MathF.Min(1f, core.Z * 0.4f + 0.6f);
            core.W = lifeAlpha;

            // Pass 1: jittered polyline points along the bolt.
            var pts = _boltPathScratch;
            for (int s = 0; s <= Segments; s++)
            {
                float t = (float)s / Segments;
                rng = rng * 1664525u + 1013904223u;
                float jx = ((rng & 0xFFFF) / 65535f - 0.5f) * xAmp;
                rng = rng * 1664525u + 1013904223u;
                float jy = ((rng & 0xFFFF) / 65535f - 0.5f) * yAmp;
                if (s == 0 || s == Segments) { jx = 0f; jy = 0f; }
                pts[s] = b.Source + dir * t + side * jx + up * jy;
            }

            // Pass 2: shared per-junction perpendicular = neighbor-averaged
            // tangent crossed with view-direction-to-camera at this point.
            var perp = _boltPerpScratch;
            for (int s = 0; s <= Segments; s++)
            {
                Vector3 tangent;
                if      (s == 0)        tangent = pts[1] - pts[0];
                else if (s == Segments) tangent = pts[Segments] - pts[Segments - 1];
                else                    tangent = pts[s + 1] - pts[s - 1];
                var toCam = cameraPos - pts[s];
                var sv = Vector3.Cross(tangent, toCam);
                float slen = sv.Length();
                if (slen < 1e-6f) sv = Vector3.UnitY; else sv /= slen;
                perp[s] = sv * thickness;
            }

            // Pass 3: emit 2 tris (6 verts) per segment; corners at s and s+1
            // share their perpendicular with the neighbor segment.
            EnsureRibbonCapacity(_ribbonVertCount + Segments * 6);
            for (int s = 0; s < Segments; s++)
            {
                float u0 = (float)s / Segments;
                float u1 = (float)(s + 1) / Segments;
                var p0a = pts[s]     + perp[s];
                var p0b = pts[s]     - perp[s];
                var p1a = pts[s + 1] + perp[s + 1];
                var p1b = pts[s + 1] - perp[s + 1];
                EmitRibbonVert(p0b, u0, 0f, core);
                EmitRibbonVert(p1b, u1, 0f, core);
                EmitRibbonVert(p1a, u1, 1f, core);
                EmitRibbonVert(p0b, u0, 0f, core);
                EmitRibbonVert(p1a, u1, 1f, core);
                EmitRibbonVert(p0a, u0, 1f, core);
            }
        }
    }

    void EmitRibbonVert(Vector3 pos, float u, float v, Vector4 c)
    {
        int o = _ribbonVertCount * RibbonVertFloats;
        _ribbonVerts[o + 0] = pos.X;
        _ribbonVerts[o + 1] = pos.Y;
        _ribbonVerts[o + 2] = pos.Z;
        _ribbonVerts[o + 3] = u;
        _ribbonVerts[o + 4] = v;
        _ribbonVerts[o + 5] = c.X;
        _ribbonVerts[o + 6] = c.Y;
        _ribbonVerts[o + 7] = c.Z;
        _ribbonVerts[o + 8] = c.W;
        _ribbonVertCount++;
    }

    void EnsureRibbonCapacity(int verts)
    {
        int needed = verts * RibbonVertFloats;
        if (_ribbonVerts.Length < needed)
            _ribbonVerts = new float[Math.Max(_ribbonVerts.Length * 2, needed)];
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-B — build a fan of camera-facing
    /// tapered streaks for each sray. Each ray is a single quad: base at
    /// the anchor with WidthStart, tip at anchor+dir*length with WidthEnd.
    /// Direction: single-ray scripts shoot straight up; multi-ray fans
    /// distribute evenly in azimuth around the Y axis. Color gradient
    /// goes ColorStart at base → ColorEnd at tip. Lifetime alpha ramps
    /// in for the first 20% then out for the last 30%.</summary>
    void EmitSrayQuads(Vector3 cameraPos)
    {
        if (_srays.Count == 0) return;
        for (int si = 0; si < _srays.Count; si++)
        {
            var s = _srays[si];
            float t01 = s.Elapsed / s.TotalLife;
            float alpha = 1f;
            if (t01 < 0.20f) alpha = t01 / 0.20f;
            else if (t01 > 0.70f) alpha = MathF.Max(0f, 1f - (t01 - 0.70f) / 0.30f);

            int n = s.RayCount;
            uint rng = s.Seed;
            EnsureRibbonCapacity(_ribbonVertCount + n * 6);
            var c0 = s.ColorStart; c0.W *= alpha;
            var c1 = s.ColorEnd;   c1.W *= alpha;

            for (int k = 0; k < n; k++)
            {
                rng = rng * 1664525u + 1013904223u;
                float lenT = (rng & 0xFFFF) / 65535f;
                rng = rng * 1664525u + 1013904223u;
                float widT = (rng & 0xFFFF) / 65535f;
                float length = s.LengthMin + (s.LengthMax - s.LengthMin) * lenT;
                float ws     = s.WidthStart * (0.7f + 0.6f * widT);
                float we     = s.WidthEnd   * (0.7f + 0.6f * widT);

                Vector3 dir;
                if (n == 1) dir = Vector3.UnitY;
                else
                {
                    float ang = (float)k / n * MathF.Tau;
                    dir = new Vector3(MathF.Cos(ang), 0.05f, MathF.Sin(ang));
                    dir = Vector3.Normalize(dir);
                }
                var basePos = s.Anchor;
                var tipPos  = s.Anchor + dir * length;
                var toCam   = cameraPos - (basePos + tipPos) * 0.5f;
                var perp    = Vector3.Cross(dir, toCam);
                if (perp.LengthSquared() < 0.0001f) perp = Vector3.UnitX;
                perp = Vector3.Normalize(perp);

                var bL = basePos + perp * ws;
                var bR = basePos - perp * ws;
                var tL = tipPos  + perp * we;
                var tR = tipPos  - perp * we;
                EmitRibbonVert(bL, 0f, 0f, c0);
                EmitRibbonVert(bR, 1f, 0f, c0);
                EmitRibbonVert(tL, 0f, 1f, c1);
                EmitRibbonVert(bR, 1f, 0f, c0);
                EmitRibbonVert(tR, 1f, 1f, c1);
                EmitRibbonVert(tL, 0f, 1f, c1);
            }
        }
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-A — build a flat textured ring at
    /// each cylinder's anchor.</summary>
    void EmitCylinderQuads(Vector3 cameraPos)
    {
        if (_cylinders.Count == 0) return;
        for (int ci = 0; ci < _cylinders.Count; ci++)
        {
            var c = _cylinders[ci];
            // Lifetime alpha — tin ramp at start, tout ramp at end.
            float life = c.TotalLife;
            float t = c.Elapsed;
            float alpha = 1f;
            if (c.FadeIn > 0f && t < c.FadeIn)
                alpha = t / c.FadeIn;
            float toutStart = life - c.FadeOut;
            if (c.FadeOut > 0f && t > toutStart)
                alpha = MathF.Max(0f, 1f - (t - toutStart) / c.FadeOut);
            if (alpha <= 0.001f) continue;

            int seg = c.Segments;
            float rOuter = c.RadiusOuter;
            float rInner = rOuter * c.ThicknessRatio;
            float spinOff = c.Spin * t / MathF.Tau; // U-coordinate shift per spin

            EnsureRibbonCapacity(_ribbonVertCount + seg * 6);

            // Build ring on the Y plane at Anchor.Y. Each segment k spans
            // angles [angK, angK+1]. Two triangles per segment connecting
            // the inner ring to the outer ring.
            var color = c.Color;
            color.W *= alpha;
            for (int k = 0; k < seg; k++)
            {
                float a0 = (float)k       / seg * MathF.Tau;
                float a1 = (float)(k + 1) / seg * MathF.Tau;
                float u0 = (float)k       / seg + spinOff;
                float u1 = (float)(k + 1) / seg + spinOff;
                var ax = c.Anchor;
                var p0Out = ax + new Vector3(MathF.Cos(a0) * rOuter, 0f, MathF.Sin(a0) * rOuter);
                var p1Out = ax + new Vector3(MathF.Cos(a1) * rOuter, 0f, MathF.Sin(a1) * rOuter);
                var p0In  = ax + new Vector3(MathF.Cos(a0) * rInner, 0f, MathF.Sin(a0) * rInner);
                var p1In  = ax + new Vector3(MathF.Cos(a1) * rInner, 0f, MathF.Sin(a1) * rInner);
                // Triangle 1: outer0, outer1, inner0
                EmitRibbonVert(p0Out, u0, 1f, color);
                EmitRibbonVert(p1Out, u1, 1f, color);
                EmitRibbonVert(p0In,  u0, 0f, color);
                // Triangle 2: outer1, inner1, inner0
                EmitRibbonVert(p1Out, u1, 1f, color);
                EmitRibbonVert(p1In,  u1, 0f, color);
                EmitRibbonVert(p0In,  u0, 0f, color);
            }
        }
    }

    void DrawRibbons(Matrix4x4 view, Matrix4x4 proj)
    {
        _gl.BindBuffer(GLEnum.ArrayBuffer, _ribbonVbo);
        unsafe
        {
            fixed (float* p = _ribbonVerts)
                _gl.BufferData(GLEnum.ArrayBuffer,
                    (nuint)(_ribbonVertCount * RibbonVertFloats * sizeof(float)),
                    p, GLEnum.DynamicDraw);
        }

        bool depthWasOn = _gl.IsEnabled(GLEnum.DepthTest);
        bool cullWasOn  = _gl.IsEnabled(GLEnum.CullFace);
        _gl.Enable(GLEnum.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(GLEnum.CullFace);
        _gl.Enable(GLEnum.Blend);
        _gl.BlendFunc(GLEnum.One, GLEnum.One); // straight additive — guaranteed glow

        _ribbonShader.Use();
        _ribbonShader.SetMatrix4("uView", view);
        _ribbonShader.SetMatrix4("uProj", proj);
        for (int i = 0; i < _textures.Length; i++)
            _ribbonShader.SetInt("uTex" + i, i);
        for (int slot = 0; slot < _textures.Length; slot++)
        {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + slot));
            _gl.BindTexture(GLEnum.Texture2D, _textures[slot]?.Handle ?? 0);
        }

        _gl.BindVertexArray(_ribbonVao);
        // Phase 21-SC-SPELL-VISUAL-A/B — 3-pass ribbon draw, single VBO:
        //   bolts     [0, _cylinderVertStart)        — BoltTexSlot
        //   cylinders [_cylinderVertStart, _srayVertStart) — CylinderTexSlot
        //   srays     [_srayVertStart, _ribbonVertCount)   — slot 2 (sparkle01,
        //                                                     soft glow read)
        if (_cylinderVertStart > 0)
        {
            _ribbonShader.SetInt("uSlot", BoltTexSlot);
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)_cylinderVertStart);
        }
        if (_srayVertStart > _cylinderVertStart)
        {
            _ribbonShader.SetInt("uSlot", CylinderTexSlot);
            _gl.DrawArrays(GLEnum.Triangles, _cylinderVertStart,
                (uint)(_srayVertStart - _cylinderVertStart));
        }
        if (_ribbonVertCount > _srayVertStart)
        {
            // sray uses slot 2 (b_sfx_sparkle01) — DS1 ships srays with NO
            // texture param, but a soft sparkle billboard reads as the
            // additive black-to-gold streak the color0/color1 gradient
            // implies, far better than a hard solid quad would.
            _ribbonShader.SetInt("uSlot", 2);
            _gl.DrawArrays(GLEnum.Triangles, _srayVertStart,
                (uint)(_ribbonVertCount - _srayVertStart));
        }
        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        if (!depthWasOn) _gl.Disable(GLEnum.DepthTest);
        if (cullWasOn)   _gl.Enable(GLEnum.CullFace);
    }

    void EmitProjectileHeads()
    {
        // Phase 21-SC-SPELL-VFX — every frame, stamp a bright "head" particle
        // for each in-flight projectile. Without this the only visible mark of
        // the projectile would be the drifting trail particles, which lag
        // behind the actual current position.
        for (int pi = 0; pi < _projectiles.Count; pi++)
        {
            var pr = _projectiles[pi];
            var c = pr.Color; c.W = 1f;
            _particles.Add(new Particle
            {
                Position  = pr.Position,
                Velocity  = Vector3.Zero,
                Accel     = Vector3.Zero,
                Color0    = c,
                Color1    = new Vector4(c.X, c.Y, c.Z, 0f),
                Scale0    = pr.Scale * 1.1f,
                Scale1    = pr.Scale * 0.9f,
                Life      = 0.05f,
                TotalLife = 0.05f,
                TexSlot   = 0,
                Additive  = 1,
            });
        }
    }

    void EnsureInstanceCapacity(int n)
    {
        int needed = n * InstanceFloats;
        if (_instanceBuffer.Length < needed)
            _instanceBuffer = new float[Math.Max(_instanceBuffer.Length * 2, needed)];
    }

    static float Rand(float lo, float hi) =>
        lo + (hi - lo) * (float)Random.Shared.NextDouble();

    public void Dispose()
    {
        for (int i = 0; i < _textures.Length; i++) _textures[i]?.Dispose();
        _gl.DeleteBuffer(_vboQuad);
        _gl.DeleteBuffer(_vboInstance);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
        _gl.DeleteBuffer(_ribbonVbo);
        _gl.DeleteVertexArray(_ribbonVao);
        _ribbonShader.Dispose();
    }

    static class MathHelper
    {
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    const string VertexSrc = @"#version 330 core
layout(location=0) in vec2 aQuadPos;
layout(location=1) in vec2 aQuadUv;
layout(location=2) in vec3 aPos;
layout(location=3) in float aScale;
layout(location=4) in vec4 aColor;
layout(location=5) in vec2 aSlotAdd;
uniform mat4 uView;
uniform mat4 uProj;
out vec2 vUv;
out vec4 vColor;
out float vSlot;
out float vAdditive;
void main(){
  // Build a camera-facing basis from the inverse of the view's rotation.
  // Columns 0/1 of the view matrix's transpose are right + up in world.
  vec3 right = vec3(uView[0][0], uView[1][0], uView[2][0]);
  vec3 up    = vec3(uView[0][1], uView[1][1], uView[2][1]);
  vec3 world = aPos + (right * aQuadPos.x + up * aQuadPos.y) * aScale;
  gl_Position = uProj * uView * vec4(world, 1.0);
  vUv = aQuadUv;
  vColor = aColor;
  vSlot = aSlotAdd.x;
  vAdditive = aSlotAdd.y;
}";

    const string FragmentSrc = @"#version 330 core
in vec2 vUv;
in vec4 vColor;
in float vSlot;
in float vAdditive;
uniform sampler2D uTex0;
uniform sampler2D uTex1;
uniform sampler2D uTex2;
uniform sampler2D uTex3;
uniform sampler2D uTex4;
uniform sampler2D uTex5;
uniform sampler2D uTex6;
uniform sampler2D uTex7;
uniform sampler2D uTex8;
out vec4 frag;
void main(){
  vec4 tex;
  int slot = int(vSlot + 0.5);
  if      (slot == 0) tex = texture(uTex0, vUv);
  else if (slot == 1) tex = texture(uTex1, vUv);
  else if (slot == 2) tex = texture(uTex2, vUv);
  else if (slot == 3) tex = texture(uTex3, vUv);
  else if (slot == 4) tex = texture(uTex4, vUv);
  else if (slot == 5) tex = texture(uTex5, vUv);
  else if (slot == 6) tex = texture(uTex6, vUv);
  else if (slot == 7) tex = texture(uTex7, vUv);
  else                tex = texture(uTex8, vUv);
  vec4 c = tex * vColor;
  // Additive look via the shared SrcAlpha/OneMinusSrcAlpha blend: brighten
  // RGB and keep alpha so the fragment actually contributes (the prior
  // c.a=0 zeroed every additive particle and made bolts/sparks invisible).
  if (vAdditive > 0.5) {
    c.rgb *= 1.8;
  }
  frag = c;
}";

    // --- ribbon shader ----------------------------------------------------
    // CPU has already computed world-space positions with shared per-junction
    // perpendiculars, so the vertex shader just transforms straight through.
    const string RibbonVertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
layout(location=2) in vec4 aColor;
uniform mat4 uView;
uniform mat4 uProj;
out vec2 vUv;
out vec4 vColor;
void main(){
  gl_Position = uProj * uView * vec4(aPos, 1.0);
  vUv    = aUv;
  vColor = aColor;
}";

    const string RibbonFragmentSrc = @"#version 330 core
in vec2 vUv;
in vec4 vColor;
uniform sampler2D uTex0;
uniform sampler2D uTex1;
uniform sampler2D uTex2;
uniform sampler2D uTex3;
uniform sampler2D uTex4;
uniform sampler2D uTex5;
uniform sampler2D uTex6;
uniform sampler2D uTex7;
uniform sampler2D uTex8;
uniform int uSlot;
uniform sampler2D uTex9;
uniform sampler2D uTex10;
uniform sampler2D uTex11;
out vec4 frag;
void main(){
  vec4 tex;
  if      (uSlot == 0) tex = texture(uTex0, vUv);
  else if (uSlot == 1) tex = texture(uTex1, vUv);
  else if (uSlot == 2) tex = texture(uTex2, vUv);
  else if (uSlot == 3) tex = texture(uTex3, vUv);
  else if (uSlot == 4) tex = texture(uTex4, vUv);
  else if (uSlot == 5) tex = texture(uTex5, vUv);
  else if (uSlot == 6) tex = texture(uTex6, vUv);
  else if (uSlot == 7) tex = texture(uTex7, vUv);
  else if (uSlot == 8) tex = texture(uTex8, vUv);
  else if (uSlot == 9) tex = texture(uTex9, vUv);
  else if (uSlot == 10) tex = texture(uTex10, vUv);
  else                 tex = texture(uTex11, vUv);
  vec4 c = tex * vColor;
  c.rgb *= 1.8; // ribbons always read as additive bright glow
  frag = c;
}";
}

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

    /// <summary>SC-SPELL-AUDIT — fade toward a cooled version of the color:
    /// warm (red-dominant) colors take the authored fire bias (embers cool
    /// red-orange), everything else fades proportionally in its own hue so
    /// blue sparks and green acid never drift fire-orange. The warm test
    /// mirrors the VM's MapMode rule (R dominant within ~10%).</summary>
    public static Vector4 WarmAwareFade(Vector4 c, float rK, float gK, float bK)
    {
        bool warm = c.X >= c.Y * 1.1f && c.X >= c.Z * 1.1f;
        if (warm) return new Vector4(c.X * rK, c.Y * gK, c.Z * bK, 0f);
        float k = (rK + gK + bK) / 3f;
        return new Vector4(c.X * k, c.Y * k, c.Z * k, 0f);
    }
    public float   Scale0;          // world-space half-size at birth
    public float   Scale1;          // world-space half-size at death
    public float   Life;            // remaining seconds
    public float   TotalLife;       // total seconds (for lerp)
    public byte    TexSlot;         // 0=fire, 1=smoke, 2=sparkle, 3=spark
    public byte    Additive;        // 1 = additive blend, 0 = alpha
    /// <summary>Phase 23d-2a — DS1 explosion `fade_range(s,e,0)` window as
    /// life fractions: alpha holds at Color0.W until <see cref="FadeStart"/>,
    /// reaches 0 by <see cref="FadeEnd"/>. FadeEnd 0 = disabled (legacy
    /// linear Color0→Color1 lerp).</summary>
    public float   FadeStart;
    public float   FadeEnd;
    /// <summary>Phase 23-fold — explosion ground interaction: particles
    /// bounce off the spawn plane with <see cref="Rebound"/> elasticity
    /// (doc default 0.85); splat() sticks them where they land instead.</summary>
    public byte    Bounce;     // 0=off, 1=bounce, 2=splat-stick
    public float   Rebound;
    public float   GroundY;
    /// <summary>Rigid attachment: when non-zero, this particle is pinned to a
    /// moving anchor (a flying projectile's motion handle) — every frame its
    /// world position is re-set to that anchor plus <see cref="LocalOffset"/>,
    /// so the whole cluster travels as one body (a single fireball) regardless
    /// of the projectile's speed profile. On losing its anchor it detaches and
    /// resumes normal velocity integration.</summary>
    public int     FollowId;
    public Vector3 LocalOffset;
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
    /// <summary>Phase 23d-2a — SU 212 lightning: displacement is a SIGNED
    /// range [MinDisplace, Displace] (zap authors -0.15..0.15); Subd /
    /// MinSubd control subdivision density. 0 = renderer defaults.</summary>
    public float   MinDisplace;
    public float   Subd;
    public float   MinSubd;
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
    /// <summary>Phase 23d-2b — SU-212 ring profiles, each the documented
    /// (start, end, increment) triple. Ring 0 = radius Rp0 at height Hp0,
    /// ring 1 = Rp1 at Hp1; the tube wall connects them. Increment steps
    /// the value per second toward end (clamped); increment 0 with
    /// start != end lerps across TotalLife; otherwise static.</summary>
    public Vector3 Rp0, Rp1, Hp0, Hp1;
    public float   Alpha;           // alpha(f) starting alpha
    /// <summary>Axis spin rate, radians/sec (spin(15) ≈ 2.39 rev/s).</summary>
    public float   Spin;
    public float   FadeIn;          // tin — seconds to ramp alpha 0→1
    public float   FadeOut;         // tout — seconds to ramp alpha 1→0 at end
    public float   TotalLife;       // dur
    public float   Elapsed;
    public Vector3 Rotate;          // rotate(x,y,z) — degrees
    public Vector3 IRotate;         // irotate(x,y,z) — degrees/sec
    public byte    TexSlot;         // ParticleSystem texture slot index
    public byte    Segments;        // segments(N), doc default 16
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
public struct SrayRay
{
    public Vector3 Anchor;
    public Vector4 Color0;          // color0 (base end)
    public Vector4 Color1;          // color1 (tip end)
    public float   Theta, Phi;      // current polar angles (radians)
    public float   ThetaRate, PhiRate; // per-ray spin rates (SU theta/phi triples)
    public float   Radius;          // origin-sphere offset
    public float   Length;          // per-ray length (lmin..lmax roll)
    public float   WidthStart, WidthEnd;
    public float   Alpha;           // current alpha
    public float   FadeRate;        // per-ray fade per second (alpha triple roll)
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

    /// <summary>ALPHA-2V — Options → Video → Object Detail. Scales the DENSITY
    /// of decorative particle spawns (fire licks, smoke puffs, sparks, weather
    /// precipitation) without touching functional singles (projectile heads,
    /// bolts, tracers stay). 1.0 = authored counts; the Options slider maps
    /// its 0..1 detail to a 0.25..1.0 floor so minimum detail still reads.</summary>
    public static float DetailScale = 1f;
    static int Detail(int n) => n <= 1 || DetailScale >= 0.999f
        ? n : Math.Max(1, (int)MathF.Round(n * DetailScale));

    /// <summary>ALPHA-2V — Options → Video → Gamma, mirrored from the world
    /// shader so glow effects track the scene's response curve.</summary>
    public float Gamma = 1f;

    private readonly List<Particle>        _particles   = new(2048);
    // Live world positions of attachment anchors (projectile motion handles),
    // keyed by motion id. Refreshed each tick by the VM; attached particles
    // re-pin to these in Tick.
    private readonly Dictionary<int, Vector3> _followAnchors = new(8);
    private readonly List<LightningBolt>   _bolts       = new(64);
    // Phase 21-SC-SPELL-VISUAL-A — DS1 cylinder primitive, drawn via the
    // ribbon path with a different texture slot.
    private readonly List<SpellCylinder>   _cylinders   = new(16);
    // Phase 23d-2b — sray: live rays plus their timed-spawn emitters
    // (SU 212: srate spawns one ray per period up to count).
    private readonly List<SrayRay>         _srayRays    = new(32);
    private List<(SraySpec Spec, float Carry, int Spawned, float Age)> _srayEmits = new(8);
    // Phase 23d-2e — ribbon draw ranges: (vertStart, vertCount, texSlot).
    private readonly List<(int Start, int Count, int Slot)> _ribbonRanges = new(8);
    void AddRibbonRange(ref int start, int slot)
    {
        if (_ribbonVertCount > start)
        {
            _ribbonRanges.Add((start, _ribbonVertCount - start, slot));
            start = _ribbonVertCount;
        }
    }
    // Phase 23d-2e — polygonal-explosion shards + tessellated spheres.
    private struct PolyShard
    {
        public Vector3 Pos, Vel, Rot, RotRate; // Rot in degrees
        public Vector4 Color;
        public float Age, Life, FadeStart, FadeEnd, Size, GroundY;
        public byte Sides;
        public bool Stuck;
    }
    private readonly List<PolyShard> _polyShards = new(64);
    private struct SphereMeshP
    {
        public SphereMeshSpec Spec;
        public float Age;
    }
    private readonly List<SphereMeshP> _sphereMeshes = new(4);
    // Phase 23d-2b — flurry: procedural spherical-polar swarm particles.
    private struct FlurryP
    {
        public Vector3 Anchor;
        public Vector4 Color;
        public float Radius, Phi, Theta, PhiRate, ThetaRate, AmpSpeed, Amp;
        public float Age, Life, FadeIn, FadeOut;
        public float GrowStart, GrowMid, GrowEnd;
        public byte  Tex;
    }
    private readonly List<FlurryP> _flurry = new(64);
    // Phase 23d-2d — SPE / sparkles / charge procedural swarms.
    private struct SpeP
    {
        public SpeSpec Spec;
        public int Index;
        public float Age;
    }
    private readonly List<SpeP> _spes = new(64);
    private struct SparkleP
    {
        public Vector3 Position;
        public Vector4 Color;
        public float YVel, Age, Life, Size;
        public byte Tex;
    }
    private readonly List<SparkleP> _sparkles = new(64);
    private struct ChargeP
    {
        public Vector3 Anchor, Dir;   // Dir = spawn direction on the sphere
        public Vector4 Color;
        public float Radius, Speed, Age, Life, CenterSize, IAlpha;
        public bool IsCenter;
        public byte Tex;
    }
    private readonly List<ChargeP> _charges = new(32);
    private readonly List<SpellProjectile> _projectiles = new(32);

    // SC-WEATHER-E — mood-driven precipitation. Rain drops render as
    // velocity-aligned ribbon streaks (a billboard dot doesn't read as rain);
    // snow rides the standard billboard list via MaintainWeather. Densities
    // are authored drops/flakes-per-second (mood [rain]/[snow] blocks,
    // shipped 30–225 rain / 75–500 snow); alive-count caps keep a blizzard
    // from unbounded growth when the fall time is long.
    private struct RainDropP
    {
        public Vector3 Position, Velocity;
        public float GroundY;
    }
    private readonly List<RainDropP> _rainDrops = new(512);
    private struct SnowFlakeP
    {
        public Vector3 Position, Velocity;
        public float Life, TotalLife, Size, GroundY;
        public byte Stuck;   // 1 = landed; holds position, fades out
    }
    private readonly List<SnowFlakeP> _snowFlakes = new(1024);
    private float _rainCarry, _snowCarry;
    private readonly Random _wxRng = new();
    private const int MaxRainDrops = 900;
    private const int MaxSnowFlakes = 3200;

    /// <summary>SC-WEATHER-E — steady-state weather emitter, called once per
    /// frame while a mood authors precipitation. <paramref name="rainPerSec"/>
    /// and <paramref name="snowPerSec"/> are the authored densities (already
    /// drift-adjusted by WeatherSystem); <paramref name="wind"/> is the mood
    /// wind vector (shears rain, drifts snow); spawn volume is a disc over
    /// <paramref name="focus"/> (the player), and everything dies at
    /// <paramref name="floorY"/> — DS1 rain doesn't collide with geometry,
    /// it draws in a cylinder around the camera focus exactly like this.</summary>
    public void MaintainWeather(float dt, float rainPerSec, float snowPerSec,
        Vector3 wind, Vector3 focus, float floorY)
    {
        if (rainPerSec > 0f)
        {
            _rainCarry += rainPerSec * dt;
            int n = (int)_rainCarry;
            _rainCarry -= n;
            for (int i = 0, dn = Detail(n); i < dn && _rainDrops.Count < MaxRainDrops; i++)
            {
                var (dx, dz) = RandomInDisc(22f);
                _rainDrops.Add(new RainDropP
                {
                    Position = new Vector3(focus.X + dx, focus.Y + 10f + (float)_wxRng.NextDouble() * 4f, focus.Z + dz),
                    // Heavy vertical speed + a wind shear: retail streaks lean
                    // with the storm but stay predominantly vertical.
                    Velocity = wind * 1.5f + new Vector3(0f, -15f - (float)_wxRng.NextDouble() * 4f, 0f),
                    GroundY = floorY,
                });
            }
        }
        else _rainCarry = 0f;

        if (snowPerSec > 0f)
        {
            _snowCarry += snowPerSec * dt;
            int n = (int)_snowCarry;
            _snowCarry -= n;
            for (int i = 0, dn = Detail(n); i < dn && _snowFlakes.Count < MaxSnowFlakes; i++)
            {
                var (dx, dz) = RandomInDisc(20f);
                float fall = 0.9f + (float)_wxRng.NextDouble() * 0.8f;
                float spawnH = 7f + (float)_wxRng.NextDouble() * 4f;
                _snowFlakes.Add(new SnowFlakeP
                {
                    Position = new Vector3(focus.X + dx, focus.Y + spawnH, focus.Z + dz),
                    Velocity = wind + new Vector3(
                        ((float)_wxRng.NextDouble() - 0.5f) * 0.7f,
                        -fall,
                        ((float)_wxRng.NextDouble() - 0.5f) * 0.7f),
                    // Life covers the fall plus a short linger on the ground
                    // (landed flakes hold + fade — a cheap dusting read).
                    Life = (focus.Y + spawnH - floorY) / fall + 1.5f,
                    TotalLife = (focus.Y + spawnH - floorY) / fall + 1.5f,
                    Size = 0.03f + (float)_wxRng.NextDouble() * 0.03f,
                    GroundY = floorY,
                });
            }
        }
        else _snowCarry = 0f;
    }

    private (float dx, float dz) RandomInDisc(float radius)
    {
        // Uniform disc sample (sqrt-radius) so density doesn't bunch center.
        float r = radius * MathF.Sqrt((float)_wxRng.NextDouble());
        float a = (float)_wxRng.NextDouble() * MathF.Tau;
        return (MathF.Cos(a) * r, MathF.Sin(a) * r);
    }

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
    // (Phase 23d-2e — the bolt/cylinder/sray split points were replaced
    // by the _ribbonRanges slot list.)
    private readonly Shader _ribbonShader;
    private readonly uint _ribbonVao;
    private readonly uint _ribbonVbo;

    public int LiveParticleCount   => _particles.Count;
    public int LiveBoltCount       => _bolts.Count;
    public int LiveCylinderCount   => _cylinders.Count;
    public int LiveSrayCount       => _srayRays.Count;
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
        for (int i = 0, dn = Detail(count); i < dn; i++)
        {
            var jitter = new Vector3(Rand(-0.15f, 0.15f), 0f, Rand(-0.15f, 0.15f)) * scale;
            _particles.Add(new Particle
            {
                Position  = position + jitter,
                Velocity  = new Vector3(Rand(-0.05f, 0.05f), Rand(0.6f, 1.1f), Rand(-0.05f, 0.05f)) * scale,
                Accel     = new Vector3(0f, 0.4f * scale, 0f),
                Color0    = color,
                Color1    = Particle.WarmAwareFade(color, 0.4f, 0.2f, 0.05f),
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
        for (int i = 0, dn = Detail(count); i < dn; i++)
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
        for (int i = 0, dn = Detail(count); i < dn; i++)
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
        for (int i = 0, dn = Detail(count); i < dn; i++)
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
    /// <summary>Legacy flat-ring entry point — adapts onto the SU-212 tube
    /// as a near-flat annulus (outer wall at y=0, inner at y=0.05).</summary>
    public void SpawnCylinder(Vector3 anchor, Vector4 color,
                              float radiusOuter,    float thicknessRatio,
                              float spinPerSec,     float fadeIn, float fadeOut,
                              float duration,       byte texSlot, byte segments)
    {
        float outer = MathF.Max(0.05f, radiusOuter);
        float inner = outer * Math.Clamp(thicknessRatio, 0f, 0.95f);
        var spec = new CylinderSpec
        {
            Anchor   = anchor,
            Color    = color,
            Rp0      = new Vector3(outer, outer, 0f),
            Rp1      = new Vector3(inner, inner, 0f),
            Hp0      = Vector3.Zero,
            Hp1      = new Vector3(0.05f, 0.05f, 0f),
            Alpha    = 1f,
            Spin     = spinPerSec,
            FadeIn   = MathF.Max(0f, fadeIn),
            FadeOut  = MathF.Max(0f, fadeOut),
            Duration = MathF.Max(0.10f, duration),
            TexSlot  = texSlot,
            Segments = segments < 4 ? (byte)4 : segments,
        };
        SpawnCylinderTube(in spec);
    }

    /// <summary>Phase 23d-2b — SU-212 cylinder tube between two animated
    /// (start, end, increment) ring profiles.</summary>
    public void SpawnCylinderTube(in CylinderSpec spec)
    {
        _cylinders.Add(new SpellCylinder
        {
            Anchor    = spec.Anchor,
            Color     = spec.Color,
            Rp0       = spec.Rp0,
            Rp1       = spec.Rp1,
            Hp0       = spec.Hp0,
            Hp1       = spec.Hp1,
            Alpha     = Math.Clamp(spec.Alpha <= 0f ? 0.5f : spec.Alpha, 0.02f, 1f),
            Spin      = spec.Spin,
            FadeIn    = MathF.Max(0f, spec.FadeIn),
            FadeOut   = MathF.Max(0f, spec.FadeOut),
            TotalLife = MathF.Max(0.10f, spec.Duration),
            Elapsed   = 0f,
            Rotate    = spec.Rotate,
            IRotate   = spec.IRotate,
            TexSlot   = spec.TexSlot,
            Segments  = spec.Segments < 4 ? (byte)4 : spec.Segments,
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

        for (int i = 0, dn = Detail(count); i < dn; i++)
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
                // Color1: warm fire bias only for genuinely warm colors —
                // SC-SPELL-AUDIT: the unconditional bias made every blue and
                // green spell trail drift fire-orange (spark read as fire).
                Color1    = Particle.WarmAwareFade(color, 0.6f, 0.3f, 0.10f),
                Scale0    = scale * 0.6f,
                Scale1    = scale * 1.2f,
                Life      = lifetime,
                TotalLife = lifetime,
                TexSlot   = 0,         // b_sfx_fireball-01
                Additive  = 1,
            });
        }
    }

    /// <summary>Legacy sray entry point — adapts onto the SU-212 timed
    /// emitter with doc-default spin/fade triples.</summary>
    public void SpawnSray(Vector3 anchor, Vector4 colorStart, Vector4 colorEnd,
                          float lengthMin, float lengthMax,
                          float widthStart, float widthEnd,
                          float duration, int rayCount)
    {
        var spec = new SraySpec
        {
            Anchor = anchor, Color0 = colorStart, Color1 = colorEnd,
            Radius = 0.0005f,
            Count  = Math.Clamp(rayCount, 1, 96),
            LMin   = MathF.Max(0.05f, lengthMin),
            LMax   = MathF.Max(lengthMin, lengthMax),
            WsMin  = widthStart, WsMax = widthStart,
            WeMin  = widthEnd,   WeMax = widthEnd,
            Theta  = new Vector3(0f, 1f, 3f),
            Phi    = new Vector3(0f, 1f, -3f),
            Alpha  = new Vector3(1f, 0.5f, 0.5f),
            SpawnPeriod = 0.015f,
            Duration    = MathF.Max(0.05f, duration),
        };
        SpawnSrayTimed(in spec);
    }

    /// <summary>Phase 23d-2b — SU-212 sray emitter: one ray per
    /// SpawnPeriod up to Count; each ray gets its own length/width rolls,
    /// polar spin rates from the theta/phi (start, min-inc, max-inc)
    /// triples, and a fade rate from the alpha triple.</summary>
    public void SpawnSrayTimed(in SraySpec spec)
    {
        if (spec.Count <= 0) return;
        _srayEmits.Add((spec, 1f /* spawn the first ray immediately */, 0, 0f));
    }

    /// <summary>Phase 23-fold — SU-212 LineTracer as a pinned, fading
    /// tracer ribbon from source to target (reuses the ray pipeline with
    /// zero spin and the authored fade_rate).</summary>
    public void SpawnLineTracer(Vector3 source, Vector3 target,
                                Vector4 color0, Vector4 color1,
                                float fadeRate, float tin, float tout)
    {
        var dir = target - source;
        float len = dir.Length();
        if (len < 0.01f) return;
        dir /= len;
        _srayRays.Add(new SrayRay
        {
            Anchor     = source,
            Color0     = color0,
            Color1     = color1,
            Theta      = MathF.Atan2(dir.Z, dir.X),
            Phi        = MathF.Acos(Math.Clamp(dir.Y, -1f, 1f)),
            ThetaRate  = 0f,
            PhiRate    = 0f,
            Radius     = 0f,
            Length     = len,
            WidthStart = 0.035f,
            WidthEnd   = 0.02f,
            Alpha      = 1f,
            FadeRate   = MathF.Max(0.05f, fadeRate),
        });
    }

    void EmitOneRay(in SraySpec spec)
    {
        _srayRays.Add(new SrayRay
        {
            Anchor     = spec.Anchor,
            Color0     = spec.Color0,
            Color1     = spec.Color1,
            Theta      = spec.Theta.X + Rand(0f, MathF.Tau),
            Phi        = spec.Phi.X + Rand(0f, MathF.PI),
            ThetaRate  = Rand(MathF.Min(spec.Theta.Y, spec.Theta.Z), MathF.Max(spec.Theta.Y, spec.Theta.Z)),
            PhiRate    = Rand(MathF.Min(spec.Phi.Y, spec.Phi.Z), MathF.Max(spec.Phi.Y, spec.Phi.Z)),
            Radius     = spec.Radius,
            Length     = Rand(MathF.Min(spec.LMin, spec.LMax), MathF.Max(spec.LMin, spec.LMax)),
            WidthStart = Rand(MathF.Min(spec.WsMin, spec.WsMax), MathF.Max(spec.WsMin, spec.WsMax)),
            WidthEnd   = Rand(MathF.Min(spec.WeMin, spec.WeMax), MathF.Max(spec.WeMin, spec.WeMax)),
            Alpha      = spec.Alpha.X,
            FadeRate   = Rand(MathF.Min(spec.Alpha.Y, spec.Alpha.Z), MathF.Max(spec.Alpha.Y, spec.Alpha.Z)),
        });
    }

    /// <summary>Phase 23d-2b — SU-212 flurry swarm. Random initial polar
    /// angles + amp phase per particle; common authored rates.</summary>
    public void SpawnFlurry(in FlurrySpec spec)
    {
        for (int i = 0, dn = Detail(spec.Count); i < dn; i++)
        {
            _flurry.Add(new FlurryP
            {
                Anchor    = spec.Anchor,
                Color     = spec.Color,
                Radius    = MathF.Max(0.05f, spec.Radius),
                Phi       = Rand(0f, MathF.Tau),
                Theta     = Rand(0f, MathF.Tau),
                PhiRate   = spec.IPhi,
                ThetaRate = spec.ITheta,
                AmpSpeed  = spec.IAmp,
                Amp       = spec.Amplitude * 0.25f * spec.Radius, // interference relative to orbit size
                Age       = 0f,
                Life      = MathF.Max(0.10f, spec.Duration),
                FadeIn    = spec.FadeIn,
                FadeOut   = spec.FadeOut,
                GrowStart = spec.GrowStart,
                GrowMid   = spec.GrowMid,
                GrowEnd   = spec.GrowEnd,
                Tex       = spec.TexSlot,
            });
        }
    }

    public void SpawnSpark(Vector3 position, Vector4 color, float scale, float duration, int count = 16)
    {
        for (int i = 0, dn = Detail(count); i < dn; i++)
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
                Color1    = Particle.WarmAwareFade(color, 1f, 0.5f, 0.2f),
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
        for (int i = 0, dn = Detail(count); i < dn; i++)
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
        => SpawnLightning(source, target, color, duration, -displace, displace, 0f, 0f);

    /// <summary>Phase 23d-2a — full-fidelity bolt per SU 212: signed
    /// [minDisplace, maxDisplace] stray range + subd/minsubd subdivision
    /// density (0 = defaults).</summary>
    public void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration,
                               float minDisplace, float maxDisplace, float subd, float minSubd)
    {
        _bolts.Add(new LightningBolt
        {
            Source      = source,
            Target      = target,
            Color       = color,
            Life        = duration,
            TotalLife   = duration,
            Seed        = (uint)Random.Shared.Next(),
            Displace    = maxDisplace,
            MinDisplace = minDisplace,
            Subd        = subd,
            MinSubd     = minSubd,
        });
    }

    /// <summary>Phase 23d-2a — authored-parameter explosion (SU 212). When
    /// <see cref="ExplosionSpec.SpawnOver"/> (srate) is set the burst is
    /// spread across that many seconds via the tick-drained queue instead
    /// of popping in one frame.</summary>
    public void SpawnExplosion(in ExplosionSpec spec)
    {
        if (spec.Count <= 0) return;
        if (spec.SpawnOver > 0.02f)
        {
            _burstQueue.Add((spec, 0f, spec.Count));
            return;
        }
        EmitExplosion(in spec, spec.Count);
    }

    // Deferred explosion bursts being spread over srate seconds:
    // (spec, fractional carry, particles left to spawn).
    private readonly List<(ExplosionSpec Spec, float Carry, int Remaining)> _burstQueue = new(8);

    void EmitExplosion(in ExplosionSpec spec, int n)
    {
        for (int i = 0, dn = Detail(n); i < dn; i++)
        {
            // Direction: omni_dir = uniform sphere (the (z, theta) trick);
            // directional default = up, per the doc's "explode in a set
            // direction". Speed = rand[vmin, vmax] scalar along that
            // direction, plus the ivel base vector, plus rvel random
            // per-axis jitter.
            Vector3 dir;
            if (spec.OmniDir)
            {
                float z  = Rand(-1f, 1f);
                float th = Rand(0f, MathF.Tau);
                float rr = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
                dir = new Vector3(rr * MathF.Cos(th), z, rr * MathF.Sin(th));
            }
            else dir = Vector3.UnitY;

            var vel = dir * Rand(spec.VMin, spec.VMax)
                    + spec.IVel
                    + new Vector3(Rand(-spec.RVel.X, spec.RVel.X),
                                  Rand(-spec.RVel.Y, spec.RVel.Y),
                                  Rand(-spec.RVel.Z, spec.RVel.Z));

            // Spawn point within the radius: disc for directional bursts,
            // squashed ball for omni.
            float ang = Rand(0f, MathF.Tau);
            float rad = Rand(0f, MathF.Max(0.01f, spec.Radius));
            var pos = spec.Anchor + new Vector3(
                MathF.Cos(ang) * rad,
                spec.OmniDir ? Rand(-spec.Radius, spec.Radius) * 0.5f : 0f,
                MathF.Sin(ang) * rad);

            float sc = Rand(MathF.Min(spec.ScaleMin, spec.ScaleMax),
                            MathF.Max(spec.ScaleMin, spec.ScaleMax));
            // Phase 23-fold — doc color1 is per-particle VARIANCE: each
            // particle tints color0 + variance*rand.
            var pc = spec.Color;
            if (spec.HasColorVar)
            {
                float vr = Rand(0f, 1f);
                pc = new Vector4(
                    Math.Clamp(pc.X + spec.ColorVar.X * vr, 0f, 1f),
                    Math.Clamp(pc.Y + spec.ColorVar.Y * vr, 0f, 1f),
                    Math.Clamp(pc.Z + spec.ColorVar.Z * vr, 0f, 1f),
                    pc.W);
            }
            _particles.Add(new Particle
            {
                Position  = pos,
                Velocity  = vel,
                Accel     = new Vector3(0f, -3.5f, 0f),
                Bounce    = (byte)(spec.Splat ? 2 : spec.Bounce ? 1 : 0),
                Rebound   = spec.Rebound,
                GroundY   = spec.GroundY,
                Color0    = pc,
                Color1    = new Vector4(pc.X, pc.Y, pc.Z, 0f),
                Scale0    = sc,
                Scale1    = sc,
                Life      = spec.Duration,
                TotalLife = spec.Duration,
                TexSlot   = spec.TexSlot,
                Additive  = 1,
                FadeStart = spec.FadeStart,
                FadeEnd   = spec.FadeEnd,
            });
        }
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

    /// <summary>SC-TORCH-FLAME — a crisp, dense torch/sconce flame (not the
    /// general SpawnFire plume, which grows + lives long and reads as smoke).
    /// Licks start wide at the base with a hot yellow-white core and TAPER to
    /// a point as they rise, short-lived and tight so the shape stays a flame
    /// rather than a drifting cloud. Additive so it glows.</summary>
    public float MaintainTorchFlame(Vector3 position, float scale, float dt, float rate, float carry)
    {
        float budget = carry + rate * dt;
        int n = (int)budget;
        for (int i = 0, dn = Detail(n); i < dn; i++)
        {
            var jitter = new Vector3(Rand(-0.04f, 0.04f), Rand(0f, 0.03f), Rand(-0.04f, 0.04f)) * scale;
            // Longer-lived, gentler-rising licks so the flame breathes slowly
            // instead of strobing (the fast churn was short life + fast rise).
            float life = Rand(0.75f, 1.15f);
            // Hotter (whiter) near the core, cooler (redder) on the outer licks.
            float heat = Rand(0f, 1f);
            var hot = new Vector4(1.0f, 0.72f + 0.22f * heat, 0.30f + 0.30f * heat, 1f);
            _particles.Add(new Particle
            {
                Position  = position + jitter,
                Velocity  = new Vector3(Rand(-0.025f, 0.025f), Rand(0.38f, 0.62f), Rand(-0.025f, 0.025f)) * scale,
                Accel     = new Vector3(0f, 0.2f * scale, 0f),
                Color0    = hot,                                     // bright hot base
                Color1    = new Vector4(0.85f, 0.18f, 0.03f, 0f),    // fade to red, gone
                Scale0    = scale * 0.85f,                           // wide at the wick
                Scale1    = scale * 0.10f,                           // taper to a lick tip
                Life      = life,
                TotalLife = life,
                TexSlot   = 0,
                Additive  = 1,
            });
        }
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

    /// <summary>Phase 23d-2d — exact SU-212 SPE. Particle positions are
    /// computed procedurally in the Draw fill from the documented
    /// two-sine average; nothing to integrate here beyond age.</summary>
    public void SpawnSpe(in SpeSpec spec)
    {
        for (int i = 0, dn = Detail(spec.Count); i < dn; i++)
            _spes.Add(new SpeP { Spec = spec, Index = i, Age = 0f });
    }

    /// <summary>Phase 23d-2d — SU-212 sparkles: static spawn points inside
    /// the radius ball; alpha in over the first half of life, out over the
    /// second; yvel is the only motion.</summary>
    public void SpawnSparkles(in SparklesSpec spec)
    {
        for (int i = 0, dn = Detail(spec.Count); i < dn; i++)
        {
            float z  = Rand(-1f, 1f);
            float th = Rand(0f, MathF.Tau);
            float rr = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            float rad = Rand(0f, MathF.Max(0.02f, spec.Radius));
            _sparkles.Add(new SparkleP
            {
                Position = spec.Anchor + new Vector3(rr * MathF.Cos(th), z, rr * MathF.Sin(th)) * rad,
                Color    = spec.Color,
                YVel     = spec.YVel,
                Age      = Rand(0f, spec.Duration * 0.4f), // staggered twinkle
                Life     = MathF.Max(0.10f, spec.Duration),
                Size     = 0.10f * MathF.Max(0.05f, spec.PSize),
                Tex      = spec.TexSlot,
            });
        }
    }

    /// <summary>Phase 23d-2d — SU-212 charge: particles spawn on the
    /// radius sphere and coalesce inward while alpha ramps in at ialpha;
    /// a center particle grows toward centersize.</summary>
    public void SpawnCharge(in ChargeSpec spec)
    {
        for (int i = 0, dn = Detail(spec.Count); i < dn; i++)
        {
            float z  = Rand(-1f, 1f);
            float th = Rand(0f, MathF.Tau);
            float rr = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            _charges.Add(new ChargeP
            {
                Anchor     = spec.Anchor,
                Dir        = new Vector3(rr * MathF.Cos(th), z, rr * MathF.Sin(th)),
                Color      = spec.Color,
                Radius     = MathF.Max(0.05f, spec.Radius),
                Speed      = spec.Speed0 * Rand(0.6f, 1.4f),
                Age        = Rand(0f, 0.25f * spec.Duration),
                Life       = MathF.Max(0.15f, spec.Duration),
                CenterSize = spec.CenterSize,
                IAlpha     = spec.IAlpha,
                Tex        = spec.TexSlot,
            });
        }
        _charges.Add(new ChargeP
        {
            Anchor = spec.Anchor, Dir = Vector3.Zero,
            Color = spec.Color, Radius = 0f, Speed = 0f,
            Age = 0f, Life = MathF.Max(0.15f, spec.Duration),
            CenterSize = spec.CenterSize, IAlpha = spec.IAlpha,
            IsCenter = true, Tex = spec.TexSlot,
        });
    }

    /// <summary>Phase 23d-2c — authored plume pump (SU-212 fire/smoke/
    /// steam). Population model: DS1's count is a live-particle cap and
    /// alphafade sets the fade-out speed, so steady-state spawn rate =
    /// count / life with life ≈ 1/alphafade.</summary>
    /// <summary>VM hook: publish the current world position of an attachment
    /// anchor (a flying projectile's motion handle) so particles pinned to it
    /// re-pin here next Tick. Call every tick the projectile is alive.</summary>
    public void SetFollowAnchor(int id, Vector3 pos)
    {
        if (id != 0) _followAnchors[id] = pos;
    }

    /// <summary>Drop an anchor so its particles detach and fly off on their
    /// last velocity (the projectile hit or expired).</summary>
    public void ClearFollowAnchor(int id)
    {
        if (id != 0) _followAnchors.Remove(id);
    }

    public float MaintainPlume(in SiegeFX.Core.Sfx.PlumeSpec s, Vector3 position, float age, float dt, float carry)
    {
        float life = Math.Clamp(1f / MathF.Max(0.15f, s.AlphaFade), 0.30f, 3.5f);
        float rate = MathF.Max(1f, s.Count / life);
        float budget = carry + rate * dt;
        int n = (int)budget;
        if (n > 0) BurstPlume(in s, position, n, age);
        return budget - n;
    }

    /// <summary>Phase 23d-2c — spawn <paramref name="n"/> plume particles
    /// at once (the instant() volume fill, and MaintainPlume's per-tick
    /// batch). Spawn point: the [min_radius, max_radius] annulus (or the
    /// anchor→LineEnd segment for line() fires), plus the random Y
    /// displacement range; velocity/accel/flamesize/fctrl as authored.</summary>
    public void BurstPlume(in SiegeFX.Core.Sfx.PlumeSpec s, Vector3 position, int n)
        => BurstPlume(in s, position, n, age: 0f);

    public void BurstPlume(in SiegeFX.Core.Sfx.PlumeSpec s, Vector3 position, int n, float age)
    {
        float lifeBase = Math.Clamp(1f / MathF.Max(0.15f, s.AlphaFade), 0.30f, 3.5f);
        // Phase 23-fold - burn_body sine wobble: max_radius grows by
        // sin(sinpos + sinspeed*age) toward radius_rmax.
        float maxR = s.MaxRadius;
        if (s.HasSinAnim)
        {
            float cap = s.RadiusRMax > 0f ? s.RadiusRMax : s.MaxRadius + 1f;
            maxR = Math.Clamp(s.MaxRadius
                + MathF.Sin(s.SinPos + s.SinSpeed * age) * MathF.Max(0f, cap - s.MaxRadius),
                s.MinRadius, cap);
        }
        for (int i = 0, dn = Detail(n); i < dn; i++)
        {
            Vector3 pos;
            if (s.Line)
            {
                // Phase 23-fold - gom_icesnake: the spawn point WALKS the
                // line at linespeed from linepos instead of scattering.
                float t01 = s.HasLineAnim
                    ? (s.LinePos + s.LineSpeed * age) % 1f
                    : Rand(0f, 1f);
                if (t01 < 0f) t01 += 1f;
                pos = Vector3.Lerp(position, s.LineEnd, t01);
                pos.Y += Rand(MathF.Min(s.MinDisplace, s.MaxDisplace), MathF.Max(s.MinDisplace, s.MaxDisplace));
            }
            else if (s.Instant)
            {
                // instant()/area() volume fill (campfire, area_smoke): scatter
                // across the [min_radius, max_radius] disc to paint the volume.
                float ang = Rand(0f, MathF.Tau);
                float r = Rand(MathF.Min(s.MinRadius, maxR), MathF.Max(s.MinRadius, maxR));
                pos = position + new Vector3(MathF.Cos(ang) * r, 0f, MathF.Sin(ang) * r);
                pos.Y += Rand(MathF.Min(s.MinDisplace, s.MaxDisplace), MathF.Max(s.MinDisplace, s.MaxDisplace));
            }
            else
            {
                // Continuous point emitter (fireshot's flying fire, torches):
                // spawn a tight, center-dense ball around the emitter so a
                // moving fire stays a cohesive fireball. max_radius is DS1's
                // drift bound for velocity-driven spread, NOT a spawn scatter —
                // flinging particles across a max_radius(7) disc is what turned
                // the fireball into a spray. Ball radius = the authored displace
                // jitter (fireshot ±1), falling back to a small radius-derived
                // default when displace is unauthored.
                // Pack the cluster TIGHT. b_sfx_fireball-01 is itself a whole
                // little fireball, so spaced-out particles read as many
                // explosions, not one. A small spawn ball makes them fully
                // overlap — their bright centers saturate into a single fused
                // mass and the PARTICLE SIZE (not the spread) sets the
                // fireball's extent. Cap hard so a big displace can't scatter
                // the fire back into separate blobs.
                float jr = 0.28f * MathF.Max(MathF.Abs(s.MinDisplace), MathF.Abs(s.MaxDisplace));
                if (jr < 0.01f) jr = MathF.Max(0.12f, s.MaxRadius * 0.4f);
                jr = MathF.Min(jr, 0.35f);
                float z   = Rand(-1f, 1f);
                float rho = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
                float th  = Rand(0f, MathF.Tau);
                float rr  = jr * Rand(0f, 1f);   // center-biased → dense core, soft edge
                pos = position + new Vector3(rho * MathF.Cos(th), z, rho * MathF.Sin(th)) * rr;
            }

            var vel = s.Velocity * Rand(0.8f, 1.2f) + s.CarrierVelocity;
            // Particle radius tracks the authored scale the same way the
            // hand-tuned SpawnFire one-shot does (~scale*0.6→*1.1 over life).
            // The old *0.4 rendered every authored plume at half size, so
            // fireshot's scale(1.1) core never fused into a solid fireball.
            float fs = MathF.Max(0.04f, s.FlameSize * 0.8f);
            float s0, s1;
            if (s.HasFctrl)
            {
                // fctrl(min, max, i) — flame expansion over its life:
                // birth scaled by |min|, death by |max| (doc: "controls
                // for how a flame expands over time").
                s0 = fs * Math.Clamp(MathF.Abs(s.Fctrl.X) * 0.5f, 0.10f, 2.0f);
                s1 = fs * Math.Clamp(MathF.Abs(s.Fctrl.Y) * 0.7f, 0.10f, 3.0f);
            }
            else
            {
                s0 = fs * (s.Kind == 2 ? 0.5f : 0.6f);
                s1 = fs * (s.Kind == 2 ? 1.5f : 1.1f);
            }
            // Fire fades warm ONLY when its authored color is warm; cool
            // plumes keep their hue (SC-SPELL-AUDIT — the unconditional warm
            // fade dragged blue/green spell plumes to fire-orange).
            var c1 = s.Kind == 0
                ? Particle.WarmAwareFade(s.Color, 0.5f, 0.25f, 0.08f)
                : new Vector4(s.Color.X, s.Color.Y, s.Color.Z, 0f);
            float life = lifeBase * Rand(0.8f, 1.2f);
            _particles.Add(new Particle
            {
                Position  = pos,
                Velocity  = vel,
                Accel     = s.Accel,
                Color0    = s.Color,
                Color1    = c1,
                Scale0    = s0,
                Scale1    = s1,
                Life      = life,
                TotalLife = life,
                TexSlot   = s.TexSlot,
                Additive  = (byte)(s.Kind == 1 ? 0 : 1),
                // Rigid attach to the emitter's moving anchor: the fixed local
                // offset is exactly the tight-ball jitter, so the whole plume
                // rides the projectile as one fireball.
                FollowId    = s.FollowId,
                LocalOffset = s.FollowId != 0 ? pos - position : Vector3.Zero,
            });
        }
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
        for (int i = 0, dn = Detail(n); i < dn; i++)
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
        // Phase 23d-2a — drain srate-spread explosion bursts.
        for (int i = _burstQueue.Count - 1; i >= 0; i--)
        {
            var (spec, carry, remaining) = _burstQueue[i];
            float rate = spec.Count / MathF.Max(0.02f, spec.SpawnOver);
            carry += rate * dt;
            int n = Math.Min(remaining, (int)carry);
            if (n > 0)
            {
                EmitExplosion(in spec, n);
                carry -= n;
                remaining -= n;
            }
            if (remaining <= 0) { _burstQueue.RemoveAt(i); continue; }
            _burstQueue[i] = (spec, carry, remaining);
        }

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0f) { _particles.RemoveAt(i); continue; }
            p.Velocity += p.Accel * dt;
            p.Position += p.Velocity * dt;
            // Rigid attachment: pin to the live anchor so the cluster rides a
            // moving projectile as one body. If the anchor is gone (projectile
            // hit / expired), detach and let the last velocity carry it off.
            if (p.FollowId != 0)
            {
                if (_followAnchors.TryGetValue(p.FollowId, out var anchor))
                    p.Position = anchor + p.LocalOffset;
                else
                    { p.FollowId = 0; p.Velocity = Vector3.Zero; } // freeze + fade at impact
            }
            // Phase 23-fold — ground plane interaction for explosion
            // particles: bounce with rebound elasticity, or splat-stick.
            if (p.Bounce != 0 && p.Position.Y < p.GroundY && p.Velocity.Y < 0f)
            {
                p.Position.Y = p.GroundY;
                if (p.Bounce == 2) { p.Velocity = Vector3.Zero; p.Accel = Vector3.Zero; }
                else p.Velocity = new Vector3(p.Velocity.X * 0.8f, -p.Velocity.Y * p.Rebound, p.Velocity.Z * 0.8f);
            }
            _particles[i] = p;
        }
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            b.Life -= dt;
            if (b.Life <= 0f) { _bolts.RemoveAt(i); continue; }
            _bolts[i] = b;
        }
        // SC-WEATHER-E — integrate precipitation. Rain dies at the floor
        // (no bounce; DS1 rain has no impact splash we can source, noted
        // as a possible polish item). Snow sticks at the floor and rides
        // its remaining Life out as a fading ground dusting.
        for (int i = _rainDrops.Count - 1; i >= 0; i--)
        {
            var r = _rainDrops[i];
            r.Position += r.Velocity * dt;
            if (r.Position.Y <= r.GroundY) { _rainDrops.RemoveAt(i); continue; }
            _rainDrops[i] = r;
        }
        for (int i = _snowFlakes.Count - 1; i >= 0; i--)
        {
            var s = _snowFlakes[i];
            s.Life -= dt;
            if (s.Life <= 0f) { _snowFlakes.RemoveAt(i); continue; }
            if (s.Stuck == 0)
            {
                s.Position += s.Velocity * dt;
                if (s.Position.Y <= s.GroundY)
                {
                    s.Position.Y = s.GroundY + 0.01f;
                    s.Stuck = 1;
                    // Landed: cap the linger so a long-fall flake doesn't
                    // sit on the ground for its whole unspent fall budget.
                    s.Life = MathF.Min(s.Life, 1.5f);
                }
            }
            _snowFlakes[i] = s;
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
        // Phase 23d-2b — sray emitters spawn one ray per SpawnPeriod up to
        // Count (emitter also expires at its Duration); live rays spin
        // their polar angles and fade at their per-ray rate.
        for (int i = _srayEmits.Count - 1; i >= 0; i--)
        {
            var (spec, carry, spawned, age) = _srayEmits[i];
            age += dt;
            carry += dt / MathF.Max(0.002f, spec.SpawnPeriod);
            while (carry >= 1f && spawned < spec.Count)
            {
                EmitOneRay(in spec);
                carry -= 1f;
                spawned++;
            }
            if (spawned >= spec.Count || (spec.Duration > 0.01f && age >= spec.Duration))
            { _srayEmits.RemoveAt(i); continue; }
            _srayEmits[i] = (spec, carry, spawned, age);
        }
        for (int i = _srayRays.Count - 1; i >= 0; i--)
        {
            var r = _srayRays[i];
            r.Theta += r.ThetaRate * dt;
            r.Phi   += r.PhiRate * dt;
            r.Alpha -= r.FadeRate * dt;
            if (r.Alpha <= 0.002f) { _srayRays.RemoveAt(i); continue; }
            _srayRays[i] = r;
        }
        // Phase 23d-2b — flurry swarm aging.
        for (int i = _flurry.Count - 1; i >= 0; i--)
        {
            var f = _flurry[i];
            f.Age += dt;
            if (f.Age >= f.Life) { _flurry.RemoveAt(i); continue; }
            _flurry[i] = f;
        }
        // Phase 23d-2d — SPE / sparkles / charge aging.
        for (int i = _spes.Count - 1; i >= 0; i--)
        {
            var s = _spes[i];
            s.Age += dt;
            if (s.Age >= s.Spec.Duration) { _spes.RemoveAt(i); continue; }
            _spes[i] = s;
        }
        for (int i = _sparkles.Count - 1; i >= 0; i--)
        {
            var s = _sparkles[i];
            s.Age += dt;
            if (s.Age >= s.Life) { _sparkles.RemoveAt(i); continue; }
            s.Position.Y += s.YVel * dt;
            _sparkles[i] = s;
        }
        for (int i = _charges.Count - 1; i >= 0; i--)
        {
            var s = _charges[i];
            s.Age += dt;
            if (s.Age >= s.Life) { _charges.RemoveAt(i); continue; }
            _charges[i] = s;
        }
        // Phase 23d-2e — poly shards fly, rotate, and stick where they
        // land on the spawn plane; sphere meshes just age.
        for (int i = _polyShards.Count - 1; i >= 0; i--)
        {
            var s = _polyShards[i];
            s.Age += dt;
            if (s.Age >= s.Life) { _polyShards.RemoveAt(i); continue; }
            if (!s.Stuck)
            {
                s.Vel += new Vector3(0f, -6f, 0f) * dt;
                s.Pos += s.Vel * dt;
                s.Rot += s.RotRate * dt;
                if (s.Pos.Y <= s.GroundY && s.Vel.Y < 0f)
                {
                    s.Pos.Y = s.GroundY;
                    s.Stuck = true;
                }
            }
            _polyShards[i] = s;
        }
        for (int i = _sphereMeshes.Count - 1; i >= 0; i--)
        {
            var m = _sphereMeshes[i];
            m.Age += dt;
            if (m.Age >= m.Spec.Duration) { _sphereMeshes.RemoveAt(i); continue; }
            _sphereMeshes[i] = m;
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
        _followAnchors.Clear();
        _bolts.Clear();
        _projectiles.Clear();
        _cylinders.Clear();
        _srayRays.Clear();
        _srayEmits.Clear();
        _flurry.Clear();
        _spes.Clear();
        _sparkles.Clear();
        _charges.Clear();
        _polyShards.Clear();
        _sphereMeshes.Clear();
        _burstQueue.Clear();
    }

    public void Draw(Matrix4x4 view, Matrix4x4 proj, Vector3 cameraPos)
    {
        if (_particles.Count == 0 && _bolts.Count == 0
            && _projectiles.Count == 0 && _cylinders.Count == 0
            && _srayRays.Count == 0 && _flurry.Count == 0
            && _spes.Count == 0 && _sparkles.Count == 0 && _charges.Count == 0
            && _polyShards.Count == 0 && _sphereMeshes.Count == 0
            && _rainDrops.Count == 0 && _snowFlakes.Count == 0) return;

        // Phase 23d-2e — every ribbon-drawn primitive appends its slice to
        // _ribbonRanges with the texture slot it draws with (250+ = pure
        // vertex color). Cylinders group by their authored texture so
        // cyl_01/cyl_02 spells stop collapsing onto cyl_03.
        _ribbonVertCount = 0;
        _ribbonRanges.Clear();
        int rangeStart = 0;
        EmitBoltQuads(cameraPos);
        AddRibbonRange(ref rangeStart, BoltTexSlot);
        for (int slot = 0; slot < _textures.Length; slot++)
        {
            EmitCylinderQuads((byte)slot);
            AddRibbonRange(ref rangeStart, slot);
        }
        EmitSrayQuads(cameraPos);
        AddRibbonRange(ref rangeStart, 2); // sparkle01 — soft additive streak read
        // SC-WEATHER-E — rain streaks ride the ribbon path (velocity-aligned
        // thin quads, pure vertex color slot).
        EmitRainQuads(cameraPos);
        AddRibbonRange(ref rangeStart, 250);
        EmitPolyShards();
        EmitSphereMeshes();
        AddRibbonRange(ref rangeStart, 250);
        EmitProjectileHeads();

        // Ribbon (lightning bolts + cylinders) draw before particles so
        // additive particles still composite on top cleanly.
        if (_ribbonVertCount > 0) DrawRibbons(view, proj);

        int totalInstances = _particles.Count + _flurry.Count
                           + _spes.Count + _sparkles.Count + _charges.Count
                           + _snowFlakes.Count;
        if (totalInstances == 0) return;

        // Rebuild instance buffer.
        EnsureInstanceCapacity(totalInstances);
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            float frac = 1f - (p.Life / MathF.Max(0.0001f, p.TotalLife));
            float scale = MathHelper.Lerp(p.Scale0, p.Scale1, frac);
            Vector4 col;
            if (p.FadeEnd > 0f)
            {
                // Phase 23d-2a — fade_range window: hold full alpha until
                // FadeStart of life, reach 0 by FadeEnd. RGB stays Color0
                // (DS1's explosion color1 is per-particle VARIANCE, not an
                // end-fade tint).
                float a = frac <= p.FadeStart ? 1f
                        : frac >= p.FadeEnd   ? 0f
                        : 1f - (frac - p.FadeStart) / MathF.Max(0.0001f, p.FadeEnd - p.FadeStart);
                col = new Vector4(p.Color0.X, p.Color0.Y, p.Color0.Z, p.Color0.W * a);
            }
            else col = Vector4.Lerp(p.Color0, p.Color1, frac);
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

        // Phase 23d-2b — append flurry swarm instances: procedural
        // spherical-polar position with sinusoidal radial interference,
        // tin/tout alpha envelope, grow_params (start→mid→end) scale.
        for (int i = 0; i < _flurry.Count; i++)
        {
            var f = _flurry[i];
            float r = f.Radius + f.Amp * MathF.Sin(f.AmpSpeed * f.Age * MathF.Tau);
            float phi = f.Phi + f.PhiRate * f.Age;
            float th  = f.Theta + f.ThetaRate * f.Age;
            var pos = f.Anchor + new Vector3(
                MathF.Sin(phi) * MathF.Cos(th) * r,
                MathF.Cos(phi) * r,
                MathF.Sin(phi) * MathF.Sin(th) * r);
            float alpha = 1f;
            if (f.FadeIn > 0f && f.Age < f.FadeIn) alpha = f.Age / f.FadeIn;
            float toutStart = f.Life - f.FadeOut;
            if (f.FadeOut > 0f && f.Age > toutStart)
                alpha = MathF.Min(alpha, MathF.Max(0f, 1f - (f.Age - toutStart) / f.FadeOut));
            float t01 = f.Age / f.Life;
            float grow = t01 < 0.5f
                ? MathHelper.Lerp(f.GrowStart, f.GrowMid, t01 * 2f)
                : MathHelper.Lerp(f.GrowMid, f.GrowEnd, (t01 - 0.5f) * 2f);
            int o = (_particles.Count + i) * InstanceFloats;
            _instanceBuffer[o + 0] = pos.X;
            _instanceBuffer[o + 1] = pos.Y;
            _instanceBuffer[o + 2] = pos.Z;
            _instanceBuffer[o + 3] = 0.12f * MathF.Max(0.05f, grow) * MathF.Max(0.5f, f.Radius);
            _instanceBuffer[o + 4] = f.Color.X;
            _instanceBuffer[o + 5] = f.Color.Y;
            _instanceBuffer[o + 6] = f.Color.Z;
            _instanceBuffer[o + 7] = f.Color.W * alpha;
            _instanceBuffer[o + 8] = f.Tex;
            _instanceBuffer[o + 9] = 1f; // additive
            _instanceBuffer[o + 10] = 0f;
            _instanceBuffer[o + 11] = 0f;
        }

        // Phase 23d-2d — SPE / sparkles / charge procedural instances.
        int cursor = _particles.Count + _flurry.Count;
        for (int i = 0; i < _spes.Count; i++, cursor++)
        {
            var s = _spes[i];
            var sp = s.Spec;
            float t = s.Age;
            int idx = s.Index;
            // Exact doc model per axis: (sin(i0 + v0*t + s0*i) + sin(i1 + v1*t + s1*i)) / 2.
            var pos = sp.Anchor + new Vector3(
                (MathF.Sin(sp.Index0.X + sp.Speed0.X * t + sp.Space0.X * idx) + MathF.Sin(sp.Index1.X + sp.Speed1.X * t + sp.Space1.X * idx)) * 0.5f,
                (MathF.Sin(sp.Index0.Y + sp.Speed0.Y * t + sp.Space0.Y * idx) + MathF.Sin(sp.Index1.Y + sp.Speed1.Y * t + sp.Space1.Y * idx)) * 0.5f,
                (MathF.Sin(sp.Index0.Z + sp.Speed0.Z * t + sp.Space0.Z * idx) + MathF.Sin(sp.Index1.Z + sp.Speed1.Z * t + sp.Space1.Z * idx)) * 0.5f)
                * sp.Radius;
            float alpha = 1f;
            if (sp.FadeIn > 0f && t < sp.FadeIn) alpha = t / sp.FadeIn;
            float toutStart = sp.Duration - sp.FadeOut;
            if (sp.FadeOut > 0f && t > toutStart)
                alpha = MathF.Min(alpha, MathF.Max(0f, 1f - (t - toutStart) / sp.FadeOut));
            FillInstance(cursor, pos, sp.Scale, new Vector4(sp.Color.X, sp.Color.Y, sp.Color.Z, sp.Color.W * alpha), sp.TexSlot);
        }
        for (int i = 0; i < _sparkles.Count; i++, cursor++)
        {
            var s = _sparkles[i];
            float half = s.Life * 0.5f;
            float alpha = s.Age < half ? s.Age / half : MathF.Max(0f, 1f - (s.Age - half) / half);
            FillInstance(cursor, s.Position, s.Size, new Vector4(s.Color.X, s.Color.Y, s.Color.Z, s.Color.W * alpha), s.Tex);
        }
        for (int i = 0; i < _charges.Count; i++, cursor++)
        {
            var s = _charges[i];
            float t01 = s.Age / s.Life;
            float alpha = MathF.Min(1f, s.Age * s.IAlpha);
            if (t01 > 0.85f) alpha *= MathF.Max(0f, 1f - (t01 - 0.85f) / 0.15f);
            Vector3 pos;
            float size;
            if (s.IsCenter)
            {
                pos = s.Anchor;
                size = MathF.Max(0.03f, s.CenterSize * t01);
            }
            else
            {
                float r = MathF.Max(0f, s.Radius * (1f - t01 * MathF.Max(0.3f, s.Speed)));
                pos = s.Anchor + s.Dir * r;
                size = 0.08f;
            }
            FillInstance(cursor, pos, size, new Vector4(s.Color.X, s.Color.Y, s.Color.Z, s.Color.W * alpha), s.Tex);
        }

        // SC-WEATHER-E — snow flakes: soft white billboards (sparkle slot,
        // alpha blend). Airborne flakes ride at steady alpha; landed flakes
        // fade out over their remaining linger.
        for (int i = 0; i < _snowFlakes.Count; i++, cursor++)
        {
            var s = _snowFlakes[i];
            float alpha = 0.85f;
            float age = s.TotalLife - s.Life;
            if (age < 0.4f) alpha *= age / 0.4f;                       // spawn fade-in
            if (s.Stuck == 1) alpha *= Math.Clamp(s.Life / 1.5f, 0f, 1f); // ground fade
            else if (s.Life < 0.6f) alpha *= s.Life / 0.6f;            // altitude expiry
            int o = cursor * InstanceFloats;
            _instanceBuffer[o + 0] = s.Position.X;
            _instanceBuffer[o + 1] = s.Position.Y;
            _instanceBuffer[o + 2] = s.Position.Z;
            _instanceBuffer[o + 3] = s.Size;
            _instanceBuffer[o + 4] = 0.95f;
            _instanceBuffer[o + 5] = 0.96f;
            _instanceBuffer[o + 6] = 1.0f;
            _instanceBuffer[o + 7] = alpha;
            _instanceBuffer[o + 8] = 2f;   // sparkle texture slot
            _instanceBuffer[o + 9] = 0f;   // alpha blend, not additive
            _instanceBuffer[o + 10] = 0f;
            _instanceBuffer[o + 11] = 0f;
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboInstance);
        unsafe
        {
            fixed (float* p = _instanceBuffer)
                _gl.BufferData(GLEnum.ArrayBuffer,
                    (nuint)(totalInstances * InstanceFloats * sizeof(float)),
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
        _shader.SetFloat("uGamma", Gamma);
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
        _gl.DrawArraysInstanced(GLEnum.Triangles, 0, 6, (uint)totalInstances);

        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        if (!depthWasOn) _gl.Disable(GLEnum.DepthTest);
    }

    // Phase 23d-2d — shared instance-buffer writer for the procedural
    // swarms (SPE / sparkles / charge). Additive, no fade window.
    void FillInstance(int slot, Vector3 pos, float scale, Vector4 color, byte tex)
    {
        int o = slot * InstanceFloats;
        _instanceBuffer[o + 0] = pos.X;
        _instanceBuffer[o + 1] = pos.Y;
        _instanceBuffer[o + 2] = pos.Z;
        _instanceBuffer[o + 3] = scale;
        _instanceBuffer[o + 4] = color.X;
        _instanceBuffer[o + 5] = color.Y;
        _instanceBuffer[o + 6] = color.Z;
        _instanceBuffer[o + 7] = color.W;
        _instanceBuffer[o + 8] = tex;
        _instanceBuffer[o + 9] = 1f;
        _instanceBuffer[o + 10] = 0f;
        _instanceBuffer[o + 11] = 0f;
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
        // (Draw resets _ribbonVertCount before the emit sequence.)
        if (_bolts.Count == 0) return;
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

            // Phase 23d-2a — subdivision density from subd/minsubd
            // (SU 212: subd = "level of bolt subdivision" default 0.4,
            // minsubd = minimum level default 2.0). Exact GPG mapping is
            // undocumented; inference: segments scale with length x
            // subd-ratio, floored by 2^minsubd — 4u bolts land at the
            // pre-23d 24 segments when unauthored. Revisit against DS1
            // side-by-side capture.
            // SU 212 Lightning is RECURSIVE MIDPOINT DISPLACEMENT, not uniform
            // per-vertex noise. subd/minsubd are subdivision LEVELS (minsubd =
            // floor, subd = added density); longer bolts get a level or two more
            // so detail stays proportional. 2^levels segments; cap at 6 (=64
            // segments, the scratch size).
            float subd    = b.Subd    > 0f ? b.Subd    : 0.4f;
            float minsubd = b.MinSubd > 0f ? b.MinSubd : 2.0f;
            int levels = (int)Math.Clamp(minsubd + len * 0.35f * (subd / 0.4f), 2f, 6f);

            // Displacement is the TOP-LEVEL perpendicular stray range
            // (mindisplace..maxdisplace, e.g. chain_lightning's ±0.1). It HALVES
            // each subdivision level — that halving is what yields a taut, thin
            // bolt with a few sharp kinks instead of the old fuzzy band. When
            // unauthored, a small length-relative default keeps it tight.
            bool hasRange = b.Displace > 0.001f || b.MinDisplace < -0.001f;
            float dMin = hasRange ? b.MinDisplace : -MathF.Min(len * 0.05f, 0.22f);
            float dMax = hasRange ? b.Displace    :  MathF.Min(len * 0.05f, 0.22f);

            // Orthonormal perpendicular basis to the bolt direction (world-up
            // `side` alone isn't perpendicular once the bolt tilts).
            var perpA = side;
            var perpB = Vector3.Cross(fwd, side);
            perpB = perpB.LengthSquared() < 1e-6f ? Vector3.UnitY : Vector3.Normalize(perpB);

            // Thin, roughly screen-constant half-width: scale with camera
            // distance so the bolt stays a ~1.5px wire near or far instead of
            // ballooning into a tube up close. DS1's bolts are wire-thin.
            float distMid = Vector3.Distance(cameraPos, (b.Source + b.Target) * 0.5f);
            float thickness = Math.Clamp(distMid * 0.0011f, 0.008f, 0.045f);

            float lifeAlpha = MathF.Max(0.25f, 1f - frac);
            var core = b.Color;
            // Bright core that KEEPS the authored hue — the old +0.6 floor washed
            // every bolt to near-white regardless of color0.
            core.X = MathF.Min(1f, core.X * 0.6f + 0.35f);
            core.Y = MathF.Min(1f, core.Y * 0.6f + 0.35f);
            core.Z = MathF.Min(1f, core.Z * 0.6f + 0.35f);
            core.W = lifeAlpha;

            // Pass 1: recursive midpoint displacement. Begin with [source,target]
            // and subdivide `levels` times; each new midpoint strays perpendicular
            // by a random [dMin,dMax] offset whose amplitude halves each level.
            var pts = _boltPathScratch;
            pts[0] = b.Source;
            pts[1] = b.Target;
            int n = 1;              // current segment count
            float ampScale = 1f;
            for (int lvl = 0; lvl < levels; lvl++)
            {
                for (int i = n; i >= 1; i--) pts[2 * i] = pts[i];   // spread to even slots
                for (int i = 0; i < n; i++)
                {
                    var mid = (pts[2 * i] + pts[2 * i + 2]) * 0.5f;
                    rng = rng * 1664525u + 1013904223u;
                    float rx = (dMin + ((rng & 0xFFFF) / 65535f) * (dMax - dMin)) * ampScale;
                    rng = rng * 1664525u + 1013904223u;
                    float ry = (dMin + ((rng & 0xFFFF) / 65535f) * (dMax - dMin)) * ampScale;
                    pts[2 * i + 1] = mid + perpA * rx + perpB * ry;
                }
                n *= 2;
                ampScale *= 0.5f;
            }
            int segments = n;

            // Pass 2: shared per-junction perpendicular = neighbor-averaged
            // tangent crossed with view-direction-to-camera at this point.
            var perp = _boltPerpScratch;
            for (int s = 0; s <= segments; s++)
            {
                Vector3 tangent;
                if      (s == 0)        tangent = pts[1] - pts[0];
                else if (s == segments) tangent = pts[segments] - pts[segments - 1];
                else                    tangent = pts[s + 1] - pts[s - 1];
                var toCam = cameraPos - pts[s];
                var sv = Vector3.Cross(tangent, toCam);
                float slen = sv.Length();
                if (slen < 1e-6f) sv = Vector3.UnitY; else sv /= slen;
                perp[s] = sv * thickness;
            }

            // Pass 3: emit 2 tris (6 verts) per segment; corners at s and s+1
            // share their perpendicular with the neighbor segment.
            EnsureRibbonCapacity(_ribbonVertCount + segments * 6);
            for (int s = 0; s < segments; s++)
            {
                float u0 = (float)s / segments;
                float u1 = (float)(s + 1) / segments;
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
        // Phase 23d-2b — every live ray is an individually-spinning
        // tapered streak: direction from its current polar angles,
        // base offset by the origin-sphere radius, alpha per-ray.
        if (_srayRays.Count == 0) return;
        EnsureRibbonCapacity(_ribbonVertCount + _srayRays.Count * 6);
        for (int si = 0; si < _srayRays.Count; si++)
        {
            var r = _srayRays[si];
            float a = Math.Clamp(r.Alpha, 0f, 1f);
            var c0 = r.Color0; c0.W *= a;
            var c1 = r.Color1; c1.W *= a;

            var dir = new Vector3(
                MathF.Sin(r.Phi) * MathF.Cos(r.Theta),
                MathF.Cos(r.Phi),
                MathF.Sin(r.Phi) * MathF.Sin(r.Theta));
            var basePos = r.Anchor + dir * r.Radius;
            var tipPos  = basePos + dir * r.Length;
            var toCam   = cameraPos - (basePos + tipPos) * 0.5f;
            var perp    = Vector3.Cross(dir, toCam);
            if (perp.LengthSquared() < 0.0001f) perp = Vector3.UnitX;
            perp = Vector3.Normalize(perp);

            var bL = basePos + perp * r.WidthStart;
            var bR = basePos - perp * r.WidthStart;
            var tL = tipPos  + perp * r.WidthEnd;
            var tR = tipPos  - perp * r.WidthEnd;
            EmitRibbonVert(bL, 0f, 0f, c0);
            EmitRibbonVert(bR, 1f, 0f, c0);
            EmitRibbonVert(tL, 0f, 1f, c1);
            EmitRibbonVert(bR, 1f, 0f, c0);
            EmitRibbonVert(tR, 1f, 1f, c1);
            EmitRibbonVert(tL, 0f, 1f, c1);
        }
    }

    /// <summary>SC-WEATHER-E — one thin velocity-aligned quad per rain drop.
    /// The streak trails the drop's head along its (wind-sheared) velocity;
    /// the head vertex is brighter than the tail so the eye reads downward
    /// motion even in a still frame. Pure vertex color (slot 250), alpha
    /// blend via the shared ribbon pass.</summary>
    void EmitRainQuads(Vector3 cameraPos)
    {
        if (_rainDrops.Count == 0) return;
        EnsureRibbonCapacity(_ribbonVertCount + _rainDrops.Count * 6);
        var cHead = new Vector4(0.72f, 0.78f, 0.90f, 0.34f);
        var cTail = new Vector4(0.72f, 0.78f, 0.90f, 0.05f);
        const float streakLen = 0.55f;
        const float halfWidth = 0.012f;
        for (int i = 0; i < _rainDrops.Count; i++)
        {
            var r = _rainDrops[i];
            var dir = Vector3.Normalize(r.Velocity);
            var head = r.Position;
            var tail = head - dir * streakLen;
            var toCam = cameraPos - head;
            var perp = Vector3.Cross(dir, toCam);
            if (perp.LengthSquared() < 0.0001f) perp = Vector3.UnitX;
            perp = Vector3.Normalize(perp) * halfWidth;

            var hL = head + perp; var hR = head - perp;
            var tL = tail + perp; var tR = tail - perp;
            EmitRibbonVert(hL, 0f, 0f, cHead);
            EmitRibbonVert(hR, 1f, 0f, cHead);
            EmitRibbonVert(tL, 0f, 1f, cTail);
            EmitRibbonVert(hR, 1f, 0f, cHead);
            EmitRibbonVert(tR, 1f, 1f, cTail);
            EmitRibbonVert(tL, 0f, 1f, cTail);
        }
    }

    /// <summary>Phase 23d-2b — SU-212 profile animation: (start, end,
    /// increment). increment != 0 steps toward end (clamped, or unbounded
    /// when start == end — pure increment); increment 0 with distinct
    /// start/end lerps across the duration; otherwise static.</summary>
    static float ProfileValue(Vector3 p, float t, float dur)
    {
        float start = p.X, end = p.Y, inc = p.Z;
        if (MathF.Abs(inc) > 0.0001f)
        {
            float v = start + inc * t;
            if (MathF.Abs(end - start) > 0.0001f)
                v = inc > 0f ? MathF.Min(v, MathF.Max(start, end))
                             : MathF.Max(v, MathF.Min(start, end));
            return v;
        }
        if (MathF.Abs(end - start) > 0.0001f)
            return start + (end - start) * Math.Clamp(t / MathF.Max(0.01f, dur), 0f, 1f);
        return start;
    }

    /// <summary>Phase 23d-2b — build each cylinder as a tube wall between
    /// its two animated rings (ring 0 = Rp0 radius at Hp0 height, ring 1 =
    /// Rp1 at Hp1), with spin rolling the texture and rotate/irotate
    /// orienting the whole tube.</summary>
    void EmitCylinderQuads(byte slotFilter)
    {
        if (_cylinders.Count == 0) return;
        for (int ci = 0; ci < _cylinders.Count; ci++)
        {
            var c = _cylinders[ci];
            if (c.TexSlot != slotFilter) continue;
            // Lifetime alpha — tin ramp at start, tout ramp at end, scaled
            // by the authored starting alpha.
            float life = c.TotalLife;
            float t = c.Elapsed;
            float alpha = 1f;
            if (c.FadeIn > 0f && t < c.FadeIn)
                alpha = t / c.FadeIn;
            float toutStart = life - c.FadeOut;
            if (c.FadeOut > 0f && t > toutStart)
                alpha = MathF.Max(0f, 1f - (t - toutStart) / c.FadeOut);
            alpha *= c.Alpha;
            if (alpha <= 0.001f) continue;

            int seg = c.Segments;
            float r0 = MathF.Max(0.01f, ProfileValue(c.Rp0, t, life));
            float r1 = MathF.Max(0.01f, ProfileValue(c.Rp1, t, life));
            float h0 = ProfileValue(c.Hp0, t, life);
            float h1 = ProfileValue(c.Hp1, t, life);
            float spinOff = c.Spin * t / MathF.Tau; // U shift, revs

            // rotate + irotate — XYZ euler in degrees (+ degrees/sec).
            var rotDeg = c.Rotate + c.IRotate * t;
            Matrix4x4 rot = Matrix4x4.Identity;
            if (rotDeg.LengthSquared() > 0.0001f)
                rot = Matrix4x4.CreateRotationX(rotDeg.X * MathF.PI / 180f)
                    * Matrix4x4.CreateRotationY(rotDeg.Y * MathF.PI / 180f)
                    * Matrix4x4.CreateRotationZ(rotDeg.Z * MathF.PI / 180f);

            EnsureRibbonCapacity(_ribbonVertCount + seg * 6);
            var color = c.Color;
            color.W *= alpha;
            for (int k = 0; k < seg; k++)
            {
                float a0 = (float)k       / seg * MathF.Tau;
                float a1 = (float)(k + 1) / seg * MathF.Tau;
                float u0 = (float)k       / seg + spinOff;
                float u1 = (float)(k + 1) / seg + spinOff;
                var p0Ring0 = new Vector3(MathF.Cos(a0) * r0, h0, MathF.Sin(a0) * r0);
                var p1Ring0 = new Vector3(MathF.Cos(a1) * r0, h0, MathF.Sin(a1) * r0);
                var p0Ring1 = new Vector3(MathF.Cos(a0) * r1, h1, MathF.Sin(a0) * r1);
                var p1Ring1 = new Vector3(MathF.Cos(a1) * r1, h1, MathF.Sin(a1) * r1);
                if (rotDeg.LengthSquared() > 0.0001f)
                {
                    p0Ring0 = Vector3.Transform(p0Ring0, rot);
                    p1Ring0 = Vector3.Transform(p1Ring0, rot);
                    p0Ring1 = Vector3.Transform(p0Ring1, rot);
                    p1Ring1 = Vector3.Transform(p1Ring1, rot);
                }
                var q00 = c.Anchor + p0Ring0;
                var q10 = c.Anchor + p1Ring0;
                var q01 = c.Anchor + p0Ring1;
                var q11 = c.Anchor + p1Ring1;
                // Wall quad between the rings (v: ring0 = 1, ring1 = 0).
                EmitRibbonVert(q00, u0, 1f, color);
                EmitRibbonVert(q10, u1, 1f, color);
                EmitRibbonVert(q01, u0, 0f, color);
                EmitRibbonVert(q10, u1, 1f, color);
                EmitRibbonVert(q11, u1, 0f, color);
                EmitRibbonVert(q01, u0, 0f, color);
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
        _ribbonShader.SetFloat("uGamma", Gamma);
        for (int i = 0; i < _textures.Length; i++)
            _ribbonShader.SetInt("uTex" + i, i);
        for (int slot = 0; slot < _textures.Length; slot++)
        {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + slot));
            _gl.BindTexture(GLEnum.Texture2D, _textures[slot]?.Handle ?? 0);
        }

        _gl.BindVertexArray(_ribbonVao);
        // Phase 23d-2e — draw each recorded range with its slot (250+ =
        // pure vertex color for textureless poly shards / sphere meshes).
        for (int i = 0; i < _ribbonRanges.Count; i++)
        {
            var (rStart, rCount, rSlot) = _ribbonRanges[i];
            _ribbonShader.SetInt("uSlot", rSlot);
            _gl.DrawArrays(GLEnum.Triangles, rStart, (uint)rCount);
        }
        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        if (!depthWasOn) _gl.Disable(GLEnum.DepthTest);
        if (cullWasOn)   _gl.Enable(GLEnum.CullFace);
    }

    /// <summary>Phase 23d-2e — SU-212 polygonal explosion. Each shard is
    /// an n-gon fan oriented by its live euler rotation; shards fly out at
    /// mag-scaled velocity, rotate at their rotrange roll, and stick where
    /// they land on the spawn plane.</summary>
    public void SpawnPolyExplosion(in SiegeFX.Core.Sfx.PolyExplosionSpec spec)
    {
        int count = Math.Clamp(spec.Count, 1, 400);
        for (int i = 0, dn = Detail(count); i < dn; i++)
        {
            float ang = Rand(0f, MathF.Tau);
            float rad = Rand(0f, MathF.Max(0.02f, spec.Radius));
            var pos = spec.Anchor + new Vector3(
                MathF.Cos(ang) * rad + Rand(-spec.Displace.X, spec.Displace.X),
                Rand(-spec.Displace.Y, spec.Displace.Y),
                MathF.Sin(ang) * rad + Rand(-spec.Displace.Z, spec.Displace.Z));
            // Up-biased hemisphere burst scaled by mag.
            float z  = Rand(0.15f, 1f);
            float th = Rand(0f, MathF.Tau);
            float rr = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            var dir  = new Vector3(rr * MathF.Cos(th), z, rr * MathF.Sin(th));
            _polyShards.Add(new PolyShard
            {
                Pos       = pos,
                Vel       = dir * (2.5f * MathF.Max(0.1f, spec.Mag) * Rand(0.6f, 1.4f)),
                Rot       = new Vector3(Rand(0f, 360f), Rand(0f, 360f), Rand(0f, 360f)),
                RotRate   = new Vector3(Rand(-spec.RotRange.X, spec.RotRange.X),
                                        Rand(-spec.RotRange.Y, spec.RotRange.Y),
                                        Rand(-spec.RotRange.Z, spec.RotRange.Z)),
                Color     = spec.Color,
                Age       = 0f,
                Life      = MathF.Max(0.15f, spec.Duration),
                FadeStart = spec.FadeStart,
                FadeEnd   = spec.FadeEnd,
                // Shard size is a documented-inference constant (the doc
                // gives no per-shard size knob) — small chips read right.
                Size      = 0.07f * MathF.Max(0.5f, spec.Mag),
                GroundY   = spec.Anchor.Y,
                Sides     = (byte)Math.Clamp(
                    (int)Rand(3f, MathF.Max(3f, spec.PolySides + 1)), 3, 12),
            });
        }
    }

    /// <summary>Phase 23d-2e — SU-212 tessellated translucent sphere.</summary>
    public void SpawnSphereMesh(in SiegeFX.Core.Sfx.SphereMeshSpec spec)
    {
        _sphereMeshes.Add(new SphereMeshP { Spec = spec, Age = 0f });
    }

    void EmitPolyShards()
    {
        if (_polyShards.Count == 0) return;
        for (int i = 0; i < _polyShards.Count; i++)
        {
            var s = _polyShards[i];
            float t01 = s.Age / s.Life;
            float alpha = t01 <= s.FadeStart ? 1f
                        : t01 >= s.FadeEnd   ? 0f
                        : 1f - (t01 - s.FadeStart) / MathF.Max(0.0001f, s.FadeEnd - s.FadeStart);
            if (alpha <= 0.002f) continue;
            var col = s.Color;
            col.W *= alpha * 0.85f;

            var rot = Matrix4x4.CreateRotationX(s.Rot.X * MathF.PI / 180f)
                    * Matrix4x4.CreateRotationY(s.Rot.Y * MathF.PI / 180f)
                    * Matrix4x4.CreateRotationZ(s.Rot.Z * MathF.PI / 180f);
            int sides = s.Sides;
            EnsureRibbonCapacity(_ribbonVertCount + sides * 3);
            var center = s.Pos;
            Vector3 First(int k)
            {
                float a = (float)k / sides * MathF.Tau;
                var local = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * s.Size;
                return center + Vector3.Transform(local, rot);
            }
            var prev = First(0);
            for (int k = 1; k <= sides; k++)
            {
                var next = First(k);
                EmitRibbonVert(center, 0.5f, 0.5f, col);
                EmitRibbonVert(prev,   0f,   0f,   col);
                EmitRibbonVert(next,   1f,   1f,   col);
                prev = next;
            }
        }
    }

    void EmitSphereMeshes()
    {
        if (_sphereMeshes.Count == 0) return;
        for (int i = 0; i < _sphereMeshes.Count; i++)
        {
            var m = _sphereMeshes[i];
            var sp = m.Spec;
            float t = m.Age;
            float alpha = 1f;
            if (sp.FadeIn > 0f && t < sp.FadeIn) alpha = t / sp.FadeIn;
            float toutStart = sp.Duration - sp.FadeOut;
            if (sp.FadeOut > 0f && t > toutStart)
                alpha = MathF.Min(alpha, MathF.Max(0f, 1f - (t - toutStart) / sp.FadeOut));
            if (alpha <= 0.002f) continue;
            // grow_params start→mid→end radius envelope over the life.
            float t01 = t / MathF.Max(0.01f, sp.Duration);
            float grow = t01 < 0.5f
                ? MathHelper.Lerp(sp.GrowStart, sp.GrowMid, t01 * 2f)
                : MathHelper.Lerp(sp.GrowMid, sp.GrowEnd, (t01 - 0.5f) * 2f);
            float radius = MathF.Max(0.02f, sp.Radius * grow);
            var col = sp.Color;
            col.W *= alpha * 0.45f; // translucent shell read

            var rotDeg = sp.Rotate + sp.IRotate * t;
            var rot = Matrix4x4.CreateRotationX(rotDeg.X * MathF.PI / 180f)
                    * Matrix4x4.CreateRotationY(rotDeg.Y * MathF.PI / 180f)
                    * Matrix4x4.CreateRotationZ(rotDeg.Z * MathF.PI / 180f);

            int segs  = Math.Clamp(sp.Sides, 4, 32);
            int rings = Math.Clamp(2 + 2 * sp.Subd, 2, 16);
            EnsureRibbonCapacity(_ribbonVertCount + rings * segs * 6);
            Vector3 P(int ring, int seg)
            {
                float phi = MathF.PI * ring / rings;          // 0..π
                float th  = MathF.Tau * seg / segs;           // 0..2π
                var local = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(th),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(th)) * radius;
                return sp.Anchor + Vector3.Transform(local, rot);
            }
            for (int r = 0; r < rings; r++)
            for (int sgi = 0; sgi < segs; sgi++)
            {
                var p00 = P(r, sgi);
                var p01 = P(r, sgi + 1);
                var p10 = P(r + 1, sgi);
                var p11 = P(r + 1, sgi + 1);
                EmitRibbonVert(p00, 0f, 0f, col);
                EmitRibbonVert(p01, 1f, 0f, col);
                EmitRibbonVert(p10, 0f, 1f, col);
                EmitRibbonVert(p01, 1f, 0f, col);
                EmitRibbonVert(p11, 1f, 1f, col);
                EmitRibbonVert(p10, 0f, 1f, col);
            }
        }
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
uniform float uGamma;
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
  c.rgb = pow(c.rgb, vec3(1.0 / max(uGamma, 0.1))); // ALPHA-2V options gamma
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
uniform float uGamma;
out vec4 frag;
void main(){
  vec4 tex;
  if      (uSlot >= 250) tex = vec4(1.0); // Phase 23d-2e — textureless (pure vertex color)
  else if (uSlot == 0) tex = texture(uTex0, vUv);
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
  c.rgb = pow(c.rgb, vec3(1.0 / max(uGamma, 0.1))); // ALPHA-2V options gamma
  frag = c;
}";
}

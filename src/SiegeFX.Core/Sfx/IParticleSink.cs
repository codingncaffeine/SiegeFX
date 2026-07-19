using System.Numerics;

namespace SiegeFX.Core.Sfx;

/// <summary>Phase 17-SC-J — public mode tag for persistent emitter
/// registration paths (legacy <c>emt_particle</c> instances). Mirrors the
/// internal EmitterMode discriminant in SfxRuntime; kept here so callers
/// outside Sfx don't have to re-derive it.</summary>
public enum ParticleKind { Fire, Smoke, Steam }

/// <summary>Phase 23d-2a — full authored parameter set for DS1's
/// `explosion` effect, per SU 212 Appendix A. Particles spawn within
/// <see cref="Radius"/>, fly along a set direction (or omni-directionally),
/// with speed scalars <see cref="VMin"/>..<see cref="VMax"/> plus the
/// <see cref="IVel"/> base vector and <see cref="RVel"/> random vector,
/// staying opaque until <see cref="FadeStart"/> of their life and fading
/// out by <see cref="FadeEnd"/>. <see cref="SpawnOver"/> spreads the burst
/// across N seconds (srate) instead of one instant pop.</summary>
public struct ExplosionSpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public float   Radius;       // spawn radius factor (doc default 0.5)
    public int     Count;        // doc default 32
    public float   ScaleMin;     // scale_range start (doc default 0.2)
    public float   ScaleMax;     // scale_range end   (doc default 0.7)
    public float   VMin;         // min velocity scalar (doc default 3.0)
    public float   VMax;         // max velocity scalar (doc default 6.5)
    public System.Numerics.Vector4 ColorVar;   // color1 - per-particle variance (doc)
    public bool    HasColorVar;
    public float   Rebound;      // bounce elasticity (doc default 0.85)
    public bool    Bounce;       // collide()/splat-adjacent ground interaction
    public bool    Splat;        // particles stick where they land
    public float   GroundY;      // spawn-plane height for bounce/splat
    public System.Numerics.Vector3 IVel;  // initial velocity vector
    public System.Numerics.Vector3 RVel;  // random velocity vector (doc default .25,.25,.25)
    public bool    OmniDir;      // omni_dir()
    public float   FadeStart;    // fade_range start fraction (doc default 0.5)
    public float   FadeEnd;      // fade_range end fraction   (doc default 1.0)
    public float   Duration;     // dur
    public float   SpawnOver;    // srate — spawn spread in seconds (0 = instant)
    public byte    TexSlot;
}

/// <summary>Phase 23d-2b — SU-212 cylinder: a tube between two animated
/// rings. Ring 0 sits at height <see cref="Hp0"/> with radius profile
/// <see cref="Rp0"/>; ring 1 at <see cref="Hp1"/>/<see cref="Rp1"/>. Each
/// profile is the documented (start, end, increment) triple: increment
/// steps the value per second toward end (clamped); increment 0 with
/// start != end lerps across the duration; otherwise static. DS1's
/// defaults (rp 0.5/0.5/0, hp0 2.5, hp1 0) describe a 2.5u-tall tube;
/// impact shockwaves author expanding rp with near-flat hp.</summary>
public struct CylinderSpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public System.Numerics.Vector3 Rp0, Rp1, Hp0, Hp1; // (start,end,increment)
    public float   Alpha;      // alpha(f) starting alpha (doc default 0.5)
    public float   Spin;       // spin(f)
    public float   FadeIn;     // tin (doc default 0.5)
    public float   FadeOut;    // tout (doc default 0.5)
    public float   Duration;   // dur
    public System.Numerics.Vector3 Rotate;   // rotate(x,y,z) degrees
    public System.Numerics.Vector3 IRotate;  // irotate(x,y,z) degrees/sec
    public bool    Dark;       // dark() — opaque blend variant
    public byte    TexSlot;
    public byte    Segments;   // doc default 16
}

/// <summary>Phase 23d-2b — SU-212 sray: spinning rays spawn one per
/// <see cref="SpawnPeriod"/> seconds (srate, doc default 0.015) up to
/// <see cref="Count"/>, each with per-ray random length/width in the
/// authored ranges, polar angles advancing at per-ray random rates from
/// the theta/phi (start, min-inc, max-inc) triples, and alpha fading at
/// a per-ray random rate from the alpha (start, fade-min, fade-max)
/// triple.</summary>
public struct SraySpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color0;   // start color
    public System.Numerics.Vector4 Color1;   // end color
    public float   Radius;      // origin sphere (doc default 0.0005)
    public int     Count;       // doc default 16
    public float   LMin, LMax;  // ray length range (doc default 10/10)
    public float   WsMin, WsMax, WeMin, WeMax; // widths (doc default 0.15)
    public System.Numerics.Vector3 Theta;    // (start, min inc, max inc) (doc 0,1,3)
    public System.Numerics.Vector3 Phi;      // (start, min inc, max inc) (doc 0,1,-3)
    public System.Numerics.Vector3 Alpha;    // (start, fade min, fade max) (doc 1,.5,.5)
    public float   SpawnPeriod; // srate (doc default 0.015)
    public float   Duration;    // dur — emitter lifetime cap
}

/// <summary>Phase 23d-2b — SU-212 flurry: <see cref="Count"/> particles
/// moving in spherical polar coordinates around the anchor with
/// sinusoidal radial interference (amplitude/iamp), alpha shaped by
/// tin/tout and scale by the grow_params (start, mid, end) envelope.</summary>
public struct FlurrySpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public float   Radius;      // doc default 1.0
    public int     Count;       // doc default 50
    public float   IPhi;        // latitude rate (doc default 1.0)
    public float   ITheta;      // longitude rate (doc default 1.0)
    public float   IAmp;        // interference speed (doc default 1.0)
    public float   Amplitude;   // interference factor (doc default 1.0)
    public float   GrowStart, GrowMid, GrowEnd; // grow_params (doc 1,1,1)
    public float   FadeIn;      // tin (doc default 1.0)
    public float   FadeOut;     // tout (doc default 1.0)
    public float   Duration;
    public byte    TexSlot;
}

/// <summary>Phase 23d-2e — SU-212 polygonal explosion: textureless
/// n-sided polygons explode from a flat plane while rotating, then
/// stick into the ground (explode_body).</summary>
public struct PolyExplosionSpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public int     PolySides;   // max random sides (doc default 9)
    public int     Count;       // doc default 200
    public float   Radius;      // spawn area (doc default 0.75)
    public float   Mag;         // explosion magnitude (doc default 1.0)
    public System.Numerics.Vector3 RotRange;   // rotation range deg (doc 200,200,200)
    public System.Numerics.Vector3 Displace;   // origin displacement range
    public float   FadeStart, FadeEnd;         // fade_range fractions
    public float   Duration;
}

/// <summary>Phase 23d-2e — SU-212 sphere: textureless tessellated
/// translucent sphere with grow_params envelope, rotate/irotate
/// orientation and tin/tout fades (firebomb / bombard shells).</summary>
public struct SphereMeshSpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public float   Radius;
    public int     Sides;       // segment count (doc default 20)
    public int     Subd;        // tessellation level (doc default 1)
    public float   GrowStart, GrowMid, GrowEnd; // grow_params
    public System.Numerics.Vector3 Rotate, IRotate; // degrees, degrees/sec
    public float   FadeIn, FadeOut, Duration;
}

/// <summary>Phase 23d-2d — SU-212 SPE (spatiotemporal pattern effect).
/// Exact documented model, per axis and per particle i:
/// pos = anchor + radius * (sin(index0 + speed0*t + space0*i)
///                        + sin(index1 + speed1*t + space1*i)) / 2.</summary>
public struct SpeSpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public float   Radius;      // doc default 1.0
    public float   Scale;       // per-particle size (doc default 0.12)
    public System.Numerics.Vector3 Index0, Index1;   // doc default (0,0,1.57)
    public System.Numerics.Vector3 Speed0, Speed1;   // doc default (0,1,0)
    public System.Numerics.Vector3 Space0, Space1;   // doc default (.098,0,.098)
    public int     Count;       // doc default 64
    public float   FadeIn, FadeOut, Duration;
    public byte    TexSlot;
}

/// <summary>Phase 23d-2d — SU-212 sparkles: particles alpha in and out
/// while NEVER moving from their spawn point (yvel is the only motion).</summary>
public struct SparklesSpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public float   Radius;      // doc default 1.0
    public int     Count;       // doc default 60
    public float   PSize;       // particle size scalar (doc default 1.0)
    public float   YVel;        // Y velocity (doc default 0)
    public float   Duration;
    public byte    TexSlot;
}

/// <summary>Phase 23d-2d — SU-212 charge: spherically spawned particles
/// coalesce inward to a larger version of themselves (fireskull's
/// build-up). ialpha is the per-second alpha ramp; centersize caps the
/// central particle's growth.</summary>
public struct ChargeSpec
{
    public System.Numerics.Vector3 Anchor;
    public System.Numerics.Vector4 Color;
    public float   Radius;      // doc default 1.0
    public int     Count;       // doc default 16
    public float   Tout;        // doc default 1.0
    public float   Speed0;      // random accel factor (doc default 1.0)
    public float   CenterSize;  // doc default 0.75
    public float   IAlpha;      // alpha increment (doc default 4.0)
    public float   Duration;
    public byte    TexSlot;
}

/// <summary>Phase 23d-2c — SU-212 fire/smoke/steam plume parameters.
/// DS1's fire maintains a POPULATION of <see cref="Count"/> particles
/// whose lifetime derives from <see cref="AlphaFade"/> ("how fast to
/// fade the flame out", doc default 0.85 → ~1.2s), spawning within the
/// [<see cref="MinRadius"/>, <see cref="MaxRadius"/>] annulus with
/// random Y displacement in [<see cref="MinDisplace"/>,
/// <see cref="MaxDisplace"/>], flying at the authored velocity/accel
/// vectors, sized by flamesize with the fctrl expansion curve.
/// instant() fills the volume immediately; line() spawns along the
/// source→target segment.</summary>
public struct PlumeSpec
{
    public byte    Kind;         // 0=fire, 1=smoke, 2=steam
    public System.Numerics.Vector4 Color;
    public System.Numerics.Vector3 Velocity;  // doc default fire (0,8,0), steam (0,5.75,0)
    public System.Numerics.Vector3 Accel;     // doc default fire (0,14,0), steam (0,4,0)
    // Velocity of the emitter itself when it rides a motion handle (trackball/
    // orbiter). Refreshed each tick. Spawned particles inherit it so on detach
    // they fly off at the ball's last speed. Zero for static emitters.
    public System.Numerics.Vector3 CarrierVelocity;
    // Non-zero when this emitter rides a motion handle: the anchor id its
    // particles rigidly attach to, so the whole plume travels as one body
    // (a single fireball) regardless of the projectile's speed profile.
    public int FollowId;
    public float   FlameSize;    // flamesize / wispsize (doc default 1.75 / 2.25)
    public System.Numerics.Vector3 Fctrl;     // fire expansion (min, max, inc)
    public bool    HasFctrl;
    public float   AlphaFade;    // fade factor — particle life ≈ 1/AlphaFade
    public int     Count;        // population cap (doc default fire 30, steam 96)
    public float   MinRadius, MaxRadius;      // spawn annulus
    public float   MinDisplace, MaxDisplace;  // random Y displacement range
    public bool    Instant;      // full volume immediately
    public bool    Line;         // spawn along Anchor→LineEnd
    public System.Numerics.Vector3 LineEnd;
    public byte    TexSlot;
    // Phase 23-fold — line-position animation (gom_icesnake: the fire
    // spawns at a point that walks the line at linespeed from linepos)
    // and the burn_body sine radius wobble.
    public float   LinePos;      // 0..1 start point on the line
    public float   LineSpeed;    // per-second advance
    public bool    HasLineAnim;
    public float   SinPos, SinSpeed, RadiusRMax;
    public bool    HasSinAnim;
}

/// <summary>Phase 17-SC-F-2 — particle backend abstraction. The shipped
/// implementation is the GL-backed billboard system in
/// <c>SiegeFX.Runtime.Render.ParticleSystem</c>; tests and CLIs swap in a
/// counting stub so the VM can be exercised without standing up GL. Keeps
/// <see cref="SfxRuntime"/> in Core (no Render dependency) so the same VM
/// drives both the live renderer and the headless audit path.</summary>
public interface IParticleSink
{
    /// <summary>SC-VFX-TEXCACHE — resolve an authored `texture(NAME)` value
    /// to a live renderer slot, loading the bitmap on demand. Sinks without
    /// a texture cache (labs, tests) return <paramref name="fallback"/> —
    /// the static family mapping the caller already computed.</summary>
    int ResolveTextureSlot(string? name, int fallback) => fallback;

    void SpawnFire(Vector3 position, Vector4 color, float scale, float duration, int count = 12);
    void SpawnSmoke(Vector3 position, Vector4 color, float scale, float duration, int count = 8);
    void SpawnSteam(Vector3 position, Vector4 color, float scale, float duration, int count = 8);
    void SpawnSpark(Vector3 position, Vector4 color, float scale, float duration, int count = 16);
    void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration);

    /// <summary>Phase 21-SC-SPELL-VFX-2 — DS1 lightning's
    /// <c>maxdisplace(N)</c> param. <paramref name="displace"/> 0 means
    /// "use renderer default" (length-relative jitter).</summary>
    void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration, float displace);

    /// <summary>Phase 23d-2a — full-fidelity lightning per SU 212:
    /// displacement is the SIGNED [minDisplace, maxDisplace] stray range
    /// (zap authors -0.15..0.15), subd/minSubd control fractal
    /// subdivision density. Zero subd/minSubd = renderer defaults.</summary>
    void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration,
                        float minDisplace, float maxDisplace, float subd, float minSubd);

    /// <summary>Phase 23d-2a — authored-parameter explosion (SU 212
    /// Appendix A). Replaces the SpawnSpark approximation for
    /// `sfx create explosion` sites.</summary>
    void SpawnExplosion(in ExplosionSpec spec);

    /// <summary>Phase 23d-2b — authored-parameter cylinder tube (two
    /// animated rings). Replaces the flat-ring approximation.</summary>
    void SpawnCylinderTube(in CylinderSpec spec);

    /// <summary>Phase 23d-2b — authored-parameter spinning-ray emitter
    /// (timed spawn + polar spin + per-ray fade).</summary>
    void SpawnSrayTimed(in SraySpec spec);

    /// <summary>Phase 23d-2b — authored-parameter flurry (spherical polar
    /// swarm with sinusoidal interference).</summary>
    void SpawnFlurry(in FlurrySpec spec);

    /// <summary>Phase 23d-2c — authored-parameter plume pump. Population
    /// model: spawn rate = Count / life where life derives from
    /// AlphaFade. Returns the leftover budget like the legacy
    /// Maintain* trio.</summary>
    float MaintainPlume(in PlumeSpec spec, Vector3 position, float age, float dt, float carry);

    /// <summary>Publish an attachment anchor's live world position so plume
    /// particles pinned to it (a flying projectile's fire) ride it as one
    /// body. Call every tick the projectile is alive.</summary>
    void SetFollowAnchor(int id, Vector3 pos);

    /// <summary>Drop an attachment anchor so its particles detach and fly off
    /// on their last velocity (projectile hit / expired).</summary>
    void ClearFollowAnchor(int id);

    /// <summary>Phase 23d-2c — instant() volume fill: burst n plume
    /// particles at once at spawn time.</summary>
    void BurstPlume(in PlumeSpec spec, Vector3 position, int n);

    /// <summary>Phase 23d-2d — exact SU-212 spatiotemporal pattern effect.</summary>
    void SpawnSpe(in SpeSpec spec);

    /// <summary>Phase 23d-2d — static alpha-in/out sparkles.</summary>
    void SpawnSparkles(in SparklesSpec spec);

    /// <summary>Phase 23d-2d — inward-coalescing charge build-up.</summary>
    void SpawnCharge(in ChargeSpec spec);

    /// <summary>Phase 23d-2e — textureless polygonal-shard explosion.</summary>
    void SpawnPolyExplosion(in PolyExplosionSpec spec);

    /// <summary>Phase 23d-2e — textureless tessellated translucent sphere.</summary>
    void SpawnSphereMesh(in SphereMeshSpec spec);

    /// <summary>Phase 23-fold — SU-212 LineTracer: a textureless tracer
    /// ribbon between two points that fades at fade_rate.</summary>
    void SpawnLineTracer(Vector3 source, Vector3 target,
                         Vector4 color0, Vector4 color1,
                         float fadeRate, float tin, float tout);

    /// <summary>Phase 21-SC-SPELL-VFX — flying fireball-style projectile from
    /// <paramref name="source"/> toward <paramref name="target"/>. The
    /// implementation stamps a fire+ember trail along the flight path and
    /// triggers a fire/spark explosion on arrival. <paramref name="impactKind"/>
    /// selects the explosion flavor (0=fire, 1=ice/frost, 2=lightning crack).
    /// Headless stub no-ops for tests.</summary>
    void SpawnProjectile(Vector3 source, Vector3 target, Vector4 color, float scale, float speed, int impactKind);
    float MaintainFire(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry);
    float MaintainSmoke(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry);
    float MaintainSteam(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry);

    /// <summary>Phase 21-SC-SPELL-VISUAL-A — DS1 cylinder primitive. Flat
    /// textured ring on the Y-up plane at <paramref name="anchor"/>, with
    /// per-axis spin and tin/tout fade timings. The 19 cylinder-using
    /// spells in DS1 author this as a ground-snapped impact ring; only
    /// laser_major (sun_ray) is a true beam between two points and gets
    /// deferred to a follow-up tweak. Headless sinks no-op.</summary>
    void SpawnCylinder(Vector3 anchor, Vector4 color,
                       float radiusOuter, float thicknessRatio,
                       float spinPerSec,  float fadeIn, float fadeOut,
                       float duration,    byte texSlot, byte segments);

    /// <summary>Phase 21-SC-SPELL-VISUAL-B — DS1 sray streak. Tapered ray
    /// emitted radially out from <paramref name="anchor"/>; <paramref name="rayCount"/>
    /// rays distribute evenly in azimuth. Headless sinks no-op.</summary>
    void SpawnSray(Vector3 anchor, Vector4 colorStart, Vector4 colorEnd,
                   float lengthMin, float lengthMax,
                   float widthStart, float widthEnd,
                   float duration, int rayCount);

    /// <summary>Phase 21-SC-SPELL-VISUAL-C — DS1 fireb directional cone.
    /// Spawns a one-shot batch of fire particles flying in
    /// <paramref name="velocity"/> direction at the cone radii defined
    /// by lower/upper. Headless sinks no-op.</summary>
    void SpawnFireb(Vector3 anchor, Vector4 color, Vector3 velocity,
                    Vector3 accel,  float lifetime, float maxDisplace,
                    float lowerRadius, float upperRadius,
                    int count, float flameSize);

    /// <summary>Phase 21-SC-SPELL-VISUAL-D — DS1 lightsource glow pulse.
    /// Continuous emit pump for a bright additive halo at the live
    /// motion-handle position. Returns the leftover spawn budget so the
    /// caller carries it across frames (matches MaintainFire/Smoke/Steam
    /// shape).</summary>
    float MaintainGlow(Vector3 position, Vector4 color, float radius,
                       float dt, float rate, float carry);

    /// <summary>Phase 21-SC-SPELL-VISUAL-H+sphere fold — DS1 sphere
    /// primitive. Omni-directional expanding shell of particles around
    /// <paramref name="anchor"/> at <paramref name="radius"/>; color-
    /// preserving (no warm-bias drift like SpawnSpark). firebomb /
    /// bombard / dave_shield etc. author this; the headless sink
    /// counts and no-ops.</summary>
    void SpawnSphere(Vector3 anchor, Vector4 color,
                     float radius, float duration, int count);
}

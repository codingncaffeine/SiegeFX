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
    public System.Numerics.Vector3 IVel;  // initial velocity vector
    public System.Numerics.Vector3 RVel;  // random velocity vector (doc default .25,.25,.25)
    public bool    OmniDir;      // omni_dir()
    public float   FadeStart;    // fade_range start fraction (doc default 0.5)
    public float   FadeEnd;      // fade_range end fraction   (doc default 1.0)
    public float   Duration;     // dur
    public float   SpawnOver;    // srate — spawn spread in seconds (0 = instant)
    public byte    TexSlot;
}

/// <summary>Phase 17-SC-F-2 — particle backend abstraction. The shipped
/// implementation is the GL-backed billboard system in
/// <c>SiegeFX.Runtime.Render.ParticleSystem</c>; tests and CLIs swap in a
/// counting stub so the VM can be exercised without standing up GL. Keeps
/// <see cref="SfxRuntime"/> in Core (no Render dependency) so the same VM
/// drives both the live renderer and the headless audit path.</summary>
public interface IParticleSink
{
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

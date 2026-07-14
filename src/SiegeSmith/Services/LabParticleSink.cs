using System;
using System.Collections.Generic;
using System.Numerics;
using SiegeFX.Core.Sfx;

namespace SiegeSmith.Services;

/// <summary>SS-FXLAB — CPU implementation of the engine's <see cref="IParticleSink"/>
/// for the Effects Lab preview. The REAL <see cref="SfxRuntime"/> VM drives this sink,
/// so timing, verb dispatch, motion handles and population models are the engine's own;
/// only the rasterization is approximate (soft splats via <see cref="SoftwareRenderer"/>
/// instead of the GL billboard atlas). The one-click filmstrip covers engine-exact
/// pixels; this sink covers instant every-keystroke feedback.
///
/// Coordinate note: the VM works in engine space (Y up); the preview scene is Z-up
/// (matching every other SiegeSmith viewport), so <see cref="Collect"/> maps
/// (x, y, z) → (x, z, y) at emit time.</summary>
public sealed class LabParticleSink : IParticleSink
{
    // ── free ballistic particles ─────────────────────────────────
    private struct P
    {
        public Vector3 Pos, Vel, Acc;
        public Vector4 Color;
        public float Size, Grow, Age, Life, FadeStart, FadeEnd;
        public bool Additive;
        public int FollowId;        // >0 → rides _anchors[FollowId] + FollowOfs
        public Vector3 FollowOfs;
    }

    /// <summary>Parametric shapes (bolts, rings, swarms) that compute their look
    /// from age each frame instead of integrating a pool particle. ALL state
    /// mutation and pool spawning happens in Tick; Emit must stay pure — the
    /// viewport re-Collects without ticking on every camera move, so an impure
    /// Emit double-spawns.</summary>
    private interface ILabFx
    {
        bool Tick(float dt, LabParticleSink sink); // false = expired
        void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink);
    }

    private const int PoolCap = 2600;              // preview budget, oldest culled first

    private readonly List<P> _pool = new();
    private readonly List<ILabFx> _fx = new();
    private readonly Dictionary<int, Vector3> _anchors = new();
    private readonly Random _rng = new(12345);
    private float _time;

    public int LiveParticles => _pool.Count;
    public int LiveShapes => _fx.Count;
    public bool IsIdle => _pool.Count == 0 && _fx.Count == 0;

    public void Clear()
    {
        _pool.Clear();
        _fx.Clear();
        _anchors.Clear();
        _time = 0f;
    }

    public void Tick(float dt)
    {
        _time += dt;
        for (int i = _pool.Count - 1; i >= 0; i--)
        {
            var p = _pool[i];
            p.Age += dt;
            if (p.Age >= p.Life) { _pool.RemoveAt(i); continue; }
            p.Vel += p.Acc * dt;
            if (p.FollowId > 0 && _anchors.ContainsKey(p.FollowId))
                p.FollowOfs += p.Vel * dt;   // rigid ride: local offset advances, anchor carries the body
            else
                p.Pos += p.Vel * dt;
            _pool[i] = p;
        }
        for (int i = _fx.Count - 1; i >= 0; i--)
            if (!_fx[i].Tick(dt, this)) _fx.RemoveAt(i);
    }

    /// <summary>Convert the live state to render splats (Z-up preview space).</summary>
    public void Collect(List<SoftwareRenderer.Splat> into)
    {
        foreach (var p in _pool)
        {
            float frac = p.Age / MathF.Max(0.001f, p.Life);
            float a = frac <= p.FadeStart ? 1f
                : p.FadeEnd <= p.FadeStart ? 0f
                : 1f - (frac - p.FadeStart) / (p.FadeEnd - p.FadeStart);
            a = Math.Clamp(a, 0f, 1f) * Math.Clamp(p.Color.W, 0f, 1f);
            if (a <= 0.01f) continue;

            var simPos = p.FollowId > 0 && _anchors.TryGetValue(p.FollowId, out var anchor)
                ? anchor + p.FollowOfs
                : p.Pos;
            float size = p.Size * (1f + p.Grow * frac * 1.5f);
            AddSplat(into, simPos, size, p.Color, a, p.Additive);
        }
        foreach (var fx in _fx) fx.Emit(into, this);
    }

    // ── shared helpers ───────────────────────────────────────────

    private static Vector3 ToPreview(Vector3 sim) => new(sim.X, sim.Z, sim.Y); // Y-up → Z-up

    private static void AddSplat(List<SoftwareRenderer.Splat> into, Vector3 simPos,
        float radius, Vector4 color, float alpha, bool additive)
    {
        var c = NormColor(color);
        into.Add(new SoftwareRenderer.Splat(ToPreview(simPos), MathF.Max(0.015f, radius),
            (byte)Math.Clamp(c.X * 255f, 0f, 255f),
            (byte)Math.Clamp(c.Y * 255f, 0f, 255f),
            (byte)Math.Clamp(c.Z * 255f, 0f, 255f),
            Math.Clamp(alpha, 0f, 0.92f), additive));
    }

    /// <summary>Scripts author colors 0–1, but be tolerant of 0–255 payloads
    /// and of unset (all-zero) colors, which fall back to the kind default.</summary>
    private static Vector4 NormColor(Vector4 c)
    {
        if (c.X > 2f || c.Y > 2f || c.Z > 2f) c = new Vector4(c.X / 255f, c.Y / 255f, c.Z / 255f, c.W > 2f ? c.W / 255f : c.W);
        return c;
    }

    private static Vector4 Fallback(Vector4 c, Vector4 def)
        => c.X <= 0.001f && c.Y <= 0.001f && c.Z <= 0.001f ? def : c;

    private float Rnd() => (float)_rng.NextDouble();
    private float Rnd(float lo, float hi) => lo + (hi - lo) * Rnd();
    private float RndSym(float amp) => (Rnd() * 2f - 1f) * amp;

    private Vector3 RndOnSphere()
    {
        float z = Rnd() * 2f - 1f;
        float a = Rnd() * MathF.PI * 2f;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(r * MathF.Cos(a), z, r * MathF.Sin(a));
    }

    private Vector3 RndInSphere() => RndOnSphere() * MathF.Pow(Rnd(), 1f / 3f);

    private static float Hash01(uint x)
    {
        x ^= x >> 16; x *= 2654435761u; x ^= x >> 13; x *= 2246822519u; x ^= x >> 16;
        return (x & 0xFFFFFF) / 16777216f;
    }

    /// <summary>grow_params (start, mid, end) — piecewise-linear envelope.</summary>
    private static float Grow3(float frac, float s, float m, float e)
        => frac < 0.5f ? s + (m - s) * (frac * 2f) : m + (e - m) * ((frac - 0.5f) * 2f);

    /// <summary>tin/tout alpha shaping over a lifetime.</summary>
    private static float TinTout(float age, float life, float tin, float tout)
    {
        float a = 1f;
        if (tin > 0.001f) a = MathF.Min(a, age / tin);
        if (tout > 0.001f && life > 0.001f) a = MathF.Min(a, (life - age) / tout);
        return Math.Clamp(a, 0f, 1f);
    }

    private void Emit(Vector3 pos, Vector3 vel, Vector3 acc, Vector4 col, float size,
        float life, bool additive, float grow = 0f, float fadeStart = 0f, float fadeEnd = 1f,
        int followId = 0)
    {
        if (_pool.Count >= PoolCap) _pool.RemoveAt(0);
        var p = new P
        {
            Pos = pos, Vel = vel, Acc = acc, Color = NormColor(col), Size = size,
            Life = MathF.Max(0.05f, life), Additive = additive, Grow = grow,
            FadeStart = Math.Clamp(fadeStart, 0f, 1f), FadeEnd = Math.Clamp(fadeEnd, 0.01f, 1f),
        };
        if (followId > 0 && _anchors.TryGetValue(followId, out var anchor))
        {
            p.FollowId = followId;
            p.FollowOfs = pos - anchor;
        }
        _pool.Add(p);
    }

    // ── IParticleSink: point bursts ──────────────────────────────

    public void SpawnFire(Vector3 position, Vector4 color, float scale, float duration, int count = 12)
    {
        var col = Fallback(NormColor(color), new Vector4(1f, 0.62f, 0.18f, 0.85f));
        for (int i = 0; i < count; i++)
            Emit(position + RndInSphere() * 0.12f * scale,
                new Vector3(RndSym(0.5f), Rnd(1.1f, 2.6f), RndSym(0.5f)) * MathF.Max(0.4f, scale),
                new Vector3(0f, 1.6f, 0f),
                col, 0.15f * MathF.Max(0.5f, scale) * Rnd(0.7f, 1.3f),
                MathF.Max(0.15f, duration) * Rnd(0.6f, 1.15f), additive: true, grow: 0.4f);
    }

    public void SpawnSmoke(Vector3 position, Vector4 color, float scale, float duration, int count = 8)
    {
        var col = Fallback(NormColor(color), new Vector4(0.52f, 0.52f, 0.58f, 0.5f));
        for (int i = 0; i < count; i++)
            Emit(position + RndInSphere() * 0.15f * scale,
                new Vector3(RndSym(0.35f), Rnd(0.6f, 1.3f), RndSym(0.35f)) * MathF.Max(0.4f, scale),
                new Vector3(0f, 0.25f, 0f),
                col, 0.26f * MathF.Max(0.5f, scale) * Rnd(0.8f, 1.3f),
                MathF.Max(0.3f, duration) * Rnd(0.7f, 1.3f), additive: false, grow: 0.8f, fadeStart: 0.15f);
    }

    public void SpawnSteam(Vector3 position, Vector4 color, float scale, float duration, int count = 8)
    {
        var col = Fallback(NormColor(color), new Vector4(0.82f, 0.85f, 0.9f, 0.4f));
        for (int i = 0; i < count; i++)
            Emit(position + RndInSphere() * 0.1f * scale,
                new Vector3(RndSym(0.3f), Rnd(1.4f, 2.4f), RndSym(0.3f)) * MathF.Max(0.4f, scale),
                new Vector3(0f, 0.9f, 0f),
                col, 0.2f * MathF.Max(0.5f, scale) * Rnd(0.8f, 1.25f),
                MathF.Max(0.25f, duration) * Rnd(0.6f, 1.1f), additive: false, grow: 0.9f);
    }

    public void SpawnSpark(Vector3 position, Vector4 color, float scale, float duration, int count = 16)
    {
        var col = Fallback(NormColor(color), new Vector4(1f, 0.9f, 0.5f, 0.9f));
        for (int i = 0; i < count; i++)
        {
            var dir = RndOnSphere();
            dir.Y = MathF.Abs(dir.Y) * 0.9f + 0.1f;
            Emit(position, dir * Rnd(2f, 5.2f) * MathF.Max(0.4f, scale),
                new Vector3(0f, -6.5f, 0f),
                col, 0.05f * MathF.Max(0.5f, scale) * Rnd(0.8f, 1.4f),
                MathF.Max(0.12f, duration) * Rnd(0.4f, 1f), additive: true);
        }
    }

    // ── IParticleSink: lightning / tracer ────────────────────────

    public void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration)
        => SpawnLightning(source, target, color, duration, 0f);

    public void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration, float displace)
        => SpawnLightning(source, target, color, duration, -MathF.Abs(displace), MathF.Abs(displace), 0f, 0f);

    public void SpawnLightning(Vector3 source, Vector3 target, Vector4 color, float duration,
        float minDisplace, float maxDisplace, float subd, float minSubd)
        => _fx.Add(new BoltFx
        {
            A = source, B = target,
            Color = Fallback(NormColor(color), new Vector4(0.75f, 0.8f, 1f, 0.95f)),
            Life = MathF.Max(0.1f, duration),
            MinD = minDisplace, MaxD = maxDisplace,
            Segments = subd > 0.5f ? Math.Clamp((int)subd, 4, 44) : 18,
            Seed = (uint)_rng.Next(),
        });

    public void SpawnLineTracer(Vector3 source, Vector3 target, Vector4 color0, Vector4 color1,
        float fadeRate, float tin, float tout)
        => _fx.Add(new TracerFx
        {
            A = source, B = target,
            C0 = NormColor(color0), C1 = NormColor(color1),
            FadeRate = MathF.Max(0.05f, fadeRate), Tin = tin, Tout = tout,
            Life = fadeRate > 0.05f ? MathF.Min(4f, 1f / fadeRate + tin + tout) : 2f,
        });

    // ── IParticleSink: authored-parameter shapes ─────────────────

    public void SpawnExplosion(in ExplosionSpec spec)
    {
        int n = Math.Clamp(spec.Count <= 0 ? 32 : spec.Count, 1, 320);
        float life = spec.Duration > 0.01f ? spec.Duration : 1.2f;
        if (spec.SpawnOver > 0.05f)
        {
            _fx.Add(new SpawnerFx { Remaining = n, Rate = n / spec.SpawnOver, Life = spec.SpawnOver + 0.01f, Spec = spec, LifeEach = life });
            return;
        }
        for (int i = 0; i < n; i++) EmitExplosionParticle(spec, life);
    }

    internal void EmitExplosionParticle(in ExplosionSpec spec, float life)
    {
        float radius = spec.Radius <= 0f ? 0.5f : spec.Radius;
        var origin = spec.Anchor + RndInSphere() * radius;
        var dir = spec.OmniDir || spec.IVel.LengthSquared() < 1e-6f
            ? RndOnSphere()
            : Vector3.Normalize(spec.IVel);
        float vmin = spec.VMin <= 0f ? 3f : spec.VMin;
        float vmax = spec.VMax <= vmin ? vmin + 2f : spec.VMax;
        var rvel = spec.RVel.LengthSquared() < 1e-8f ? new Vector3(0.25f) : spec.RVel;
        var vel = dir * Rnd(vmin, vmax)
                  + new Vector3(RndSym(rvel.X), RndSym(rvel.Y), RndSym(rvel.Z)) * 2f;
        var col = Fallback(NormColor(spec.Color), new Vector4(1f, 0.6f, 0.2f, 0.9f));
        if (spec.HasColorVar)
        {
            var v = NormColor(spec.ColorVar);
            col += new Vector4(RndSym(v.X), RndSym(v.Y), RndSym(v.Z), 0f);
        }
        float smin = spec.ScaleMin <= 0f ? 0.2f : spec.ScaleMin;
        float smax = spec.ScaleMax <= smin ? smin + 0.2f : spec.ScaleMax;
        Emit(origin, vel, new Vector3(0f, -2.2f, 0f), col,
            Rnd(smin, smax) * 0.35f, life * Rnd(0.8f, 1.1f), additive: true,
            fadeStart: spec.FadeStart is > 0f and < 1f ? spec.FadeStart : 0.5f,
            fadeEnd: spec.FadeEnd is > 0f and <= 1f ? spec.FadeEnd : 1f);
    }

    public void SpawnPolyExplosion(in PolyExplosionSpec spec)
    {
        int n = Math.Clamp(spec.Count <= 0 ? 200 : spec.Count, 1, 160);
        float life = spec.Duration > 0.01f ? spec.Duration : 1.6f;
        float radius = spec.Radius <= 0f ? 0.75f : spec.Radius;
        float mag = spec.Mag <= 0f ? 1f : spec.Mag;
        var col = Fallback(NormColor(spec.Color), new Vector4(0.6f, 0.55f, 0.5f, 0.95f));
        for (int i = 0; i < n; i++)
        {
            var flat = RndInSphere(); flat.Y = 0f;
            var dir = RndOnSphere(); dir.Y = MathF.Abs(dir.Y);
            Emit(spec.Anchor + flat * radius,
                dir * Rnd(2f, 5f) * mag, new Vector3(0f, -9f, 0f), col,
                Rnd(0.05f, 0.14f), life * Rnd(0.7f, 1.1f), additive: false,
                fadeStart: spec.FadeStart is > 0f and < 1f ? spec.FadeStart : 0.6f);
        }
    }

    public void SpawnCylinderTube(in CylinderSpec spec) => _fx.Add(new CylFx { S = spec, Life = spec.Duration > 0.01f ? spec.Duration : 1.5f });
    public void SpawnSrayTimed(in SraySpec spec) => _fx.Add(new SrayFx(spec, this));
    public void SpawnFlurry(in FlurrySpec spec) => _fx.Add(new FlurryFx(spec, this));
    public void SpawnSpe(in SpeSpec spec) => _fx.Add(new SpeFx { S = spec, Life = spec.Duration > 0.01f ? spec.Duration : 2f });
    public void SpawnSparkles(in SparklesSpec spec) => _fx.Add(new SparklesFx(spec, this));
    public void SpawnCharge(in ChargeSpec spec) => _fx.Add(new ChargeFx(spec, this));
    public void SpawnSphereMesh(in SphereMeshSpec spec) => _fx.Add(new SphereFx { S = spec, Life = spec.Duration > 0.01f ? spec.Duration : 1.2f });

    public void SpawnProjectile(Vector3 source, Vector3 target, Vector4 color, float scale, float speed, int impactKind)
        => _fx.Add(new ProjFx
        {
            Pos = source, Target = target,
            Color = Fallback(NormColor(color), new Vector4(1f, 0.6f, 0.2f, 0.9f)),
            Scale = MathF.Max(0.3f, scale),
            Speed = MathF.Max(1.5f, speed),
            ImpactKind = impactKind,
            Life = 12f, // hard cap; normally dies at arrival
        });

    // ── IParticleSink: population maintenance ────────────────────

    public float MaintainFire(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry)
    {
        float n = rate * dt + carry;
        int k = (int)n;
        for (int i = 0; i < k; i++) SpawnFire(position, color, scale, 0.85f, 1);
        return n - k;
    }

    public float MaintainSmoke(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry)
    {
        float n = rate * dt + carry;
        int k = (int)n;
        for (int i = 0; i < k; i++) SpawnSmoke(position, color, scale, 1.7f, 1);
        return n - k;
    }

    public float MaintainSteam(Vector3 position, Vector4 color, float scale, float dt, float rate, float carry)
    {
        float n = rate * dt + carry;
        int k = (int)n;
        for (int i = 0; i < k; i++) SpawnSteam(position, color, scale, 1.1f, 1);
        return n - k;
    }

    public float MaintainPlume(in PlumeSpec spec, Vector3 position, float age, float dt, float carry)
    {
        float life = 1f / MathF.Max(0.2f, spec.AlphaFade);
        int cap = spec.Count <= 0 ? 30 : spec.Count;
        float rate = cap / life;
        float n = rate * dt + carry;
        int k = (int)n;
        for (int i = 0; i < k; i++) EmitPlumeParticle(spec, position, life);
        return n - k;
    }

    public void BurstPlume(in PlumeSpec spec, Vector3 position, int n)
    {
        float life = 1f / MathF.Max(0.2f, spec.AlphaFade);
        for (int i = 0; i < Math.Clamp(n, 1, 400); i++) EmitPlumeParticle(spec, position, life);
    }

    private void EmitPlumeParticle(in PlumeSpec spec, Vector3 position, float life)
    {
        var basePos = position;
        if (spec.Line)
        {
            float t01 = spec.HasLineAnim ? Math.Clamp(spec.LinePos, 0f, 1f) : Rnd();
            basePos = Vector3.Lerp(position, spec.LineEnd, t01);
        }
        // annulus in the horizontal plane + authored random Y displacement
        float rMin = MathF.Max(0f, spec.MinRadius), rMax = MathF.Max(rMin, spec.MaxRadius);
        float r = MathF.Sqrt(Rnd(rMin * rMin, MathF.Max(rMin * rMin, rMax * rMax)));
        float ang = Rnd() * MathF.PI * 2f;
        float yD = Rnd(spec.MinDisplace, MathF.Max(spec.MinDisplace, spec.MaxDisplace));
        var pos = basePos + new Vector3(MathF.Cos(ang) * r, yD, MathF.Sin(ang) * r);

        var vel = spec.Velocity * Rnd(0.75f, 1.25f) + spec.CarrierVelocity;
        bool smoke = spec.Kind == 1;
        bool steam = spec.Kind == 2;
        var col = Fallback(NormColor(spec.Color),
            smoke ? new Vector4(0.5f, 0.5f, 0.56f, 0.5f)
          : steam ? new Vector4(0.82f, 0.85f, 0.9f, 0.4f)
                  : new Vector4(1f, 0.62f, 0.18f, 0.85f));
        float flame = spec.FlameSize <= 0f ? 1.75f : spec.FlameSize;
        float grow = spec.HasFctrl ? Math.Clamp(spec.Fctrl.Z, 0f, 3f) : (smoke ? 0.8f : 0.45f);
        Emit(pos, vel, spec.Accel, col,
            0.14f * flame * Rnd(0.8f, 1.2f), life * Rnd(0.75f, 1.2f),
            additive: !smoke, grow: grow, fadeStart: smoke ? 0.2f : 0.05f,
            followId: spec.FollowId);
    }

    // ── legacy DS1 primitives (pre-spec overloads) ───────────────

    public void SpawnCylinder(Vector3 anchor, Vector4 color,
        float radiusOuter, float thicknessRatio,
        float spinPerSec, float fadeIn, float fadeOut,
        float duration, byte texSlot, byte segments)
        => _fx.Add(new CylFx
        {
            S = new CylinderSpec
            {
                Anchor = anchor, Color = color,
                Rp0 = new Vector3(radiusOuter, radiusOuter, 0f),
                Rp1 = new Vector3(radiusOuter * MathF.Max(0.2f, 1f - thicknessRatio), radiusOuter, 0f),
                Hp0 = Vector3.Zero, Hp1 = new Vector3(0.05f, 0.05f, 0f),
                Alpha = 0.55f, Spin = spinPerSec, FadeIn = fadeIn, FadeOut = fadeOut,
                Duration = duration, Segments = segments,
            },
            Life = duration > 0.01f ? duration : 1.2f,
        });

    public void SpawnSray(Vector3 anchor, Vector4 colorStart, Vector4 colorEnd,
        float lengthMin, float lengthMax, float widthStart, float widthEnd,
        float duration, int rayCount)
        => _fx.Add(new SrayFx(new SraySpec
        {
            Anchor = anchor, Color0 = colorStart, Color1 = colorEnd,
            LMin = lengthMin, LMax = lengthMax,
            WsMin = widthStart, WsMax = widthStart, WeMin = widthEnd, WeMax = widthEnd,
            Theta = new Vector3(0f, 1f, 3f), Phi = new Vector3(0f, 1f, -3f),
            Alpha = new Vector3(1f, 0.5f, 0.5f),
            Count = rayCount, SpawnPeriod = 0.015f, Duration = duration,
        }, this));

    public void SpawnFireb(Vector3 anchor, Vector4 color, Vector3 velocity,
        Vector3 accel, float lifetime, float maxDisplace,
        float lowerRadius, float upperRadius, int count, float flameSize)
    {
        var col = Fallback(NormColor(color), new Vector4(1f, 0.62f, 0.18f, 0.85f));
        int n = Math.Clamp(count <= 0 ? 24 : count, 1, 240);
        float life = lifetime > 0.01f ? lifetime : 0.9f;
        for (int i = 0; i < n; i++)
        {
            // directional cone: spawn in the lower disc, fly the velocity with
            // radial spread growing toward the upper radius
            float ang = Rnd() * MathF.PI * 2f;
            float r0 = MathF.Sqrt(Rnd()) * MathF.Max(0.01f, lowerRadius);
            var radial = new Vector3(MathF.Cos(ang), 0f, MathF.Sin(ang));
            float spread = MathF.Max(0f, upperRadius - lowerRadius) / MathF.Max(0.2f, life);
            Emit(anchor + radial * r0 + new Vector3(0f, RndSym(maxDisplace), 0f),
                velocity * Rnd(0.8f, 1.2f) + radial * spread * Rnd(0.4f, 1f),
                accel, col, 0.13f * MathF.Max(0.4f, flameSize) * Rnd(0.8f, 1.25f),
                life * Rnd(0.75f, 1.15f), additive: true, grow: 0.5f);
        }
    }

    public float MaintainGlow(Vector3 position, Vector4 color, float radius, float dt, float rate, float carry)
    {
        float n = rate * dt + carry;
        int k = (int)n;
        var col = Fallback(NormColor(color), new Vector4(1f, 0.9f, 0.6f, 0.85f));
        for (int i = 0; i < k; i++)
            Emit(position + RndInSphere() * radius * 0.25f, Vector3.Zero, Vector3.Zero,
                col, MathF.Max(0.1f, radius) * Rnd(0.5f, 0.85f), 0.3f, additive: true);
        return n - k;
    }

    public void SpawnSphere(Vector3 anchor, Vector4 color, float radius, float duration, int count)
    {
        var col = Fallback(NormColor(color), new Vector4(0.8f, 0.75f, 1f, 0.8f));
        int n = Math.Clamp(count <= 0 ? 48 : count, 1, 220);
        float life = duration > 0.01f ? duration : 0.8f;
        for (int i = 0; i < n; i++)
        {
            var dir = RndOnSphere();
            // expanding shell: start at the anchor, fly outward to ~radius over the lifetime
            Emit(anchor + dir * radius * 0.15f, dir * (radius / MathF.Max(0.2f, life)) * Rnd(0.8f, 1.1f),
                Vector3.Zero, col, 0.07f * MathF.Max(0.5f, radius) * Rnd(0.8f, 1.2f),
                life * Rnd(0.8f, 1.1f), additive: true);
        }
    }

    public void SetFollowAnchor(int id, Vector3 pos) => _anchors[id] = pos;

    public void ClearFollowAnchor(int id)
    {
        if (!_anchors.TryGetValue(id, out var last)) return;
        _anchors.Remove(id);
        for (int i = 0; i < _pool.Count; i++)
        {
            var p = _pool[i];
            if (p.FollowId != id) continue;
            p.Pos = last + p.FollowOfs;   // detach in place, keep flying at last velocity
            p.FollowId = 0;
            _pool[i] = p;
        }
    }

    // ── parametric shape implementations ─────────────────────────

    private sealed class BoltFx : ILabFx
    {
        public Vector3 A, B; public Vector4 Color; public float Life, Age, MinD, MaxD; public int Segments; public uint Seed;

        public bool Tick(float dt, LabParticleSink sink) { Age += dt; return Age < Life; }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            var dir = B - A;
            float len = dir.Length();
            if (len < 1e-4f) return;
            dir /= len;
            var u = Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.92f ? Vector3.UnitX : Vector3.UnitY);
            u = Vector3.Normalize(u);
            var v = Vector3.Cross(dir, u);
            float lo = MinD, hi = MaxD;
            if (MathF.Abs(lo) < 1e-5f && MathF.Abs(hi) < 1e-5f) { hi = len * 0.06f; lo = -hi; }
            uint slice = (uint)(sink._time * 16f); // re-jitter ~16×/sec → flicker
            float alpha = Math.Clamp(1f - Age / Life, 0f, 1f) * Color.W;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                float taper = MathF.Sin(t * MathF.PI); // ends pinned to the anchors
                float dU = (lo + Hash01(Seed ^ (uint)(i * 73856093) ^ slice * 19349663u) * (hi - lo)) * taper;
                float dV = (lo + Hash01(Seed ^ (uint)(i * 83492791) ^ slice * 2654435761u) * (hi - lo)) * taper;
                var p = A + dir * (len * t) + u * dU + v * dV;
                AddSplat(into, p, 0.045f + len * 0.004f, Color, alpha * 0.85f, additive: true);
            }
            AddSplat(into, A, 0.12f, Color, alpha, additive: true);
            AddSplat(into, B, 0.16f, Color, alpha, additive: true);
        }
    }

    private sealed class TracerFx : ILabFx
    {
        public Vector3 A, B; public Vector4 C0, C1; public float FadeRate, Tin, Tout, Life, Age;

        public bool Tick(float dt, LabParticleSink sink) { Age += dt; return Age < Life; }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            float a = TinTout(Age, Life, Tin, Tout) * MathF.Max(0f, 1f - FadeRate * Age);
            if (a <= 0.01f) return;
            const int seg = 16;
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                var col = Vector4.Lerp(C0, C1, t);
                AddSplat(into, Vector3.Lerp(A, B, t), 0.05f, col, a * col.W, additive: true);
            }
        }
    }

    private sealed class SpawnerFx : ILabFx
    {
        public float Remaining, Rate, Life, Age, LifeEach, Carry;
        public ExplosionSpec Spec;

        public bool Tick(float dt, LabParticleSink sink)
        {
            Age += dt;
            Carry += Rate * dt;
            int k = (int)Carry;
            Carry -= k;
            for (int i = 0; i < k && Remaining > 0f; i++, Remaining--)
                sink.EmitExplosionParticle(Spec, LifeEach);
            return Age < Life && Remaining > 0f;
        }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink) { }
    }

    private sealed class CylFx : ILabFx
    {
        public CylinderSpec S; public float Life, Age;

        public bool Tick(float dt, LabParticleSink sink) { Age += dt; return Age < Life; }

        static float Profile(Vector3 p, float t, float dur)
        {
            // (start, end, increment): inc steps per second toward end; inc 0
            // with start != end lerps across the duration; else static.
            if (MathF.Abs(p.Z) > 1e-5f)
            {
                float v = p.X + p.Z * t;
                return p.Z > 0f ? MathF.Min(v, MathF.Max(p.X, p.Y)) : MathF.Max(v, MathF.Min(p.X, p.Y));
            }
            if (MathF.Abs(p.X - p.Y) > 1e-5f && dur > 0.01f) return p.X + (p.Y - p.X) * Math.Clamp(t / dur, 0f, 1f);
            return p.X;
        }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            float a = TinTout(Age, Life, S.FadeIn, S.FadeOut) * (S.Alpha <= 0f ? 0.5f : S.Alpha);
            if (a <= 0.01f) return;
            var col = Fallback(NormColor(S.Color), new Vector4(0.95f, 0.8f, 0.4f, 0.8f));
            int seg = S.Segments > 0 ? Math.Clamp((int)S.Segments, 8, 32) : 16;
            float r0 = Profile(S.Rp0, Age, Life), r1 = Profile(S.Rp1, Age, Life);
            float h0 = Profile(S.Hp0, Age, Life), h1 = Profile(S.Hp1, Age, Life);
            for (int ring = 0; ring < 3; ring++)
            {
                float t = ring / 2f;
                float r = r0 + (r1 - r0) * t, h = h0 + (h1 - h0) * t;
                float ringA = ring == 1 ? a * 0.55f : a; // faint mid ring for the tube read
                for (int i = 0; i < seg; i++)
                {
                    float ang = i / (float)seg * MathF.PI * 2f;
                    var p = S.Anchor + new Vector3(MathF.Cos(ang) * r, h, MathF.Sin(ang) * r);
                    AddSplat(into, p, 0.06f + MathF.Abs(r) * 0.035f, col, ringA * col.W, additive: !S.Dark);
                }
            }
        }
    }

    private sealed class SrayFx : ILabFx
    {
        readonly SraySpec _s;
        readonly List<(float Len, float Theta, float Phi, float DTheta, float DPhi, float Alpha, float Fade, float W0, float W1)> _rays = new();
        float _age, _spawnCarry, _life;

        public SrayFx(SraySpec s, LabParticleSink sink)
        {
            _s = s;
            _life = s.Duration > 0.01f ? s.Duration : 1.6f;
        }

        public bool Tick(float dt, LabParticleSink sink)
        {
            _age += dt;
            int cap = _s.Count <= 0 ? 16 : Math.Clamp(_s.Count, 1, 64);
            float period = _s.SpawnPeriod <= 0f ? 0.015f : _s.SpawnPeriod;
            _spawnCarry += dt / period;
            while (_spawnCarry >= 1f && _rays.Count < cap)
            {
                _spawnCarry -= 1f;
                var sk = sink;
                _rays.Add((
                    sk.Rnd(MathF.Min(_s.LMin, _s.LMax), MathF.Max(_s.LMin, _s.LMax) <= 0f ? 10f : MathF.Max(_s.LMin, _s.LMax)),
                    _s.Theta.X, _s.Phi.X,
                    sk.Rnd(_s.Theta.Y, MathF.Max(_s.Theta.Y, _s.Theta.Z)),
                    sk.Rnd(MathF.Min(_s.Phi.Y, _s.Phi.Z), MathF.Max(_s.Phi.Y, _s.Phi.Z)),
                    _s.Alpha.X <= 0f ? 1f : _s.Alpha.X,
                    sk.Rnd(MathF.Min(_s.Alpha.Y, _s.Alpha.Z), MathF.Max(MathF.Max(_s.Alpha.Y, _s.Alpha.Z), 0.1f)),
                    sk.Rnd(0.7f, 1.3f), sk.Rnd(0.7f, 1.3f)));
            }
            for (int i = _rays.Count - 1; i >= 0; i--)
            {
                var r = _rays[i];
                r.Theta += r.DTheta * dt;
                r.Phi += r.DPhi * dt;
                r.Alpha -= r.Fade * dt;
                if (r.Alpha <= 0f) { _rays.RemoveAt(i); continue; }
                _rays[i] = r;
            }
            return _age < _life || _rays.Count > 0;
        }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            var c0 = Fallback(NormColor(_s.Color0), new Vector4(1f, 0.85f, 0.4f, 0.9f));
            var c1 = Fallback(NormColor(_s.Color1), c0);
            foreach (var r in _rays)
            {
                var dir = new Vector3(
                    MathF.Cos(r.Phi) * MathF.Cos(r.Theta),
                    MathF.Sin(r.Phi),
                    MathF.Cos(r.Phi) * MathF.Sin(r.Theta));
                for (int i = 1; i <= 6; i++)
                {
                    float t = i / 6f;
                    var col = Vector4.Lerp(c0, c1, t);
                    float w = (_s.WsMin + (_s.WeMin - _s.WsMin) * t) * r.W0;
                    AddSplat(into, _s.Anchor + dir * (r.Len * t * 0.999f),
                        MathF.Max(0.03f, MathF.Abs(w) <= 0.001f ? 0.08f : MathF.Abs(w)),
                        col, Math.Clamp(r.Alpha, 0f, 1f) * col.W, additive: true);
                }
            }
        }
    }

    private sealed class FlurryFx : ILabFx
    {
        readonly FlurrySpec _s;
        readonly float[] _phase, _theta0, _phi0;
        float _age, _life;

        public FlurryFx(FlurrySpec s, LabParticleSink sink)
        {
            _s = s;
            _life = s.Duration > 0.01f ? s.Duration : 2f;
            int n = Math.Clamp(s.Count <= 0 ? 50 : s.Count, 1, 140);
            _phase = new float[n]; _theta0 = new float[n]; _phi0 = new float[n];
            for (int i = 0; i < n; i++)
            {
                _phase[i] = sink.Rnd() * MathF.PI * 2f;
                _theta0[i] = sink.Rnd() * MathF.PI * 2f;
                _phi0[i] = (sink.Rnd() - 0.5f) * MathF.PI;
            }
        }

        public bool Tick(float dt, LabParticleSink sink) { _age += dt; return _age < _life; }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            float a = TinTout(_age, _life, _s.FadeIn, _s.FadeOut);
            if (a <= 0.01f) return;
            var col = Fallback(NormColor(_s.Color), new Vector4(0.7f, 0.85f, 1f, 0.8f));
            float radius = _s.Radius <= 0f ? 1f : _s.Radius;
            float grow = Grow3(Math.Clamp(_age / _life, 0f, 1f),
                _s.GrowStart <= 0f ? 1f : _s.GrowStart, _s.GrowMid <= 0f ? 1f : _s.GrowMid, _s.GrowEnd <= 0f ? 1f : _s.GrowEnd);
            for (int i = 0; i < _phase.Length; i++)
            {
                float theta = _theta0[i] + _s.ITheta * _age;
                float phi = _phi0[i] + _s.IPhi * _age * 0.6f;
                float r = radius * (1f + _s.Amplitude * 0.3f * MathF.Sin(_s.IAmp * _age * 4f + _phase[i]));
                var p = _s.Anchor + new Vector3(
                    MathF.Cos(phi) * MathF.Cos(theta), MathF.Sin(phi), MathF.Cos(phi) * MathF.Sin(theta)) * r;
                AddSplat(into, p, 0.07f * grow, col, a * col.W, additive: true);
            }
        }
    }

    private sealed class SpeFx : ILabFx
    {
        public SpeSpec S; public float Life, Age;

        public bool Tick(float dt, LabParticleSink sink) { Age += dt; return Age < Life; }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            float a = TinTout(Age, Life, S.FadeIn, S.FadeOut);
            if (a <= 0.01f) return;
            var col = Fallback(NormColor(S.Color), new Vector4(0.8f, 0.7f, 1f, 0.8f));
            int n = Math.Clamp(S.Count <= 0 ? 64 : S.Count, 1, 140);
            float radius = S.Radius <= 0f ? 1f : S.Radius;
            for (int i = 0; i < n; i++)
            {
                // exact SU-212 model: per axis, radius * (sin(i0 + s0·t + sp0·i) + sin(i1 + s1·t + sp1·i)) / 2
                var p = S.Anchor + radius * new Vector3(
                    (MathF.Sin(S.Index0.X + S.Speed0.X * Age + S.Space0.X * i) + MathF.Sin(S.Index1.X + S.Speed1.X * Age + S.Space1.X * i)) * 0.5f,
                    (MathF.Sin(S.Index0.Y + S.Speed0.Y * Age + S.Space0.Y * i) + MathF.Sin(S.Index1.Y + S.Speed1.Y * Age + S.Space1.Y * i)) * 0.5f,
                    (MathF.Sin(S.Index0.Z + S.Speed0.Z * Age + S.Space0.Z * i) + MathF.Sin(S.Index1.Z + S.Speed1.Z * Age + S.Space1.Z * i)) * 0.5f);
                AddSplat(into, p, S.Scale <= 0f ? 0.12f : S.Scale, col, a * col.W, additive: true);
            }
        }
    }

    private sealed class SparklesFx : ILabFx
    {
        readonly SparklesSpec _s;
        readonly Vector3[] _pts;
        readonly float[] _phase, _period;
        float _age, _life;

        public SparklesFx(SparklesSpec s, LabParticleSink sink)
        {
            _s = s;
            _life = s.Duration > 0.01f ? s.Duration : 2f;
            int n = Math.Clamp(s.Count <= 0 ? 60 : s.Count, 1, 160);
            _pts = new Vector3[n]; _phase = new float[n]; _period = new float[n];
            float radius = s.Radius <= 0f ? 1f : s.Radius;
            for (int i = 0; i < n; i++)
            {
                _pts[i] = sink.RndInSphere() * radius;
                _phase[i] = sink.Rnd() * MathF.PI * 2f;
                _period[i] = sink.Rnd(0.5f, 1.3f);
            }
        }

        public bool Tick(float dt, LabParticleSink sink) { _age += dt; return _age < _life; }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            var col = Fallback(NormColor(_s.Color), new Vector4(1f, 0.95f, 0.7f, 0.9f));
            float tail = Math.Clamp((_life - _age) / 0.4f, 0f, 1f);
            for (int i = 0; i < _pts.Length; i++)
            {
                float a = MathF.Max(0f, MathF.Sin(_phase[i] + _age / _period[i] * MathF.PI * 2f)) * tail;
                if (a <= 0.03f) continue;
                var p = _s.Anchor + _pts[i] + new Vector3(0f, _s.YVel * _age, 0f);
                AddSplat(into, p, 0.05f * (_s.PSize <= 0f ? 1f : _s.PSize), col, a * col.W, additive: true);
            }
        }
    }

    private sealed class ChargeFx : ILabFx
    {
        readonly ChargeSpec _s;
        readonly Vector3[] _dirs;
        readonly float[] _stagger;
        float _age, _life;

        public ChargeFx(ChargeSpec s, LabParticleSink sink)
        {
            _s = s;
            _life = s.Duration > 0.01f ? s.Duration : (s.Tout <= 0f ? 1f : s.Tout) + 0.4f;
            int n = Math.Clamp(s.Count <= 0 ? 16 : s.Count, 1, 64);
            _dirs = new Vector3[n]; _stagger = new float[n];
            for (int i = 0; i < n; i++) { _dirs[i] = sink.RndOnSphere(); _stagger[i] = sink.Rnd() * 0.35f; }
        }

        public bool Tick(float dt, LabParticleSink sink) { _age += dt; return _age < _life; }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            var col = Fallback(NormColor(_s.Color), new Vector4(0.6f, 0.8f, 1f, 0.9f));
            float radius = _s.Radius <= 0f ? 1f : _s.Radius;
            float tout = _s.Tout <= 0f ? 1f : _s.Tout;
            float speed = _s.Speed0 <= 0f ? 1f : _s.Speed0;
            float centerFrac = Math.Clamp(_age / _life, 0f, 1f);
            foreach (var (dir, st) in Zip(_dirs, _stagger))
            {
                float u = Math.Clamp((_age - st) / tout * speed, 0f, 1f);
                float dist = radius * MathF.Pow(1f - u, 1.6f);
                float a = Math.Clamp((_s.IAlpha <= 0f ? 4f : _s.IAlpha) * (_age - st), 0f, 1f);
                if (a <= 0.02f || u >= 1f) continue;
                AddSplat(into, _s.Anchor + dir * dist, 0.06f, col, a * col.W, additive: true);
            }
            // the coalesced core grows toward CenterSize
            AddSplat(into, _s.Anchor, 0.08f + (_s.CenterSize <= 0f ? 0.75f : _s.CenterSize) * 0.35f * centerFrac,
                col, Math.Clamp(centerFrac * 1.4f, 0f, 1f) * col.W, additive: true);
        }

        static IEnumerable<(Vector3, float)> Zip(Vector3[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++) yield return (a[i], b[i]);
        }
    }

    private sealed class SphereFx : ILabFx
    {
        public SphereMeshSpec S; public float Life, Age;

        public bool Tick(float dt, LabParticleSink sink) { Age += dt; return Age < Life; }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            float a = TinTout(Age, Life, S.FadeIn, S.FadeOut) * 0.45f;
            if (a <= 0.01f) return;
            var col = Fallback(NormColor(S.Color), new Vector4(1f, 0.55f, 0.25f, 0.7f));
            float grow = Grow3(Math.Clamp(Age / Life, 0f, 1f),
                S.GrowStart <= 0f ? 1f : S.GrowStart, S.GrowMid <= 0f ? 1f : S.GrowMid, S.GrowEnd <= 0f ? 1f : S.GrowEnd);
            float radius = (S.Radius <= 0f ? 1f : S.Radius) * grow;
            const int lat = 7, lon = 12;
            for (int i = 1; i < lat; i++)
            {
                float phi = i / (float)lat * MathF.PI - MathF.PI / 2f;
                for (int j = 0; j < lon; j++)
                {
                    float theta = j / (float)lon * MathF.PI * 2f;
                    var p = S.Anchor + radius * new Vector3(
                        MathF.Cos(phi) * MathF.Cos(theta), MathF.Sin(phi), MathF.Cos(phi) * MathF.Sin(theta));
                    AddSplat(into, p, 0.05f + radius * 0.04f, col, a * col.W, additive: true);
                }
            }
        }
    }

    private sealed class ProjFx : ILabFx
    {
        public Vector3 Pos, Target; public Vector4 Color; public float Scale, Speed, Life, Age;
        public int ImpactKind;

        public bool Tick(float dt, LabParticleSink sink)
        {
            Age += dt;
            if (Age >= Life) return false;
            var to = Target - Pos;
            float dist = to.Length();
            float step = Speed * dt;
            if (step >= dist)
            {
                // arrival: paint the impact ONCE, here in Tick, then die
                var impact = ImpactKind switch
                {
                    1 => new Vector4(0.6f, 0.85f, 1f, 0.9f),   // ice/frost
                    2 => new Vector4(0.85f, 0.8f, 1f, 0.95f),  // lightning crack
                    _ => new Vector4(1f, 0.6f, 0.2f, 0.9f),    // fire
                };
                sink.SpawnSpark(Target, impact, Scale, 0.5f, 22);
                sink.SpawnFire(Target, impact, Scale * 1.2f, 0.45f, 10);
                return false;
            }
            Pos += to / dist * step;
            // short-lived trail stamp rides in the pool
            sink.Emit(Pos, new Vector3(sink.RndSym(0.3f), sink.Rnd(0.3f, 0.9f), sink.RndSym(0.3f)),
                Vector3.Zero, Color, 0.09f * Scale, 0.35f, additive: true);
            return true;
        }

        public void Emit(List<SoftwareRenderer.Splat> into, LabParticleSink sink)
        {
            // glowing head only — pure draw, no state
            AddSplat(into, Pos, 0.16f * Scale, Color, Math.Clamp(Color.W, 0f, 1f), additive: true);
        }
    }
}

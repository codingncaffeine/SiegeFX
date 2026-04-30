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
    private readonly GlTexture?[] _textures = new GlTexture?[4];

    private readonly List<Particle>        _particles   = new(2048);
    private readonly List<LightningBolt>   _bolts       = new(64);
    private readonly List<SpellProjectile> _projectiles = new(32);

    private const int InstanceFloats = 12; // pos(3) + scale(1) + color(4) + texSlotF(1) + lifeFrac(1) + reserved(2)
    private float[] _instanceBuffer = new float[2048 * InstanceFloats];

    public int LiveParticleCount   => _particles.Count;
    public int LiveBoltCount       => _bolts.Count;
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
            ImpactKind = (byte)Math.Clamp(impactKind, 0, 2),
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
            // Trail: 60 particles/sec ~ one fire puff per frame at 60fps,
            // plus a thinner ember spark stream.
            float trailRate = 90f;
            float budget = pr.TrailCarry + trailRate * dt;
            int n = (int)budget;
            if (n > 0)
            {
                SpawnFire(pr.Position, pr.Color, pr.Scale * 0.55f, 0.30f, n);
                if ((n & 1) == 0)
                    SpawnSpark(pr.Position, new Vector4(1f, 0.9f, 0.55f, 1f),
                               pr.Scale * 0.6f, 0.20f, 2);
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
    }

    public void Draw(Matrix4x4 view, Matrix4x4 proj, Vector3 cameraPos)
    {
        if (_particles.Count == 0 && _bolts.Count == 0 && _projectiles.Count == 0) return;

        // Bolts compile into transient particle quads each frame (cheap; max
        // a few dozen segments per bolt). This keeps draw paths unified.
        EmitBoltQuads();
        EmitProjectileHeads();

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

    void EmitBoltQuads()
    {
        // Phase 21-SC-SPELL-VFX — denser segments + larger bright sparks so the
        // bolt actually reads against terrain at gameplay distance. The prior
        // 8-segment / 0.18-scale stand-in was technically rendering but visually
        // invisible against a sunlit ground plane.
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
            // Phase 21-SC-SPELL-VFX-2: when DS1 supplies maxdisplace(N) honour it
            // (clamped to a sane minimum so a tiny script value doesn't render
            // as a perfectly straight pencil-line at gameplay distance).
            float xAmp = b.Displace > 0.001f
                ? MathF.Max(b.Displace, MathF.Min(len * 0.06f, 0.20f))
                : MathF.Min(len * 0.10f, 0.45f);
            float yAmp = b.Displace > 0.001f
                ? MathF.Max(b.Displace * 0.7f, MathF.Min(len * 0.05f, 0.18f))
                : MathF.Min(len * 0.08f, 0.35f);
            for (int s = 0; s < Segments; s++)
            {
                float t = (s + 0.5f) / Segments;
                rng = rng * 1664525u + 1013904223u;
                float jx = ((rng & 0xFFFF) / 65535f - 0.5f) * xAmp;
                rng = rng * 1664525u + 1013904223u;
                float jy = ((rng & 0xFFFF) / 65535f - 0.5f) * yAmp;
                var p = b.Source + dir * t + side * jx + up * jy;
                var c = b.Color; c.W = MathF.Min(1f, c.W * (1f - frac * 0.8f));
                _particles.Add(new Particle
                {
                    Position  = p,
                    Velocity  = Vector3.Zero,
                    Accel     = Vector3.Zero,
                    Color0    = c,
                    Color1    = new Vector4(c.X, c.Y, c.Z, 0f),
                    Scale0    = 0.55f,
                    Scale1    = 0.35f,
                    Life      = 0.10f,
                    TotalLife = 0.10f,
                    TexSlot   = 2,
                    Additive  = 1,
                });
            }
            // Bright "core" particles at source and target so the endpoints
            // read as anchored hits even when the camera grazes the line.
            var core = b.Color; core.W = 1f;
            _particles.Add(new Particle
            {
                Position = b.Source, Color0 = core,
                Color1 = new Vector4(core.X, core.Y, core.Z, 0f),
                Scale0 = 0.85f, Scale1 = 0.55f,
                Life = 0.10f, TotalLife = 0.10f,
                TexSlot = 2, Additive = 1,
            });
            _particles.Add(new Particle
            {
                Position = b.Target, Color0 = core,
                Color1 = new Vector4(core.X, core.Y, core.Z, 0f),
                Scale0 = 0.95f, Scale1 = 0.60f,
                Life = 0.10f, TotalLife = 0.10f,
                TexSlot = 2, Additive = 1,
            });
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
out vec4 frag;
void main(){
  vec4 tex;
  int slot = int(vSlot + 0.5);
  if      (slot == 0) tex = texture(uTex0, vUv);
  else if (slot == 1) tex = texture(uTex1, vUv);
  else if (slot == 2) tex = texture(uTex2, vUv);
  else                tex = texture(uTex3, vUv);
  vec4 c = tex * vColor;
  // Additive: pre-multiply RGB by alpha so the shared SrcAlpha blend
  // approximates the brighter additive look (alpha stays 1, RGB carries
  // the modulated brightness).
  if (vAdditive > 0.5) {
    c.rgb *= c.a;
    c.a   = 0.0;
    c.rgb *= 2.0;
  }
  frag = c;
}";
}

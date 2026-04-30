using System.Numerics;

namespace SiegeFX.Core.Sfx;

/// <summary>Phase 17-SC-J — public mode tag for persistent emitter
/// registration paths (legacy <c>emt_particle</c> instances). Mirrors the
/// internal EmitterMode discriminant in SfxRuntime; kept here so callers
/// outside Sfx don't have to re-derive it.</summary>
public enum ParticleKind { Fire, Smoke, Steam }

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
}

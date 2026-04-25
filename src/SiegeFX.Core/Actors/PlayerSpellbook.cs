using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>Why a cast attempt did or didn't fire. Drives HUD feedback —
/// "no mana" / "out of range" / "no target" all want different messages.</summary>
public enum CastOutcome
{
    Cast,
    NoSpell,
    NoTarget,
    TargetDead,
    OutOfRange,
    NoMana,
    OnCooldown,
}

/// <summary>Result of a <see cref="PlayerSpellbook.TryCast"/> call. <see cref="Damage"/>
/// is meaningful only when <see cref="Outcome"/> is <see cref="CastOutcome.Cast"/>.</summary>
public readonly record struct CastResult(
    CastOutcome Outcome,
    SpellTemplate? Spell,
    float ManaSpent,
    float Damage,
    bool TargetKilled);

/// <summary>
/// Single-slot spellbook attached to the player actor. Owns the slotted spell,
/// the running cooldown clock, and the cast-validation pipeline. Phase 17a
/// keeps just one slot — primary spell — so 'Q' has a fixed binding; multi-slot
/// hotbars are a later UI/inventory job once the spell book GUI lands.
///
/// All gating (target alive, in range, mana, cooldown) lives here so the
/// render layer's job is purely "press key → see message + side effects".
/// Damage is rolled by <see cref="SpellTemplate.RollDamage"/> using the
/// player's <see cref="ActorStats.Intelligence"/> as the proxy magic level
/// — DS1 distinguishes nature/combat magic skill levels per pool, which is
/// the Phase 17b+ split (matches the four-skill spread parked in
/// <see cref="PlayerProgression"/>).
/// </summary>
public sealed class PlayerSpellbook
{
    readonly Actor _player;
    readonly Random _rng;

    public SpellTemplate? Primary { get; private set; }

    /// <summary>Seconds remaining until the slot can be cast again. Counts
    /// down inside <see cref="Tick"/>; a fresh cast resets it from the
    /// spell's <see cref="SpellTemplate.CastReloadDelay"/>.</summary>
    public float CooldownRemaining { get; private set; }

    public PlayerSpellbook(Actor player, Random rng)
    {
        _player = player;
        _rng = rng;
    }

    public void Slot(SpellTemplate? spell)
    {
        Primary = spell;
        CooldownRemaining = 0f;
    }

    public void Tick(float dt)
    {
        if (CooldownRemaining > 0f) CooldownRemaining = MathF.Max(0f, CooldownRemaining - dt);
    }

    /// <summary>Try to cast the slotted spell at <paramref name="target"/>.
    /// <paramref name="distance"/> is the world-space gap between caster and
    /// target (XZ distance, computed at the call site since this class is
    /// math-only and doesn't reach into transforms). <paramref name="magicLevel"/>
    /// is the caster's effective magic skill level — DS1 spell formulas
    /// substitute it for <c>#magic</c>. Until per-skill pools land in 17b+,
    /// the render layer passes the player's progression level.</summary>
    public CastResult TryCast(Actor? target, float distance, float magicLevel)
    {
        if (Primary is null) return new CastResult(CastOutcome.NoSpell, null, 0, 0, false);
        if (target is null) return new CastResult(CastOutcome.NoTarget, Primary, 0, 0, false);
        if (target.Combat.IsDead) return new CastResult(CastOutcome.TargetDead, Primary, 0, 0, false);
        if (CooldownRemaining > 0f) return new CastResult(CastOutcome.OnCooldown, Primary, 0, 0, false);
        if (distance > Primary.CastRange) return new CastResult(CastOutcome.OutOfRange, Primary, 0, 0, false);

        float cost = Primary.ManaCost(magicLevel);
        if (_player.Combat.CurrentMana < cost) return new CastResult(CastOutcome.NoMana, Primary, 0, 0, false);

        float spent = _player.Combat.SpendMana(cost);
        float damage = Primary.RollDamage(magicLevel, _rng);
        float dealt = damage > 0f ? target.Combat.ApplyDamage(damage) : 0f;
        bool killed = target.Combat.IsDead;

        CooldownRemaining = Primary.CastReloadDelay;
        return new CastResult(CastOutcome.Cast, Primary, spent, dealt, killed);
    }
}

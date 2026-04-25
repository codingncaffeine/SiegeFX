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
    AlreadyFull,
}

/// <summary>Identifier for a slot in the player's hotbar. Phase 17a was a
/// single primary slot; 17c added Secondary so a heal can sit alongside the
/// attack spell. Multi-slot hotbar UI ships when the spellbook GUI lands.</summary>
public enum SpellSlot
{
    Primary,
    Secondary,
}

/// <summary>Result of a <see cref="PlayerSpellbook.TryCast"/> call. <see cref="Damage"/>
/// is meaningful for offensive spells; <see cref="HealAmount"/> for self-heals.
/// Both are zero unless <see cref="Outcome"/> is <see cref="CastOutcome.Cast"/>.</summary>
public readonly record struct CastResult(
    CastOutcome Outcome,
    SpellTemplate? Spell,
    float ManaSpent,
    float Damage,
    float HealAmount,
    bool TargetKilled);

/// <summary>
/// Two-slot spellbook attached to the player actor. Owns the slotted spells,
/// per-slot cooldown clocks, and the cast-validation pipeline. Phase 17c
/// keeps two slots — primary attack + secondary heal — so 'Q' and 'W' have
/// fixed bindings; multi-slot hotbars are a later UI/inventory job once the
/// spell book GUI lands.
///
/// All gating (target alive, in range, mana, cooldown, full-life skip) lives
/// here so the render layer's job is purely "press key → see message + side
/// effects". Damage is rolled by <see cref="SpellTemplate.RollDamage"/>;
/// heals by <see cref="SpellTemplate.HealAmount"/>. The caller passes the
/// effective magic level — DS1 spell formulas substitute it for <c>#magic</c>.
/// Until per-skill pools land, the render layer passes the player's overall
/// progression level (the four-skill spread is parked in
/// <see cref="PlayerProgression"/>).
/// </summary>
public sealed class PlayerSpellbook
{
    readonly Actor _player;
    readonly Random _rng;

    public SpellTemplate? Primary { get; private set; }
    public SpellTemplate? Secondary { get; private set; }

    /// <summary>Seconds remaining until the slot can be cast again. Counts
    /// down inside <see cref="Tick"/>; a fresh cast resets it from the
    /// spell's <see cref="SpellTemplate.CastReloadDelay"/>.</summary>
    public float PrimaryCooldownRemaining { get; private set; }
    public float SecondaryCooldownRemaining { get; private set; }

    /// <summary>Phase 17a back-compat alias — the only slot that existed
    /// when <see cref="CooldownRemaining"/> was the public surface.</summary>
    public float CooldownRemaining => PrimaryCooldownRemaining;

    public PlayerSpellbook(Actor player, Random rng)
    {
        _player = player;
        _rng = rng;
    }

    public void Slot(SpellTemplate? spell) => Slot(SpellSlot.Primary, spell);

    public void Slot(SpellSlot slot, SpellTemplate? spell)
    {
        switch (slot)
        {
            case SpellSlot.Primary:
                Primary = spell;
                PrimaryCooldownRemaining = 0f;
                break;
            case SpellSlot.Secondary:
                Secondary = spell;
                SecondaryCooldownRemaining = 0f;
                break;
        }
    }

    public void Tick(float dt)
    {
        if (PrimaryCooldownRemaining   > 0f) PrimaryCooldownRemaining   = MathF.Max(0f, PrimaryCooldownRemaining   - dt);
        if (SecondaryCooldownRemaining > 0f) SecondaryCooldownRemaining = MathF.Max(0f, SecondaryCooldownRemaining - dt);
    }

    /// <summary>Try to cast the primary-slot spell at <paramref name="target"/>.
    /// Phase 17a back-compat overload — defaults to <see cref="SpellSlot.Primary"/>.</summary>
    public CastResult TryCast(Actor? target, float distance, float magicLevel)
        => TryCast(SpellSlot.Primary, target, distance, magicLevel);

    /// <summary>Try to cast the spell in <paramref name="slot"/> at
    /// <paramref name="target"/>. <paramref name="distance"/> is the world-space
    /// XZ gap between caster and target. Self-target heals ignore both target
    /// and distance and apply to the caster.</summary>
    public CastResult TryCast(SpellSlot slot, Actor? target, float distance, float magicLevel)
    {
        var spell = slot == SpellSlot.Primary ? Primary : Secondary;
        if (spell is null) return new CastResult(CastOutcome.NoSpell, null, 0, 0, 0, false);

        float cd = slot == SpellSlot.Primary ? PrimaryCooldownRemaining : SecondaryCooldownRemaining;
        if (cd > 0f) return new CastResult(CastOutcome.OnCooldown, spell, 0, 0, 0, false);

        if (spell.Kind == SpellKind.SelfHeal)
            return TryCastSelfHeal(slot, spell, magicLevel);

        if (target is null) return new CastResult(CastOutcome.NoTarget, spell, 0, 0, 0, false);
        if (target.Combat.IsDead) return new CastResult(CastOutcome.TargetDead, spell, 0, 0, 0, false);
        if (distance > spell.CastRange) return new CastResult(CastOutcome.OutOfRange, spell, 0, 0, 0, false);

        float cost = spell.ManaCost(magicLevel);
        if (_player.Combat.CurrentMana < cost) return new CastResult(CastOutcome.NoMana, spell, 0, 0, 0, false);

        float spent = _player.Combat.SpendMana(cost);
        float damage = spell.RollDamage(magicLevel, _rng);
        float dealt  = damage > 0f ? target.Combat.ApplyDamage(damage) : 0f;
        bool killed  = target.Combat.IsDead;

        StartCooldown(slot, spell.CastReloadDelay);
        return new CastResult(CastOutcome.Cast, spell, spent, dealt, 0f, killed);
    }

    CastResult TryCastSelfHeal(SpellSlot slot, SpellTemplate spell, float magicLevel)
    {
        // No-op when already at full life — DS1 grays out heal icons in that
        // state. Better to refund the cast than to spend mana for nothing.
        if (_player.Combat.CurrentLife >= _player.Stats.MaxLife)
            return new CastResult(CastOutcome.AlreadyFull, spell, 0, 0, 0, false);

        float cost = spell.ManaCost(magicLevel);
        if (_player.Combat.CurrentMana < cost)
            return new CastResult(CastOutcome.NoMana, spell, 0, 0, 0, false);

        float spent = _player.Combat.SpendMana(cost);
        float heal  = spell.HealAmount(magicLevel);
        if (heal > 0f) _player.Combat.Heal(heal);
        StartCooldown(slot, spell.CastReloadDelay);
        return new CastResult(CastOutcome.Cast, spell, spent, 0f, heal, false);
    }

    void StartCooldown(SpellSlot slot, float seconds)
    {
        if (slot == SpellSlot.Primary) PrimaryCooldownRemaining = seconds;
        else                            SecondaryCooldownRemaining = seconds;
    }
}

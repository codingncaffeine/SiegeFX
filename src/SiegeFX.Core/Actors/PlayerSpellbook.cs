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

    /// <summary>Phase 21-SC-SCROLL-C-1 — the 10 user-organized "placed"
    /// rows below the two active slots in the spellbook UI. A learned
    /// spell that isn't in an active slot lives here. Null entries
    /// render as empty cells in <see cref="Hud.SpellBookPanel"/>; non-
    /// null entries are draggable scrolls. Length is fixed at 10 (matches
    /// SpellBookPanel.InactiveSlots).</summary>
    public SpellTemplate?[] Placed { get; } = new SpellTemplate?[10];

    /// <summary>Total number of <see cref="Placed"/> rows. Public so
    /// callers can iterate without hardcoding the constant.</summary>
    public int PlacedCount => Placed.Length;

    /// <summary>Phase 21-SC-SCROLL-C-1 — write to a <see cref="Placed"/>
    /// slot. Pass null to clear. Out-of-range indices silently no-op
    /// rather than throw, matching the existing Slot() forgiveness for
    /// accidental Tertiary etc.</summary>
    public void SetPlaced(int index, SpellTemplate? spell)
    {
        if ((uint)index < (uint)Placed.Length)
            Placed[index] = spell;
    }

    /// <summary>Phase 17a back-compat alias — the only slot that existed
    /// when <see cref="CooldownRemaining"/> was the public surface.</summary>
    public float CooldownRemaining => PrimaryCooldownRemaining;

    public PlayerSpellbook(Actor player, Random rng)
    {
        _player = player;
        _rng = rng;
    }

    public void Slot(SpellTemplate? spell) => Slot(SpellSlot.Primary, spell);

    /// <summary>Phase 21-SC-SCROLL-C-2 — <paramref name="resetCooldown"/>
    /// defaults true (the original 17a behavior — fresh slot starts with
    /// no cooldown), but the scroll-drag drop path passes false. Otherwise
    /// a player mid-cooldown could exploit a drag-and-redrop to reset:
    /// cast fireball -> 5s cd -> open spellbook, drag fireball out, drag
    /// it back into Active1 -> cd reset to 0 -> cast again. Caught by
    /// 27cb12a review.</summary>
    public void Slot(SpellSlot slot, SpellTemplate? spell, bool resetCooldown = true)
    {
        switch (slot)
        {
            case SpellSlot.Primary:
                Primary = spell;
                if (resetCooldown) PrimaryCooldownRemaining = 0f;
                break;
            case SpellSlot.Secondary:
                Secondary = spell;
                if (resetCooldown) SecondaryCooldownRemaining = 0f;
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

        // SC-HEAL-AUDIT — a heal's target (when supplied) is the party
        // member to restore; TryCastSelfHeal falls back to the caster.
        if (spell.Kind == SpellKind.SelfHeal)
            return TryCastSelfHeal(slot, spell, magicLevel, target);

        if (target is null) return new CastResult(CastOutcome.NoTarget, spell, 0, 0, 0, false);
        if (target.Combat.IsDead) return new CastResult(CastOutcome.TargetDead, spell, 0, 0, 0, false);
        if (distance > spell.CastRange) return new CastResult(CastOutcome.OutOfRange, spell, 0, 0, 0, false);

        // Build the caster/target context once. Cost expressions reference
        // caster fields (#src_*, occasionally #maxlife as a self-scaling cost
        // — spell_freeze does this); damage expressions reference target life
        // (#maxlife/#life, e.g. spell_charm scales duration by target maxlife).
        var costCtx = new SpellEvalContext(magicLevel,
            maxLife: _player.Stats.MaxLife,
            life:    _player.Combat.CurrentLife,
            srcMana: _player.Combat.CurrentMana,
            srcLife: _player.Combat.CurrentLife);
        var dmgCtx = new SpellEvalContext(magicLevel,
            maxLife: target.Stats.MaxLife,
            life:    target.Combat.CurrentLife,
            srcMana: _player.Combat.CurrentMana,
            srcLife: _player.Combat.CurrentLife);

        float cost = spell.ManaCost(costCtx);
        if (_player.Combat.CurrentMana < cost) return new CastResult(CastOutcome.NoMana, spell, 0, 0, 0, false);

        float spent = _player.Combat.SpendMana(cost);
        // Session difficulty scales player-dealt spell damage the same as
        // melee (difficulty_easy/medium/hard_player from [combat_constants]).
        float damage = spell.RollDamage(dmgCtx, _rng) * CombatResolver.PlayerDamageMultiplier;
        float dealt  = damage > 0f ? target.Combat.ApplyDamage(damage) : 0f;
        bool killed  = target.Combat.IsDead;

        StartCooldown(slot, spell.CastReloadDelay);
        return new CastResult(CastOutcome.Cast, spell, spent, dealt, 0f, killed);
    }

    /// <summary>SC-SPELLFX-IMPACT — projectile-spell variant: identical
    /// gating and costs to <see cref="TryCast(SpellSlot, Actor?, float, float)"/>
    /// but the rolled damage is NOT applied — it rides the projectile and
    /// the caller resolves it at impact (DS1's fireshot syncs damage to the
    /// ball's collision via WE_SPELL_SYNC_END; a target that steps away is
    /// a genuine miss).</summary>
    public CastResult TryCastDeferred(SpellSlot slot, Actor? target, float distance, float magicLevel)
    {
        var spell = slot == SpellSlot.Primary ? Primary : Secondary;
        if (spell is null) return new CastResult(CastOutcome.NoSpell, null, 0, 0, 0, false);
        float cd = slot == SpellSlot.Primary ? PrimaryCooldownRemaining : SecondaryCooldownRemaining;
        if (cd > 0f) return new CastResult(CastOutcome.OnCooldown, spell, 0, 0, 0, false);
        if (spell.Kind == SpellKind.SelfHeal)
            return TryCastSelfHeal(slot, spell, magicLevel, target);
        if (target is null) return new CastResult(CastOutcome.NoTarget, spell, 0, 0, 0, false);
        if (target.Combat.IsDead) return new CastResult(CastOutcome.TargetDead, spell, 0, 0, 0, false);
        if (distance > spell.CastRange) return new CastResult(CastOutcome.OutOfRange, spell, 0, 0, 0, false);

        var costCtx = new SpellEvalContext(magicLevel,
            maxLife: _player.Stats.MaxLife,
            life:    _player.Combat.CurrentLife,
            srcMana: _player.Combat.CurrentMana,
            srcLife: _player.Combat.CurrentLife);
        var dmgCtx = new SpellEvalContext(magicLevel,
            maxLife: target.Stats.MaxLife,
            life:    target.Combat.CurrentLife,
            srcMana: _player.Combat.CurrentMana,
            srcLife: _player.Combat.CurrentLife);
        float cost = spell.ManaCost(costCtx);
        if (_player.Combat.CurrentMana < cost) return new CastResult(CastOutcome.NoMana, spell, 0, 0, 0, false);

        float spent = _player.Combat.SpendMana(cost);
        float damage = spell.RollDamage(dmgCtx, _rng) * CombatResolver.PlayerDamageMultiplier;
        StartCooldown(slot, spell.CastReloadDelay);
        return new CastResult(CastOutcome.Cast, spell, spent, damage, 0f, false);
    }

    /// <summary>Phase 21-SC-BARREL-B — cast variant for non-actor targets
    /// (breakable props). Same gating as <see cref="TryCast(SpellSlot, Actor?, float, float)"/>
    /// — cooldown / range / mana — but the damage roll uses the caster as
    /// both the cost and damage context (no target maxlife / life to read).
    /// Returns the rolled damage in <see cref="CastResult.Damage"/>; the
    /// caller is responsible for applying it to the prop's life pool.</summary>
    public CastResult TryCastAtPoint(SpellSlot slot, float distance, float magicLevel)
    {
        var spell = slot == SpellSlot.Primary ? Primary : Secondary;
        if (spell is null) return new CastResult(CastOutcome.NoSpell, null, 0, 0, 0, false);

        float cd = slot == SpellSlot.Primary ? PrimaryCooldownRemaining : SecondaryCooldownRemaining;
        if (cd > 0f) return new CastResult(CastOutcome.OnCooldown, spell, 0, 0, 0, false);

        // Self-heal slots can't target props — pass through to the actor path.
        if (spell.Kind == SpellKind.SelfHeal)
            return new CastResult(CastOutcome.NoTarget, spell, 0, 0, 0, false);

        if (distance > spell.CastRange) return new CastResult(CastOutcome.OutOfRange, spell, 0, 0, 0, false);

        var ctx = new SpellEvalContext(magicLevel,
            maxLife: _player.Stats.MaxLife,
            life:    _player.Combat.CurrentLife,
            srcMana: _player.Combat.CurrentMana,
            srcLife: _player.Combat.CurrentLife);
        float cost = spell.ManaCost(ctx);
        if (_player.Combat.CurrentMana < cost) return new CastResult(CastOutcome.NoMana, spell, 0, 0, 0, false);

        float spent = _player.Combat.SpendMana(cost);
        float damage = spell.RollDamage(ctx, _rng) * CombatResolver.PlayerDamageMultiplier;
        StartCooldown(slot, spell.CastReloadDelay);
        return new CastResult(CastOutcome.Cast, spell, spent, damage, 0f, false);
    }

    CastResult TryCastSelfHeal(SpellSlot slot, SpellTemplate spell, float magicLevel,
                               Actor? healTarget = null)
    {
        // SC-HEAL-AUDIT — DS1's single-target heals (healing_hands /
        // battle_healing / major_heal) restore "a single Party Member":
        // the clicked member when the caller passes one, else the caster.
        // Mana always comes from the CASTER (#src_*); the #maxlife/#life
        // the heal formulas clamp against are the TARGET's.
        var target = healTarget ?? _player;
        // No-op when already at full life — DS1 grays out heal icons in that
        // state. Better to refund the cast than to spend mana for nothing.
        if (target.Combat.CurrentLife >= target.Stats.MaxLife)
            return new CastResult(CastOutcome.AlreadyFull, spell, 0, 0, 0, false);

        // spell_healing_hands' ternary clamps heal-against-mana with
        // these values (heals only what the caster's mana can pay for).
        var ctx = new SpellEvalContext(magicLevel,
            maxLife: target.Stats.MaxLife,
            life:    target.Combat.CurrentLife,
            srcMana: _player.Combat.CurrentMana,
            srcLife: _player.Combat.CurrentLife);

        float cost = spell.ManaCost(ctx);
        if (_player.Combat.CurrentMana < cost)
            return new CastResult(CastOutcome.NoMana, spell, 0, 0, 0, false);

        float spent = _player.Combat.SpendMana(cost);
        float heal  = spell.HealAmount(ctx);
        if (heal > 0f) target.Combat.Heal(heal);
        StartCooldown(slot, spell.CastReloadDelay);
        return new CastResult(CastOutcome.Cast, spell, spent, 0f, heal, false);
    }

    /// <summary>Phase 19b — restore both per-slot cooldowns from a save
    /// snapshot. Clamps to non-negative so a corrupt save can't drive the
    /// counter below zero (which Tick wouldn't recover from). Replaces
    /// the value rather than adding so a re-load from the same save is
    /// idempotent.</summary>
    public void RestoreCooldowns(float primary, float secondary)
    {
        PrimaryCooldownRemaining   = MathF.Max(0f, primary);
        SecondaryCooldownRemaining = MathF.Max(0f, secondary);
    }

    void StartCooldown(SpellSlot slot, float seconds)
    {
        if (slot == SpellSlot.Primary) PrimaryCooldownRemaining = seconds;
        else                            SecondaryCooldownRemaining = seconds;
    }
}

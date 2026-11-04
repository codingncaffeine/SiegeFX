using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>Phase 18 — DS1's weapon → animation-stance mapping. The fs# in
/// every character anim name (<c>a_c_gah_fb_fs1_at.prs</c>) is an
/// <c>eAnimStance</c> ordinal; the retail namingkey.nnk CANIMCLASS table
/// names them: 0 Unarmed, 1 Single Sword, 2 Sword &amp; Shield, 3 Battle Axe
/// (shaft-handled two-handers), 4 Long Sword (short-handled two-handers),
/// 5 Staff, 6 Bow &amp; Arrow, 7 Mini-gun (crossbows + all exotic ranged),
/// 8 Shield only. The wielder side is selected from the weapon's authored
/// <c>[attack] attack_class</c> + <c>is_two_handed</c> plus shield presence
/// (job_attack skrits read <c>inventory.animstance</c>; the class fields are
/// the only authored inputs).</summary>
public static class WeaponStance
{
    public const int Unarmed = 0;
    public const int SingleMelee = 1;
    public const int SingleMeleeShield = 2;
    public const int TwoHandedMelee = 3;
    public const int TwoHandedSword = 4;
    public const int Staff = 5;
    public const int Bow = 6;
    public const int Minigun = 7;
    public const int ShieldOnly = 8;

    /// <summary>Resolve the animation stance for a wielder. <paramref name="weapon"/>
    /// null = unarmed (or shield-only when a real shield is equipped).</summary>
    public static int Resolve(TemplateStore store, Template? weapon, bool shieldEquipped)
    {
        if (weapon is null) return shieldEquipped ? ShieldOnly : Unarmed;
        var cls = (store.GetAttribute(weapon, "attack", "attack_class") ?? "")
            .Trim().ToLowerInvariant();
        bool twoHanded = IsTrue(store.GetAttribute(weapon, "attack", "is_two_handed"));
        return cls switch
        {
            "ac_bow"     => Bow,
            "ac_minigun" => Minigun, // crossbows, miniguns, flamethrowers
            "ac_staff"   => Staff,
            // Retail 2H swords are ac_sword + is_two_handed → the Long Sword
            // stance; every other two-handed melee class (axe/hammer/mace/club)
            // uses the shaft-handled Battle Axe stance.
            "ac_sword"   => twoHanded ? TwoHandedSword
                          : shieldEquipped ? SingleMeleeShield : SingleMelee,
            _            => twoHanded ? TwoHandedMelee
                          : shieldEquipped ? SingleMeleeShield : SingleMelee,
        };
    }

    /// <summary>True when the item template's specializes chain hits
    /// <c>base_shield</c> — the shield check for stance 2/8. (krug_throw
    /// carries a rock in es_shield_hand; a slot check alone would put every
    /// rock-chucker in the sword-and-board stance.)</summary>
    public static bool IsShield(Template? tpl)
    {
        for (var t = tpl; t is not null; t = t.Specializes)
            if (string.Equals(t.Name, "base_shield", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static bool IsTrue(string? v) =>
        v is not null && v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Phase 18 — the PRS critical-event vocabulary (NOTE chunk). Tokens
/// are FourCCs authored on the Siege Max "CriticalEvents" note track; times
/// are NORMALIZED 0..1 across the clip (the exporter multiplies out by
/// fps × length on import). The runtime contract, per select_attack.skrit:
/// FIRE = the strike lands / projectile looses (→ WE_ANIM_WEAPON_FIRE, the
/// hit frame), ATTA = attach ammo (nock the arrow), BSWG/ESWG = the swing
/// window (whoosh + weapon trail), plus SFX1-4 / DEAD / HIDE / SHOW / loop
/// and footstep markers not consumed by combat.</summary>
public static class AnimNotes
{
    public const uint Fire       = 0x45524946; // 'FIRE'
    public const uint BeginSwing = 0x47575342; // 'BSWG'
    public const uint EndSwing   = 0x47575345; // 'ESWG'
    public const uint AttachAmmo = 0x41545441; // 'ATTA'

    /// <summary>All event times for <paramref name="token"/>, converted to
    /// SECONDS into the clip (normalized × AnimLength), ascending. Empty
    /// array when the clip authors none.</summary>
    public static float[] Times(PrsAnimation clip, uint token)
    {
        List<float>? hits = null;
        foreach (var n in clip.Notes)
        {
            if (n.Token != token) continue;
            (hits ??= new List<float>(2)).Add(Math.Clamp(n.Time, 0f, 1f) * clip.AnimLength);
        }
        if (hits is null) return Array.Empty<float>();
        hits.Sort();
        return hits.ToArray();
    }

    /// <summary>The SFX1..SFX4 note number (1-4) of <paramref name="token"/>,
    /// or 0 when it is another note.</summary>
    public static int SfxNumber(uint token) => token switch
    {
        0x31584653 => 1, // 'SFX1'
        0x32584653 => 2, // 'SFX2'
        0x33584653 => 3, // 'SFX3'
        0x34584653 => 4, // 'SFX4'
        _ => 0,
    };

    /// <summary>SC-ANIM-SFX — the SFX1..4 notes a clip passed while its
    /// clock moved from <paramref name="before"/> to <paramref name="after"/>
    /// seconds, in order, as note numbers (1-4). The clock is the unwrapped
    /// play time: a looping idle keeps counting past the clip length, so a
    /// note fires once per loop, every loop. The interval is (before, after],
    /// plus a note at exactly 0 when the clip just started (before == 0).
    /// DS1's chore skrits post WE_ANIM_SFX(n) to the actor for each of these
    /// (select_fidget's ANIMEVENT_SFX_n tests).</summary>
    public static void SfxCrossed(IReadOnlyList<PrsAnimation.NoteEvent> notes, float animLength,
                                  double before, double after, List<int> into)
    {
        if (notes.Count == 0 || animLength <= 0f || after <= before) return;
        long first = (long)Math.Floor(before / animLength);
        long last = (long)Math.Floor(after / animLength);
        // A clock that jumped many loops at once (a hitch) fires one loop's worth.
        if (last - first > 1) first = last - 1;
        List<(double At, int Sfx)>? hits = null;
        for (long k = first; k <= last; k++)
        {
            foreach (var n in notes)
            {
                int sfx = SfxNumber(n.Token);
                if (sfx == 0) continue;
                double at = k * (double)animLength + Math.Clamp(n.Time, 0f, 1f) * animLength;
                if ((at > before && at <= after) || (before == 0 && at == 0))
                    (hits ??= new()).Add((at, sfx));
            }
        }
        if (hits is null) return;
        hits.Sort((a, b) => a.At.CompareTo(b.At));
        foreach (var h in hits) into.Add(h.Sfx);
    }
}

/// <summary>Phase 18 — one in-flight attack iteration, DS1's model: the clip
/// plays at natural rate; damage lands as each FIRE note crosses; the swing
/// whoosh fires at BSWG; the iteration completes at
/// <c>period = weapon reload_delay + base attack duration</c> (the gap after
/// the clip is the between-swings pad the original fills with the qffg
/// fidget). A clip with no FIRE note lands its hit at clip end
/// (job_attack_object_melee's Error_FireNotFound fallback).</summary>
public sealed class SwingSchedule
{
    public Assets.PrsAnimation Clip { get; }
    public float ClipLength { get; }
    public float Period { get; }
    public float Elapsed { get; private set; }

    readonly float[] _fireTimes;
    int _nextFire;
    readonly float _swingSfxTime;
    bool _swingSfxFired;
    bool _padStarted;

    public SwingSchedule(Assets.PrsAnimation clip, float period)
    {
        Clip = clip;
        ClipLength = clip.AnimLength > 0f ? clip.AnimLength : 0.6f;
        Period = MathF.Max(period, ClipLength);
        var fires = AnimNotes.Times(clip, AnimNotes.Fire);
        // Missing-FIRE fallback: land the hit at clip end.
        _fireTimes = fires.Length > 0 ? fires : new[] { ClipLength };
        var bswg = AnimNotes.Times(clip, AnimNotes.BeginSwing);
        _swingSfxTime = bswg.Length > 0 ? bswg[0] : 0f;
    }

    public bool Complete => Elapsed >= Period;

    /// <summary>Advance the swing clock. Returns the number of FIRE events
    /// crossed this tick (usually 0 or 1; the unarmed 1-2 punch clips carry
    /// two per iteration), and flags the one-shot swing-whoosh and the
    /// clip-end pad transition.</summary>
    public int Advance(float dt, out bool swingSfx, out bool startPad)
    {
        float prev = Elapsed;
        Elapsed += dt;
        swingSfx = false;
        if (!_swingSfxFired && Elapsed >= _swingSfxTime)
        {
            _swingSfxFired = true;
            swingSfx = true;
        }
        int fires = 0;
        while (_nextFire < _fireTimes.Length && Elapsed >= _fireTimes[_nextFire])
        {
            _nextFire++;
            fires++;
        }
        startPad = false;
        if (!_padStarted && prev < ClipLength && Elapsed >= ClipLength && Period > ClipLength + 0.2f)
        {
            _padStarted = true;
            startPad = true;
        }
        return fires;
    }
}

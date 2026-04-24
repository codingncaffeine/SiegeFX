using System;
using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>Concrete <see cref="IHostBridge"/> that wires up enough of DS1's host surface
/// to run real shipped skrits in isolation (no actor world yet). Stubs Math, Report,
/// GpConsole, StringTool, PostWorldMessage, and a small synthetic WorldState. Unknown
/// externs return null silently so a partially-mapped skrit still runs end-to-end.
///
/// Phase 9 will plug a real actor reference into this class so <c>owner.blender.*</c>
/// calls can drive actual animation. For 8d the bridge records blender activity for
/// inspection (<see cref="BlenderLog"/>) and the viewer reads it to pick which clip
/// to play.</summary>
public sealed class ActorHostBridge : IHostBridge
{
    public SkritInstance? Instance { get; set; }

    readonly Dictionary<string, SkritValue> _externs = new(StringComparer.OrdinalIgnoreCase);
    readonly Random _rng;

    public List<string> BlenderLog { get; } = new();

    /// <summary>Most recent anim index the script asked to blend in via
    /// <c>owner.blender.AddAnimToBlendGroup(idx, weight)</c>. -1 before any request.</summary>
    public int CurrentAnimIndex { get; private set; } = -1;

    /// <summary>Number of sub-anims the blender reports — set by the host to match the
    /// real clip list so <c>Math.RandomInt(0, num-1)</c> picks a valid index.</summary>
    public int NumSubAnims { get; set; } = 1;

    public ActorHostBridge(int rngSeed = 0)
    {
        _rng = rngSeed == 0 ? new Random() : new Random(rngSeed);

        // Seed a tiny WorldState enum so shipped auto-skrits ("auto/jipper.skrit" etc.)
        // don't stall on LoadExtern miss. Values are arbitrary placeholders; a real
        // world-state driver in Phase 9 overwrites them.
        foreach (var k in new[]
        {
            "WS_INTRO", "WS_MAIN_MENU", "WS_SP_INGAME", "WS_MP_INGAME",
            "WS_MP_LAN_GAME", "WS_MP_STAGING_AREA_SERVER", "WS_MP_PROVIDER_SELECT",
            "WS_GAME_ENDED",
        })
            _externs[k] = SkritValue.FromString(k);
    }

    public void SetExternValue(string name, SkritValue value) => _externs[name] = value;
    public bool TryGetExternValue(string name, out SkritValue value) => _externs.TryGetValue(name, out value);

    // ---- IHostBridge ----

    public SkritValue GetExtern(string name)
    {
        if (_externs.TryGetValue(name, out var v)) return v;
        // ANIMEVENT_* bitmask constants — synthesise single-bit masks on demand so
        // AnimEventBitTest(events, ANIMEVENT_x) returns coherent truthy/falsy values.
        if (name.StartsWith("ANIMEVENT_", StringComparison.OrdinalIgnoreCase))
        {
            int bit = AnimEventBit(name);
            _externs[name] = SkritValue.FromInt(1L << bit);
            return _externs[name];
        }
        return SkritValue.Null;
    }

    public void SetExtern(string name, SkritValue value) => _externs[name] = value;

    public SkritValue CallExtern(string name, SkritValue[] args)
    {
        switch (name)
        {
            case "AnimEventBitTest":
                if (args.Length < 2) return SkritValue.False;
                return SkritValue.FromBool((args[0].AsInt & args[1].AsInt) != 0);
            case "PostWorldMessage":
                // Stub: would route to the world-message bus in Phase 9.
                return SkritValue.Null;
            case "NULL":
                return SkritValue.Null;
        }
        return SkritValue.Null;
    }

    public SkritValue GetMember(SkritValue receiver, string member)
    {
        // `owner.blender.<something>` — the intermediate hop returns a tag value so the
        // next CallMember can tell it's hitting the blender surface.
        if (string.Equals(member, "blender", StringComparison.OrdinalIgnoreCase)
            || string.Equals(member, "Blender", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromString("<blender>");
        if (string.Equals(member, "Name", StringComparison.OrdinalIgnoreCase)
            || string.Equals(member, "goid", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromString("actor");
        return SkritValue.Null;
    }

    public void SetMember(SkritValue receiver, string member, SkritValue value) { }

    public SkritValue CallMember(SkritValue receiver, string member, SkritValue[] args)
    {
        // Math.RandomInt(lo, hi) — inclusive range.
        if (receiver.Tag == SkritValueTag.Null || receiver.Tag == SkritValueTag.String)
        {
            if (string.Equals(member, "RandomInt", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
            {
                long lo = args[0].AsInt, hi = args[1].AsInt;
                if (hi < lo) return SkritValue.FromInt(lo);
                return SkritValue.FromInt(lo + _rng.NextInt64(hi - lo + 1));
            }
            if (string.Equals(member, "RandomFloat", StringComparison.OrdinalIgnoreCase))
            {
                double lo = args.Length > 0 ? args[0].AsFloat : 0.0;
                double hi = args.Length > 1 ? args[1].AsFloat : 1.0;
                return SkritValue.FromFloat(lo + _rng.NextDouble() * (hi - lo));
            }
        }

        // `<blender>.X(...)` — capture blend operations.
        if (receiver.Tag == SkritValueTag.String && string.Equals(receiver.AsString, "<blender>", StringComparison.Ordinal))
            return BlenderCall(member, args);

        // `owner.blender.X` was already bridged above; `owner.UpdateBlender(dt)` etc.
        // route through the "blender" tag because receiver on those calls is "<blender>".
        if (string.Equals(member, "UpdateBlender", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromInt(0); // zero event bits — shipped anim handlers loop cleanly

        if (string.Equals(member, "GetNumSubAnims", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromInt(NumSubAnims);

        return SkritValue.Null;
    }

    public void SetState(string stateName) => Instance?.RequestSetState(stateName);

    // ---- internals ----

    SkritValue BlenderCall(string member, SkritValue[] args)
    {
        BlenderLog.Add($"{member}({string.Join(", ", args)})");
        switch (member)
        {
            case "GetNumSubAnims": return SkritValue.FromInt(NumSubAnims);
            case "OpenBlendGroup": return SkritValue.FromInt(0);
            case "AddAnimToBlendGroup":
                if (args.Length >= 1) CurrentAnimIndex = (int)args[0].AsInt;
                return SkritValue.Null;
            case "CloseBlendGroup":
            case "SetBlendGroupWeight":
            case "ResetTimeWarp":
                return SkritValue.Null;
        }
        return SkritValue.Null;
    }

    static int AnimEventBit(string name) => name.ToUpperInvariant() switch
    {
        "ANIMEVENT_FINISH"          => 0,
        "ANIMEVENT_SFX_1"           => 1,
        "ANIMEVENT_SFX_2"           => 2,
        "ANIMEVENT_SFX_3"           => 3,
        "ANIMEVENT_SFX_4"           => 4,
        "ANIMEVENT_HIDE_MESH"       => 5,
        "ANIMEVENT_SHOW_MESH"       => 6,
        "ANIMEVENT_LEFT_FOOT_DOWN"  => 7,
        "ANIMEVENT_RIGHT_FOOT_DOWN" => 8,
        "ANIMEVENT_WEAPON_FIRE"     => 9,
        _                           => 31,
    };
}

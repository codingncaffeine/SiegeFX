using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// SC-WEATHER-C — DS1 mood-driven weather state machine. Owns the *state* only
/// (fog params, precipitation rates, wind, lightning schedule); rendering reads
/// the public snapshot each frame (fog uniforms in the mesh pass, rain/snow
/// emitters in ParticleSystem, flash overlay + thunder cue in RenderHost).
///
/// Behavior model comes from two shipped sources:
///  - default_moods.gas: a component block OMITTED from a mood means DISABLED —
///    so a mood_change to a mood without [rain] fades rain out.
///  - mood_manager.skrit (authored game script): every 15s there is a 60% chance
///    the weather drifts; each enabled component moves ±10% (80%), ±20% (10%),
///    ±30% (6%) or ±50% (4%) of its AUTHORED BASELINE, with the running value
///    clamped to [0.5×, 1.5×] baseline. Lightning is forced ON while rain ≥ 200
///    and forced OFF below that unless the mood authors lightning=true. Heavy
///    rain (> 200) darkens fog to 85%. Lightning never fires in interior moods.
///
/// Mood transitions lerp fog/density/wind over the mood's transition_time
/// (0–15s shipped). Drift steps apply instantly (mood_manager calls
/// ForceUpdateRain(0) — a zero-time update); a density step is a spawn-rate
/// change and reads as natural variation.
///
/// Lightning strike cadence is NOT authored anywhere we can find — the interval
/// and flash envelope below are tuned by eye against retail fh_r1_3 and flagged
/// as inferred. Thunder pairing (amb_thunder → s_e_ambient_thunder) is engine
/// -side: no shipped region places a thunder emitter, but the clip ships in
/// Sound.dsres and sounddb.gas maps the event, so the engine owns the pairing.
/// </summary>
public sealed class WeatherSystem
{
    // ---- rendered snapshot (readers: mesh pass, particle system, overlay) ----
    public bool FogActive { get; private set; }
    /// <summary>Meters from camera where fog starts / reaches full.</summary>
    public float FogNear { get; private set; }
    public float FogFar { get; private set; }
    /// <summary>Linear-space 0..1 rgb, heavy-rain darkening already folded in.</summary>
    public Vector3 FogColor { get; private set; }
    /// <summary>Current rain drops/sec (baseline × drift, transition-lerped).</summary>
    public float RainRate { get; private set; }
    /// <summary>Current snow flakes/sec.</summary>
    public float SnowRate { get; private set; }
    public float WindVelocity { get; private set; }
    /// <summary>Radians clockwise from north (north = world -Z, east = +X).</summary>
    public float WindDirection { get; private set; }
    /// <summary>World-space wind velocity vector derived from the two above.</summary>
    public Vector3 WindVector =>
        new(MathF.Sin(WindDirection) * WindVelocity, 0f, -MathF.Cos(WindDirection) * WindVelocity);
    public bool Interior { get; private set; }
    /// <summary>0..1 additive screen flash for the current frame (lightning).</summary>
    public float FlashIntensity { get; private set; }
    /// <summary>True while the lightning scheduler is armed (rain ≥ 200 or authored).</summary>
    public bool LightningArmed { get; private set; }
    /// <summary>Name of the mood currently driving the targets ("" = none).</summary>
    public string ActiveMoodName { get; private set; } = "";
    /// <summary>True when any weather is visible (precipitation, flash) or fog
    /// differs from disabled — cheap skip for render passes.</summary>
    public bool AnyWeatherVisible => RainRate > 0.5f || SnowRate > 0.5f || FlashIntensity > 0f;

    // ---- targets + transition ----
    private sealed record Targets(
        bool FogOn, float FogNear, float FogFar, Vector3 FogColor,
        float Rain, bool RainAuthoredLightning, float Snow, float WindVel, float WindDir);

    private Targets _from = Disabled;
    private Targets _to = Disabled;
    private static readonly Targets Disabled =
        new(false, 0f, 0f, Vector3.Zero, 0f, false, 0f, 0f, 0f);

    private float _transitionT;      // seconds elapsed in current transition
    private float _transitionLen;    // total seconds (0 = instant)

    // ---- drift (mood_manager.skrit) ----
    private float _driftTimer;
    private float _rainDrift = 1f, _snowDrift = 1f, _windDrift = 1f;
    private const float DriftPeriod = 15f;

    // ---- lightning scheduler (cadence inferred, envelope inferred) ----
    private float _nextStrikeIn;
    private float _strikeAge = float.MaxValue;   // seconds since current strike began
    private readonly List<float> _thunderQueue = new(); // countdowns to thunder claps
    /// <summary>Thunder cues that elapsed this frame — host consumes and plays
    /// amb_thunder once per entry.</summary>
    public int ConsumeThunderClaps()
    {
        int n = 0;
        for (int i = _thunderQueue.Count - 1; i >= 0; i--)
            if (_thunderQueue[i] <= 0f) { _thunderQueue.RemoveAt(i); n++; }
        return n;
    }

    private readonly Random _rng = new();

    /// <summary>Blend toward <paramref name="mood"/>'s weather over its
    /// transition_time. Missing component blocks fade that component OUT
    /// (default_moods.gas omit-disables semantics).</summary>
    public void ApplyMood(MoodSetting mood)
    {
        if (string.Equals(ActiveMoodName, mood.Name, StringComparison.OrdinalIgnoreCase))
            return;
        _from = Snapshot();
        _to = new Targets(
            FogOn: mood.Fog is not null,
            FogNear: mood.Fog?.NearDist ?? _from.FogNear,
            FogFar: mood.Fog?.FarDist ?? _from.FogFar,
            FogColor: mood.Fog is null ? _from.FogColor : ColorToRgb(mood.Fog.Color),
            Rain: mood.Rain?.Density ?? 0f,
            RainAuthoredLightning: mood.Rain?.Lightning ?? false,
            Snow: mood.Snow?.Density ?? 0f,
            WindVel: mood.Wind?.Velocity ?? 0f,
            WindDir: mood.Wind?.Direction ?? _from.WindDir);
        // Fog turning ON from a fog-less state (boot → first region) must not
        // lerp its distances up from 0 — that reads as a dark fog wall racing
        // out from the camera. Seed the from-state with the target fog so the
        // atmosphere is simply there on arrival (only mood→mood changes blend).
        if (!_from.FogOn && _to.FogOn)
            _from = _from with { FogNear = _to.FogNear, FogFar = _to.FogFar, FogColor = _to.FogColor };
        _transitionLen = MathF.Max(0f, mood.TransitionTime);
        _transitionT = 0f;
        Interior = mood.Interior;
        ActiveMoodName = mood.Name;
        // Fresh mood = fresh drift baseline (mood_manager reads the ORIGINAL
        // mood setting as the drift anchor, so multipliers reset).
        _rainDrift = _snowDrift = _windDrift = 1f;
        _driftTimer = 0f;
    }

    /// <summary>Hard reset (region unload / new game). Fog off, sky dry.</summary>
    public void Reset()
    {
        _from = _to = Disabled;
        _transitionT = _transitionLen = 0f;
        _rainDrift = _snowDrift = _windDrift = 1f;
        _driftTimer = 0f;
        _strikeAge = float.MaxValue;
        _thunderQueue.Clear();
        Interior = false;
        ActiveMoodName = "";
        FlashIntensity = 0f;
        FogActive = false;
        RainRate = SnowRate = 0f;
        WindVelocity = 0f;
        FogColor = Vector3.Zero;
    }

    public void Tick(float dt)
    {
        if (dt <= 0f) return;

        // -- transition lerp --
        _transitionT = MathF.Min(_transitionT + dt, MathF.Max(_transitionLen, 0.0001f));
        float k = _transitionLen <= 0f ? 1f : Math.Clamp(_transitionT / _transitionLen, 0f, 1f);

        // -- drift step (mood_manager.skrit, every 15s, 60% change chance) --
        _driftTimer += dt;
        if (_driftTimer >= DriftPeriod)
        {
            _driftTimer = 0f;
            if (_rng.NextDouble() >= 0.40)
            {
                if (_to.Rain > 0f) _rainDrift = DriftStep(_rainDrift);
                if (_to.Snow > 0f) _snowDrift = DriftStep(_snowDrift);
                if (_to.WindVel > 0f) _windDrift = DriftStep(_windDrift);
            }
        }

        FogActive = _to.FogOn || (_from.FogOn && k < 1f);
        FogNear = Lerp(_from.FogNear, _to.FogNear, k);
        FogFar = Lerp(_from.FogFar, _to.FogFar, k);
        var fogRgb = Vector3.Lerp(_from.FogColor, _to.FogColor, k);
        RainRate = Lerp(_from.Rain, _to.Rain * _rainDrift, k);
        SnowRate = Lerp(_from.Snow, _to.Snow * _snowDrift, k);
        WindVelocity = Lerp(_from.WindVel, _to.WindVel * _windDrift, k);
        WindDirection = LerpAngle(_from.WindDir, _to.WindDir, k);

        // Heavy rain darkens fog to 85% (mood_manager.skrit, rain > 200).
        if (RainRate > 200f) fogRgb *= 0.85f;
        FogColor = fogRgb;

        // -- lightning (skrit: 100% on at rain ≥ 200; authored flag keeps it
        //    on below threshold; interiors never flash) --
        LightningArmed = !Interior && (RainRate >= 200f || (_to.RainAuthoredLightning && RainRate > 0f));
        TickLightning(dt);
    }

    private void TickLightning(float dt)
    {
        for (int i = 0; i < _thunderQueue.Count; i++) _thunderQueue[i] -= dt;

        if (LightningArmed)
        {
            _nextStrikeIn -= dt;
            if (_nextStrikeIn <= 0f)
            {
                _strikeAge = 0f;
                // Inferred cadence: a strike every 3–10s reads like retail's
                // opening storm without turning the field into a strobe.
                _nextStrikeIn = 3f + (float)_rng.NextDouble() * 7f;
                // Thunder rolls in 0.4–2.0s after the flash (distance feel).
                _thunderQueue.Add(0.4f + (float)_rng.NextDouble() * 1.6f);
            }
        }
        else
        {
            // Re-arm delay so the first strike after (re)arming isn't instant.
            _nextStrikeIn = MathF.Max(_nextStrikeIn, 1.5f);
        }

        // Flash envelope: double pulse over ~0.45s (main flash, dip, echo).
        _strikeAge += dt;
        float a = _strikeAge;
        FlashIntensity = a switch
        {
            < 0.10f => a / 0.10f,                    // rise
            < 0.20f => 1f - (a - 0.10f) / 0.10f * 0.75f, // fall to 0.25
            < 0.30f => 0.25f + (a - 0.20f) / 0.10f * 0.45f, // second pulse to 0.7
            < 0.45f => 0.70f * (1f - (a - 0.30f) / 0.15f),  // decay
            _ => 0f,
        };
    }

    /// <summary>One mood_manager drift step: ±10/20/30/50% of BASELINE weighted
    /// 80/10/6/4, result clamped to [0.5, 1.5] × baseline. We track the
    /// multiplier (baseline-normalized), so baseline = 1.</summary>
    private float DriftStep(float current)
    {
        float sign = _rng.NextDouble() < 0.5 ? 1f : -1f;
        double roll = _rng.NextDouble();
        float mag = roll < 0.80 ? 0.10f : roll < 0.90 ? 0.20f : roll < 0.96 ? 0.30f : 0.50f;
        return Math.Clamp(current + sign * mag, 0.5f, 1.5f);
    }

    private Targets Snapshot() => new(
        FogActive, FogNear, FogFar, FogColor,
        RainRate, _to.RainAuthoredLightning, SnowRate, WindVelocity, WindDirection);

    private static float Lerp(float a, float b, float k) => a + (b - a) * k;

    private static float LerpAngle(float a, float b, float k)
    {
        float d = b - a;
        while (d > MathF.PI) d -= 2f * MathF.PI;
        while (d < -MathF.PI) d += 2f * MathF.PI;
        return a + d * k;
    }

    private static Vector3 ColorToRgb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f);
}

using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Net;

namespace SiegeFX.Runtime;

/// <summary>A full co-op game's network load, headless and in one process:
/// a host and up to seven joiners (<see cref="LoopbackHub"/>) in a synthetic
/// world of mobs that walk, stop, swing and take damage, on a simulated clock
/// (minutes run in seconds). The host's uplink goes through
/// <see cref="ShapedTransport"/>. Every 10 simulated seconds it logs the
/// host's upload as the link carried it, how late world updates reach the
/// joiners, and how far what each joiner holds for the mobs near its player
/// is from the host's own state at that update.
/// <c>--selftest-mp-load [players=8] [actors=360] [moving=360] [seconds=300]
/// [link=kbit=1000;loss=2;delay=100] [near=30] [max-kbit=N]
/// [max-latency-growth-ms=N] [max-error-cm=N] [max-missing=N]</c>.
/// Exits 1 when a stated limit is broken.</summary>
public static class NetLoadTest
{
    readonly record struct Mob(uint Scid, Vector3 Home, float Radius, float Speed, float Phase, bool Moves, bool Swings);

    sealed class Joiner
    {
        public int Index;
        public MpSession Session = null!;
        public readonly Dictionary<uint, MpActorState> View = new();
        public readonly List<double> Latency = new();
        public double MaxError;
        public long Missing, Checked;
        public uint LastTick;
    }

    const double Step = 1.0 / 60.0;

    public static int Run(string[] args)
    {
        var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in args)
        {
            int eq = a.IndexOf('=');
            if (eq > 0) opt[a[..eq].TrimStart('-')] = a[(eq + 1)..];
        }
        double Num(string k, double d) => opt.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : d;
        int players = Math.Clamp((int)Num("players", 8), 2, 8);
        int actorCount = (int)Num("actors", 360);
        int moving = Math.Min(actorCount, (int)Num("moving", actorCount));
        double seconds = Num("seconds", 300);
        double near = Num("near", 30);
        var shape = MpLinkShape.Parse(opt.TryGetValue("link", out var l) ? l.Replace(';', ',') : "kbit=1000,loss=2,delay=100");

        NetLog.Verbose = false;
        var log = Console.Out;
        Console.SetOut(TextWriter.Null);   // the sessions' own [net] lines
        double now = 0;
        var hub = new LoopbackHub(players - 1);
        var hostLink = shape is null ? null : new ShapedTransport(hub.Host, shape, () => now);
        ISessionTransport hostT = hostLink ?? hub.Host;
        var host = new MpSession(hostT, isHost: true, "Host") { StatsInterval = 0 };

        var rng = new Random(20261107);
        var mobs = new Mob[actorCount];
        for (int i = 0; i < actorCount; i++)
            mobs[i] = new Mob(0x01000000u + (uint)i * 7u,
                new Vector3((float)(rng.NextDouble() * 240 - 120), 0, (float)(rng.NextDouble() * 240 - 120)),
                2f + (float)rng.NextDouble() * 6f, 1.5f + (float)rng.NextDouble() * 2.5f, (float)rng.NextDouble(),
                i < moving, i % 10 == 0);

        var tickTime = new Dictionary<uint, double>();
        var current = new List<MpActorState>(actorCount);
        host.EnumerateActors = () =>
        {
            tickTime[host.CurrentTick] = now;   // kept for the whole run: a late update still counts
            current.Clear();
            foreach (var m in mobs) current.Add(MobAt(m, now));
            return current;
        };
        host.BuildLocalPlayerState = () => PlayerAt(0, now);

        var joiners = new List<Joiner>();
        for (int i = 1; i < players; i++)
        {
            var j = new Joiner { Index = i };
            j.Session = new MpSession(hub.Clients[i - 1], isHost: false, $"Joiner {i}") { StatsInterval = 0 };
            int me = i;
            j.Session.BuildLocalPlayerState = () => PlayerAt(me, now);
            j.Session.ApplyDelta = (tick, list) =>
            {
                foreach (var s in list) j.View[s.Scid] = s;
                if (tick <= j.LastTick || !tickTime.TryGetValue(tick, out var sent)) return;
                j.LastTick = tick;
                j.Latency.Add(now - sent);
                var p = PlayerAt(me, sent);
                foreach (var m in mobs)
                {
                    var truth = MobAt(m, sent);
                    float dx = truth.X - p.X, dz = truth.Z - p.Z;
                    if (dx * dx + dz * dz > near * near) continue;
                    j.Checked++;
                    if (!j.View.TryGetValue(m.Scid, out var have)) { j.Missing++; continue; }
                    var e = Vector3.Distance(new Vector3(have.X, have.Y, have.Z), new Vector3(truth.X, truth.Y, truth.Z));
                    if (have.Life != truth.Life || have.Chore.Seq != truth.Chore.Seq) e = Math.Max(e, 1e6f);
                    j.MaxError = Math.Max(j.MaxError, e);
                }
            };
            joiners.Add(j);
        }

        void Advance(double dt)
        {
            now += dt;
            host.Tick(dt);
            hostLink?.Pump();
            foreach (var j in joiners) j.Session.Tick(dt);
        }

        foreach (var j in joiners) j.Session.SendJoinRequest();
        // Joining goes over the reliable lane: on a bad link it takes a while.
        for (int i = 0; i < 60 * 30 && !joiners.All(j => j.Session.Joined); i++) Advance(Step);
        for (int i = 0; i < 30; i++) Advance(Step);
        int joined = joiners.Count(j => j.Session.Joined);

        log.WriteLine($"[load] {players} players, {actorCount} mobs ({moving} moving), link {(shape?.ToString() ?? "unshaped")}, " +
                      $"protocol v{MpProtocol.Version}; {joined}/{joiners.Count} joined");
        var minutes = new List<(double Kbit, double P95)>();
        long wireAtMinute = hostLink?.WireBytes ?? 0, payloadAtMinute = host.Stats.TotalUpBytes;
        var minuteLatency = new List<double>();
        double worstError = 0; long missing = 0, checkedN = 0;
        long wireAtWindow = wireAtMinute, payloadAtWindow = payloadAtMinute;
        double t0 = now, windowStart = now, minuteStart = now;
        int windows = (int)Math.Ceiling(seconds / 10 - 1e-9);
        for (int w = 1; w <= windows; w++)
        {
            double end = t0 + Math.Min(seconds, w * 10.0);
            while (now < end - 1e-9) Advance(Step);
            double span = now - windowStart;
            var lat = joiners.SelectMany(j => j.Latency).OrderBy(x => x).ToList();
            minuteLatency.AddRange(lat);
            double err = joiners.Max(j => j.MaxError);
            long miss = joiners.Sum(j => j.Missing), chk = joiners.Sum(j => j.Checked);
            worstError = Math.Max(worstError, err); missing += miss; checkedN += chk;
            long wire = (hostLink?.WireBytes ?? 0) - wireAtWindow, payload = host.Stats.TotalUpBytes - payloadAtWindow;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[load] t={now - t0,4:0} s host sends {wire * 8 / 1000.0 / span,7:0.0} kbit/s into its uplink ({payload * 8 / 1000.0 / span:0.0} payload), " +
                $"queue {(hostLink?.QueueDelay ?? 0) * 1000:0} ms; update age p50 {Pct(lat, 50) * 1000:0} / p95 {Pct(lat, 95) * 1000:0} / max {(lat.Count > 0 ? lat[^1] : 0) * 1000:0} ms " +
                $"({lat.Count / span / Math.Max(1, joiners.Count):0.0}/s per joiner); near mobs off by up to {Cm(err)}, missing {miss}/{chk}"));
            if (hostLink is not null) log.WriteLine("[load]   " + hostLink.Report(span));
            log.WriteLine("[load]   session " + host.Stats.Report(span));
            foreach (var j in joiners) { j.Latency.Clear(); j.MaxError = 0; j.Missing = 0; j.Checked = 0; }
            wireAtWindow = hostLink?.WireBytes ?? 0; payloadAtWindow = host.Stats.TotalUpBytes;
            windowStart = now;
            if (now - minuteStart >= 60 - 1e-9 || w == windows)
            {
                minuteLatency.Sort();
                long wm = (hostLink?.WireBytes ?? 0) - wireAtMinute;
                minutes.Add((wm * 8 / 1000.0 / (now - minuteStart), Pct(minuteLatency, 95)));
                minuteLatency.Clear(); wireAtMinute = hostLink?.WireBytes ?? 0; minuteStart = now;
            }
        }
        Console.SetOut(log);

        log.WriteLine("[load] per minute: " + string.Join("; ", minutes.Select((m, i) => string.Create(CultureInfo.InvariantCulture,
            $"{i + 1}: {m.Kbit:0} kbit/s, update age p95 {m.P95 * 1000:0} ms"))));
        bool ok = joined == joiners.Count;
        if (!ok) log.WriteLine($"[load] FAIL: only {joined}/{joiners.Count} joiners joined");
        void Limit(string key, double value, string what, string unit)
        {
            if (!opt.ContainsKey(key)) return;
            double max = Num(key, 0);
            bool pass = value <= max;
            ok &= pass;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[load] {(pass ? "ok" : "FAIL")}: {what} {value:0.##} {unit} (limit {max:0.##})"));
        }
        if (minutes.Count > 0)
        {
            Limit("max-kbit", minutes.Max(m => m.Kbit), "host upload, busiest minute", "kbit/s");
            Limit("max-latency-growth-ms", (minutes[^1].P95 - minutes[0].P95) * 1000, "update age p95, last minute minus first", "ms");
        }
        Limit("max-error-cm", worstError * 100, "largest error on a near mob", "cm");
        Limit("max-missing", missing, $"near mobs a joiner never received (of {checkedN} checks)", "");
        log.WriteLine(ok ? "[load] PASS" : "[load] FAILED");
        host.Dispose();
        foreach (var j in joiners) j.Session.Dispose();
        return ok ? 0 : 1;
    }

    static string Cm(double meters) => meters >= 1e5 ? "a wrong life or swing" : string.Create(CultureInfo.InvariantCulture, $"{meters * 100:0.0} cm");

    static double Pct(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1)];

    // A mob's state at time t: walks its circle 4 s of every 7, stands 3 s;
    // loses life every few seconds; one in ten swings every 1.5 s.
    static MpActorState MobAt(in Mob m, double t)
    {
        double life = 100 - Math.Floor(t / 5 + m.Phase * 10) % 50;
        var chore = m.Swings ? new MpChore((byte)(int)(t / 1.5), 3, 10) : default;
        if (!m.Moves)
            return new MpActorState(m.Scid, m.Home.X, Ground(m.Home.X, m.Home.Z), m.Home.Z, (ushort)life, chore);
        double c = t + m.Phase * 7;
        double walked = Math.Floor(c / 7) * 4 + Math.Min(c % 7, 4);
        double ang = walked * m.Speed / m.Radius + m.Phase * Math.Tau;
        float x = m.Home.X + m.Radius * (float)Math.Cos(ang), z = m.Home.Z + m.Radius * (float)Math.Sin(ang);
        return new MpActorState(m.Scid, x, Ground(x, z), z, (ushort)life, chore);
    }

    static float Ground(float x, float z) => 8f + 0.8f * MathF.Sin(x * 0.07f) * MathF.Cos(z * 0.05f);

    // The host walks a wide circle (3 m/s, stopping now and then); joiners
    // stay within ~10 m of it, as the leash keeps them.
    static MpPlayerState PlayerAt(int player, double t)
    {
        double c = t % 20;
        double walked = Math.Floor(t / 20) * 15 + Math.Min(c, 15);
        double ang = walked * 3.0 / 40.0;
        float hx = 40f * (float)Math.Cos(ang), hz = 40f * (float)Math.Sin(ang);
        float ox = player == 0 ? 0 : 6f * MathF.Cos(player * 0.9f + (float)t * 0.2f);
        float oz = player == 0 ? 0 : 6f * MathF.Sin(player * 0.9f + (float)t * 0.2f);
        float x = hx + ox, z = hz + oz;
        byte flags = c < 15 ? (byte)MpPlayerFlags.Moving : (byte)0;
        return new MpPlayerState((byte)player, x, Ground(x, z), z, (float)(ang + Math.PI / 2), 200, flags);
    }
}

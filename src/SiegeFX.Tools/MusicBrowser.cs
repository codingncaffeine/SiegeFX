using SiegeFX.Audio;
using SiegeFX.Core.IO;
using SiegeFX.Core.Tank;

/// <summary><c>siegefx music browse</c> — every music track in the install's
/// Sound.dsres in a numbered menu, in the spirit of test-all: play one
/// (it streams through the game's own MusicPlayer while the menu stays
/// usable), stop, filter, and extract one or all to the player's own disk.</summary>
static class MusicBrowser
{
    const string MusicDir = "/sound/music/";

    sealed record Track(int Number, string Path, string Name, long Size);

    public static int Run(string[] a)
    {
        var pos = a.Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList();
        var outDir = a.FirstOrDefault(x => x.StartsWith("--out=", StringComparison.Ordinal))?["--out=".Length..] ?? DefaultOutDir();

        var soundPath = ResolveSoundTank(pos.FirstOrDefault());
        if (soundPath is null)
        {
            Console.Error.WriteLine("usage: siegefx music browse [<Dungeon Siege folder> | <Sound.dsres>] [--out=DIR]");
            Console.Error.WriteLine("  no install found: pass the folder, set SIEGEFX_DS1, or write it to " + DsInstallLocator.UserPathFile);
            return 1;
        }

        using var tank = TankFile.Open(soundPath);
        var reader = new TankReader(tank);
        var tracks = reader.ListFiles()
            .Where(p => p.StartsWith(MusicDir, StringComparison.OrdinalIgnoreCase) && p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select((p, i) => new Track(i + 1, p, DisplayName(p), reader.TryGetFile(p, out var e) ? e.Size : 0))
            .ToList();
        if (tracks.Count == 0) { Console.Error.WriteLine($"no music in {soundPath}"); return 2; }

        using var player = new BackgroundPlayer();
        string filter = "";
        Track? current = null;
        bool repeat = true;

        PrintMenu();
        while (true)
        {
            Console.Write(current is null ? "Choose: " : $"[{(player.IsPlaying ? "playing" : "ended")}: {current.Name}] Choose: ");
            var line = Console.ReadLine();
            if (line is null) break;   // end of input
            var cmd = line.Trim();
            var (verb, rest) = Split(cmd);
            switch (verb.ToLowerInvariant())
            {
                case "": break;
                case "q": return 0;
                case "l": PrintMenu(); break;
                case "s": player.Stop(); Console.WriteLine("  stopped"); break;
                case "r":
                    repeat = !repeat;
                    Console.WriteLine($"  repeat {(repeat ? "on" : "off")} (applies from the next track played)");
                    break;
                case "f":
                    filter = rest;
                    PrintMenu();
                    break;
                case "n":
                case "p":
                {
                    var shown = Shown();
                    if (shown.Count == 0) break;
                    int at = current is null ? -1 : shown.FindIndex(t => t.Number == current.Number);
                    int next = verb.Equals("n", StringComparison.OrdinalIgnoreCase)
                        ? (at + 1) % shown.Count
                        : (at <= 0 ? shown.Count - 1 : at - 1);
                    Play(shown[next]);
                    break;
                }
                case "v":
                    if (int.TryParse(rest, out var vol) && vol is >= 0 and <= 100) { player.SetVolume(vol / 100f); Console.WriteLine($"  volume {vol}"); }
                    else Console.WriteLine("  v takes 0-100");
                    break;
                case "x":
                    if (Find(rest) is { } tx) Extract([tx]);
                    else Console.WriteLine("  x takes a track number");
                    break;
                case "xa":
                    Extract(Shown());
                    break;
                default:
                    if (Find(cmd) is { } t) Play(t);
                    else Console.WriteLine("  unknown choice; L lists the menu again");
                    break;
            }
        }
        return 0;

        List<Track> Shown() => filter.Length == 0 ? tracks
            : tracks.Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        Track? Find(string s) => int.TryParse(s, out var n) ? tracks.FirstOrDefault(t => t.Number == n) : null;

        void PrintMenu()
        {
            var shown = Shown();
            Console.WriteLine();
            Console.WriteLine($"  SiegeFX music player - {tracks.Count} tracks in {soundPath}");
            if (filter.Length > 0) Console.WriteLine($"  filter \"{filter}\": {shown.Count} shown (F alone clears it)");
            Console.WriteLine();
            foreach (var t in shown) Console.WriteLine($"  {t.Number,4}. {t.Name,-40} {t.Size / 1048576.0,6:0.0} MB");
            Console.WriteLine();
            Console.WriteLine("  <n>  play track n         S  stop          N / P  next / previous");
            Console.WriteLine("  X n  extract track n      XA extract all shown, to " + outDir);
            Console.WriteLine("  F t  show names with t    R  repeat on/off V n  volume 0-100");
            Console.WriteLine("  L    list again           Q  quit");
            Console.WriteLine();
        }

        void Play(Track t)
        {
            var bytes = reader.ExtractToMemory(t.Path);
            switch (player.Play(bytes, repeat))
            {
                case PlayResult.NoAudioDevice:
                    Console.WriteLine("  no audio device: listing and extracting still work");
                    return;
                case PlayResult.DecodeFailed:
                    Console.WriteLine($"  could not decode {t.Name}");
                    return;
            }
            current = t;
            Console.WriteLine($"  playing {t.Number}. {t.Name}{Length(bytes)}{(repeat ? ", repeating" : "")}");
        }

        void Extract(IReadOnlyList<Track> which)
        {
            Directory.CreateDirectory(outDir);
            foreach (var t in which)
                File.WriteAllBytes(System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(t.Path)), reader.ExtractToMemory(t.Path));
            Console.WriteLine($"  extracted {which.Count} track(s) to {outDir}");
            Console.WriteLine("  (your copy of the game's music, from your own install: keep it out of the repository)");
        }
    }

    static (string Verb, string Arg) Split(string cmd)
    {
        int sp = cmd.IndexOf(' ');
        return sp < 0 ? (cmd, "") : (cmd[..sp], cmd[(sp + 1)..].Trim());
    }

    /// <summary>"/sound/music/s_m_fh_battle_01.mp3" → "fh_battle_01".</summary>
    static string DisplayName(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("s_m_", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
    }

    static string Length(byte[] mp3)
    {
        try
        {
            using var f = new NLayer.MpegFile(new MemoryStream(mp3));
            var d = f.Duration;
            return $" ({(int)d.TotalMinutes}:{d.Seconds:00})";
        }
        catch (Exception) { return ""; }
    }

    static string? ResolveSoundTank(string? arg)
    {
        if (arg is not null && File.Exists(arg)) return arg;
        var root = arg ?? DsInstallLocator.Locate();
        if (root is null) return null;
        var path = System.IO.Path.Combine(root, "Resources", "Sound.dsres");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The player's Music folder (…/Music/SiegeFX), never the repository.</summary>
    static string DefaultOutDir()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrEmpty(music))
            music = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
        return System.IO.Path.Combine(music, "SiegeFX");
    }

    enum PlayResult { Playing, NoAudioDevice, DecodeFailed }

    /// <summary>A MusicPlayer pumped on a background thread so the menu stays
    /// responsive; every call into the player holds one lock.</summary>
    sealed class BackgroundPlayer : IDisposable
    {
        readonly object _gate = new();
        readonly CancellationTokenSource _stop = new();
        AudioEngine? _engine;
        MusicPlayer? _music;
        Thread? _pump;
        bool _tried;
        float _volume = 1f;

        public bool IsPlaying { get { lock (_gate) return _music?.IsPlaying ?? false; } }

        public PlayResult Play(byte[] mp3, bool loop)
        {
            lock (_gate)
            {
                if (!_tried)
                {
                    _tried = true;
                    _engine = AudioEngine.TryCreate();
                    _music = _engine is null ? null : MusicPlayer.TryCreate(_engine);
                    _music?.SetVolume(_volume);
                    if (_music is not null)
                    {
                        _pump = new Thread(Pump) { IsBackground = true, Name = "music pump" };
                        _pump.Start();
                    }
                }
                if (_music is null) return PlayResult.NoAudioDevice;
                _music.Stop();
                return _music.Play(mp3, loop) ? PlayResult.Playing : PlayResult.DecodeFailed;
            }
        }

        public void Stop() { lock (_gate) _music?.Stop(); }

        public void SetVolume(float v) { lock (_gate) { _volume = v; _music?.SetVolume(v); } }

        void Pump()
        {
            while (!_stop.IsCancellationRequested)
            {
                lock (_gate) _music?.Tick();
                Thread.Sleep(16);
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _pump?.Join();
            lock (_gate)
            {
                _music?.Stop();
                _music?.Dispose();
                _engine?.Dispose();
            }
        }
    }
}

using System.Globalization;

namespace SiegeFX.Core.Net;

/// <summary>What a player loses on death (the original's Drop on Death list,
/// in its order).</summary>
public enum MpDropOnDeath : byte { Nothing = 0, EquippedItems = 1, Inventory = 2, All = 3 }

/// <summary>The host's game rules, as the original's staging area set them
/// (staging_area_game_settings.gas and staging_area_map_settings.gas): player
/// limit, difficulty, time limit, Drop on Death, team play, player-vs-player
/// combat, start locations, new characters only, pausing, join in progress and
/// the game password, plus the map, its world and the party's start location.
/// The host owns them; joiners receive them in <see cref="MpMsg.Settings"/>
/// (the password itself never travels: only whether there is one).</summary>
public sealed record MpGameSettings
{
    public const int MinPlayers = 2, MaxPlayers = 8;
    /// <summary>The time limits the original offers, in minutes (multiplayer.gas
    /// [game_timer]); 0 = none.</summary>
    public static readonly IReadOnlyList<int> TimeLimits = new[] { 0, 15, 30, 45, 60, 90, 120, 180, 300 };
    public static readonly IReadOnlyList<string> DifficultyNames = new[] { "Easy", "Normal", "Hard" };
    public static readonly IReadOnlyList<string> DropNames = new[] { "Nothing", "Equipped Items", "Inventory", "All" };

    public int PlayerLimit { get; init; } = MaxPlayers;
    /// <summary>0 Easy, 1 Normal, 2 Hard (the game's difficulty).</summary>
    public int Difficulty { get; init; } = 1;
    public int TimeLimitMinutes { get; init; }
    public MpDropOnDeath DropOnDeath { get; init; } = MpDropOnDeath.Nothing;
    public bool TeamPlay { get; init; }
    public bool PlayerVsPlayer { get; init; }
    public bool AllowStartChoice { get; init; }
    public bool NewCharactersOnly { get; init; }
    public bool AllowPause { get; init; }
    public bool AllowJoinInProgress { get; init; } = true;
    public bool HasPassword { get; init; }
    /// <summary>The map's name (world/maps/NAME) and screen name.</summary>
    public string Map { get; init; } = "map_world";
    public string MapName { get; init; } = "Kingdom of Ehb";
    /// <summary>The map's world (its [worlds] block) and screen name.</summary>
    public string World { get; init; } = "normal";
    public string WorldName { get; init; } = "Regular";
    /// <summary>The start group the party starts at ("" = the map's default)
    /// and its screen name and required level.</summary>
    public string StartGroup { get; init; } = "";
    public string StartName { get; init; } = "";
    public int StartLevel { get; init; }

    public static string TimeLimitName(int minutes) => minutes <= 0 ? "None"
        : minutes % 60 == 0 ? (minutes == 60 ? "1 hour" : $"{minutes / 60} hours")
        : minutes == 1 ? "1 minute" : $"{minutes} minutes";

    public string DifficultyName => DifficultyNames[Math.Clamp(Difficulty, 0, 2)];
    public string DropName => DropNames[Math.Clamp((int)DropOnDeath, 0, 3)];

    /// <summary>Within the original's ranges (a hostile or old frame cannot
    /// set a limit of 0 or 200).</summary>
    public MpGameSettings Clamped() => this with
    {
        PlayerLimit = Math.Clamp(PlayerLimit, MinPlayers, MaxPlayers),
        Difficulty = Math.Clamp(Difficulty, 0, 2),
        TimeLimitMinutes = TimeLimits.Contains(TimeLimitMinutes) ? TimeLimitMinutes : 0,
        DropOnDeath = (MpDropOnDeath)Math.Clamp((int)DropOnDeath, 0, 3),
        StartLevel = Math.Max(0, StartLevel),
    };

    // ---- wire ----------------------------------------------------------

    [Flags]
    enum Bits : ushort
    {
        None = 0, TeamPlay = 1, Pvp = 2, StartChoice = 4, NewOnly = 8, Pause = 16, JoinInProgress = 32, Password = 64,
    }

    public void Write(MpWriter w)
    {
        var b = Bits.None;
        if (TeamPlay) b |= Bits.TeamPlay;
        if (PlayerVsPlayer) b |= Bits.Pvp;
        if (AllowStartChoice) b |= Bits.StartChoice;
        if (NewCharactersOnly) b |= Bits.NewOnly;
        if (AllowPause) b |= Bits.Pause;
        if (AllowJoinInProgress) b |= Bits.JoinInProgress;
        if (HasPassword) b |= Bits.Password;
        w.U8((byte)PlayerLimit).U8((byte)Difficulty).U16((ushort)TimeLimitMinutes).U8((byte)DropOnDeath).U16((ushort)b)
         .Str(Map).Str(MapName).Str(World).Str(WorldName).Str(StartGroup).Str(StartName).U16((ushort)StartLevel);
    }

    /// <summary>Null when the frame is short.</summary>
    public static MpGameSettings? Read(ref MpReader r)
    {
        int limit = r.U8(), diff = r.U8(), time = r.U16();
        var drop = (MpDropOnDeath)r.U8();
        var b = (Bits)r.U16();
        string map = r.Str(), mapName = r.Str(), world = r.Str(), worldName = r.Str(), start = r.Str(), startName = r.Str();
        int startLevel = r.U16();
        if (r.Bad) return null;
        return new MpGameSettings
        {
            PlayerLimit = limit, Difficulty = diff, TimeLimitMinutes = time, DropOnDeath = drop,
            TeamPlay = b.HasFlag(Bits.TeamPlay), PlayerVsPlayer = b.HasFlag(Bits.Pvp),
            AllowStartChoice = b.HasFlag(Bits.StartChoice), NewCharactersOnly = b.HasFlag(Bits.NewOnly),
            AllowPause = b.HasFlag(Bits.Pause), AllowJoinInProgress = b.HasFlag(Bits.JoinInProgress),
            HasPassword = b.HasFlag(Bits.Password),
            Map = map, MapName = mapName, World = world, WorldName = worldName,
            StartGroup = start, StartName = startName, StartLevel = startLevel,
        }.Clamped();
    }

    /// <summary>One line for the region relaunch (an environment variable):
    /// the same fields as the wire frame, hex-encoded.</summary>
    public string ToEnv()
    {
        var w = new MpWriter();
        Write(w);
        return Convert.ToHexString(w.Span);
    }

    public static MpGameSettings? FromEnv(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        byte[] bytes;
        try { bytes = Convert.FromHexString(s.Trim()); }
        catch (FormatException) { return null; }
        var r = new MpReader(bytes);
        return Read(ref r);
    }

    /// <summary>The original's staging-area notices for what changed, in the
    /// order its Game Settings and Map Settings screens list the options.</summary>
    public static IReadOnlyList<string> Describe(MpGameSettings before, MpGameSettings after)
    {
        var lines = new List<string>();
        if (before.Map != after.Map) lines.Add($"Host changed the map to \"{after.MapName}\".");
        if (before.World != after.World) lines.Add($"Host changed the World Difficulty to {after.WorldName}.");
        if (before.PlayerLimit != after.PlayerLimit) lines.Add($"Host set the player limit to {after.PlayerLimit} players.");
        if (before.Difficulty != after.Difficulty) lines.Add($"Host set the game difficulty to {after.DifficultyName}.");
        if (before.TimeLimitMinutes != after.TimeLimitMinutes) lines.Add($"Host set the time limit to {TimeLimitName(after.TimeLimitMinutes)}.");
        if (before.DropOnDeath != after.DropOnDeath)
            lines.Add(after.DropOnDeath switch
            {
                MpDropOnDeath.All => "Host set \"Drop on Death\" to All.",
                MpDropOnDeath.Inventory => "Host set \"Drop on Death\" to Inventory only.",
                MpDropOnDeath.EquippedItems => "Host set \"Drop on Death\" to Equipped Items only.",
                _ => "Host set \"Drop on Death\" to Nothing.",
            });
        if (before.TeamPlay != after.TeamPlay) lines.Add(after.TeamPlay ? "Host has enabled team play." : "Host has disabled team play.");
        if (before.PlayerVsPlayer != after.PlayerVsPlayer)
            lines.Add(after.PlayerVsPlayer ? "Host has enabled Player-vs-Player combat." : "Host has disabled Player-vs-Player combat.");
        if (before.AllowStartChoice != after.AllowStartChoice)
            lines.Add(after.AllowStartChoice ? "Host has allowed the selection of Start Locations." : "Host has prevented the selection of Start Locations.");
        if (before.NewCharactersOnly != after.NewCharactersOnly)
            lines.Add(after.NewCharactersOnly ? "Host has restricted all players to new characters." : "Host has allowed saved characters to be used.");
        if (before.AllowPause != after.AllowPause)
            lines.Add(after.AllowPause ? "Host has allowed all players to be able to pause the game." : "Host has restricted all players from pausing the game.");
        if (before.AllowJoinInProgress != after.AllowJoinInProgress)
            lines.Add(after.AllowJoinInProgress ? "Host has allowed new players to join the game while in progress."
                                                : "Host has prevented new players from joining the game while in progress.");
        if (before.StartGroup.Length > 0 && before.StartGroup != after.StartGroup && after.StartName.Length > 0)
            lines.Add($"The party will start at {after.StartName}.");
        return lines;
    }

    /// <summary>The Settings Summary list (staging_game_settings_listbox).</summary>
    public IReadOnlyList<string> Summary(int players)
    {
        var s = new List<string>
        {
            $"Map: {MapName}",
            $"World: {WorldName}",
            $"Players: {players}/{PlayerLimit}",
            $"Difficulty: {DifficultyName}",
            $"Time Limit: {TimeLimitName(TimeLimitMinutes)}",
            $"Drop on Death: {DropName}",
        };
        if (StartName.Length > 0) s.Add($"Start: {StartName}");
        if (TeamPlay) s.Add("Team Play");
        if (PlayerVsPlayer) s.Add("Player vs. Player");
        if (NewCharactersOnly) s.Add("New Characters Only");
        if (AllowPause) s.Add("Pausing allowed");
        s.Add(AllowJoinInProgress ? "Join in Progress" : "No Join in Progress");
        if (HasPassword) s.Add("Password");
        return s;
    }

    /// <summary>A host's choices, kept between games (one key=value per line).</summary>
    public string ToPrefs() => string.Join('\n', new[]
    {
        $"player_limit={PlayerLimit}", $"difficulty={Difficulty}", $"time_limit={TimeLimitMinutes}",
        $"drop_on_death={(int)DropOnDeath}", $"team_play={B(TeamPlay)}", $"pvp={B(PlayerVsPlayer)}",
        $"start_choice={B(AllowStartChoice)}", $"new_characters_only={B(NewCharactersOnly)}",
        $"allow_pause={B(AllowPause)}", $"join_in_progress={B(AllowJoinInProgress)}",
    }) + "\n";

    static string B(bool v) => v ? "1" : "0";

    public static MpGameSettings FromPrefs(string text)
    {
        var s = new MpGameSettings();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line[..eq].Trim(), v = line[(eq + 1)..].Trim();
            int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
            s = k switch
            {
                "player_limit" => s with { PlayerLimit = n },
                "difficulty" => s with { Difficulty = n },
                "time_limit" => s with { TimeLimitMinutes = n },
                "drop_on_death" => s with { DropOnDeath = (MpDropOnDeath)n },
                "team_play" => s with { TeamPlay = n != 0 },
                "pvp" => s with { PlayerVsPlayer = n != 0 },
                "start_choice" => s with { AllowStartChoice = n != 0 },
                "new_characters_only" => s with { NewCharactersOnly = n != 0 },
                "allow_pause" => s with { AllowPause = n != 0 },
                "join_in_progress" => s with { AllowJoinInProgress = n != 0 },
                _ => s,
            };
        }
        return s.Clamped();
    }
}

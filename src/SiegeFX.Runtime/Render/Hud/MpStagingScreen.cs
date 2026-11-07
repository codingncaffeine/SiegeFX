using System.Numerics;
using Silk.NET.Input;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>SC-MP-EOS P8 — the staging area, drawn from the original's
/// staging_area.gas and staging_area_players.gas over the multiplayer
/// backdrop: the player list (Player / Team / Start Location / Character, one
/// staging row per player, the ready mark, your own row framed with your
/// Character cell as its "Select character..." button), the chat window with
/// its entry box, the settings summary, Leave Game, and Start Game (host) or
/// "I'm Ready to Begin" (joiner) or Enter Game (a joiner arriving in a game
/// already under way). The co-op character screens (<see cref="MpCharacterPanel"/>)
/// take the player list's place while they are open. Characters are each
/// player's own (retail model); the host owns the disposable world.</summary>
internal sealed class MpStagingScreen
{
    public enum Action { None, Leave, Start, ToggleReady, EnterGame, ChooseCharacter, SendChat }

    public bool IsActive { get; set; }
    public bool IsHost;
    public bool LocalReady;
    /// <summary>The game is already under way: this joiner enters it (Enter
    /// Game) instead of marking ready.</summary>
    public bool InProgress;
    public string HostIp = "";
    public string Map = "Kingdom of Ehb";
    public string Difficulty = "Regular";
    public string Status = "";

    /// <summary>One player list row. Player 0 is the host; <see cref="Local"/>
    /// is this machine's player.</summary>
    public readonly record struct Row(string Name, bool Host, bool Ready, string Character, bool Local);
    public readonly List<Row> Players = new();
    public readonly List<string> Chat = new();
    /// <summary>This player's chosen character for its Character cell ("" = none yet).</summary>
    public string LocalCharacter = "";

    public readonly MpCharacterPanel Characters = new();

    public string ChatInput { get; private set; } = "";
    /// <summary>The line Enter sent (read with the SendChat action).</summary>
    public string SentChat { get; private set; } = "";

    readonly AuthoredFrame _f = new();
    string _hover = "", _pressed = "";
    Action _pending = Action.None;
    public Action ConsumeAction() { var a = _pending; _pending = Action.None; return a; }

    string HitName(int px, int py)
    {
        bool H(int x0, int y0, int x1, int y1) => _f.Hit(x0, y0, x1, y1, px, py);
        if (H(580, 470, 756, 486)) return "leave";
        if (IsHost) { if (H(580, 498, 756, 514)) return "start"; }
        else if (InProgress) { if (H(580, 498, 756, 514)) return "enter"; }
        else if (H(584, 498, 756, 514)) return "ready";
        if (H(24, 490, 546, 514)) return "chat";
        if (!Characters.IsOpen)
        {
            int local = Players.FindIndex(r => r.Local);
            if (local >= 0 && local < 8 && H(541, 102 + 30 * local, 762, 118 + 30 * local)) return "character";
        }
        return "";
    }

    AuthoredFrame.Press State(string name) =>
        _pressed == name && _hover == name ? AuthoredFrame.Press.Down
        : _hover == name ? AuthoredFrame.Press.Hover : AuthoredFrame.Press.Up;

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        _f.Layout(vw, vh);
        Characters.OnMouseMove(px, py, vw, vh);
        _hover = Characters.AskingDelete ? "" : HitName(px, py);
    }

    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsActive) return;
        _f.Layout(vw, vh);
        if (Characters.AskingDelete) { Characters.OnMouseDown(px, py, vw, vh); return; }
        _pressed = HitName(px, py);
        if (_pressed.Length == 0) Characters.OnMouseDown(px, py, vw, vh);
    }

    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsActive) { _pressed = ""; return; }
        _f.Layout(vw, vh);
        if (Characters.AskingDelete || _pressed.Length == 0) { Characters.OnMouseUp(px, py, vw, vh); _pressed = ""; return; }
        string hit = HitName(px, py);
        if (hit == _pressed)
            _pending = hit switch
            {
                "leave" => Action.Leave,
                "start" => Action.Start,
                "enter" => Action.EnterGame,
                "ready" => Action.ToggleReady,
                "character" => Action.ChooseCharacter,
                _ => Action.None,
            };
        _pressed = "";
    }

    public void OnWheel(int delta) { if (IsActive) Characters.OnWheel(delta); }

    /// <summary>A typed character: the character screens' name box when it is
    /// up, else the chat entry.</summary>
    public void OnChar(char c)
    {
        if (!IsActive || Characters.OnChar(c)) return;
        if (c == '\b') { if (ChatInput.Length > 0) ChatInput = ChatInput[..^1]; return; }
        if (c >= ' ' && ChatInput.Length < 120) ChatInput += c;
    }

    public void OnKey(Key key)
    {
        if (!IsActive || Characters.OnKey(key)) return;
        if (key == Key.Backspace) OnChar('\b');
        else if (key is Key.Enter or Key.KeypadEnter && ChatInput.Trim().Length > 0)
        {
            SentChat = ChatInput.Trim(); ChatInput = ""; _pending = Action.SendChat;
        }
    }

    public void Draw(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
    {
        _f.Begin(vw, vh, icons, bars, text, tex, common);

        // background.gas — the multiplayer backdrop.
        _f.Window(0, 0, 256, 256, "b_gui_fe_m_mp_background_01"); _f.Window(256, 0, 512, 256, "b_gui_fe_m_mp_background_02");
        _f.Window(512, 0, 768, 256, "b_gui_fe_m_mp_background_03"); _f.Window(0, 256, 256, 512, "b_gui_fe_m_mp_background_04");
        _f.Window(256, 256, 512, 512, "b_gui_fe_m_mp_background_05"); _f.Window(512, 256, 768, 512, "b_gui_fe_m_mp_background_06");
        _f.Window(0, 512, 256, 600, "b_gui_fe_m_mp_background_07", 1f, 0, 0.65625f, 1, 1);
        _f.Window(256, 512, 512, 600, "b_gui_fe_m_mp_background_07", 1f, 0, 0.3125f, 1, 0.65625f);
        _f.Window(512, 512, 768, 600, "b_gui_fe_m_mp_background_08", 1f, 0, 0.65625f, 1, 1);
        _f.Window(768, 0, 800, 256, "b_gui_fe_m_mp_background_09", 1f, 0, 0, 0.125f, 1);
        _f.Window(768, 256, 800, 512, "b_gui_fe_m_mp_background_09", 1f, 0.125f, 0, 0.25f, 1);
        _f.Window(768, 512, 800, 600, "b_gui_fe_m_mp_background_09", 1f, 0.25f, 0.65625f, 0.375f, 1);

        // staging_area.gas
        _f.Box(8, 8, 792, 592, "cpbox_wide");
        _f.Label(24, 28, 420, 50, "STAGING AREA", "20p");
        _f.Box(24, 60, 776, 340, "cpbox_wide");
        if (Characters.IsOpen) Characters.Draw(vw, vh, icons, bars, text, tex, common);
        else DrawPlayers();

        _f.Label(24, 348, 500, 366, "Chat Window", "14p");
        _f.Box(24, 368, 546, 486);
        int lineH = Math.Max(1, (int)MathF.Round(_f.LineHeight() / _f.Scale) + 2);
        int fit = Math.Max(1, (482 - 372) / lineH);
        int first = Math.Max(0, Chat.Count - fit);
        for (int i = first; i < Chat.Count; i++)
        {
            int y = 372 + (i - first) * lineH;
            _f.Label(28, y, 526, y + lineH, Chat[i]);
        }
        _f.Box(24, 490, 546, 514);
        bool caret = !Characters.IsOpen && (Environment.TickCount64 / 500) % 2 == 0;
        if (ChatInput.Length > 0 || caret) _f.Label(28, 494, 542, 510, ChatInput + (caret ? "_" : ""));
        else _f.Label(28, 494, 542, 510, "Type a message, then press Enter", "", AuthoredFrame.Faint);

        _f.Label(556, 348, 776, 366, "Settings Summary", "14p");
        _f.Box(556, 368, 776, 460);
        var settings = new List<string> { $"Map: {Map}", $"Difficulty: {Difficulty}", $"Players: {Players.Count}/8" };
        if (IsHost && HostIp.Length > 0)
        {
            settings.Add("Your address:");
            if (HostIp.Length > 18) { settings.Add(HostIp[..16]); settings.Add(HostIp[16..]); }
            else settings.Add(HostIp);
        }
        for (int i = 0; i < settings.Count && 371 + 14 * (i + 1) <= 457; i++)
            _f.Label(562, 371 + 14 * i, 770, 385 + 14 * i, settings[i], "", i >= 3 ? AuthoredFrame.Faint : null);

        _f.Button(580, 470, 756, 486, "Leave Game", State("leave"));
        if (IsHost) _f.Button(580, 498, 756, 514, "Start Game", State("start"), true, AuthoredFrame.Green);
        else if (InProgress) _f.Button(580, 498, 756, 514, "Enter Game", State("enter"));
        else
        {
            _f.Window(584, 498, 600, 514, "b_gui_cmn_checkbox");
            if (LocalReady) _f.Window(584, 498, 600, 514, "b_gui_cmn_checkbox_x");
            _f.Label(608, 490, 756, 520, "I'm Ready to Begin", "", _hover == "ready" ? AuthoredFrame.White : AuthoredFrame.Green);
        }

        _f.Box(24, 530, 776, 576);
        if (Status.Length > 0) _f.Label(32, 530, 768, 576, Status, "", null, AuthoredFrame.Justify.Center);
        Characters.DrawModal(vw, vh, icons, bars, text, tex, common);
    }

    // staging_area_players.gas
    void DrawPlayers()
    {
        _f.Box(32, 70, 768, 94);
        _f.Label(37, 73, 221, 91, "Player", "14p");
        _f.Box(225, 70, 315, 94, "cpbox_thin");
        _f.Label(228, 73, 312, 91, "Team", "14p", null, AuthoredFrame.Justify.Center);
        _f.Box(316, 70, 536, 94, "cpbox_thin");
        _f.Label(319, 73, 533, 91, "Start Location", "14p");
        _f.Box(537, 70, 766, 94, "cpbox_thin");
        _f.Label(543, 73, 760, 91, "Character", "14p");

        for (int i = 0; i < Math.Min(8, Players.Count); i++)
        {
            var p = Players[i];
            int y = 98 + 30 * i;
            if (p.Local) _f.Box(34, y - 2, 766, y + 26, "cpbox_thin");
            _f.Window(34, y, 766, y + 24, "b_gui_fe_m_mp_staging_player_bg", i % 2 == 0 ? 0.2f : 0.3f);
            _f.Box(34, y + 2, 224, y + 22, "cpbox_thin", 0.6f);
            if (p.Ready) _f.Window(36, y + 4, 52, y + 20, "b_gui_fe_m_mp_staging_ready");
            _f.Label(74, y + 5, 220, y + 19, p.Name);
            _f.Window(224, y + 4, 225, y + 20, "b_gui_fe_m_mp_staging_player_bg", 0.8f);
            _f.Window(315, y + 4, 316, y + 20, "b_gui_fe_m_mp_staging_player_bg", 0.8f);
            _f.Box(320, y + 4, 532, y + 20, "cpbox_thin", 0.4f);
            _f.Label(322, y + 5, 532, y + 19, p.Host || !InProgress ? "Farmhouse" : "Beside the host");
            _f.Window(536, y + 4, 537, y + 20, "b_gui_fe_m_mp_staging_player_bg", 0.8f);
            _f.Box(537, y + 2, 766, y + 22, "cpbox_thin", 0.6f);
            if (p.Local)
                _f.Button(541, y + 4, 762, y + 20, LocalCharacter.Length > 0 ? LocalCharacter : "Select character...",
                          State("character"), true, null, AuthoredFrame.Justify.Left);
            else
                _f.Label(543, y + 5, 760, y + 19, p.Character);
        }
    }
}
